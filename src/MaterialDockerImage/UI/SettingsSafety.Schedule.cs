using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace MaterialDockerImage.UI;

/// <summary>One scheduled setting rule. Times are local clock times in the computer's time zone, "HH:mm", the end exclusive. Dates are local calendar dates, "yyyy-MM-dd", both ends inclusive.</summary>
public sealed class ScheduleRule
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>One of ScheduleSettings.All.</summary>
    public string Setting { get; set; } = ScheduleSettings.Language;
    public string Value { get; set; } = "";
    /// <summary>Home Assistant rules only: the value applied while the entity is off. Empty means nothing is applied while it is off.</summary>
    public string OffValue { get; set; } = "";
    /// <summary>"Local" uses Value. "File" and "Https" take the value from that source's validated JSON. "HomeAssistant" maps the entity state on and off to Value and OffValue.</summary>
    public string Source { get; set; } = ScheduleSources.Local;
    public bool EveryDay { get; set; } = true;
    /// <summary>Selected weekdays when EveryDay is false, 0 = Sunday to 6 = Saturday. A cross-midnight window belongs to the day it starts.</summary>
    public List<int> Days { get; set; } = new();
    public string Start { get; set; } = "20:00";
    public string End { get; set; } = "07:00";
    /// <summary>Optional first calendar day the rule may apply, "yyyy-MM-dd". Empty means no lower bound.</summary>
    public string FromDate { get; set; } = "";
    /// <summary>Optional last calendar day the rule may apply, "yyyy-MM-dd". Empty means no upper bound. A cross-midnight window that starts on this day finishes after midnight.</summary>
    public string ToDate { get; set; } = "";
    /// <summary>Higher wins. Equal priorities: the rule later in the list wins.</summary>
    public int Priority { get; set; }
}

public static class ScheduleSources
{
    public const string Local = "Local";
    public const string File = "File";
    public const string Https = "Https";
    public const string HomeAssistant = "HomeAssistant";
    public static readonly string[] All = [Local, File, Https, HomeAssistant];
}

/// <summary>The settings a schedule may lend. The names are stored in rules; with a lower case first letter they are also the JSON property names of the File and Https sources.</summary>
public static class ScheduleSettings
{
    public const string Language = "Language";
    public const string Theme = "Theme";
    public const string Density = "Density";
    public const string Accent = "Accent";
    public const string FontSize = "FontSize";
    public const string Motion = "Motion";
    public const string DisplayName = "DisplayName";
    public static readonly string[] All = [Language, Theme, Density, Accent, FontSize, Motion, DisplayName];
    public static string JsonName(string setting) => char.ToLowerInvariant(setting[0]) + setting[1..];
}

/// <summary>Validated values read from the File or Https source. Values holds only settings that passed validation; Error is empty when the whole payload was accepted.</summary>
public sealed record ScheduleSourceValues(IReadOnlyDictionary<string, string> Values, string Error)
{
    public string? Language => Get(ScheduleSettings.Language);
    public string? Theme => Get(ScheduleSettings.Theme);
    public string? Get(string setting) => Values.TryGetValue(setting, out var v) ? v : null;
    public static ScheduleSourceValues Fail(string error) => new(new Dictionary<string, string>(), error);
}

/// <summary>Everything the network sources currently know. Https is null when that source has nothing usable.</summary>
public sealed record ScheduleExternal(ScheduleSourceValues? Https, HomeAssistantState HomeAssistant);

public sealed record ScheduleOutcome(IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> ActiveRuleIds)
{
    public string? Language => Get(ScheduleSettings.Language);
    public string? Theme => Get(ScheduleSettings.Theme);
    public string? Get(string setting) => Values.TryGetValue(setting, out var v) ? v : null;
    public static ScheduleOutcome Empty { get; } = new(new Dictionary<string, string>(), Array.Empty<string>());
}

