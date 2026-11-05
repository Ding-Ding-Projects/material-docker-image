using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MaterialDockerImage.UI;

/// <summary>
/// Underline tab style from the design: transparent tabs with a 3 px primary line under the selected one and a bold
/// label. Used for section tabs that sit on a 1 px divider (Swarm and the Minecraft server detail).
/// </summary>
public static class UnderlineTabs
{
    private static Style? style;

    public static Style Style => style ??= Build();

    /// <summary>A tab strip host: the tabs on a 1 px divider.</summary>
    public static Border Strip(Panel tabs)
    {
        var host = new Border { Child = tabs, BorderThickness = new Thickness(0, 0, 0, 1) };
        host.SetResourceReference(Border.BorderBrushProperty, "SurfaceHighest");
        return host;
    }

    private static Style Build()
    {
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "bg" };
        border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        border.SetValue(Border.BorderBrushProperty, System.Windows.Media.Brushes.Transparent);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 3));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8, 8, 0, 0));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("HoverLayer"), "bg"));
        var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Primary"), "bg"));
        checkedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("OnSurface")));
        checkedTrigger.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("PressLayer"), "bg"));
        template.Triggers.Add(hover); template.Triggers.Add(checkedTrigger); template.Triggers.Add(focus);
        var s = new Style(typeof(RadioButton));
        s.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("OnSurfaceVariant")));
        s.Setters.Add(new Setter(FrameworkElement.HeightProperty, 44.0));
        s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        s.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        s.Setters.Add(new Setter(Control.TemplateProperty, template));
        s.Seal();
        return s;
    }
}
