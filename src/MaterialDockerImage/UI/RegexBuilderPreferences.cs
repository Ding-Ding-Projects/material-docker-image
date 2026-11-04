using System.Text;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>One panel (section) of the builder. Required panels cannot be hidden, so the pattern box and the controls that restore a layout are always reachable.</summary>
public sealed record RegexPanel(string Id, string Title, bool Required = false);

/// <summary>
/// Which builder panels are shown and in what order, remembered per user. The set of panels is fixed by the builder; a stored layout that names an
/// unknown panel is rejected, and a panel the stored layout does not mention (added by a later version) is appended in its default position.
/// </summary>
public sealed record RegexPanelLayout(IReadOnlyList<string> Order, IReadOnlyList<string> Hidden)
{
    public static readonly IReadOnlyList<RegexPanel> Panels =
    [
        new("pattern", "Pattern", true), new("flags", "Flags"), new("insert", "Insert"), new("explanation", "Explanation"), new("test", "Test"),
        new("replace", "Replace preview"), new("split", "Split preview"), new("cases", "Test cases"), new("compare", "Compare patterns"), new("subject", "Subject tools"),
        new("diagnostics", "Diagnostics and timing"), new("convert", "Convert and copy as"), new("templates", "Code templates"), new("flavors", "Flavor comparison"),
        new("files", "Search in files"), new("preferences", "Preferences and layout", true),
    ];

    public static RegexPanelLayout Default { get; } = new(Panels.Select(p => p.Id).ToList(), []);

    public static RegexPanel? Panel(string id) => Panels.FirstOrDefault(p => p.Id == id);

    /// <summary>The panel ids to show, in order: the stored order with unknown ids dropped, missing ids appended in default order, hidden panels removed. Required panels always stay.</summary>
    public IReadOnlyList<string> Visible()
    {
        var order = Normalize();
        return order.Where(id => !Hidden.Contains(id) || Panel(id)!.Required).ToList();
    }

    /// <summary>The full order including hidden panels, with every known panel exactly once.</summary>
    public IReadOnlyList<string> Normalize()
    {
        var order = new List<string>();
        foreach (var id in Order) if (Panel(id) != null && !order.Contains(id)) order.Add(id);
        foreach (var p in Panels) if (!order.Contains(p.Id)) order.Add(p.Id);
        return order;
    }

    public bool IsHidden(string id) => Hidden.Contains(id) && Panel(id) is { Required: false };

    /// <summary>Shows or hides a panel. A required panel stays shown and the request is refused with the reason.</summary>
    public (RegexPanelLayout Layout, string? Problem) SetHidden(string id, bool hidden)
    {
        var panel = Panel(id);
        if (panel == null) return (this, "There is no panel called " + id + ".");
        if (hidden && panel.Required) return (this, panel.Title + " cannot be hidden: it holds the pattern or the controls that restore the layout.");
        var set = Hidden.Where(h => h != id).ToList();
        if (hidden) set.Add(id);
        return (this with { Order = Normalize(), Hidden = set }, null);
    }

    /// <summary>Moves a panel up (-1) or down (+1) among all panels. Moving past the end changes nothing.</summary>
    public RegexPanelLayout Move(string id, int direction)
    {
        var order = Normalize().ToList();
        var index = order.IndexOf(id);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= order.Count) return this;
        (order[index], order[target]) = (order[target], order[index]);
        return this with { Order = order };
    }
}

/// <summary>Builder preferences remembered per user: default flags for new search fields, the match time limit and the font size of the pattern box.</summary>
public sealed record RegexBuilderPreferences
{
    public const double MinFontSize = 10, MaxFontSize = 24, DefaultFontSize = 13;
    public const string Format = "material-docker-image/regex-preferences";
    public const string FileName = "regex-preferences.json";

    /// <summary>The preferences the running application uses. New search fields read their default flags from here; nothing else is global.</summary>
    public static RegexBuilderPreferences Current { get; private set; } = new();

    public RegexBuilderFlags DefaultFlags { get; init; } = new();
    public int MatchTimeoutMs { get; init; } = RegexBuilderEngine.DefaultTimeoutMs;
    public double PatternFontSize { get; init; } = DefaultFontSize;
    public string ExplanationDetail { get; init; } = "Full";
    public RegexPanelLayout Layout { get; init; } = RegexPanelLayout.Default;

    public static readonly IReadOnlyList<string> Details = ["Overview", "Standard", "Full"];

    public static string DefaultPath => Path.Combine(UserSettings.DataDirectory, FileName);

