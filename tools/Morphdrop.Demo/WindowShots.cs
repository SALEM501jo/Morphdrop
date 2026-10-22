using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Morphdrop.Actions;
using Morphdrop.Overlay;

namespace Morphdrop.Demo;

/// <summary>
/// <c>--windows=builder|organizer|wheel</c>: opens one of the PDF windows off screen with the demo files and saves a
/// picture of it (or of the wheel pages for pictures and PDFs), to check the look without clicking through it.
/// One per run: without Application.Run, the demo's App doesn't outlive its first window.
/// </summary>
internal static class WindowShots
{
    public static void Render(DemoAssets assets, string folder, string which)
    {
        // A real message loop (without Application.Run, which would start Morphdrop itself), so the windows load,
        // lay out and draw the way they do in the app.
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await RenderAsync(assets, folder, which);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                dispatcher.InvokeShutdown();
            }
        });
        Dispatcher.Run();
    }

    private static async Task RenderAsync(DemoAssets assets, string folder, string which)
    {
        var runner = new ActionRunner();
        if (which is "builder" or "all")
        {
            Window window = Open(Find("a.jpg", "PDF + text"), new[] { assets.Sunset, assets.Scans[0], assets.Scans[1] }, runner);
            await Task.Delay(TimeSpan.FromSeconds(4));
            Field<TextBox>(window, "_above").Text = "رحلة الصيف 2026";
            Field<TextBox>(window, "_below").Text = "Sunset over the lake, the first evening.";
            await Task.Delay(TimeSpan.FromSeconds(1));
            Save(window, Path.Combine(folder, "window-pdf-builder.png"));
            window.Close();
        }

        if (which is "organizer" or "all")
        {
            Window window = Open(Find("a.pdf", "Organize"), new[] { assets.ScansPdf }, runner);
            await Task.Delay(TimeSpan.FromSeconds(4));
            Save(window, Path.Combine(folder, "window-organizer.png"));
            window.Close();
        }

        if (which is "sign" or "all")
        {
            // A made-up signature, as if drawn earlier and remembered.
            var strokes = new System.Windows.Ink.StrokeCollection();
            var points = new System.Windows.Input.StylusPointCollection();
            for (double x = 0; x < 260; x += 2)
                points.Add(new System.Windows.Input.StylusPoint(x, 40 + 22 * Math.Sin(x / 14) * Math.Exp(-x / 300)));
            strokes.Add(new System.Windows.Ink.Stroke(points) { DrawingAttributes = { Color = System.Windows.Media.Color.FromRgb(0x1A, 0x23, 0x5C), Width = 3, Height = 3, FitToCurve = true } });
            Directory.CreateDirectory(AppPaths.DataFolder);
            File.WriteAllBytes(Morphdrop.Pdf.SignaturePad.SavedPath, Morphdrop.Pdf.SignaturePad.Render(strokes));

            Window window = Open(Find("a.pdf", "Sign"), new[] { assets.ScansPdf }, runner);
            await Task.Delay(TimeSpan.FromSeconds(3));
            window.GetType().GetMethod("AddTextBox", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { "Jordan Lee" });
            window.GetType().GetMethod("AddSignature", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { false });
            await Task.Delay(TimeSpan.FromSeconds(1));
            Save(window, Path.Combine(folder, "window-sign.png"));
            window.Close();
        }

        if (which is "password" or "all")
        {
            Window window = Open(Find("a.pdf", "Password"), new[] { assets.ScansPdf }, runner);
            await Task.Delay(TimeSpan.FromSeconds(1));
            Save(window, Path.Combine(folder, "window-password.png"));
            window.Close();
        }

        if (which is "wheel" or "all")
            RenderWheels(Path.Combine(folder, "wheel-pages.png"));
    }

    /// <summary>Opens the slice's window and moves it off screen, so it doesn't flash up on the desktop.</summary>
    private static Window Open(IFileAction action, string[] files, ActionRunner runner)
    {
        var before = Application.Current.Windows.OfType<Window>().ToHashSet();
        ((WindowAction)action).Open(files, runner);
        Window window = Application.Current.Windows.OfType<Window>().First(candidate => !before.Contains(candidate));
        window.Left = -20000;
        return window;
    }

    private static IFileAction Find(string file, string label) =>
        ActionCatalog.ForFileName(file).SelectMany(page => page.Actions).First(action => action.Label == label);

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    private static void RenderWheels(string path)
    {
        var row = new WrapPanel { Width = 1150, Background = Brushes.DimGray };
        foreach (string file in new[] { "photo.jpg", "report.pdf" })
        {
            foreach (ActionPage page in ActionCatalog.ForFileName(file))
            {
                var wheel = new WheelView { Margin = new Thickness(10) };
                wheel.SetDarkTheme(false);
                wheel.SetSlices(page.Actions.Select(action => new WheelItem(action.Label, action.Glyph)).ToList());
                wheel.SetHub(file, page.Name);
                row.Children.Add(wheel);
            }
        }

        row.Measure(new Size(row.Width, double.PositiveInfinity));
        row.Arrange(new Rect(row.DesiredSize));
        row.UpdateLayout();
        SaveVisual(row, row.ActualWidth, row.ActualHeight, path);
    }

    private static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        SaveVisual(content, content.ActualWidth, content.ActualHeight, path);
    }

    private static void SaveVisual(Visual visual, double width, double height, string path)
    {
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (DrawingContext context = background.RenderOpen())
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }
}
