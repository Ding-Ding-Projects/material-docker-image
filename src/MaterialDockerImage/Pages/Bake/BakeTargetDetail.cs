using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// The target inspector: a header with the target name and summary, pill tabs (Resolved definition, HCL source, Build run)
/// and a copy button, the active view, and an optional notice row such as the registry push warning.
/// </summary>
public sealed class BakeTargetDetail : Grid
{
    public enum View { Resolved, Hcl, Run }

    private readonly TextBlock title = Ui.Text("Target definition", 15, FontWeights.SemiBold, "OnSurface", false, true);
    private readonly TextBlock subtitle = Ui.Text("", 12, null, "OnSurfaceVariant", false, false);
    // One search surface serves both the resolved definition and the HCL source; the active view decides its text.
    private readonly ReadOnlySearchText viewer = new("Search definition", 160);
    private readonly FrameworkElement empty;
    private readonly Border viewerBox;
    private readonly FrameworkElement timeline;
    private readonly ToggleButton[] chips;
    private readonly Button copy;
    private readonly Border notice = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(16, 0, 16, 16) };
    private readonly TextBlock noticeText = Ui.Text("", 13, null, "OnSurfaceVariant", true, false);
    private View view = View.Resolved;
    private BakeTarget? target;
    private bool syncing;

    /// <summary>Returns the HCL block of a target, or an empty string when no HCL file declares it.</summary>
    public Func<BakeTarget, string>? HclSource { get; set; }
    public event Action? LoadLocallyRequested;
    public View Current => view;

    public BakeTargetDetail(FrameworkElement runView)
    {
        timeline = runView;
        timeline.Margin = new Thickness(12);
        empty = Ui.EmptyState(Icons.FrameInspect, "No target selected", "Click a target card to see its resolved definition: context, Dockerfile, tags, platforms, arguments and outputs as bake resolved them.");
        empty.Margin = new Thickness(0, 32, 0, 32);
        viewerBox = Inset(viewer);

        title.TextTrimming = TextTrimming.CharacterEllipsis;
        subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        var left = Ui.HStack(10, Ui.Icon(Icons.FrameInspect, 20, false, "Primary"), Ui.VStack(2, title, subtitle));
        left.Children[0].SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var labels = new[] { "Resolved", "HCL source", "Build run" };
        chips = new ToggleButton[labels.Length];
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < labels.Length; i++)
        {
            var index = i;
            chips[i] = Ui.Chip(labels[i], i == 0, on =>
            {
                if (syncing) return;
                if (on) ShowView((View)index);
                else Sync();
            });
            chips[i].Margin = new Thickness(0, 0, 4, 0);
            tabs.Children.Add(chips[i]);
        }
        copy = Ui.IconButton(Icons.ContentCopy, "Copy definition", () =>
        {
            var text = viewer.Text;
            if (text.Length > 0) { ActionSounds.Copy(text); AppHost.Current?.Notify.Info("Copied the definition."); }
        });
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(tabs);
        right.Children.Add(copy);
        var header = new HeaderPanel(left, right);
        header.Margin = new Thickness(16, 12, 12, 12);

        var body = new Grid();
        body.Children.Add(empty);
        body.Children.Add(viewerBox);
        body.Children.Add(timeline);

        var load = Ui.Button("Load locally instead", null, ButtonKind.Outlined, () => LoadLocallyRequested?.Invoke());
        load.Margin = new Thickness(12, 0, 0, 0);
        var noticeRow = new DockPanel();
        noticeRow.Children.Add(Ui.Icon(Icons.Warning, 18, false, "Warning").Margin(0, 0, 10, 0).Dock(Dock.Left));
        noticeRow.Children.Add(load.Dock(Dock.Right));
        noticeRow.Children.Add(noticeText);
        notice.Child = noticeRow;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Children.Add(header);
        Children.Add(Ui.Divider().At(1));
        Children.Add(body.At(2));
        Children.Add(notice.At(3).Margin(0, 12, 0, 0));
        Refresh();
    }

    private static Border Inset(FrameworkElement child)
    {
        var box = new Border { Child = child, CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(12) };
        box.SetResourceReference(Border.BackgroundProperty, "Surface");
        return box;
    }

    public void Show(BakeTarget? value)
    {
        target = value;
        Refresh();
    }

    public void ShowView(View value)
    {
        view = value;
        Sync();
        Refresh();
    }

    /// <summary>Shows a one-line notice with a "Load locally instead" action, or hides the row when <paramref name="text"/> is null.</summary>
    public void SetNotice(string? text)
    {
        noticeText.Text = text ?? "";
        notice.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Sync()
    {
        syncing = true;
        for (var i = 0; i < chips.Length; i++) chips[i].IsChecked = i == (int)view;
        syncing = false;
    }

    private void Refresh()
    {
        var hasTarget = target != null;
        empty.Visibility = view != View.Run && !hasTarget ? Visibility.Visible : Visibility.Collapsed;
        viewerBox.Visibility = view != View.Run && hasTarget ? Visibility.Visible : Visibility.Collapsed;
        timeline.Visibility = view == View.Run ? Visibility.Visible : Visibility.Collapsed;
        copy.Visibility = view == View.Run || !hasTarget ? Visibility.Collapsed : Visibility.Visible;
        if (view == View.Run)
        {
            title.Text = Loc.T("Build run");
            subtitle.Text = Loc.T("Each BuildKit step with its status, cache use and duration");
            return;
        }
        if (target == null)
        {
            title.Text = Loc.T("Target definition");
            subtitle.Text = Loc.T("Click a target card to inspect it");
            viewer.Text = "";
            return;
        }
        title.Text = $"{Loc.T("Target")} {target.Name} · {Loc.T(view == View.Hcl ? "HCL source" : "resolved definition")}";
        var parts = new List<string>();
        if (target.Context.Length > 0) parts.Add(target.Context);
        parts.Add(target.Dockerfile.Length > 0 ? target.Dockerfile : "Dockerfile");
        if (target.Stage.Length > 0) parts.Add(Loc.T("stage") + " " + target.Stage);
        if (target.Platforms.Count > 0) parts.Add(string.Join(", ", target.Platforms));
        parts.Add($"{target.Tags.Count} {Loc.T(target.Tags.Count == 1 ? "tag" : "tags")}");
        if (target.Args.Count > 0) parts.Add($"{target.Args.Count} {Loc.T(target.Args.Count == 1 ? "arg" : "args")}");
        subtitle.Text = string.Join(" · ", parts);
        subtitle.ToolTip = subtitle.Text;
        if (view == View.Resolved) viewer.Text = target.Json;
        else
        {
            var source = HclSource?.Invoke(target) ?? "";
            viewer.Text = source.Length > 0 ? source : Loc.T("No HCL target block named") + " " + target.Name + " " + Loc.T("was found in the HCL files bake reads. The target may come from a JSON or Compose file or a matrix, so only the resolved definition is available.");
        }
    }
}
