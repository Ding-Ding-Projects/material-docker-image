using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>Small helpers shared by the Images and Containers destinations.</summary>
internal static class PageKit
{
    private static AppHost Host => AppHost.Current;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Quotes a token for display only. Commands are always executed as an argument list, never through a shell.</summary>
    public static string Quote(string token) =>
        token.Length > 0 && token.All(c => char.IsLetterOrDigit(c) || "-_./:=@,+%[]\\".Contains(c)) ? token : "\"" + token.Replace("\"", "\\\"") + "\"";

    /// <summary>Renders the exact argument list as a display string. Environment values are masked as NAME=•••.</summary>
    public static string Preview(IReadOnlyList<string> args, string? context)
    {
        var parts = new List<string> { "docker" };
        if (!string.IsNullOrWhiteSpace(context)) { parts.Add("--context"); parts.Add(Quote(context)); }
        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (i > 0 && args[i - 1] is "--env" or "-e")
            {
                var eq = token.IndexOf('=');
                parts.Add(Quote(eq < 0 ? token : token[..eq]) + "=•••");
                continue;
            }
            parts.Add(Quote(token));
        }
        return string.Join(" ", parts);
    }

    /// <summary>Pretty-prints inspect JSON. When masking, every "Env" array entry becomes NAME=•••. Returns null when the JSON cannot be parsed.</summary>
    public static string? FormatInspect(string json, bool maskEnvironment)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (maskEnvironment) MaskEnv(node);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "";
        }
        catch (JsonException) { return null; }
    }

    private static void MaskEnv(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToList())
            {
                if (key == "Env" && obj[key] is JsonArray env)
                {
                    for (var i = 0; i < env.Count; i++)
                    {
                        if (env[i] is JsonValue value && value.TryGetValue<string>(out var text))
                        {
                            var eq = text.IndexOf('=');
                            env[i] = (eq < 0 ? text : text[..eq]) + "=•••";
                        }
                    }
                }
                else
                {
                    MaskEnv(obj[key]);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array) MaskEnv(child);
        }
    }

    /// <summary>
    /// Splits a command line into argv without any shell: whitespace separates arguments, single or double quotes group,
    /// and a backslash outside quotes (or before a quote or backslash inside double quotes) escapes the next character.
    /// Pipes, redirections and variables are passed through literally.
    /// </summary>
    public static string[] SplitCommand(string text)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inToken = false;
        var quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote) { quote = '\0'; continue; }
                if (c == '\\' && quote == '"' && i + 1 < text.Length && text[i + 1] is '"' or '\\') { current.Append(text[++i]); continue; }
                current.Append(c);
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                if (inToken) { args.Add(current.ToString()); current.Clear(); inToken = false; }
                continue;
            }
            inToken = true;
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == '\\' && i + 1 < text.Length) { current.Append(text[++i]); continue; }
            current.Append(c);
        }
        if (quote != '\0') throw new ArgumentException("A quotation mark is not closed.");
        if (inToken) args.Add(current.ToString());
        foreach (var a in args) StructuredInputs.Token(a);
        if (args.Count > 256) throw new ArgumentException("At most 256 command arguments are supported.");
        return args.ToArray();
    }

    public static void Copy(string text, string what)
    {
        try { Clipboard.SetText(text); Host.Notify.Info($"Copied {what}."); }
        catch (System.Runtime.InteropServices.ExternalException e) { Host.Notify.Error(Loc.T("The clipboard is busy:") + " " + e.Message); }
    }

    /// <summary>Asks for a destination file (the dialog confirms overwriting) and writes UTF-8 text without a byte order mark.</summary>
    public static void SaveText(string fileName, string filter, string content, string what)
    {
        var dialog = new SaveFileDialog { FileName = fileName, Filter = filter, OverwritePrompt = true };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, content, new UTF8Encoding(false));
        Host.Notify.Success($"Saved {what} to {dialog.FileName}.");
    }

    /// <summary>Returns a path chosen in a save dialog, or null.</summary>
    public static string? PickSavePath(string fileName, string filter)
    {
        var dialog = new SaveFileDialog { FileName = fileName, Filter = filter, OverwritePrompt = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public static string? PickOpenPath(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static readonly Regex Reference = new(@"^(?:(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?)(?:\.(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?))*(?::[0-9]+)?/)?[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*)*(?::[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})?(?:@sha256:[a-f0-9]{64})?$", RegexOptions.None, TimeSpan.FromMilliseconds(100));

    /// <summary>Returns null when the text is a valid image reference (registry/repository[:tag][@digest]); otherwise the problem.</summary>
    public static string? ValidateReference(string value, bool allowDigest = true, bool allowTag = true)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Enter an image reference.";
        if (value.StartsWith('-')) return "Image references cannot begin with a hyphen.";
        if (value.Length > 512) return "Image references are limited to 512 characters.";
        if (!Reference.IsMatch(value)) return "Use registry/repository:tag with lowercase repository names, for example ghcr.io/acme/api:1.4.0 or nginx:1.27.";
        if (!allowDigest && value.Contains('@')) return "A digest cannot be used here.";
        if (!allowTag && HasTag(value)) return "Remove the tag: this option pulls every tag of a repository.";
        return null;
    }

    public static bool HasTag(string reference)
    {
        var withoutDigest = reference.Split('@')[0];
        var slash = withoutDigest.LastIndexOf('/');
        return withoutDigest.IndexOf(':', slash + 1) >= 0;
    }

    /// <summary>
    /// Shows a small form in an overlay. The apply callback returns null to close or a message to show inline. The confirm
    /// button stays disabled while it runs.
    /// </summary>
    public static Action Dialog(string title, string? subtitle, UIElement content, string confirmLabel, Func<Task<string?>> apply, bool danger = false, double width = 560)
    {
        Action? close = null;
        var error = Ui.Text("", 13, null, "Error", true, false);
        var ok = Ui.AsyncButton(confirmLabel, null, danger ? ButtonKind.Danger : ButtonKind.Filled, async () =>
        {
            error.Text = "";
            var problem = await apply();
            if (problem == null) close!(); else error.Text = problem;
        });
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => close!()), ok);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.VStack(14, Ui.Title(title, 20), subtitle == null ? null : Ui.Caption(subtitle), content, error, actions);
        close = Host.Overlay.Show(body, width);
        return close;
    }

    public static string SafeFileName(string text) => Regex.Replace(text, @"[^A-Za-z0-9_.-]+", "_", RegexOptions.None, RegexTimeout).Trim('_') is { Length: > 0 } s ? s : "export";

    /// <summary>Message used when a list was loaded from an engine context that is no longer selected.</summary>
    public static string StaleMessage(string? context) =>
        Loc.T("This list was loaded from engine context {0}, which is no longer the selected context. Refresh before acting.").Replace("{0}", context ?? "(default)");

    /// <summary>Short summary of a failed command for inline display.</summary>
    public static string Failure(CommandResult? result)
    {
        if (result == null) return Loc.T("The operation did not start (another operation may be active).");
        if (result.Cancelled) return Loc.T("The operation was canceled. Refresh to see the actual state.");
        var detail = result.StandardError.Trim();
        if (detail.Length > 600) detail = "…" + detail[^600..];
        return Loc.T("docker exited with code {0}.").Replace("{0}", result.ExitCode.ToString()) + (detail.Length > 0 ? " " + detail : "");
    }

    /// <summary>Shows read-only text in a wide overlay with search, copy and save.</summary>
    public static void ShowText(string title, string subtitle, string text, string fileName, FrameworkElement? extra = null, Action<SearchableText>? configure = null)
    {
        var view = new SearchableText(title, fileName) { Height = 520 };
        view.SetText(text);
        if (extra != null) view.Extra.Children.Add(extra);
        configure?.Invoke(view);
        Action? close = null;
        var closeButton = Ui.IconButton(Icons.Close, "Close", () => close!());
        var head = Ui.Columns(8, [1, 0], Ui.VStack(2, Ui.Title(Loc.Lead(title), 20), Ui.Caption(subtitle)), closeButton);
        var body = Ui.VStack(12, head, view);
        close = Host.Overlay.Show(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false }, 900);
    }

    public static string Field(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return "";
        }
        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? "",
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => current.ToString()
        };
    }

    public static JsonElement? Child(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return null;
        }
        return current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : current;
    }

    public static IEnumerable<string> Strings(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString()) : [];

    /// <summary>Parses the first element of a docker inspect array.</summary>
    public static JsonElement? FirstInspect(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) return root[0].Clone();
            if (root.ValueKind == JsonValueKind.Object) return root.Clone();
            return null;
        }
        catch (JsonException) { return null; }
    }

    public static string FormatTime(string value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var t) && t.Year > 1
            ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : value;

    /// <summary>A two-column label/value line for inspector cards.</summary>
    public static Grid Fact(string label, string value, bool mono = false)
    {
        var v = mono ? Ui.Mono(value.Length == 0 ? "—" : value, 12.5, "OnSurface", true) : Ui.Text(value.Length == 0 ? "—" : value, 13, null, "OnSurface", true, false);
        return Ui.Columns(12, [-120, 1], Ui.Text(label, 13, null, "OnSurfaceVariant"), v);
    }
}

