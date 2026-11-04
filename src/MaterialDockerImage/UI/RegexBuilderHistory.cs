using System.Text;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>One remembered experiment: pattern, flags, sample, replacement and test cases, stored only after the user chose to remember it.</summary>
public sealed record RegexExperiment(string Id, string Name, string Pattern, RegexBuilderFlags Flags, string Sample, string Replacement, string TestCases, DateTimeOffset Saved)
{
    /// <summary>Same content, ignoring name, id and time. Used to move a repeated experiment to the front instead of storing it twice.</summary>
    public bool SameContent(RegexExperiment other) =>
        Pattern == other.Pattern && Flags == other.Flags && Sample == other.Sample && Replacement == other.Replacement && TestCases == other.TestCases;
}

/// <summary>
/// The persistent experiment history, stored as JSON in the app data folder. Nothing is added unless the user presses Remember, the list is
/// bounded in count, per-field size and total size, and everything can be cleared. Pure logic.
/// </summary>
public static class RegexExperimentStore
{
    public const int MaxEntries = 50;
    public const int MaxSampleChars = 16 * 1024;
    public const int MaxReplacementChars = 1000;
    public const int MaxTestCaseChars = 8000;
    public const int MaxNameChars = 80;
    public const int MaxTotalBytes = 1024 * 1024;
    public const string FileName = "regex-experiments.json";
    private const string Format = "material-docker-image.regex-experiments";

    public static string DefaultPath => Path.Combine(UserSettings.DataDirectory, FileName);

    /// <summary>Builds an experiment, refusing text longer than the stored limits instead of silently cutting it.</summary>
    public static RegexExperiment Create(string name, string pattern, RegexBuilderFlags flags, string sample, string replacement, string testCases, DateTimeOffset? when = null)
    {
        if (string.IsNullOrWhiteSpace(pattern)) throw new ArgumentException("Enter a pattern to remember.");
        if (pattern.Length > RegexBuilderEngine.MaxPatternLength) throw new ArgumentException("The pattern is too long.");
        if ((sample ?? "").Length > MaxSampleChars) throw new ArgumentException($"The sample is longer than {MaxSampleChars / 1024} KiB. Shorten it to remember it.");
        if ((replacement ?? "").Length > MaxReplacementChars) throw new ArgumentException($"The replacement is longer than {MaxReplacementChars} characters.");
        if ((testCases ?? "").Length > MaxTestCaseChars) throw new ArgumentException($"The test cases are longer than {MaxTestCaseChars} characters.");
        var label = (name ?? "").Trim();
        if (label.Length == 0) label = pattern.Length > 40 ? pattern[..40] + "…" : pattern;
        if (label.Length > MaxNameChars) label = label[..MaxNameChars];
        return new(Guid.NewGuid().ToString("N")[..12], label, pattern, flags, sample ?? "", replacement ?? "", testCases ?? "", when ?? DateTimeOffset.Now);
    }

    /// <summary>Newest first. A repeated experiment moves to the front. The list is cut to the entry and total size limits, oldest first.</summary>
    public static List<RegexExperiment> Add(IReadOnlyList<RegexExperiment> existing, RegexExperiment entry)
    {
        var list = existing.Where(e => !e.SameContent(entry)).ToList();
        list.Insert(0, entry);
        while (list.Count > MaxEntries) list.RemoveAt(list.Count - 1);
        while (list.Count > 1 && SerializeBytes(list).Length > MaxTotalBytes) list.RemoveAt(list.Count - 1);
        if (SerializeBytes(list).Length > MaxTotalBytes) throw new ArgumentException("This experiment is too large to remember.");
        return list;
    }

    public static List<RegexExperiment> Remove(IReadOnlyList<RegexExperiment> existing, string id) => existing.Where(e => e.Id != id).ToList();

    public static List<RegexExperiment> Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 2L * MaxTotalBytes) throw new InvalidDataException("The experiment history is larger than its limit and was not read.");
        return Parse(File.ReadAllText(path));
    }

    public static List<RegexExperiment> Parse(string text)
    {
        if (text.Length > 2 * MaxTotalBytes) throw new InvalidDataException("The experiment history is larger than its limit and was not read.");
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format) throw new InvalidDataException("The experiment history has an unknown format.");
            if (!root.TryGetProperty("version", out var v) || v.GetInt32() != 1) throw new InvalidDataException("The experiment history has an unsupported version.");
            var list = new List<RegexExperiment>();
            foreach (var e in root.GetProperty("experiments").EnumerateArray())
            {
                string Text(string property, int limit)
                {
                    var value = e.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";
                    if (value.Length > limit) throw new InvalidDataException($"An experiment has a {property} longer than {limit} characters.");
                    return value;
                }
                var pattern = Text("pattern", RegexBuilderEngine.MaxPatternLength);
                if (pattern.Length == 0) throw new InvalidDataException("An experiment has no pattern.");
                var flags = new RegexBuilderFlags();
                if (e.TryGetProperty("flags", out var fl))
                    foreach (var (flag, _, _) in RegexBuilderFlags.All)
                        if (fl.TryGetProperty(flag, out var b)) flags = flags.With(flag, b.GetBoolean());
                var saved = e.TryGetProperty("saved", out var s) && s.TryGetDateTimeOffset(out var d) ? d : DateTimeOffset.MinValue;
                var id = Text("id", 32);
                list.Add(new(id.Length == 0 ? Guid.NewGuid().ToString("N")[..12] : id, Text("name", MaxNameChars), pattern, flags, Text("sample", MaxSampleChars), Text("replacement", MaxReplacementChars), Text("testCases", MaxTestCaseChars), saved));
                if (list.Count > MaxEntries) throw new InvalidDataException($"The experiment history has more than {MaxEntries} entries.");
            }
            return list;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("The experiment history is not valid: " + e.Message, e);
        }
    }

    public static void Save(string path, IReadOnlyList<RegexExperiment> experiments)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, SerializeBytes(experiments));
        File.Move(temp, path, true);
    }

    public static string Serialize(IReadOnlyList<RegexExperiment> experiments) => Encoding.UTF8.GetString(SerializeBytes(experiments));

    private static byte[] SerializeBytes(IReadOnlyList<RegexExperiment> experiments)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", 1);
            w.WriteStartArray("experiments");
            foreach (var x in experiments)
            {
                w.WriteStartObject();
                w.WriteString("id", x.Id);
                w.WriteString("name", x.Name);
                w.WriteString("pattern", x.Pattern);
                w.WriteStartObject("flags");
                foreach (var (flag, _, _) in RegexBuilderFlags.All) w.WriteBoolean(flag, x.Flags.Get(flag));
                w.WriteEndObject();
                w.WriteString("sample", x.Sample);
                w.WriteString("replacement", x.Replacement);
                w.WriteString("testCases", x.TestCases);
                w.WriteString("saved", x.Saved);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return stream.ToArray();
    }
}