    /// <summary>Why the saved preferences could not be read at start-up, or null. The defaults are used in that case and the saved file is left untouched.</summary>
    public static string? LoadProblem { get; private set; }

    /// <summary>Reads the saved preferences and makes them the ones in force. A missing file keeps the defaults; an unreadable one keeps the defaults and records why.</summary>
    public static void LoadAtStartup(string? path = null)
    {
        LoadProblem = null;
        try { Apply(Load(path ?? DefaultPath)); }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            LoadProblem = e.Message;
            Apply(new RegexBuilderPreferences());
        }
    }

    /// <summary>Makes these preferences the ones in force, clamping the values and applying the match time limit to patterns compiled from now on.</summary>
    public static void Apply(RegexBuilderPreferences next)
    {
        Current = next.Clamped();
        RegexBuilderEngine.SetMatchTimeout(Current.MatchTimeoutMs);
    }

    public RegexBuilderPreferences Clamped() => this with
    {
        MatchTimeoutMs = Math.Clamp(MatchTimeoutMs, RegexBuilderEngine.MinTimeoutMs, RegexBuilderEngine.MaxTimeoutMs),
        PatternFontSize = double.IsFinite(PatternFontSize) ? Math.Clamp(PatternFontSize, MinFontSize, MaxFontSize) : DefaultFontSize,
        ExplanationDetail = Details.Contains(ExplanationDetail) ? ExplanationDetail : "Full",
    };

    public RegexExplainPrecision Precision => ExplanationDetail switch { "Overview" => RegexExplainPrecision.Overview, "Standard" => RegexExplainPrecision.Standard, _ => RegexExplainPrecision.Full };

    // ----- persistence -----

    public static RegexBuilderPreferences Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("The preferences file is larger than 64 KiB and was not read.");
        return Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
    }

    public static RegexBuilderPreferences Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format) throw new InvalidDataException("The preferences file has an unknown format.");
            if (!root.TryGetProperty("version", out var v) || v.GetInt32() != 1) throw new InvalidDataException("The preferences file has an unsupported version.");
            var flags = new RegexBuilderFlags();
            if (root.TryGetProperty("defaultFlags", out var fl))
                foreach (var (name, _, _) in RegexBuilderFlags.All)
                    if (fl.TryGetProperty(name, out var b)) flags = flags.With(name, b.GetBoolean());
            if (flags.NonBacktracking && flags.RightToLeft) throw new InvalidDataException("The default flags combine NonBacktracking with RightToLeft, which the builder refuses.");
            var timeout = root.TryGetProperty("matchTimeoutMs", out var t) ? t.GetInt32() : RegexBuilderEngine.DefaultTimeoutMs;
            if (timeout < RegexBuilderEngine.MinTimeoutMs || timeout > RegexBuilderEngine.MaxTimeoutMs) throw new InvalidDataException($"The match time limit must be between {RegexBuilderEngine.MinTimeoutMs} and {RegexBuilderEngine.MaxTimeoutMs} ms.");
            var size = root.TryGetProperty("patternFontSize", out var s) ? s.GetDouble() : DefaultFontSize;
            if (!double.IsFinite(size) || size < MinFontSize || size > MaxFontSize) throw new InvalidDataException($"The pattern font size must be between {MinFontSize} and {MaxFontSize}.");
            var detail = root.TryGetProperty("explanationDetail", out var d) ? d.GetString() ?? "Full" : "Full";
            if (!Details.Contains(detail)) throw new InvalidDataException("The explanation detail must be Overview, Standard or Full.");
            var order = new List<string>(); var hidden = new List<string>();
            if (root.TryGetProperty("panelOrder", out var po))
                foreach (var e in po.EnumerateArray())
                {
                    var id = e.GetString() ?? "";
                    if (RegexPanelLayout.Panel(id) == null) throw new InvalidDataException("The preferences file names a panel that does not exist: " + id + ".");
                    if (order.Contains(id)) throw new InvalidDataException("The preferences file lists the panel " + id + " twice.");
                    order.Add(id);
                }
            if (root.TryGetProperty("hiddenPanels", out var hp))
                foreach (var e in hp.EnumerateArray())
                {
                    var id = e.GetString() ?? "";
                    var panel = RegexPanelLayout.Panel(id) ?? throw new InvalidDataException("The preferences file hides a panel that does not exist: " + id + ".");
                    if (panel.Required) throw new InvalidDataException("The preferences file hides " + panel.Title + ", which cannot be hidden.");
                    if (!hidden.Contains(id)) hidden.Add(id);
                }
            return new RegexBuilderPreferences { DefaultFlags = flags, MatchTimeoutMs = timeout, PatternFontSize = size, ExplanationDetail = detail, Layout = new RegexPanelLayout(order, hidden) };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The preferences file is not valid: " + e.Message, e);
        }
    }

    public string Serialize()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            var p = Clamped();
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", 1);
            w.WriteStartObject("defaultFlags");
            foreach (var (name, _, _) in RegexBuilderFlags.All) w.WriteBoolean(name, p.DefaultFlags.Get(name));
            w.WriteEndObject();
            w.WriteNumber("matchTimeoutMs", p.MatchTimeoutMs);
            w.WriteNumber("patternFontSize", p.PatternFontSize);
            w.WriteString("explanationDetail", p.ExplanationDetail);
            w.WriteStartArray("panelOrder");
            foreach (var id in p.Layout.Normalize()) w.WriteStringValue(id);
            w.WriteEndArray();
            w.WriteStartArray("hiddenPanels");
            foreach (var id in p.Layout.Hidden.Where(id => RegexPanelLayout.Panel(id) is { Required: false }).Distinct()) w.WriteStringValue(id);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes atomically (temporary file, then replace).</summary>
    public void Save(string path)
    {
        var text = Serialize();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}

