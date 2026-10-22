using System.Windows;
using System.Windows.Input;
using Morphdrop.Actions;

namespace Morphdrop.Editing;

/// <summary>What the editor window needs from each tool.</summary>
internal interface IImageEditor
{
    string SaveLabel { get; }

    /// <summary>The image with the tool's handles or drawings.</summary>
    FrameworkElement View { get; }

    /// <summary>The tool's settings, shown in the bottom bar next to Cancel and Save.</summary>
    FrameworkElement Options { get; }

    bool CanSave { get; }

    event Action? CanSaveChanged;

    /// <summary>Takes the current settings on the UI thread; the returned job saves the copy in the background.</summary>
    Func<string> PrepareSave();

    void Undo()
    {
    }
}

/// <summary>
/// The window for the Edit page's tools. It opens at once, loads the image in the background, then hosts the tool.
/// Saving hands the work to the <see cref="ActionRunner"/>, so it gets the usual notifications, and closes the window.
/// </summary>
internal partial class EditorWindow : Window
{
    private readonly EditAction _action;
    private readonly string _path;
    private readonly ActionRunner _runner;
    private EditableImage? _image;
    private IImageEditor? _editor;
    private bool _closed;
    private bool _imageHandedOver;

    private EditorWindow(EditAction action, string path, ActionRunner runner)
    {
        _action = action;
        _path = path;
        _runner = runner;
        InitializeComponent();
        Settings.AppTheme.Attach(this);
        Title = $"{action.Label} – {Path.GetFileName(path)}";

        CancelButton.Click += (_, _) => Close();
        SaveButton.Click += (_, _) => Save();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _editor?.Undo();
                e.Handled = true;
            }
        };
        Closed += (_, _) =>
        {
            _closed = true;
            if (!_imageHandedOver)
                _image?.Dispose();
        };
    }

    public static void Open(EditAction action, string path, ActionRunner runner)
    {
        var window = new EditorWindow(action, path, runner);
        window.Show();

        // Morphdrop runs in the background, so Windows may refuse to bring a new window to the front; being topmost
        // for a moment makes sure it is seen.
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
        _ = window.LoadAsync();
    }

    private async Task LoadAsync()
    {
        EditableImage image;
        try
        {
            image = await Task.Run(() => EditableImage.Load(_path));
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open '{_path}' for {_action.Label}", ex);
            Status.Text = ex is NothingToDoException ? ex.Message : $"Couldn't open {Path.GetFileName(_path)}: {ex.Message}";
            return;
        }

        if (_closed)
        {
            image.Dispose();
            return;
        }

        _image = image;
        Show(_action.Kind switch
        {
            EditKind.Crop => new CropEditor(image),
            EditKind.Redact => new RedactEditor(image),
            EditKind.RemoveBackground => CreateCutoutEditor(image),
            _ => new BackgroundEditor(image),
        });
        Status.Visibility = Visibility.Collapsed;
        Log.Info($"Opened '{_path}' in {_action.Label} ({image.Width}x{image.Height}, {image.Frames.Count} frame(s))");
    }

    private void Show(IImageEditor editor)
    {
        _editor = editor;
        EditorHost.Content = editor.View;
        OptionsHost.Content = editor.Options;
        SaveButton.Content = editor.SaveLabel;
        SaveButton.IsEnabled = editor.CanSave;
        editor.CanSaveChanged += () =>
        {
            if (_editor == editor)
                SaveButton.IsEnabled = editor.CanSave;
        };
    }

    /// <summary>"Put on a background…" carries the cut-out over to the Background editor in this same window.</summary>
    private Cutout.CutoutEditor CreateCutoutEditor(EditableImage image)
    {
        var editor = new Cutout.CutoutEditor(image);
        editor.ContinueWithBackground += () =>
        {
            EditableImage cutout = image.WithFirstFrame(editor.CreateCutout());
            _image = cutout;
            image.Dispose();
            Title = $"Background – {Path.GetFileName(_path)}";
            Show(new BackgroundEditor(cutout));
            Log.Info($"Carried the cut-out of '{_path}' over to Background");
        };
        return editor;
    }

    private void Save()
    {
        if (_editor is null || _image is null || !_editor.CanSave)
            return;

        Func<string> job;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            job = _editor.PrepareSave();
        }
        catch (Exception ex)
        {
            Log.Error($"{_action.Label} could not prepare the save", ex);
            MessageBox.Show(this, ex.Message, "Morphdrop", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        // The background job now owns the image and releases it when done.
        EditableImage image = _image;
        _imageHandedOver = true;
        _runner.RunJob(_action, _path, () =>
        {
            try
            {
                return job();
            }
            finally
            {
                image.Dispose();
            }
        });
        Close();
    }
}
