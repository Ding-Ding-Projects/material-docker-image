using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Interactive pickers for the value at the caret: colour, port, duration, permissions, user, platform, local
/// image (with digest pinning), enum chips, path browsing and the build-secret rewrite. Pickers open at the caret,
/// take focus while open (Tab moves through them, Enter applies, Escape cancels) and return focus to the editor with
/// the caret restored. Values are replaced in place through the editor's selection, so Ctrl+Z undoes them.
/// </summary>
public static class EditorValuePickers
{
    private static AppHost Host => AppHost.Current;

    public static void Open(EditorSuggestions owner, EditorValueSpan span, string variant)
    {
        switch (span.Kind)
        {
            case EditorValueKind.Path: BrowsePath(owner, span, variant); return;
            case EditorValueKind.Secret: RewriteSecret(owner, span); return;
        }
        var picker = new EditorValuePickerPopup(owner, span);
        switch (span.Kind)
        {
            case EditorValueKind.Colour: Colour(picker, span); break;
            case EditorValueKind.Port: Port(picker, owner, span); break;
            case EditorValueKind.Duration: Duration(picker, span); break;
            case EditorValueKind.Chmod: Permissions(picker, span); break;
            case EditorValueKind.User: User(picker, owner, span); break;
            case EditorValueKind.Platform:
                var platforms = (owner.Language == EditorSuggestionLanguage.Dockerfile ? new[] { "$BUILDPLATFORM", "$TARGETPLATFORM" } : Array.Empty<string>()).Concat(DockerfileCompletionData.Platforms);
                Chips(picker, "Platform", platforms, span.Text, "Choose the operating system and architecture.");
                break;
            case EditorValueKind.Image: Image(picker, span); break;
            case EditorValueKind.Choice: Chips(picker, span.Label, span.Options, span.Text, "Choose one of the allowed values."); break;
            default: return;
        }
    }

    // ------------------------------------------------------------------ pickers without a popup

