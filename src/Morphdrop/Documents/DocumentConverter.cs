using System.Diagnostics;
using System.Runtime.InteropServices;
using Morphdrop.Actions;

namespace Morphdrop.Documents;

internal enum OfficeApp { Word, Excel, PowerPoint }

/// <summary>
/// Saves Office documents as PDF with the Office that's installed (Word, Excel or PowerPoint, so the PDF looks exactly
/// like the document does there), or with LibreOffice when Office isn't available. Nothing is uploaded anywhere.
/// </summary>
internal static class DocumentConverter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private static readonly Dictionary<string, OfficeApp> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".docx"] = OfficeApp.Word, [".doc"] = OfficeApp.Word, [".docm"] = OfficeApp.Word, [".rtf"] = OfficeApp.Word, [".odt"] = OfficeApp.Word,
        [".xlsx"] = OfficeApp.Excel, [".xls"] = OfficeApp.Excel, [".xlsm"] = OfficeApp.Excel, [".ods"] = OfficeApp.Excel,
        [".pptx"] = OfficeApp.PowerPoint, [".ppt"] = OfficeApp.PowerPoint, [".pptm"] = OfficeApp.PowerPoint, [".odp"] = OfficeApp.PowerPoint,
    };

    private static readonly Lazy<string?> LibreOffice = new(FindLibreOffice);

    public static OfficeApp? AppFor(string path) => Extensions.TryGetValue(Path.GetExtension(path), out OfficeApp app) ? app : null;

    /// <summary>Office for this kind of document, or LibreOffice. A registry lookup, cheap enough for the wheel.</summary>
    public static bool CanConvert(string path) =>
        AppFor(path) is OfficeApp app && (IsInstalled(app) || LibreOffice.Value is not null);

    public static bool IsInstalled(OfficeApp app) => Type.GetTypeFromProgID(ProgId(app)) is not null;

    /// <summary>Saves "name.pdf" next to the document (or in the chosen folder) and returns its path.</summary>
    public static string ToPdf(string input)
    {
        OfficeApp app = AppFor(input) ?? throw new NotSupportedException($"{Path.GetFileName(input)} isn't a document Morphdrop can convert.");
        string fullInput = Path.GetFullPath(input);
        string output = OutputFile.Save(input, suffix: null, ".pdf", _ => { }); // reserve the name; Office fills it in
        try
        {
            if (IsInstalled(app))
                RunOnStaThread(() => ExportWithOffice(app, fullInput, output));
            else if (LibreOffice.Value is { } soffice)
                ExportWithLibreOffice(soffice, fullInput, output);
            else
                throw new NotSupportedException("Converting documents needs Microsoft Office or LibreOffice.");

            if (new FileInfo(output).Length == 0)
                throw new InvalidOperationException("Office didn't write the PDF.");
            return output;
        }
        catch
        {
            File.Delete(output);
            throw;
        }
    }

    /// <summary>Word documents other than .docx (which Morphdrop reads itself) need Word for their text.</summary>
    public static bool CanReadText(string path) => AppFor(path) == OfficeApp.Word && IsInstalled(OfficeApp.Word);

    /// <summary>The document's text, through a hidden, read-only Word (closed again afterwards).</summary>
    public static string ReadText(string input)
    {
        string fullInput = Path.GetFullPath(input);
        string text = "";
        RunOnStaThread(() =>
        {
            dynamic word = Activator.CreateInstance(Type.GetTypeFromProgID(ProgId(OfficeApp.Word))!)!;
            dynamic? document = null;
            bool quit = false;
            try
            {
                word.DisplayAlerts = 0;
                quit = word.Documents.Count == 0;
                document = word.Documents.Open(FileName: fullInput, ConfirmConversions: false, ReadOnly: true,
                    AddToRecentFiles: false, PasswordDocument: "morphdrop", Visible: false);
                text = (string)document.Content.Text;
                document.Close(SaveChanges: 0);
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException($"Word couldn't open the file (it may be password-protected or damaged): {ex.Message}", ex);
            }
            finally
            {
                if (document is not null)
                    Marshal.FinalReleaseComObject(document);
                if (quit)
                {
                    try
                    {
                        word.Quit();
                    }
                    catch (COMException)
                    {
                        // It's closing anyway.
                    }
                }

                Marshal.FinalReleaseComObject(word);
            }
        });

        // Word ends paragraphs with a bare carriage return.
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine).Trim();
    }

    private static string ProgId(OfficeApp app) => $"{app}.Application";

    /// <summary>
    /// Opens the document read-only in a hidden Office, exports it, and closes it again. Office is only quit if
    /// Morphdrop's document was the only one open, so a Word window you're working in stays as it is.
    /// </summary>
    private static void ExportWithOffice(OfficeApp app, string input, string output)
    {
        dynamic office = Activator.CreateInstance(Type.GetTypeFromProgID(ProgId(app))!)!;
        dynamic? document = null;
        bool quit = false;
        try
        {
            switch (app)
            {
                case OfficeApp.Word:
                    office.DisplayAlerts = 0; // wdAlertsNone
                    quit = office.Documents.Count == 0;
                    // A dummy password makes protected files fail at once instead of showing a prompt nobody can see.
                    document = office.Documents.Open(FileName: input, ConfirmConversions: false, ReadOnly: true,
                        AddToRecentFiles: false, PasswordDocument: "morphdrop", Visible: false);
                    document.ExportAsFixedFormat(OutputFileName: output, ExportFormat: 17); // wdExportFormatPDF
                    document.Close(SaveChanges: 0);
                    break;
                case OfficeApp.Excel:
                    office.DisplayAlerts = false;
                    quit = office.Workbooks.Count == 0;
                    document = office.Workbooks.Open(Filename: input, ReadOnly: true, Password: "morphdrop", AddToMru: false);
                    document.ExportAsFixedFormat(Type: 0, Filename: output); // xlTypePDF
                    document.Close(SaveChanges: false);
                    break;
                default:
                    quit = office.Presentations.Count == 0;
                    document = office.Presentations.Open(FileName: input, ReadOnly: -1, Untitled: 0, WithWindow: 0); // msoTrue / msoFalse
                    document.SaveAs(output, 32); // ppSaveAsPDF
                    document.Close();
                    break;
            }
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"{app} couldn't convert the file (it may be password-protected or damaged): {ex.Message}", ex);
        }
        finally
        {
            if (document is not null)
                Marshal.FinalReleaseComObject(document);
            if (quit)
            {
                try
                {
                    office.Quit();
                }
                catch (COMException)
                {
                    // It's closing anyway.
                }
            }

            Marshal.FinalReleaseComObject(office);
        }
    }

    private static void ExportWithLibreOffice(string soffice, string input, string output)
    {
        // LibreOffice names the PDF after the document, so convert into a private folder and move it into place.
        string folder = Directory.CreateTempSubdirectory("morphdrop-lo-").FullName;
        try
        {
            var start = new ProcessStartInfo(soffice) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string argument in new[] { "--headless", "--norestore", "--convert-to", "pdf", "--outdir", folder, input })
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("LibreOffice didn't start.");
            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("LibreOffice took too long.");
            }

            string pdf = Directory.GetFiles(folder, "*.pdf").SingleOrDefault()
                ?? throw new InvalidOperationException("LibreOffice couldn't convert the file.");
            File.Move(pdf, output, overwrite: true);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Office automation wants a single-threaded apartment; a stuck Office (a hidden dialog) times out.</summary>
    private static void RunOnStaThread(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true, Name = "Morphdrop Office export" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(Timeout))
            throw new TimeoutException("Office didn't answer in time; it may be showing a message. Check for an open Office window.");
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static string? FindLibreOffice()
    {
        foreach (Environment.SpecialFolder root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string candidate = Path.Combine(Environment.GetFolderPath(root), "LibreOffice", "program", "soffice.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
