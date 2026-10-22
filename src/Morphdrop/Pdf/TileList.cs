using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Morphdrop.Editing;

namespace Morphdrop.Pdf;

/// <summary>
/// Tiles in a panel that can be put in another order by dragging one onto another: dropped on the first half of a
/// tile it goes before it, on the second half after it. Files dragged in from Explorer are left to the window.
/// </summary>
internal sealed class TileList<T> where T : class
{
    private const string DragFormat = "Morphdrop.Tile";

    private readonly List<(T Item, Border Tile)> _tiles = new();
    private readonly Func<T, UIElement> _content;
    private readonly bool _horizontal;
    private Point? _pressed;
    private T? _pressedItem;

    public TileList(Panel panel, Func<T, UIElement> content)
    {
        Panel = panel;
        _content = content;
        _horizontal = panel is WrapPanel;
    }

    public event Action? Changed;

    public event Action<T>? Clicked;

    public Panel Panel { get; }

    public IReadOnlyList<T> Items => _tiles.Select(tile => tile.Item).ToList();

    public int Count => _tiles.Count;

    public void Add(T item)
    {
        var tile = new Border
        {
            Child = _content(item),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Margin = new Thickness(4),
            AllowDrop = true,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        tile.BorderBrush = Brushes.Transparent;
        tile.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pressed = e.GetPosition(Panel);
            _pressedItem = item;
        };
        tile.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_pressedItem == item && !IsInButton(e.OriginalSource))
                Clicked?.Invoke(item);
            _pressedItem = null;
        };
        tile.PreviewMouseMove += (_, e) => StartDrag(item, tile, e);
        tile.DragOver += (_, e) => Hover(tile, e);
        tile.DragLeave += (_, _) => tile.BorderBrush = Brushes.Transparent;
        tile.Drop += (_, e) => DropOn(item, tile, e);

        _tiles.Add((item, tile));
        Panel.Children.Add(tile);
        Changed?.Invoke();
    }

    public void Remove(T item)
    {
        int index = _tiles.FindIndex(tile => tile.Item == item);
        if (index < 0)
            return;

        Panel.Children.Remove(_tiles[index].Tile);
        _tiles.RemoveAt(index);
        Changed?.Invoke();
    }

    /// <summary>Redraws one tile's content (after a turn, a new thumbnail…).</summary>
    public void Refresh(T item)
    {
        foreach ((T existing, Border tile) in _tiles)
        {
            if (existing == item)
                tile.Child = _content(item);
        }
    }

    /// <summary>Marks one tile as selected (an accent border), or none.</summary>
    public void Select(T? item)
    {
        foreach ((T existing, Border tile) in _tiles)
        {
            if (existing == item)
                tile.SetResourceReference(Border.BackgroundProperty, "Theme.Chip");
            else
                tile.Background = Brushes.Transparent;
        }
    }

    private void StartDrag(T item, Border tile, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressed is not Point start || _pressedItem != item)
            return;

        Vector moved = e.GetPosition(Panel) - start;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance * 2 && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance * 2)
            return;

        _pressed = null;
        _pressedItem = null;
        tile.Opacity = 0.4;
        try
        {
            DragDrop.DoDragDrop(tile, new DataObject(DragFormat, item), DragDropEffects.Move);
        }
        finally
        {
            tile.Opacity = 1;
        }
    }

    private void Hover(Border tile, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DragFormat))
            return;

        e.Effects = DragDropEffects.Move;
        tile.BorderBrush = EditorUi.Accent;
        e.Handled = true;
    }

    private void DropOn(T target, Border tile, DragEventArgs e)
    {
        tile.BorderBrush = Brushes.Transparent;
        if (e.Data.GetData(DragFormat) is not T dragged || dragged == target)
            return;

        e.Handled = true;
        Point at = e.GetPosition(tile);
        bool after = _horizontal ? at.X > tile.ActualWidth / 2 : at.Y > tile.ActualHeight / 2;
        int from = _tiles.FindIndex(t => t.Item == dragged);
        (T Item, Border Tile) moving = _tiles[from];
        _tiles.RemoveAt(from);
        int to = _tiles.FindIndex(t => t.Item == target) + (after ? 1 : 0);
        _tiles.Insert(to, moving);
        Panel.Children.Remove(moving.Tile);
        Panel.Children.Insert(to, moving.Tile);
        Changed?.Invoke();
    }

    private static bool IsInButton(object source)
    {
        for (DependencyObject? node = source as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is Button)
                return true;
            if (node is Border { AllowDrop: true })
                return false;
        }

        return false;
    }
}
