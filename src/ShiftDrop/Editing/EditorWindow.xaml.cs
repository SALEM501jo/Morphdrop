using System.Windows;
using System.Windows.Input;
using ShiftDrop.Actions;

namespace ShiftDrop.Editing;

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

        // ShiftDrop runs in the background, so Windows may refuse to bring a new window to the front; being topmost
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
        _editor = _action.Kind switch
        {
            EditKind.Crop => new CropEditor(image),
            EditKind.Redact => new RedactEditor(image),
            _ => new BackgroundEditor(image),
        };
        Status.Visibility = Visibility.Collapsed;
        EditorHost.Content = _editor.View;
        OptionsHost.Content = _editor.Options;
        SaveButton.Content = _editor.SaveLabel;
        SaveButton.IsEnabled = _editor.CanSave;
        _editor.CanSaveChanged += () => SaveButton.IsEnabled = _editor.CanSave;
        Log.Info($"Opened '{_path}' in {_action.Label} ({image.Width}x{image.Height}, {image.Frames.Count} frame(s))");
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
            MessageBox.Show(this, ex.Message, "ShiftDrop", MessageBoxButton.OK, MessageBoxImage.Error);
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
