using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Structured views for <c>--call=targets</c> (buildable stages with descriptions) and <c>--call=outline</c>
/// (build arguments, secrets and SSH ids a target uses). Each list has its own search.
/// </summary>
public static class BuildCallResultView
{
    public static FrameworkElement Targets(IReadOnlyList<BuildKitTarget> targets, Action<string> useTarget)
    {
        var search = new SearchFilter("Search targets", 280);
        var list = new StackPanel();
        void Render()
        {
            list.Children.Clear();
            foreach (var t in targets.Where(t => search.Matches(t.Name + " " + t.Description + " " + t.Base + " " + t.Platform)))
            {
                var head = Ui.HStack(8, Ui.Mono(t.Name, 14, "OnSurface"), t.IsDefault ? Ui.Badge("default", Tone.Primary, true) : null, t.Line is { } line ? Ui.Caption(Loc.T("Line") + " " + line) : null);
                var details = Ui.Wrap(6, t.Base.Length > 0 ? Ui.Badge("FROM " + t.Base) : null, t.Platform.Length > 0 ? Ui.Badge(t.Platform) : null);
                var use = Ui.Button("Use as target", Icons.CenterFocusStrong, ButtonKind.Text, () => useTarget(t.Name));
                var body = new DockPanel();
                body.Children.Add(use.Dock(System.Windows.Controls.Dock.Right));
                body.Children.Add(Ui.VStack(4, head, t.Description.Length > 0 ? Ui.Text(t.Description, 13, null, "OnSurfaceVariant", true, false) : Ui.Caption("No description comment in the Dockerfile."), details));
                Ui.Add(list, Ui.Card(body, 12, "SurfaceHigh", 12), 8);
            }
            if (list.Children.Count == 0) list.Children.Add(Ui.Caption(targets.Count == 0 ? "buildx reported no targets." : "No targets match the search."));
        }
        search.Changed += Render;
        Render();
        return Wrap(Icons.AccountTree, $"{targets.Count} {Loc.T(targets.Count == 1 ? "target" : "targets")}", search, list);
    }

    public static FrameworkElement Outline(BuildKitOutline outline)
    {
        var search = new SearchFilter("Search arguments, secrets and SSH", 280);
        var list = new StackPanel();
        void Render()
        {
            list.Children.Clear();
            var args = outline.Args.Where(a => search.Matches(a.Name + " " + a.Value + " " + a.Description)).ToList();
            Ui.Add(list, Ui.Overline($"{Loc.T("Build arguments")} ({args.Count})"), 0);
            foreach (var a in args)
                Ui.Add(list, Ui.Card(Ui.VStack(2, Ui.Columns(12, [1, 0], Ui.Mono(a.Name, 13), Ui.Mono(a.Value.Length > 0 ? "= " + a.Value : Loc.T("no default"), 12, "OnSurfaceVariant")),
                    a.Description.Length > 0 ? Ui.Text(a.Description, 12, null, "OnSurfaceVariant", true, false) : null), 10, "SurfaceHigh", 10), 6);
            var secrets = outline.Secrets.Where(s => search.Matches(s.Name)).ToList();
            var ssh = outline.Ssh.Where(s => search.Matches(s.Name)).ToList();
            Ui.Add(list, Ui.Overline($"{Loc.T("Secrets")} ({secrets.Count}) · SSH ({ssh.Count})"), 14);
            var chips = new WrapPanel();
            foreach (var s in secrets) chips.Children.Add(Ui.Badge(Loc.T("secret") + " " + s.Name + (s.Required ? " · " + Loc.T("required") : ""), s.Required ? Tone.Warning : Tone.Neutral).Margin(0, 0, 6, 6));
            foreach (var s in ssh) chips.Children.Add(Ui.Badge("ssh " + s.Name + (s.Required ? " · " + Loc.T("required") : ""), s.Required ? Tone.Warning : Tone.Neutral).Margin(0, 0, 6, 6));
            if (chips.Children.Count == 0) chips.Children.Add(Ui.Caption("None used by this target."));
            Ui.Add(list, chips, 6);
        }
        search.Changed += Render;
        Render();
        var title = outline.Name.Length > 0 ? Loc.T("Outline of") + " " + outline.Name : Loc.T("Outline");
        return Wrap(Icons.Schema, title + (outline.Description.Length > 0 ? " · " + outline.Description : ""), search, list);
    }

    /// <summary>Shown when the call ended but its output could not be parsed as JSON.</summary>
    public static FrameworkElement Unparsed(string what) =>
        Ui.Card(Ui.Columns(10, [0, 1], Ui.Icon(Icons.WarningAmber, 20, true, "OnWarningContainer"), Ui.Text(Loc.T("The") + " " + what + " " + Loc.T("output was not JSON this application understands. The raw log below shows it as printed."), 13, null, "OnWarningContainer", true, false)), 12, "WarningContainer", 12);

    private static FrameworkElement Wrap(string icon, string title, SearchFilter search, UIElement list)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(search.Dock(System.Windows.Controls.Dock.Right));
        head.Children.Add(Ui.HStack(10, Ui.Icon(icon, 22, true, "Primary"), Ui.Text(title, 16, FontWeights.SemiBold, "OnSurface", true, false)));
        return Ui.Card(Ui.VStack(0, head, list), 16, "SurfaceContainer", 16);
    }
}