/// <summary>Read-only text with its own search (lines that match are shown), copy and save.</summary>
internal sealed class SearchableText : DockPanel
{
    private string[] lines = [];
    private readonly TextBox box = Ui.CodeBox("", 120);
    private readonly TextBlock count = Ui.Caption("");
    public SearchFilter Search { get; }
    public WrapPanel Extra { get; } = new() { VerticalAlignment = VerticalAlignment.Center };
    public string FileName { get; set; }
    /// <summary>Keeps the first line (a table header) visible while searching.</summary>
    public bool KeepFirstLine { get; set; }
    public string VisibleText => box.Text;

    public SearchableText(string name, string fileName, string placeholder = "Search")
    {
        FileName = fileName;
        LastChildFill = true;
        Search = new SearchFilter(placeholder, 240);
        box.IsReadOnly = true;
        box.AcceptsTab = false;
        System.Windows.Automation.AutomationProperties.SetName(box, Loc.T(name));
        var copy = Ui.IconButton(Icons.ContentCopy, "Copy shown text", () => PageKit.Copy(box.Text, "the shown text"));
        var save = Ui.IconButton(Icons.Save, "Save shown text as", () => PageKit.SaveText(FileName, "Text|*.txt;*.json;*.log|All files|*.*", box.Text, Search.IsActive ? "the matching lines" : "the text"));
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var e in new FrameworkElement[] { Search, count, Extra, copy, save }) { e.Margin = new Thickness(0, 0, 8, 4); e.VerticalAlignment = VerticalAlignment.Center; bar.Children.Add(e); }
        Children.Add(bar.Dock(System.Windows.Controls.Dock.Top));
        Children.Add(box);
        Search.Changed += Render;
    }

    public void SetText(string text)
    {
        lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        if (lines.Length == 1 && lines[0].Length == 0) lines = [];
        Render();
    }

    private void Render()
    {
        IEnumerable<string> shown = lines;
        var matches = 0;
        if (Search.IsActive)
        {
            var list = new List<string>();
            for (var i = 0; i < lines.Length; i++)
            {
                if (KeepFirstLine && i == 0) { list.Add(lines[i]); continue; }
                if (Search.Matches(lines[i])) { list.Add(lines[i]); matches++; }
            }
            shown = list;
        }
        box.Text = string.Join("\n", shown);
        count.Text = Search.Status.Length > 0 ? Search.Status : Search.IsActive ? $"{matches} of {lines.Length} lines match" : $"{lines.Length} lines";
    }
}
