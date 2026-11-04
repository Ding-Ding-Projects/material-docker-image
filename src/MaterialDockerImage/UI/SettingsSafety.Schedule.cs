using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>One scheduled setting rule. Times are local clock times in the computer's time zone, "HH:mm", the end exclusive.</summary>
public sealed class ScheduleRule
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>"Language" or "Theme".</summary>
    public string Setting { get; set; } = "Language";
    public string Value { get; set; } = "";
    /// <summary>"Local" uses Value. "File" takes the value from the watched local JSON file.</summary>
    public string Source { get; set; } = ScheduleSources.Local;
    public bool EveryDay { get; set; } = true;
    /// <summary>Selected weekdays when EveryDay is false, 0 = Sunday to 6 = Saturday. A cross-midnight window belongs to the day it starts.</summary>
    public List<int> Days { get; set; } = new();
    public string Start { get; set; } = "20:00";
    public string End { get; set; } = "07:00";
    /// <summary>Higher wins. Equal priorities: the rule later in the list wins.</summary>
    public int Priority { get; set; }
}

public static class ScheduleSources
{
    public const string Local = "Local";
    public const string File = "File";
}

public sealed record ScheduleFileValues(string? Language, string? Theme, string Error);
public sealed record ScheduleOutcome(string? Language, string? Theme, IReadOnlyList<string> ActiveRuleIds);

/// <summary>Pure schedule evaluation and validation. Nothing here touches the clock, the disk or a window.</summary>
public static class ScheduleEvaluator
{
    public const int Version = 1;
    public const int MaxRules = 50;
    public const int MaxFileBytes = 4096;
    public static readonly string[] Languages = ["English", "Cantonese", "Bilingual"];
    public static readonly string[] Themes = ["Dark", "Light", "System"];