    private static void BrowsePath(EditorSuggestions owner, EditorValueSpan span, string variant)
    {
        var root = owner.ContextFolder();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            Host.Notify.Warning(span.ContextOnly ? "Choose a build context folder first: COPY and ADD paths are relative to it." : "Load or save the Compose file first, so paths can be relative to its folder.");
            return;
        }
        string chosen;
        var folder = variant == "folder" || span.FolderOnly;
        if (folder)
        {
            var dialog = new OpenFolderDialog { InitialDirectory = root, Title = Loc.T(span.ContextOnly ? "Choose a folder inside the build context" : "Choose a folder") };
            if (dialog.ShowDialog() != true) { owner.Box.Focus(); return; }
            chosen = dialog.FolderName;
        }
        else
        {
            var dialog = new OpenFileDialog { InitialDirectory = root, Title = Loc.T(span.ContextOnly ? "Choose a file inside the build context" : "Choose a file"), CheckFileExists = true };
            if (dialog.ShowDialog() != true) { owner.Box.Focus(); return; }
            chosen = dialog.FileName;
        }
        if (!EditorValueFormats.TryRelativePath(root, chosen, !span.ContextOnly, out var relative, out var error))
        {
            Host.Notify.Warning(error);
            owner.Box.Focus();
            return;
        }
        if (!span.Compose && relative.Contains(' '))
        {
            Host.Notify.Warning("This path contains a space. Shell-form COPY and ADD split on spaces; use the JSON form, for example COPY [\"" + relative + "\", \"/dest/\"].");
            owner.Box.Focus();
            return;
        }
        owner.Replace(span.Start, span.Length, relative, span.Start + relative.Length);
    }

    private static void RewriteSecret(EditorSuggestions owner, EditorValueSpan span)
    {
        var edit = EditorValueFormats.SecretRewrite(owner.Box.Text, span);
        owner.Replace(edit.Start, edit.Length, edit.Insert, edit.CaretAfter);
        var id = span.SecretName.ToLowerInvariant().Replace('_', '-');
        Host.Notify.Info($"{span.SecretName} is now read from a build secret and its value was removed from the Dockerfile. Build with --secret id={id},env={span.SecretName} and add the command after the RUN mount. Ctrl+Z undoes the rewrite.");
    }

    // ------------------------------------------------------------------ picker bodies

    private static void Colour(EditorValuePickerPopup picker, EditorValueSpan span)
    {
        EditorValueFormats.TryParseColour(span.Text, out var r, out var g, out var b);
        var colour = new AppearanceColorPicker(AppearanceRgba.FromBytes(r, g, b), false, Array.Empty<string>());
        picker.Show("Pick colour", colour, () => { var c = colour.Current.Quantized(); return EditorValueFormats.FormatColour(span.Text, c.R8, c.G8, c.B8); }, 380);
    }

    private static void Port(EditorValuePickerPopup picker, EditorSuggestions owner, EditorValueSpan span)
    {
        var spec = EditorValueFormats.ParsePort(span.Text) ?? new EditorPortSpec("", "", "", "tcp");
        var protocol = spec.Protocol == "udp" ? "udp" : "tcp";
        var container = Ui.Input(spec.Container, "Container port", true);
        var host = Ui.Input(spec.Host, "Host port", true);
        var ip = Ui.Input(spec.Ip, "Host IP", true);
        var warning = Ui.Text("", 12, null, "Warning");
        var preview = Ui.Mono("", 13, "OnSurface");
        EditorPortSpec Current() => new(span.Compose ? ip.Text.Trim() : "", span.Compose ? host.Text.Trim() : "", container.Text.Trim(), protocol);
        void Update()
        {
            var c = Current();
            var notes = new List<string>();
            if (c.Container.Length > 0 && !EditorValueFormats.IsPortOrRange(c.Container)) notes.Add(Loc.T("The container port must be 1 to 65535 (or a range such as 8000-8010)."));
            if (c.Host.Length > 0 && !EditorValueFormats.IsPortOrRange(c.Host)) notes.Add(Loc.T("The host port must be 1 to 65535."));
            var first = int.TryParse(c.Container.Split('-')[0], out var cp) ? cp : 0;
            if (!span.Compose && first is > 0 and < 1024) notes.Add(Loc.T("Ports below 1024 are privileged: a non-root process needs the NET_BIND_SERVICE capability to listen on them."));
            if (span.Compose && int.TryParse(c.Host.Split('-')[0], out var hp))
            {
                if (hp < 1024) notes.Add(Loc.T("Host ports below 1024 are privileged on Linux hosts."));
                if (EditorSuggestionsSources.IsPublished(hp, c.Protocol)) notes.Add(Loc.T("This host port is already published by a running container on") + " " + EditorSuggestionsSources.ImagesContext + ".");
            }
            warning.Text = string.Join(" ", notes);
            preview.Text = span.Compose ? c.ToCompose() : c.ToDockerfile();
        }
        foreach (var box in new[] { container, host, ip }) box.TextChanged += (_, _) => Update();
        var protocolChoice = Ui.Segmented(["tcp", "udp"], protocol, v => { protocol = v; Update(); }, false);
        var body = Ui.VStack(10,
            Labelled("Container port", container),
            Labelled("Protocol", protocolChoice),
            span.Compose ? Labelled("Host port (optional)", host) : null,
            span.Compose ? Labelled("Host IP (optional, for example 127.0.0.1)", ip) : null,
            Ui.HStack(8, Ui.Caption("Result:"), preview),
            warning);
        Update();
        picker.Show(span.Compose ? "Published port" : "Exposed port", body, () =>
        {
            var c = Current();
            if (!EditorValueFormats.IsPortOrRange(c.Container) || (c.Host.Length > 0 && !EditorValueFormats.IsPortOrRange(c.Host))) { picker.Error("Enter a valid port from 1 to 65535."); return null; }
            if (!span.Compose) return c.ToDockerfile();
            var value = c.ToCompose();
            return IsQuoted(owner, span) ? value : "\"" + value + "\"";
        }, 320, container);
    }

    private static void Duration(EditorValuePickerPopup picker, EditorValueSpan span)
    {
        var m = System.Text.RegularExpressions.Regex.Match(span.Text.Trim('"', '\''), @"^(\d+)(ms|s|m|h)$");
        var parsed = EditorValueFormats.ParseDuration(span.Text);
        var number = Ui.Input(m.Success ? m.Groups[1].Value : parsed is { } p ? ((int)p.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture) : "30", "Duration amount", true);
        number.Width = 90;
        var unit = Ui.Combo(["ms", "s", "m", "h"], m.Success ? m.Groups[2].Value : "s");
        AutomationProperties.SetName(unit, Loc.T("Duration unit"));
        var presets = new WrapPanel();
        foreach (var preset in new[] { "5s", "10s", "30s", "1m" })
        {
            var b = Ui.Button(preset, null, ButtonKind.Tonal, () => picker.Commit(preset), false);
            b.Margin = new Thickness(0, 0, 8, 8);
            presets.Children.Add(b);
        }
        var body = Ui.VStack(10, Ui.Caption("Presets apply immediately."), presets, Labelled("Custom", Ui.HStack(8, number, unit)),
            Ui.Caption("Go duration format, as used by Docker and Compose: 500ms, 30s, 1m30s, 2h."));
        picker.Show("Pick duration", body, () =>
        {
            if (!int.TryParse(number.Text.Trim(), out var n) || n < 0) { picker.Error("Enter a whole number of zero or more."); return null; }
            return n.ToString(System.Globalization.CultureInfo.InvariantCulture) + (unit.SelectedItem as string ?? "s");
        }, 300, number);
    }

    private static void Permissions(EditorValuePickerPopup picker, EditorValueSpan span)
    {
        var bits = EditorValueFormats.ParsePermissions(span.Text) ?? EditorValueFormats.ParsePermissions("644")!;
        var symbolic = span.SymbolicAllowed && span.Text.Contains('=');
        var leadingZero = span.Text.Length == 4 && span.Text.StartsWith('0') || span.Label == "Mount mode";
        var preview = Ui.Mono("", 14, "OnSurface");
        void Update() => preview.Text = symbolic ? EditorValueFormats.ToSymbolic(bits) : EditorValueFormats.ToOctal(bits, leadingZero);
        var grid = new Grid();
        for (var c = 0; c < 4; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(80) : new GridLength(70) });
        for (var r = 0; r < 4; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        string[] who = ["Owner", "Group", "Others"], what = ["Read", "Write", "Execute"];
        for (var c = 0; c < 3; c++) grid.Children.Add(Ui.Overline(what[c]).At(0, c + 1));
        for (var r = 0; r < 3; r++)
        {
            grid.Children.Add(Ui.Text(who[r], 13).At(r + 1, 0));
            for (var c = 0; c < 3; c++)
            {
                var index = r * 3 + c;
                var check = new CheckBox { IsChecked = bits[index], VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(check, Loc.T(who[r]) + " " + Loc.T(what[c]).ToLowerInvariant());
                check.Checked += (_, _) => { bits[index] = true; Update(); };
                check.Unchecked += (_, _) => { bits[index] = false; Update(); };
                grid.Children.Add(check.At(r + 1, c + 1));
            }
        }
        var body = Ui.VStack(10, grid, Ui.HStack(8, Ui.Caption("Result:"), preview));
        if (span.SymbolicAllowed)
        {
            Ui.Add(body, Labelled("Format", Ui.Segmented(["Octal", "Symbolic"], symbolic ? "Symbolic" : "Octal", v => { symbolic = v == "Symbolic"; Update(); })), 10);
            Ui.Add(body, Ui.Caption("Symbolic permissions need docker/dockerfile 1.14 or newer."), 10);
        }
        Update();
        picker.Show("Permissions", body, () => preview.Text, 330);
    }

    private static void User(EditorValuePickerPopup picker, EditorSuggestions owner, EditorValueSpan span)
    {
        var numericOnly = span.Options.Contains("numeric");
        var chips = new WrapPanel();
        var choices = numericOnly
            ? new List<(string, string)> { ("0", "root"), ("1000", "First regular user in most images"), ("65532", "nonroot in distroless and Docker Hardened Images") }
            : span.KnownUsers.Where(u => u is not ("root" or "nobody")).Select(u => (u, "Created earlier in this file")).Concat(new[]
            {
                ("root", "Superuser (uid 0). Avoid for the final stage."), ("node", "The non-root user of the official node images (uid 1000)."),
                ("nobody", "Unprivileged user present in most Linux images."), ("65532:65532", "nonroot in distroless and Docker Hardened Images."),
            }).ToList();
        foreach (var (value, doc) in choices)
        {
            var b = Ui.Button(value, null, ButtonKind.Tonal, () => picker.Commit(Quote(owner, span, value)), false);
            b.ToolTip = Loc.T(doc);
            AutomationProperties.SetHelpText(b, Loc.T(doc));
            b.Margin = new Thickness(0, 0, 8, 8);
            chips.Children.Add(b);
        }
        var parts = span.Text.Trim('"', '\'').Split(':');
        var uid = Ui.Input(parts[0].All(char.IsDigit) ? parts[0] : "", numericOnly ? "ID" : "User ID", true);
        var gid = Ui.Input(parts.Length > 1 && parts[1].All(char.IsDigit) ? parts[1] : "", "Group ID", true);
        uid.Width = gid.Width = 100;
        var body = Ui.VStack(10, Ui.Caption("Common users apply immediately."), chips,
            Labelled(numericOnly ? "Numeric ID" : "Numeric user and group", numericOnly ? uid : Ui.HStack(8, uid, Ui.Text(":", 14), gid)));
        picker.Show(numericOnly ? "Pick ID" : "Pick user", body, () =>
        {
            var u = uid.Text.Trim(); var g = gid.Text.Trim();
            if (u.Length == 0 || !u.All(char.IsDigit) || (g.Length > 0 && !g.All(char.IsDigit))) { picker.Error("Enter numbers only, for example 10001 and 10001."); return null; }
            return Quote(owner, span, numericOnly || g.Length == 0 ? u : u + ":" + g);
        }, 340, uid);
    }

    private static void Chips(EditorValuePickerPopup picker, string title, IEnumerable<string> options, string currentValue, string caption)
    {
        var chips = new WrapPanel();
        foreach (var option in options)
        {
            var selected = option.Equals(currentValue.Trim('"', '\''), StringComparison.OrdinalIgnoreCase);
            var b = Ui.Button(option, selected ? Icons.Check : null, selected ? ButtonKind.Filled : ButtonKind.Tonal, () => picker.Commit(option), false);
            b.Margin = new Thickness(0, 0, 8, 8);
            chips.Children.Add(b);
        }
        picker.Show(title, Ui.VStack(8, Ui.Caption(caption), chips), null, 360);
    }

    private static void Image(EditorValuePickerPopup picker, EditorValueSpan span)
    {
        var search = new SearchFilter("Search local images", 300);
        var rows = new StackPanel();
        void Render()
        {
            rows.Children.Clear();
            var images = EditorSuggestionsSources.Images.Where(i => i.Repository is { Length: > 0 } and not "<none>").ToList();
            if (EditorSuggestionsSources.ImagesLoading && images.Count == 0) { rows.Children.Add(Ui.Caption("Reading local images…")); return; }
            if (EditorSuggestionsSources.ImageError.Length > 0 && images.Count == 0) { rows.Children.Add(Ui.Text(EditorSuggestionsSources.ImageError, 12, null, "Error")); return; }
            if (images.Count == 0) { rows.Children.Add(Ui.Caption(Loc.T("No tagged local images on") + " " + EditorSuggestionsSources.ImagesContext + ". " + Loc.T("Pull or build one first."))); return; }
            var shown = 0;
            foreach (var image in images.Where(i => search.Matches(i.Reference + " " + i.Digest + " " + i.Id)))
            {
                shown++;
                var info = Ui.VStack(2, Ui.Mono(image.Reference, 13, "OnSurface", true),
                    Ui.Text(image.Size + (image.HasDigest ? " · " + image.Digest[..Math.Min(image.Digest.Length, 19)] + "…" : " · " + Loc.T("no repository digest")), 12, null, "OnSurfaceVariant", false, false));
                var insert = Ui.Button("Insert", null, ButtonKind.Text, () => picker.Commit(image.Reference));
                AutomationProperties.SetName(insert, Loc.T("Insert") + " " + image.Reference);
                var pin = Ui.Button("Pin digest", Icons.Fingerprint, ButtonKind.Tonal, () => picker.Commit(image.Reference + "@" + image.Digest));
                pin.IsEnabled = image.HasDigest;
                pin.ToolTip = image.HasDigest ? Loc.T("Inserts") + " " + image.Reference + "@" + image.Digest : Loc.T("This image has no repository digest (it was built locally and never pushed or pulled).");
                AutomationProperties.SetName(pin, Loc.T("Pin digest for") + " " + image.Reference);
                rows.Children.Add(Ui.Columns(6, [1, 0, 0], info, insert, pin).Margin(0, 4, 0, 4));
            }
            if (shown == 0) rows.Children.Add(Ui.Caption("No local images match the search."));
        }
        search.Changed += Render;
        Action refresh = Render;
        EditorSuggestionsSources.Updated += refresh;
        picker.Closed += () => EditorSuggestionsSources.Updated -= refresh;
        Render();
        var body = Ui.VStack(8, search, new ScrollViewer { Content = rows, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Ui.Caption("Listed from docker image ls on the selected engine context. Pin digest adds the repository digest so the build always uses this exact image."));
        picker.Show("Local images", body, null, 440, search.Box);
    }

    // ------------------------------------------------------------------ helpers

    private static FrameworkElement Labelled(string label, FrameworkElement control)
    {
        var text = Ui.Text(label, 12, FontWeights.SemiBold, "OnSurfaceVariant");
        if (control is not Panel) AutomationProperties.SetName(control, Loc.T(label));
        return Ui.VStack(4, text, control);
    }

    private static bool IsQuoted(EditorSuggestions owner, EditorValueSpan span) => span.Start > 0 && span.Start <= owner.Box.Text.Length && owner.Box.Text[span.Start - 1] is '"' or '\'';

    /// <summary>Compose values with a colon are quoted (YAML would read 1000:1000 as a number).</summary>
    private static string Quote(EditorSuggestions owner, EditorValueSpan span, string value) =>
        span.Compose && value.Contains(':') && !IsQuoted(owner, span) ? "\"" + value + "\"" : value;
}

/// <summary>A themed popup at the caret that hosts one picker and applies its value in place.</summary>
public sealed class EditorValuePickerPopup
{
    private static AppHost Host => AppHost.Current;
    private readonly EditorSuggestions owner;
    private readonly EditorValueSpan span;
    private readonly string snapshot;
    private readonly int selectionStart, selectionLength;
    private readonly Popup popup = new() { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Relative };
    private readonly TextBlock error = Ui.Text("", 12, null, "Error");
    private bool applied;
    public event Action? Closed;

    public EditorValuePickerPopup(EditorSuggestions owner, EditorValueSpan span)
    {
        this.owner = owner;
        this.span = span;
        snapshot = owner.Box.Text;
        selectionStart = owner.Box.SelectionStart;
        selectionLength = owner.Box.SelectionLength;
    }

    public void Show(string title, FrameworkElement body, Func<string?>? produce, double width, FrameworkElement? focus = null)
    {
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Ui.Button("Cancel", null, ButtonKind.Text, () => popup.IsOpen = false);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(cancel);
        if (produce != null) buttons.Children.Add(Ui.Button("Apply", Icons.Check, ButtonKind.Filled, () => { if (produce() is { } v) Commit(v); }));
        var heading = Ui.Text(title, 15, FontWeights.SemiBold);
        var content = Ui.VStack(12, heading, body, error, buttons);
        var card = Ui.Card(content, 14, "SurfaceHigh", 14);
        card.Width = width;
        card.Effect = Ui.Elevation(18, 4, .35);
        card.Margin = new Thickness(0, 4, 14, 14);
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(card, KeyboardNavigationMode.Contained);
        AutomationProperties.SetName(card, Loc.T(title));
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && produce != null && e.OriginalSource is not Button)
            {
                if (produce() is { } v) Commit(v);
                e.Handled = true;
            }
        };
        var anchor = owner.AnchorFor(span.Start);
        popup.Child = card;
        popup.PlacementTarget = owner.Box;
        popup.HorizontalOffset = Math.Max(0, anchor.Left - 8);
        popup.VerticalOffset = anchor.Bottom + 2;
        popup.Opened += (_, _) => card.Dispatcher.BeginInvoke(() =>
        {
            if (focus != null && focus.Focusable) Keyboard.Focus(focus);
            else card.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }, System.Windows.Threading.DispatcherPriority.Input);
        popup.Closed += (_, _) =>
        {
            owner.PickerOpen = false;
            Closed?.Invoke();
            if (!applied)
            {
                owner.Box.Focus();
                owner.Box.Select(Math.Min(selectionStart, owner.Box.Text.Length), Math.Min(selectionLength, owner.Box.Text.Length - Math.Min(selectionStart, owner.Box.Text.Length)));
            }
        };
        owner.PickerOpen = true;
        popup.IsOpen = true;
    }

    public void Error(string message) => error.Text = Loc.T(message);

    /// <summary>Replaces the value span with <paramref name="value"/> unless the text changed meanwhile.</summary>
    public void Commit(string value)
    {
        if (owner.Box.Text != snapshot)
        {
            Host.Notify.Warning("The text changed while the picker was open, so nothing was replaced.");
            popup.IsOpen = false;
            return;
        }
        applied = true;
        popup.IsOpen = false;
        owner.Replace(span.Start, span.Length, value, span.Start + value.Length);
    }
}