/// <summary>
/// The keyboard map of the builder: one list that both the help text and the key handler read, so a shortcut cannot be documented without working or work
/// without being documented. A gesture is written like <c>Ctrl+1</c> or <c>Shift+F6</c>.
/// </summary>
public static class RegexKeyboard
{
    /// <summary>The panels that have a direct number shortcut (Ctrl+1 to Ctrl+9, then Ctrl+0), counted over the panels currently shown.</summary>
    public const int DirectPanels = 10;

    public static readonly IReadOnlyList<(string Gesture, string Action)> Map =
    [
        ("Alt+R", "Open the builder from a search field, or move into it when it is open."),
        ("Esc", "Close the builder and return to the search field."),
        ("Tab / Shift+Tab", "Move through the controls of the builder; focus stays inside until it is closed."),
        ("F6", "Jump to the next panel that is shown, and wrap from the last to the first."),
        ("Shift+F6", "Jump to the previous panel that is shown."),
        ("Ctrl+1 to Ctrl+9, Ctrl+0", "Jump to the first ten panels that are shown, in the order they appear."),
        ("Alt+Home", "Go back to the pattern box."),
        ("F3 / Shift+F3", "Select the next or previous match in the sample text."),
        ("Enter / Space", "Press the focused button, toggle the focused option."),
    ];

    /// <summary>The gesture that jumps straight to the panel at a position (0-based), or null beyond the tenth.</summary>
    public static string? DirectGesture(int position) => position is >= 0 and < DirectPanels ? "Ctrl+" + ((position + 1) % 10) : null;

    /// <summary>
    /// The panel a gesture goes to, given the panels shown (in order) and the one that has focus. Null when the gesture is not a panel gesture or the panel does not
    /// exist (for example Ctrl+7 with only five panels shown).
    /// </summary>
    public static string? Target(string gesture, IReadOnlyList<string> shown, string? current)
    {
        if (shown.Count == 0) return null;
        var index = current == null ? -1 : shown.ToList().IndexOf(current);
        switch (gesture)
        {
            case "F6": return shown[(index + 1) % shown.Count];
            case "Shift+F6": return shown[index <= 0 ? shown.Count - 1 : index - 1];
            case "Alt+Home": return shown[0];
        }
        if (gesture.StartsWith("Ctrl+", StringComparison.Ordinal) && gesture.Length == 6 && char.IsAsciiDigit(gesture[5]))
        {
            var number = gesture[5] - '0';
            var position = number == 0 ? 9 : number - 1;
            return position < shown.Count ? shown[position] : null;
        }
        return null;
    }

    /// <summary>The map as plain text for the help block, with the direct panel shortcuts listed against the panels that are shown.</summary>
    public static string Describe(IReadOnlyList<string> shown)
    {
        var sb = new StringBuilder();
        foreach (var (gesture, action) in Map) sb.AppendLine(gesture + ": " + action);
        sb.AppendLine();
        sb.AppendLine("Panels shown, in order:");
        for (var i = 0; i < shown.Count; i++)
            sb.AppendLine("  " + (DirectGesture(i) ?? "F6") + "  " + (RegexPanelLayout.Panel(shown[i])?.Title ?? shown[i]));
        return sb.ToString();
    }
}
