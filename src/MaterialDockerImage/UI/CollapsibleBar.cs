using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.UI;

/// <summary>
/// A header with a chevron that shows or hides a filter chip bar or a statistics strip. The collapsed state is stored per surface in
/// <see cref="UserSettings.CollapsedFilters"/> (rules in <see cref="CollapseFilterState"/>) and survives restart.
/// </summary>
public static class CollapsibleBar
{
    /// <summary>Wraps the content in a titled, collapsible section.</summary>
    public static FrameworkElement Wrap(string key, string title, FrameworkElement content, Func<string>? summary = null)
    {
        var host = AppHost.Current;
        var chevron = Ui.IconButton(Icons.ExpandLess, title);
        chevron.Width = chevron.Height = chevron.MinHeight = 28; chevron.FontSize = 18;
        var label = Ui.Text(title, 13, System.Windows.FontWeights.SemiBold, "OnSurfaceVariant", false);
        var hint = Ui.Caption("");
        void Apply()
        {
            var collapsed = CollapseFilterState.IsCollapsed(host.Settings.CollapsedFilters, key);
            content.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            chevron.Content = collapsed ? Icons.ExpandMore : Icons.ExpandLess;
            var name = Loc.T(collapsed ? "Show " : "Hide ") + Loc.T(title);
            chevron.ToolTip = name;
            System.Windows.Automation.AutomationProperties.SetName(chevron, name);
            System.Windows.Automation.AutomationProperties.SetHelpText(chevron, Loc.T(collapsed ? "Collapsed. Select to expand." : "Expanded. Select to collapse."));
            hint.Text = collapsed && summary != null ? Loc.T(summary()) : "";
        }
        chevron.Click += (_, _) =>
        {
            var collapsed = !CollapseFilterState.IsCollapsed(host.Settings.CollapsedFilters, key);
            if (CollapseFilterState.Set(host.Settings.CollapsedFilters, key, collapsed)) host.SaveSettings(null);
            Apply();
        };
        var header = Ui.HStack(6, chevron, label, hint);
        header.Margin = new Thickness(0, 0, 0, 4);
        Apply();
        return Ui.VStack(0, header, content);
    }
}
