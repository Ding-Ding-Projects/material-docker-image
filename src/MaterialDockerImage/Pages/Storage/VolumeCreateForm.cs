using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Create-volume dialog: name, driver, driver options and labels, with a live command preview.</summary>
public static class VolumeCreateForm
{
    private static readonly Regex NamePattern = new("^[A-Za-z0-9][A-Za-z0-9_.-]*$");

    public static void Show(Func<string?> context, Func<bool> isCurrent, Func<Task> reload)
    {
        var host = AppHost.Current;
        var name = Ui.Input("", "Name", true);
        var driver = Ui.Combo(["local"], "local", editable: true);
        var options = new KeyValueRows("Option", "Value");
        var labels = new KeyValueRows("Label", "Value");
        var preview = Ui.Mono("", 12, "OnSurfaceVariant", true);
        var error = Ui.Text("", 13, null, "Error");
        List<string>? Args(out string? problem)
        {
            problem = null;
            var n = name.Text.Trim();
            if (n.Length > 0 && !NamePattern.IsMatch(n)) { problem = "Names start with a letter or digit and use letters, digits, '_', '.' or '-'. Leave empty for an anonymous volume."; return null; }
            var d = driver.Text.Trim();
            if (d.Length == 0 || d.StartsWith('-') || d.Any(char.IsWhiteSpace)) { problem = "Choose a driver such as local."; return null; }
            var (opts, e1) = options.TryValues(); if (opts == null) { problem = e1; return null; }
            var (labs, e2) = labels.TryValues(); if (labs == null) { problem = e2; return null; }
            var args = new List<string> { "volume", "create", "--driver", d };
            foreach (var o in opts) args.AddRange(["--opt", o.Key + "=" + o.Value]);
            foreach (var l in labs) args.AddRange(["--label", l.Key + "=" + l.Value]);
            if (n.Length > 0) args.Add(n);
            return args;
        }
        void Update() { var a = Args(out var p); error.Text = p ?? ""; preview.Text = a == null ? "" : "docker " + string.Join(' ', a.Select(Quote)); }
        name.TextChanged += (_, _) => Update(); driver.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Update()));
        options.Changed += Update; labels.Changed += Update;
        Action? close = null;
        var create = Ui.AsyncButton("Create", Icons.Add, ButtonKind.Filled, async () =>
        {
            var args = Args(out var problem);
            if (args == null) { error.Text = problem ?? ""; return; }
            if (!isCurrent()) { host.Notify.Error("The engine context changed. Refresh Storage before creating a volume."); return; }
            var result = await host.Execute("Create volume " + (name.Text.Trim().Length > 0 ? name.Text.Trim() : "(anonymous)"), args, boundContext: context());
            if (result?.Success == true) { close?.Invoke(); await reload(); }
        });
        var body = Ui.VStack(14,
            Ui.Title("Create volume", 20),
            Ui.Field("Name", name, "A unique name on this engine. Leave empty and the engine generates an anonymous 64-character name.", "pgdata"),
            Ui.Field("Driver", driver, "The volume plugin that stores the data. local stores it on the engine host; other drivers come from installed plugins.", "local"),
            Ui.Field("Driver options", options, "Passed to the driver as --opt key=value. For local, type/o/device mount an existing filesystem such as NFS or tmpfs.", "type=tmpfs · device=tmpfs · o=size=100m"),
            Ui.Field("Labels", labels, "Metadata stored on the volume for filtering and tooling. Not secret: labels are visible to anyone with engine access.", "com.example.owner=web-team"),
            Ui.Card(Ui.VStack(4, Ui.Overline("Command"), preview), 12, "SurfaceHigh", 12),
            error,
            Ui.Columns(8, [1, 0, 0], new Border(), Ui.Button("Cancel", null, ButtonKind.Text, () => close?.Invoke()), create));
        close = host.Overlay.Show(Ui.Scroll(body), 560);
        Update();
    }

    public static string Quote(string arg) => arg.Length == 0 || arg.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'') ? "\"" + arg.Replace("\"", "\\\"") + "\"" : arg;
}
