using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Morphdrop.Actions;
using Morphdrop.Editing;
using Morphdrop.Imaging;
using Morphdrop.Settings;

namespace Morphdrop.Pdf;

/// <summary>
/// Every page of the dropped PDFs as a tile: drag to reorder, turn or delete pages, drop more PDFs or pictures in,
/// then save one PDF. One PDF in gives "name (edited).pdf"; several give "first name (merged).pdf".
/// </summary>
internal sealed class PageOrganizerWindow : Window
{
    private const double ThumbnailSide = 260;
    private const double TileWidth = 150;

    private readonly IFileAction _action;
    private readonly ActionRunner _runner;
    private readonly TileList<Page> _pages;
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _save;
    private Task _loading = Task.CompletedTask;

    private PageOrganizerWindow(IFileAction action, IReadOnlyList<string> files, ActionRunner runner)
    {
        _action = action;
        _runner = runner;
        Title = "Morphdrop – Organize pages";
        Width = 1100;
        Height = 760;
        MinWidth = 640;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        AppTheme.Attach(this);
        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
                AddFiles(dropped.Order(ImageConverter.NaturalOrder.Instance));
        };

        _pages = new TileList<Page>(new WrapPanel { Margin = new Thickness(16) }, Tile);
        _pages.Changed += Refresh;

        _save = new Button { Content = "Save PDF", IsEnabled = false };
        _save.SetResourceReference(StyleProperty, "AccentButton");
        _save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.SetResourceReference(StyleProperty, "ActionButton");
        cancel.Click += (_, _) => Close();

        var bottom = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 12, 20, 12),
            Child = new DockPanel
            {
                Children =
                {
                    Docked(new StackPanel { Orientation = Orientation.Horizontal, Children = { cancel, _save } }, Dock.Right),
                    _count,
                },
            },
        };
        bottom.SetResourceReference(Border.BackgroundProperty, "Theme.Panel");
        bottom.SetResourceReference(Border.BorderBrushProperty, "Theme.PanelBorder");
        Content = new DockPanel
        {
            Children =
            {
                Docked(bottom, Dock.Bottom),
                new ScrollViewer { Content = _pages.Panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            },
        };

        AddFiles(files);
    }

    public static void Open(IFileAction action, IReadOnlyList<string> files, ActionRunner runner)
    {
        var window = new PageOrganizerWindow(action, files, runner);
        window.Show();
        window.Topmost = true; // Morphdrop runs in the background, so make sure the window is seen
        window.Activate();
        window.Topmost = false;
    }

    /// <summary>Adds the files' pages after the ones already there, in order, one file after another.</summary>
    private void AddFiles(IEnumerable<string> files)
    {
        foreach (string file in files.Where(File.Exists))
        {
            if (PdfToolAction.IsPdf(file))
                _loading = _loading.ContinueWith(_ => LoadPdf(file), TaskScheduler.Default);
            else if (ImageFormat.FromPath(file) is { CanRead: true })
                _loading = _loading.ContinueWith(_ => LoadPicture(file), TaskScheduler.Default);
        }
    }

    private void LoadPdf(string path)
    {
        try
        {
            PdfPages.RenderThumbnails(path, ThumbnailSide, (index, _, png) =>
            {
                BitmapSource thumbnail = Thumbnails.FromPng(png);
                Dispatcher.Invoke(() => _pages.Add(new Page(path, index, thumbnail)));
            });
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't open '{path}' in the organizer", ex);
            Dispatcher.Invoke(() => _count.Text = $"Couldn't open {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private void LoadPicture(string path)
    {
        try
        {
            (BitmapSource thumbnail, _, _) = Thumbnails.OfPicture(path, (uint)ThumbnailSide);
            Dispatcher.Invoke(() => _pages.Add(new Page(path, -1, thumbnail)));
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't open '{path}' in the organizer", ex);
            Dispatcher.Invoke(() => _count.Text = $"Couldn't open {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private UIElement Tile(Page page)
    {
        var picture = new Image
        {
            Source = page.Thumbnail,
            Stretch = Stretch.Uniform,
            LayoutTransform = new RotateTransform(page.Rotation),
        };
        var frame = new Border
        {
            Width = TileWidth - 12,
            Height = TileWidth * 1.3,
            Child = picture,
        };
        frame.SetResourceReference(Border.BackgroundProperty, "Theme.Panel");

        string where = page.IsPicture ? Path.GetFileName(page.Source) : $"{Path.GetFileName(page.Source)} · {page.PageIndex + 1}";
        var label = new TextBlock { Text = where, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SubtleText");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(IconButton("", "Turn left", () => Turn(page, -90), mirrored: true));
        buttons.Children.Add(IconButton("", "Turn right", () => Turn(page, 90)));
        buttons.Children.Add(IconButton("", "Delete this page", () => _pages.Remove(page)));
        return new StackPanel { Width = TileWidth - 12, Children = { frame, label, buttons } };
    }

    private static Button IconButton(string glyph, string tip, Action click, bool mirrored = false)
    {
        var icon = new TextBlock { Text = glyph, FontFamily = EditorUi.IconFont, FontSize = 13 };
        if (mirrored)
        {
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = new ScaleTransform(-1, 1);
        }

        var button = new Button { Content = icon, ToolTip = tip, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(2, 0, 2, 0) };
        button.SetResourceReference(StyleProperty, "ActionButton");
        button.Click += (_, _) => click();
        return button;
    }

    private void Turn(Page page, int degrees)
    {
        page.Rotation = ((page.Rotation + degrees) % 360 + 360) % 360;
        _pages.Refresh(page);
    }

    private void Refresh()
    {
        int count = _pages.Count;
        int files = _pages.Items.Select(page => page.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _count.Text = count == 0
            ? "No pages. Drop PDFs or pictures here."
            : $"{count} {(count == 1 ? "page" : "pages")}{(files > 1 ? $" from {files} files" : "")} · drag to reorder · drop PDFs or pictures here to add them";
        _save.IsEnabled = count > 0;
    }

    private void Save()
    {
        AssembledPage[] pages = _pages.Items.Select(page => new AssembledPage(page.Source, page.PageIndex, page.Rotation)).ToArray();
        if (pages.Length == 0)
            return;

        _runner.RunJob(_action, pages[0].Source, () => PageAssembly.Save(pages));
        Close();
    }

    private static T Docked<T>(T element, Dock dock) where T : UIElement
    {
        DockPanel.SetDock(element, dock);
        return element;
    }

    private sealed class Page(string source, int pageIndex, BitmapSource thumbnail)
    {
        public string Source { get; } = source;

        public int PageIndex { get; } = pageIndex;

        public bool IsPicture => PageIndex < 0;

        public BitmapSource Thumbnail { get; } = thumbnail;

        public int Rotation { get; set; }
    }
}