/// <summary>Pure schedule evaluation and validation. Nothing here touches the clock, the disk, the network or a window.</summary>
public static class ScheduleEvaluator
{
    public const int Version = 1;
    public const int MaxRules = 50;
    public const int MaxFileBytes = 4096;
    public const int MaxHttpsBytes = 65536;
    public static readonly string[] Languages = ["English", "Cantonese", "Bilingual"];
    public static readonly string[] Themes = ["Dark", "Light", "System"];
    public static readonly string[] Densities = ["Compact", "Comfortable", "Spacious"];
    public static readonly string[] MotionLevels = ["Off", "Reduced", "Standard", "Expressive"];
    public const int MinFontSize = 12;
    public const int MaxFontSize = 22;

    public static bool TryParseTime(string? text, out int minutes)
    {
        minutes = 0;
        if (text is not { Length: 5 } || text[2] != ':') return false;
        if (!int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var h) || !int.TryParse(text.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)) return false;
        if (h > 23 || m > 59) return false;
        minutes = h * 60 + m;
        return true;
    }

    /// <summary>Strict "yyyy-MM-dd". Whitespace, other layouts and impossible dates are refused.</summary>
    public static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        return text is { Length: 10 } && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Normalizes a value for a setting (accent colours become upper case #RRGGBB). Returns false when the value is not allowed.</summary>
    public static bool TryNormalizeValue(string setting, string? value, out string normalized)
    {
        normalized = "";
        if (value == null) return false;
        switch (setting)
        {
            case ScheduleSettings.Language: if (!Languages.Contains(value)) return false; normalized = value; return true;
            case ScheduleSettings.Theme: if (!Themes.Contains(value)) return false; normalized = value; return true;
            case ScheduleSettings.Density: if (!Densities.Contains(value)) return false; normalized = value; return true;
            case ScheduleSettings.Motion: if (!MotionLevels.Contains(value)) return false; normalized = value; return true;
            case ScheduleSettings.Accent:
                if (value.Length != 7 || value[0] != '#' || !value.Skip(1).All(Uri.IsHexDigit)) return false;
                normalized = value.ToUpperInvariant(); return true;
            case ScheduleSettings.FontSize:
                if (value.Length != 2 || !value.All(char.IsAsciiDigit) || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size < MinFontSize || size > MaxFontSize) return false;
                normalized = size.ToString(CultureInfo.InvariantCulture); return true;
            case ScheduleSettings.DisplayName:
                if (!AppIdentity.TryNormalizeName(value, out var name, out _)) return false;
                normalized = name; return true;
            default: return false;
        }
    }

    public static bool ValueAllowed(string setting, string? value) => TryNormalizeValue(setting, value, out _);

    /// <summary>Returns null when the rule is complete, otherwise a short reason.</summary>
    public static string? Validate(ScheduleRule rule)
    {
        if (!ScheduleSettings.All.Contains(rule.Setting)) return "The setting is not one that can be scheduled.";
        if (!ScheduleSources.All.Contains(rule.Source)) return "The source must be Local, File, Https or HomeAssistant.";
        if ((rule.Source is ScheduleSources.Local or ScheduleSources.HomeAssistant) && !ValueAllowed(rule.Setting, rule.Value)) return "The value is not allowed for this setting.";
        if (rule.Source == ScheduleSources.HomeAssistant && rule.OffValue.Length > 0 && !ValueAllowed(rule.Setting, rule.OffValue)) return "The value for off is not allowed for this setting.";
        if (!TryParseTime(rule.Start, out _)) return "The start time must look like 08:30.";
        if (!TryParseTime(rule.End, out _)) return "The end time must look like 21:00.";
        if (!rule.EveryDay && (rule.Days.Count == 0 || rule.Days.Any(d => d < 0 || d > 6))) return "Choose at least one weekday.";
        if (rule.FromDate.Length > 0 && !TryParseDate(rule.FromDate, out _)) return "The first date must look like 2026-12-24.";
        if (rule.ToDate.Length > 0 && !TryParseDate(rule.ToDate, out _)) return "The last date must look like 2026-12-31.";
        if (TryParseDate(rule.FromDate, out var from) && TryParseDate(rule.ToDate, out var to) && to < from) return "The last date is before the first date.";
        if (rule.Label.Length > 60) return "The label is longer than 60 characters.";
        return null;
    }

    public static bool DaySelected(ScheduleRule rule, DayOfWeek day) => rule.EveryDay || rule.Days.Contains((int)day);

    /// <summary>True when the calendar day lies inside the optional date range (both ends included). An unreadable date makes the rule inactive.</summary>
    public static bool DateInRange(ScheduleRule rule, DateTime day)
    {
        var date = DateOnly.FromDateTime(day);
        if (rule.FromDate.Length > 0 && (!TryParseDate(rule.FromDate, out var from) || date < from)) return false;
        if (rule.ToDate.Length > 0 && (!TryParseDate(rule.ToDate, out var to) || date > to)) return false;
        return true;
    }

    /// <summary>
    /// A window is [start, end). Start == end means the whole day. When start is later than end the window crosses midnight:
    /// the part before midnight uses today's weekday and date and the part after midnight belongs to the window that started yesterday,
    /// so both the weekday and the optional date range are judged on the day the window started.
    /// </summary>
    public static bool IsActive(ScheduleRule rule, DateTime now)
    {
        if (!rule.Enabled) return false;
        if (!TryParseTime(rule.Start, out var start) || !TryParseTime(rule.End, out var end)) return false;
        if (!rule.EveryDay && (rule.Days.Count == 0 || rule.Days.Any(d => d < 0 || d > 6))) return false;
        var minute = now.Hour * 60 + now.Minute;
        bool Today() => DaySelected(rule, now.DayOfWeek) && DateInRange(rule, now);
        if (start == end) return Today();
        if (start < end) return minute >= start && minute < end && Today();
        if (minute >= start) return Today();
        var yesterday = now.AddDays(-1);
        return minute < end && DaySelected(rule, yesterday.DayOfWeek) && DateInRange(rule, yesterday);
    }

    private static string? ResolveValue(ScheduleRule rule, ScheduleSourceValues? file, ScheduleExternal? external)
    {
        string? raw = rule.Source switch
        {
            ScheduleSources.Local => rule.Value,
            ScheduleSources.File => file?.Get(rule.Setting),
            ScheduleSources.Https => external?.Https?.Get(rule.Setting),
            ScheduleSources.HomeAssistant => external?.HomeAssistant switch { HomeAssistantState.On => rule.Value, HomeAssistantState.Off => rule.OffValue.Length > 0 ? rule.OffValue : null, _ => null },
            _ => null
        };
        return TryNormalizeValue(rule.Setting, raw, out var value) ? value : null;
    }

    public static ScheduleOutcome Evaluate(IReadOnlyList<ScheduleRule> rules, DateTime now, ScheduleSourceValues? file, ScheduleExternal? external = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var priorities = new Dictionary<string, int>(StringComparer.Ordinal);
        var active = new List<string>();
        foreach (var rule in rules)
        {
            if (!IsActive(rule, now)) continue;
            var value = ResolveValue(rule, file, external);
            if (value == null) continue;
            active.Add(rule.Id);
            if (!priorities.TryGetValue(rule.Setting, out var best) || rule.Priority >= best) { values[rule.Setting] = value; priorities[rule.Setting] = rule.Priority; }
        }
        return new ScheduleOutcome(values, active);
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
            rule.Setting ??= ""; rule.Source ??= ScheduleSources.Local;
            rule.Value ??= ""; rule.OffValue ??= ""; rule.FromDate ??= ""; rule.ToDate ??= "";
            rule.Start ??= ""; rule.End ??= "";
            if (TryNormalizeValue(rule.Setting, rule.Value, out var value)) rule.Value = value;
            if (TryNormalizeValue(rule.Setting, rule.OffValue, out var off)) rule.OffValue = off;
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
        var value = rule.Source switch
        {
            ScheduleSources.File => "value from the file",
            ScheduleSources.Https => "value from the HTTPS source",
            ScheduleSources.HomeAssistant => rule.OffValue.Length > 0 ? $"{rule.Value} while on, {rule.OffValue} while off (Home Assistant)" : $"{rule.Value} while on (Home Assistant)",
            _ => rule.Value
        };
        var dates = rule.FromDate.Length > 0 && rule.ToDate.Length > 0 ? $", {rule.FromDate} to {rule.ToDate}" : rule.FromDate.Length > 0 ? $", from {rule.FromDate}" : rule.ToDate.Length > 0 ? $", until {rule.ToDate}" : "";
        return $"{rule.Setting} {value}, {days}, {span}{dates}";
    }

    /// <summary>Parses the local source file. See ParseSource.</summary>
    public static ScheduleSourceValues ParseFile(string? json, long byteLength) => ParseSource(json, byteLength, MaxFileBytes, "schedule file");

    /// <summary>
    /// Parses a source document: {"version":1,"language":"Cantonese","theme":"Light","density":"Compact","accent":"#AA3366","fontSize":16,"motion":"Reduced","displayName":"Lab"}.
    /// Only these fields are accepted, each at most once, the payload is bounded and shallow, and anything unexpected rejects the whole document
    /// so nothing is applied from a half-valid payload.
    /// </summary>
    public static ScheduleSourceValues ParseSource(string? json, long byteLength, int maxBytes, string what)
    {
        if (string.IsNullOrWhiteSpace(json)) return ScheduleSourceValues.Fail($"The {what} is empty.");
        if (byteLength > maxBytes) return ScheduleSourceValues.Fail($"The {what} is larger than {maxBytes / 1024} KiB.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return ScheduleSourceValues.Fail($"The {what} must hold one JSON object.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var version = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name)) return ScheduleSourceValues.Fail("A field appears twice: " + Short(property.Name));
                if (property.Name == "version")
                {
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out version)) return ScheduleSourceValues.Fail("version must be a number.");
                    continue;
                }
                var setting = ScheduleSettings.All.FirstOrDefault(s => ScheduleSettings.JsonName(s) == property.Name);
                if (setting == null) return ScheduleSourceValues.Fail("Unknown field: " + Short(property.Name));
                string? raw = null;
                if (setting == ScheduleSettings.FontSize)
                {
                    if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var size)) raw = size.ToString(CultureInfo.InvariantCulture);
                }
                else if (property.Value.ValueKind == JsonValueKind.String) raw = property.Value.GetString();
                if (!TryNormalizeValue(setting, raw, out var value)) return ScheduleSourceValues.Fail(Reason(setting));
                values[setting] = value;
            }
            if (version != Version) return ScheduleSourceValues.Fail("Only version 1 is supported.");
            return new ScheduleSourceValues(values, "");
        }
        catch (JsonException e) { return ScheduleSourceValues.Fail($"The {what} is not valid JSON: " + e.Message); }
    }

    private static string Short(string text) => text.Length > 30 ? text[..30] : text;

    private static string Reason(string setting) => setting switch
    {
        ScheduleSettings.Language => "language is not one of English, Cantonese or Bilingual.",
        ScheduleSettings.Theme => "theme is not one of Dark, Light or System.",
        ScheduleSettings.Density => "density is not one of Compact, Comfortable or Spacious.",
        ScheduleSettings.Accent => "accent must look like #3A6FD8.",
        ScheduleSettings.FontSize => "fontSize must be a whole number from 12 to 22.",
        ScheduleSettings.Motion => "motion is not one of Off, Reduced, Standard or Expressive.",
        _ => "displayName must be 1 to 80 characters without control characters."
    };
}