    public static bool TryParseTime(string? text, out int minutes)
    {
        minutes = 0;
        if (text is not { Length: 5 } || text[2] != ':') return false;
        if (!int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var h) || !int.TryParse(text.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)) return false;
        if (h > 23 || m > 59) return false;
        minutes = h * 60 + m;
        return true;
    }

    public static bool ValueAllowed(string setting, string? value) =>
        setting == "Language" ? Languages.Contains(value) : setting == "Theme" && Themes.Contains(value);

    /// <summary>Returns null when the rule is complete, otherwise a short reason.</summary>
    public static string? Validate(ScheduleRule rule)
    {
        if (rule.Setting is not ("Language" or "Theme")) return "The setting must be Language or Theme.";
        if (rule.Source is not (ScheduleSources.Local or ScheduleSources.File)) return "The source must be Local or File.";
        if (rule.Source == ScheduleSources.Local && !ValueAllowed(rule.Setting, rule.Value)) return "The value is not allowed for this setting.";
        if (!TryParseTime(rule.Start, out _)) return "The start time must look like 08:30.";
        if (!TryParseTime(rule.End, out _)) return "The end time must look like 21:00.";
        if (!rule.EveryDay && (rule.Days.Count == 0 || rule.Days.Any(d => d < 0 || d > 6))) return "Choose at least one weekday.";
        if (rule.Label.Length > 60) return "The label is longer than 60 characters.";
        return null;
    }

    public static bool DaySelected(ScheduleRule rule, DayOfWeek day) => rule.EveryDay || rule.Days.Contains((int)day);

    /// <summary>
    /// A window is [start, end). Start == end means the whole day. When start is later than end the window crosses midnight:
    /// the part before midnight uses today's weekday and the part after midnight belongs to the window that started yesterday.
    /// </summary>
    public static bool IsActive(ScheduleRule rule, DateTime now)
    {
        if (!rule.Enabled) return false;
        if (!TryParseTime(rule.Start, out var start) || !TryParseTime(rule.End, out var end)) return false;
        if (!rule.EveryDay && (rule.Days.Count == 0 || rule.Days.Any(d => d < 0 || d > 6))) return false;
        var minute = now.Hour * 60 + now.Minute;
        if (start == end) return DaySelected(rule, now.DayOfWeek);
        if (start < end) return minute >= start && minute < end && DaySelected(rule, now.DayOfWeek);
        if (minute >= start) return DaySelected(rule, now.DayOfWeek);
        return minute < end && DaySelected(rule, now.AddDays(-1).DayOfWeek);
    }

    public static ScheduleOutcome Evaluate(IReadOnlyList<ScheduleRule> rules, DateTime now, ScheduleFileValues? file)
    {
        string? language = null, theme = null;
        int languagePriority = int.MinValue, themePriority = int.MinValue;
        var active = new List<string>();
        foreach (var rule in rules)
        {
            if (!IsActive(rule, now)) continue;
            var value = rule.Source == ScheduleSources.File ? (rule.Setting == "Language" ? file?.Language : file?.Theme) : rule.Value;
            if (!ValueAllowed(rule.Setting, value)) continue;
            active.Add(rule.Id);
            if (rule.Setting == "Language" && rule.Priority >= languagePriority) { language = value; languagePriority = rule.Priority; }
            if (rule.Setting == "Theme" && rule.Priority >= themePriority) { theme = value; themePriority = rule.Priority; }
        }
        return new ScheduleOutcome(language, theme, active);
    }

    public static List<ScheduleRule> Normalize(IEnumerable<ScheduleRule>? rules)
    {
        var result = new List<ScheduleRule>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules ?? Array.Empty<ScheduleRule>())
        {
            if (rule == null || result.Count >= MaxRules) continue;
            rule.Days = (rule.Days ?? new()).Where(d => d >= 0 && d <= 6).Distinct().OrderBy(d => d).ToList();
            rule.Label = (rule.Label ?? "").Trim(); if (rule.Label.Length > 60) rule.Label = rule.Label[..60];
            rule.Priority = Math.Clamp(rule.Priority, -100, 100);
            if (string.IsNullOrWhiteSpace(rule.Id) || rule.Id.Length > 40 || !ids.Add(rule.Id)) { rule.Id = NewId(ids); ids.Add(rule.Id); }
            result.Add(rule);
        }
        return result;
    }

    private static string NewId(HashSet<string> taken)
    {
        for (var i = 1; ; i++) { var id = "rule-" + i; if (!taken.Contains(id)) return id; }
    }

    public static string Describe(ScheduleRule rule)
    {
        var days = rule.EveryDay ? "every day" : string.Join(", ", rule.Days.Select(d => ((DayOfWeek)d).ToString()[..3]));
        var span = rule.Start == rule.End ? "all day" : $"{rule.Start} to {rule.End}" + (TryParseTime(rule.Start, out var s) && TryParseTime(rule.End, out var e) && s > e ? " (past midnight)" : "");
        var value = rule.Source == ScheduleSources.File ? "value from the file" : rule.Value;
        return $"{rule.Setting} {value}, {days}, {span}";
    }

    /// <summary>
    /// Parses the local source file: {"version":1,"language":"Cantonese","theme":"Light"}. Only these fields are accepted, the file is
    /// bounded, and anything unexpected rejects the whole file so nothing is applied from a half-valid document.
    /// </summary>
    public static ScheduleFileValues ParseFile(string? json, long byteLength)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(null, null, "The schedule file is empty.");
        if (byteLength > MaxFileBytes) return new(null, null, "The schedule file is larger than 4 KiB.");
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new(null, null, "The schedule file must hold one JSON object.");
            string? language = null, theme = null; var version = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "version": if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out version)) return new(null, null, "version must be a number."); break;
                    case "language": if (property.Value.ValueKind != JsonValueKind.String || !Languages.Contains(property.Value.GetString())) return new(null, null, "language is not one of English, Cantonese or Bilingual."); language = property.Value.GetString(); break;
                    case "theme": if (property.Value.ValueKind != JsonValueKind.String || !Themes.Contains(property.Value.GetString())) return new(null, null, "theme is not one of Dark, Light or System."); theme = property.Value.GetString(); break;
                    default: return new(null, null, "Unknown field: " + (property.Name.Length > 30 ? property.Name[..30] : property.Name));
                }
            }
            if (version != Version) return new(null, null, "Only version 1 is supported.");
            return new(language, theme, "");
        }
        catch (JsonException e) { return new(null, null, "The schedule file is not valid JSON: " + e.Message); }
    }
}
