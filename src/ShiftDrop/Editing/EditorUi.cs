using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ShiftDrop.Editing;

/// <summary>Small building blocks for the editors' option bars, styled by EditorWindow's resources.</summary>
internal static class EditorUi
{
    public static readonly Brush Accent = Frozen(Color.FromRgb(0xFF, 0x5F, 0x3A));

    public static RadioButton Chip(string text, string group, bool isChecked, Action onChecked)
    {
        var chip = new RadioButton { Content = text, GroupName = group, IsChecked = isChecked };
        chip.SetResourceReference(FrameworkElement.StyleProperty, "Chip");
        chip.Checked += (_, _) => onChecked();
        return chip;
    }

    public static Button Button(string text, Action onClick)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(12, 5, 12, 5) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ActionButton");
        button.Click += (_, _) => onClick();
        return button;
    }

    public static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Frozen(Color.FromRgb(0x66, 0x66, 0x66)),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
    };

    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (UIElement child in children)
            row.Children.Add(child);
        return row;
    }

    /// <summary>A draggable handle drawn as a plain rectangle.</summary>
    public static Thumb Handle(Cursor cursor, Brush fill, Brush? stroke = null)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, fill);
        if (stroke is not null)
        {
            border.SetValue(Border.BorderBrushProperty, stroke);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        }

        return new Thumb { Cursor = cursor, Template = new ControlTemplate(typeof(Thumb)) { VisualTree = border } };
    }

    public static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
