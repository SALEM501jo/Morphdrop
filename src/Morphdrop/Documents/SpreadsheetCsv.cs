using System.Globalization;
using System.Text;
using ExcelDataReader;
using Morphdrop.Actions;

namespace Morphdrop.Documents;

/// <summary>
/// Saves the sheets of an Excel workbook (.xlsx, .xlsm, .xls) as CSV, without Excel: one sheet gives "name.csv",
/// several give a folder "name (sheets)" with a CSV per sheet. UTF-8 with a BOM, so Excel itself reads Arabic right.
/// </summary>
internal static class SpreadsheetCsv
{
    static SpreadsheetCsv() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // for old .xls files

    public static bool Supports(string path) => Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".xlsm" or ".xls";

    public static string Convert(string path)
    {
        List<(string Name, List<string[]> Rows)> sheets = Read(path);
        sheets.RemoveAll(sheet => sheet.Rows.Count == 0);
        if (sheets.Count == 0)
            throw new NothingToDoException("The workbook is empty.");

        if (sheets.Count == 1)
            return OutputFile.Save(path, null, ".csv", output => Write(output, sheets[0].Rows));

        string folder = OutputFile.CreateUniqueFolder(path, "sheets");
        foreach ((string name, List<string[]> rows) in sheets)
        {
            using FileStream output = OutputFile.CreateUnique(folder, SafeName(name), ".csv", out _);
            Write(output, rows);
        }

        return folder;
    }

    private static List<(string Name, List<string[]> Rows)> Read(string path)
    {
        var sheets = new List<(string, List<string[]>)>();
        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            using IExcelDataReader reader = ExcelReaderFactory.CreateReader(stream);
            do
            {
                var rows = new List<string[]>();
                while (reader.Read())
                {
                    var cells = new string[reader.FieldCount];
                    for (int i = 0; i < cells.Length; i++)
                        cells[i] = Format(reader.GetValue(i));
                    rows.Add(cells);
                }

                // Trailing empty rows and columns are layout leftovers, not data.
                while (rows.Count > 0 && rows[^1].All(string.IsNullOrEmpty))
                    rows.RemoveAt(rows.Count - 1);
                int width = rows.Count == 0 ? 0 : rows.Max(row => Array.FindLastIndex(row, cell => cell.Length > 0) + 1);
                sheets.Add((reader.Name, rows.Select(row => row.Take(width).ToArray()).ToList()));
            }
            while (reader.NextResult());
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Password", StringComparison.Ordinal) || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("This workbook is password-protected.", ex);
        }

        return sheets;
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        DateTime date when date.TimeOfDay == TimeSpan.Zero => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        bool flag => flag ? "TRUE" : "FALSE",
        _ => System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    private static void Write(Stream output, List<string[]> rows)
    {
        using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        foreach (string[] row in rows)
            writer.Write(string.Join(",", row.Select(Quote)) + "\r\n");
    }

    /// <summary>RFC 4180: cells with a comma, quote or line break are quoted, quotes doubled.</summary>
    internal static string Quote(string cell) =>
        cell.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;

    private static string SafeName(string name)
    {
        string safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return safe.Length > 0 ? safe : "Sheet";
    }
}
