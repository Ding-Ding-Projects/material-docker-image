using System.Text.Json;
using MaterialDockerImage.UI;

/// <summary>Checks for the extended schedules (more settings, date ranges, https and Home Assistant sources) and for sound volume and quiet hours. Framework-free: no clock, disk, network or window.</summary>
public static class SchedulesChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    static ScheduleRule Rule(string setting, string value, string start = "00:00", string end = "00:00", string from = "", string to = "", string source = ScheduleSources.Local, int priority = 0, string off = "", string id = "r") =>
        new() { Id = id, Setting = setting, Value = value, Start = start, End = end, FromDate = from, ToDate = to, Source = source, Priority = priority, OffValue = off };

    static DateTime At(string date, int hour = 12, int minute = 0) => DateTime.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).AddHours(hour).AddMinutes(minute);

    public static int Run()
    {
        count = 0;
        DateRanges();
        Settings();
        SourcePayloads();
        Addresses();
        HomeAssistant();
        Polling();
        Lending();
        Persistence();
        Sounds();
        Wav();
        Cantonese();
        Console.WriteLine($"Schedules and sound checks: {count} passed.");
        return count;
    }

    static void DateRanges()
    {
        var holiday = Rule(ScheduleSettings.Language, "Cantonese", from: "2026-12-24", to: "2026-12-26");
        Check(ScheduleEvaluator.IsActive(holiday, At("2026-12-24", 0, 0)) && ScheduleEvaluator.IsActive(holiday, At("2026-12-26", 23, 59)), "a whole-day rule includes both end dates, first minute to last minute");
        Check(!ScheduleEvaluator.IsActive(holiday, At("2026-12-23", 23, 59)) && !ScheduleEvaluator.IsActive(holiday, At("2026-12-27", 0, 0)), "the day before the first date and the day after the last date are outside");
        Check(ScheduleEvaluator.IsActive(Rule(ScheduleSettings.Theme, "Dark", from: "2026-12-24"), At("2030-01-01")) && !ScheduleEvaluator.IsActive(Rule(ScheduleSettings.Theme, "Dark", from: "2026-12-24"), At("2026-12-23")), "a first date alone has no upper bound");
        Check(ScheduleEvaluator.IsActive(Rule(ScheduleSettings.Theme, "Dark", to: "2026-12-24"), At("2020-01-01")) && !ScheduleEvaluator.IsActive(Rule(ScheduleSettings.Theme, "Dark", to: "2026-12-24"), At("2026-12-25")), "a last date alone has no lower bound");
        Check(ScheduleEvaluator.IsActive(Rule(ScheduleSettings.Theme, "Dark", from: "2026-12-24", to: "2026-12-24"), At("2026-12-24", 13, 5)), "one single date is a valid range");
        var night = Rule(ScheduleSettings.Language, "Cantonese", "22:00", "06:00", "2026-12-24", "2026-12-26");
        Check(ScheduleEvaluator.IsActive(night, At("2026-12-24", 22, 30)) && !ScheduleEvaluator.IsActive(night, At("2026-12-24", 3, 0)), "a cross-midnight window starts on its first date, and the early morning of that date belongs to the day before");
        Check(ScheduleEvaluator.IsActive(night, At("2026-12-27", 5, 59)) && !ScheduleEvaluator.IsActive(night, At("2026-12-27", 6, 0)), "a window that started on the last date finishes after midnight, then ends at its end time");
        Check(!ScheduleEvaluator.IsActive(night, At("2026-12-27", 22, 30)), "a window that would start after the last date never begins");
        Check(ScheduleEvaluator.IsActive(night, At("2026-12-25", 1, 0)), "inside the range, the early hours continue the previous evening's window");
        var mondayDec = Rule(ScheduleSettings.Language, "Cantonese", from: "2026-12-01", to: "2026-12-31"); mondayDec.EveryDay = false; mondayDec.Days = [1];
        Check(ScheduleEvaluator.IsActive(mondayDec, At("2026-12-07")) && !ScheduleEvaluator.IsActive(mondayDec, At("2026-12-08")) && !ScheduleEvaluator.IsActive(mondayDec, At("2027-01-04")), "weekdays and a date range must both match");
        var leap = Rule(ScheduleSettings.Theme, "Dark", from: "2028-02-29", to: "2028-02-29");
        Check(ScheduleEvaluator.IsActive(leap, At("2028-02-29")) && !ScheduleEvaluator.IsActive(leap, At("2028-03-01")), "a leap day is a valid date");
        foreach (var bad in new[] { "2026-2-3", "2026/12/24", "24-12-2026", "2026-02-30", "2027-02-29", "2026-13-01", " 2026-12-24", "2026-12-24 ", "abcd-ef-gh", "2026-12-2" })
            Check(!ScheduleEvaluator.TryParseDate(bad, out _) && ScheduleEvaluator.Validate(Rule(ScheduleSettings.Theme, "Dark", from: bad)) != null && !ScheduleEvaluator.IsActive(Rule(ScheduleSettings.Theme, "Dark", from: bad), At("2026-12-24")), "date \"" + bad + "\" is refused and never active");
        Check(ScheduleEvaluator.Validate(Rule(ScheduleSettings.Theme, "Dark", from: "2026-12-26", to: "2026-12-24")) != null && ScheduleEvaluator.Validate(Rule(ScheduleSettings.Theme, "Dark", from: "2026-12-24", to: "2026-12-26")) == null, "a last date before the first date is refused");
        Check(ScheduleEvaluator.Describe(holiday).Contains("2026-12-24 to 2026-12-26") && ScheduleEvaluator.Describe(Rule(ScheduleSettings.Theme, "Dark", from: "2026-12-24")).Contains("from 2026-12-24"), "the description names the date range");
    }

    static void Settings()
    {
        var rules = new List<ScheduleRule>
        {
            Rule(ScheduleSettings.Density, "Compact", id: "d"), Rule(ScheduleSettings.Accent, "#aa3366", id: "a"), Rule(ScheduleSettings.FontSize, "18", id: "f"),
            Rule(ScheduleSettings.Motion, "Reduced", id: "m"), Rule(ScheduleSettings.DisplayName, "Night desk", id: "n"), Rule(ScheduleSettings.Language, "Bilingual", id: "l"),
        };
        var outcome = ScheduleEvaluator.Evaluate(rules, At("2026-11-02"), null);
        Check(outcome.Get("Density") == "Compact" && outcome.Get("Accent") == "#AA3366" && outcome.Get("FontSize") == "18" && outcome.Get("Motion") == "Reduced" && outcome.Get("DisplayName") == "Night desk" && outcome.Language == "Bilingual" && outcome.ActiveRuleIds.Count == 6, "every schedulable setting resolves independently and accent colours are normalized");
        var low = Rule(ScheduleSettings.Density, "Spacious", priority: 1, id: "low"); var high = Rule(ScheduleSettings.Density, "Compact", priority: 5, id: "high"); var later = Rule(ScheduleSettings.Density, "Comfortable", priority: 5, id: "later");
        Check(ScheduleEvaluator.Evaluate([low, high], At("2026-11-02"), null).Get("Density") == "Compact", "the higher priority wins within one setting");
        Check(ScheduleEvaluator.Evaluate([high, later, low], At("2026-11-02"), null).Get("Density") == "Comfortable", "an equal priority is won by the later rule");
        Check(ScheduleEvaluator.Evaluate([high, Rule(ScheduleSettings.Motion, "Off", priority: -50)], At("2026-11-02"), null).Get("Motion") == "Off", "priorities are not shared between different settings");
        Check(ScheduleEvaluator.Evaluate([Rule(ScheduleSettings.Density, "Compact", from: "2027-01-01", priority: 9), Rule(ScheduleSettings.Density, "Spacious")], At("2026-11-02"), null).Get("Density") == "Spacious", "an inactive higher priority rule does not block an active one");
        foreach (var (setting, value, allowed) in new[]
        {
            ("Density", "Compact", true), ("Density", "compact", false), ("Density", "Tiny", false), ("Accent", "#3A6FD8", true), ("Accent", "#3a6fd8", true), ("Accent", "3A6FD8", false), ("Accent", "#3A6FD", false), ("Accent", "#3A6FD8FF", false), ("Accent", "#GGGGGG", false),
            ("FontSize", "12", true), ("FontSize", "22", true), ("FontSize", "11", false), ("FontSize", "23", false), ("FontSize", "016", false), ("FontSize", "1a", false), ("FontSize", "-5", false), ("FontSize", "", false), ("FontSize", "٢٠", false),
            ("Motion", "Expressive", true), ("Motion", "expressive", false), ("Motion", "Wild", false), ("DisplayName", "Lab", true), ("DisplayName", "", false), ("DisplayName", "   ", false), ("DisplayName", "bad\u0007name", false),
            ("Language", "Klingon", false), ("Theme", "System", true), ("Nonsense", "x", false)
        })
            Check(ScheduleEvaluator.ValueAllowed(setting, value) == allowed, $"{setting} value \"{value}\" is {(allowed ? "accepted" : "refused")}");
        Check(!ScheduleEvaluator.ValueAllowed("DisplayName", new string('x', 81)) && ScheduleEvaluator.ValueAllowed("DisplayName", new string('x', 80)), "a display name is 1 to 80 characters");
        Check(ScheduleEvaluator.Validate(Rule("Wallpaper", "x")) != null && ScheduleEvaluator.Validate(Rule(ScheduleSettings.Language, "English", source: "Carrier pigeon")) != null, "unknown settings and sources are refused");
        Check(ScheduleEvaluator.Validate(Rule(ScheduleSettings.Density, "", source: ScheduleSources.File)) == null && ScheduleEvaluator.Validate(Rule(ScheduleSettings.Density, "", source: ScheduleSources.Https)) == null, "File and Https rules need no local value");
        Check(ScheduleEvaluator.Validate(Rule(ScheduleSettings.Density, "", source: ScheduleSources.HomeAssistant)) != null && ScheduleEvaluator.Validate(Rule(ScheduleSettings.Density, "Compact", source: ScheduleSources.HomeAssistant, off: "Huge")) != null && ScheduleEvaluator.Validate(Rule(ScheduleSettings.Density, "Compact", source: ScheduleSources.HomeAssistant, off: "Spacious")) == null, "Home Assistant rules need an allowed on value and an allowed or empty off value");
        var messy = ScheduleEvaluator.Normalize([new ScheduleRule { Id = "x", Setting = ScheduleSettings.Accent, Value = "#abcdef", OffValue = "#123abc", FromDate = null!, ToDate = null!, Start = null!, End = null! }]);
        Check(messy[0].Value == "#ABCDEF" && messy[0].OffValue == "#123ABC" && messy[0].FromDate == "" && messy[0].ToDate == "", "normalizing repairs nulls and upper-cases accent colours");
    }

    static void SourcePayloads()
    {
        var full = "{\"version\":1,\"language\":\"Cantonese\",\"theme\":\"Dark\",\"density\":\"Spacious\",\"accent\":\"#12abEF\",\"fontSize\":17,\"motion\":\"Expressive\",\"displayName\":\"Lab desk\"}";
        var parsed = ScheduleEvaluator.ParseSource(full, full.Length, ScheduleEvaluator.MaxHttpsBytes, "response");
        Check(parsed.Error == "" && parsed.Values.Count == 7 && parsed.Get("Accent") == "#12ABEF" && parsed.Get("FontSize") == "17" && parsed.Get("DisplayName") == "Lab desk", "a complete payload is accepted and normalized");
        Check(ScheduleEvaluator.ParseSource("{\"version\":1}", 13, 65536, "response").Error == "" && ScheduleEvaluator.ParseSource("{\"version\":1}", 13, 65536, "response").Values.Count == 0, "a payload with only a version is valid and empty");
        var bad = new Dictionary<string, string>
        {
            ["not json"] = "not json", ["array root"] = "[1]", ["null root"] = "null", ["string root"] = "\"x\"", ["no version"] = "{\"theme\":\"Dark\"}", ["string version"] = "{\"version\":\"1\"}", ["version 2"] = "{\"version\":2}",
            ["unknown field"] = "{\"version\":1,\"wallpaper\":\"x\"}", ["upper-case field"] = "{\"version\":1,\"Theme\":\"Dark\"}", ["duplicate field"] = "{\"version\":1,\"theme\":\"Dark\",\"theme\":\"Light\"}",
            ["bad language"] = "{\"version\":1,\"language\":\"Klingon\"}", ["bad theme"] = "{\"version\":1,\"theme\":\"Purple\"}", ["bad density"] = "{\"version\":1,\"density\":\"Huge\"}", ["bad accent name"] = "{\"version\":1,\"accent\":\"red\"}",
            ["accent number"] = "{\"version\":1,\"accent\":123456}", ["fontSize string"] = "{\"version\":1,\"fontSize\":\"16\"}", ["fontSize fraction"] = "{\"version\":1,\"fontSize\":16.5}", ["fontSize low"] = "{\"version\":1,\"fontSize\":5}", ["fontSize high"] = "{\"version\":1,\"fontSize\":99}",
            ["bad motion"] = "{\"version\":1,\"motion\":\"Wild\"}", ["empty name"] = "{\"version\":1,\"displayName\":\"\"}", ["control name"] = "{\"version\":1,\"displayName\":\"a\\u0007b\"}", ["name null"] = "{\"version\":1,\"displayName\":null}",
            ["nested value"] = "{\"version\":1,\"language\":{\"a\":{\"b\":{\"c\":{\"d\":1}}}}}", ["good then bad"] = "{\"version\":1,\"theme\":\"Dark\",\"density\":\"Huge\"}", ["trailing comma"] = "{\"version\":1,}", ["whitespace only"] = "   ", ["empty"] = ""
        };
        foreach (var (name, json) in bad)
        {
            var result = ScheduleEvaluator.ParseSource(json, json.Length, ScheduleEvaluator.MaxHttpsBytes, "response");
            Check(result.Error.Length > 0 && result.Values.Count == 0, "payload refused whole: " + name);
        }
        Check(ScheduleEvaluator.ParseSource("{\"version\":1}", 65536, 65536, "response").Error == "" && ScheduleEvaluator.ParseSource("{\"version\":1}", 65537, 65536, "response").Error.Contains("64 KiB"), "the 64 KiB limit includes 65536 bytes and refuses one byte more");
        var padded = "{\"version\":1,\"theme\":\"Light\"" + new string(' ', 60000) + "}";
        Check(ScheduleEvaluator.ParseSource(padded, padded.Length, ScheduleEvaluator.MaxHttpsBytes, "response").Get("Theme") == "Light" && ScheduleEvaluator.ParseSource(padded, padded.Length, ScheduleEvaluator.MaxFileBytes, "schedule file").Error.Contains("4 KiB"), "a 60 KB payload is fine for https but over the 4 KiB limit of the local file");
        var file = ScheduleEvaluator.ParseFile("{\"version\":1,\"density\":\"Compact\",\"fontSize\":15}", 40);
        var fileRule = Rule(ScheduleSettings.FontSize, "", source: ScheduleSources.File);
        Check(ScheduleEvaluator.Evaluate([fileRule, Rule(ScheduleSettings.Density, "", source: ScheduleSources.File, id: "d")], At("2026-11-02"), file).Get("FontSize") == "15" && ScheduleEvaluator.Evaluate([fileRule], At("2026-11-02"), null).Values.Count == 0, "File rules read their own setting from the file and stay inactive without one");
        var theme = Rule(ScheduleSettings.Theme, "", source: ScheduleSources.File);
        Check(ScheduleEvaluator.Evaluate([theme], At("2026-11-02"), file).Values.Count == 0, "a File rule whose setting the file does not carry stays inactive");
        var httpsValues = ScheduleEvaluator.ParseSource("{\"version\":1,\"theme\":\"Light\"}", 30, ScheduleEvaluator.MaxHttpsBytes, "response");
        var httpsRule = Rule(ScheduleSettings.Theme, "", source: ScheduleSources.Https);
        Check(ScheduleEvaluator.Evaluate([httpsRule], At("2026-11-02"), null, new ScheduleExternal(httpsValues, HomeAssistantState.None)).Theme == "Light" && ScheduleEvaluator.Evaluate([httpsRule], At("2026-11-02"), file, new ScheduleExternal(null, HomeAssistantState.None)).Values.Count == 0, "Https rules use only the Https source, never the file, and stay inactive without an answer");
    }

    static void Addresses()
    {
        foreach (var good in new[] { "https://example.com/schedule.json", "https://example.com:8443/a/b.json?page=2", "https://192.168.1.5/x", "HTTPS://Example.COM/x", "https://[2001:db8::1]/x" })
            Check(ScheduleSourceRules.TryNormalizeHttpsUrl(good, out var url, out _) && url.StartsWith("https://", StringComparison.Ordinal), "https address accepted: " + good);
        foreach (var bad in new[] { "", "   ", "http://example.com/x", "ftp://example.com/x", "javascript:alert(1)", "file:///C:/x.json", "example.com/x", "https://", "https:///x", "https://user:pw@example.com/x", "https://user@example.com/x", "https://example.com/x#frag", "https://example.com/ x", "https://example.com/x?token=abc", "https://example.com/x?api_key=abc", "https://example.com/x?a=1&password=2", "https://example.com/x?key=1", "https://example.com/x?Secret=1", "https://example.com/x?sig=1", "https://" + new string('a', 600) + ".com/x", "https://exa\tmple.com/x" })
            Check(!ScheduleSourceRules.TryNormalizeHttpsUrl(bad, out var rejected, out var error) && rejected == "" && error.Length > 0, "https address refused: " + (bad.Length > 40 ? bad[..40] : bad));
        Check(ScheduleSourceRules.TryNormalizeHttpsUrl("https://example.com/x?keyword=1&page=2", out _, out _), "ordinary query parameters that merely contain letters of a secret name are fine");
        foreach (var good in new[] { "http://homeassistant.local:8123", "http://192.168.1.5:8123", "http://10.0.0.2", "http://172.16.0.1:8123", "http://172.31.255.255", "http://localhost:8123", "http://127.0.0.1:8123", "http://[::1]:8123", "http://homeassistant:8123", "http://ha.lan", "https://ha.example.com", "https://ha.example.com:8123/", "http://169.254.1.1", "http://[fd00::1]:8123" })
            Check(ScheduleSourceRules.TryNormalizeHomeAssistantUrl(good, out var url, out _) && !url.EndsWith('/'), "Home Assistant address accepted: " + good);
        foreach (var bad in new[] { "", "ftp://ha.local", "http://example.com", "http://8.8.8.8:8123", "http://172.32.0.1", "http://172.15.0.1", "http://192.169.0.1", "http://ha.local/api", "http://ha.local?x=1", "http://ha.local#x", "http://user:pw@ha.local", "ha.local:8123", "http://", "http://ha .local" })
            Check(!ScheduleSourceRules.TryNormalizeHomeAssistantUrl(bad, out var rejected, out var error) && rejected == "" && error.Length > 0, "Home Assistant address refused: " + bad);
        ScheduleSourceRules.TryNormalizeHomeAssistantUrl("http://example.com", out _, out var clear);
        Check(clear.Contains("https"), "a public host over plain http is refused with an https hint");
        Check(ScheduleSourceRules.HomeAssistantStateUrl("http://homeassistant.local:8123/", "input_boolean.night") == "http://homeassistant.local:8123/api/states/input_boolean.night" && ScheduleSourceRules.HomeAssistantStateUrl("http://homeassistant.local:8123", "Bad Entity") == null && ScheduleSourceRules.HomeAssistantStateUrl("http://example.com", "light.x") == null, "the state address is built only from valid parts");
        foreach (var good in new[] { "input_boolean.night", "light.kitchen_1", "binary_sensor.door" }) Check(ScheduleSourceRules.IsValidEntityId(good), "entity accepted: " + good);
        foreach (var bad in new[] { "", "nodot", "Input.boolean", "a.b.c", "light.", ".x", "light.kitchen light", "light.kitchen/../x", "light.kitchen\n", null! }) Check(!ScheduleSourceRules.IsValidEntityId(bad), "entity refused: " + (bad ?? "null"));
        Check(ScheduleSourceRules.IsValidToken("eyJhbGciOi.abc-DEF_123") && !ScheduleSourceRules.IsValidToken("") && !ScheduleSourceRules.IsValidToken(null) && !ScheduleSourceRules.IsValidToken("two words") && !ScheduleSourceRules.IsValidToken("tok\u00e9n") && !ScheduleSourceRules.IsValidToken("a\nb") && !ScheduleSourceRules.IsValidToken(new string('a', 513)) && ScheduleSourceRules.IsValidToken(new string('a', 512)), "tokens are 1 to 512 printable ASCII characters without spaces");
    }

    static void HomeAssistant()
    {
        string Doc(string entity, string state) => "{\"entity_id\":\"" + entity + "\",\"state\":\"" + state + "\",\"attributes\":{\"friendly_name\":\"Night\"},\"last_changed\":\"2026-11-04T10:00:00+00:00\"}";
        (HomeAssistantState State, string Error) Parse(string json, string entity = "input_boolean.night") => ScheduleSourceRules.ParseHomeAssistantState(json, json.Length, entity);
        Check(Parse(Doc("input_boolean.night", "on")) is (HomeAssistantState.On, "") && Parse(Doc("input_boolean.night", "off")) is (HomeAssistantState.Off, ""), "on and off map to their states");
        Check(Parse(Doc("input_boolean.night", "ON")).State == HomeAssistantState.On && Parse(Doc("input_boolean.night", "Off")).State == HomeAssistantState.Off, "the state text is case-insensitive");
        foreach (var other in new[] { "unavailable", "unknown", "home", "23.5", "", "true", "1", "on " }) Check(Parse(Doc("input_boolean.night", other)) is (HomeAssistantState.Indeterminate, ""), "state \"" + other + "\" is neither on nor off");
        foreach (var (name, json) in new[] { ("empty", ""), ("not json", "oops"), ("array", "[]"), ("other entity", Doc("input_boolean.day", "on")), ("no entity", "{\"state\":\"on\"}"), ("no state", "{\"entity_id\":\"input_boolean.night\"}"), ("number state", "{\"entity_id\":\"input_boolean.night\",\"state\":1}"), ("null state", "{\"entity_id\":\"input_boolean.night\",\"state\":null}"), ("entity number", "{\"entity_id\":5,\"state\":\"on\"}"), ("message", "{\"message\":\"Entity not found.\"}") })
            Check(Parse(json).State == HomeAssistantState.None && Parse(json).Error.Length > 0, "Home Assistant answer refused: " + name);
        Check(ScheduleSourceRules.ParseHomeAssistantState(Doc("input_boolean.night", "on"), 70000, "input_boolean.night").Error.Contains("64 KiB"), "an oversized Home Assistant answer is refused");
        var rule = Rule(ScheduleSettings.Theme, "Dark", source: ScheduleSources.HomeAssistant, off: "Light");
        Check(ScheduleEvaluator.Evaluate([rule], At("2026-11-02"), null, new ScheduleExternal(null, HomeAssistantState.On)).Theme == "Dark" && ScheduleEvaluator.Evaluate([rule], At("2026-11-02"), null, new ScheduleExternal(null, HomeAssistantState.Off)).Theme == "Light", "the entity being on applies Value and off applies OffValue");
        Check(ScheduleEvaluator.Evaluate([rule], At("2026-11-02"), null, new ScheduleExternal(null, HomeAssistantState.Indeterminate)).Values.Count == 0 && ScheduleEvaluator.Evaluate([rule], At("2026-11-02"), null, new ScheduleExternal(null, HomeAssistantState.None)).Values.Count == 0 && ScheduleEvaluator.Evaluate([rule], At("2026-11-02"), null).Values.Count == 0, "an unknown or missing state applies nothing");
        var noOff = Rule(ScheduleSettings.Theme, "Dark", source: ScheduleSources.HomeAssistant);
        Check(ScheduleEvaluator.Evaluate([noOff], At("2026-11-02"), null, new ScheduleExternal(null, HomeAssistantState.Off)).Values.Count == 0, "without an off value, off applies nothing");
        var windowed = Rule(ScheduleSettings.Theme, "Dark", "20:00", "22:00", source: ScheduleSources.HomeAssistant);
        Check(ScheduleEvaluator.Evaluate([windowed], At("2026-11-02", 21), null, new ScheduleExternal(null, HomeAssistantState.On)).Theme == "Dark" && ScheduleEvaluator.Evaluate([windowed], At("2026-11-02", 23), null, new ScheduleExternal(null, HomeAssistantState.On)).Values.Count == 0, "a Home Assistant rule still honours its time window");
    }

    static void Polling()
    {
        var t0 = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        Check(ScheduleSourceRules.ClampPollMinutes(1, 5) == 5 && ScheduleSourceRules.ClampPollMinutes(-9, 5) == 5 && ScheduleSourceRules.ClampPollMinutes(99999, 5) == 1440 && ScheduleSourceRules.ClampPollMinutes(30, 5) == 30 && ScheduleSourceRules.ClampPollMinutes(1, 1) == 1, "poll minutes are clamped to the minimum and 24 hours");
        Check(ScheduleSourceRules.PollDue(null, t0, 15, 5), "a source never tried is due at once");
        Check(!ScheduleSourceRules.PollDue(t0, t0.AddMinutes(14).AddSeconds(59), 15, 5) && ScheduleSourceRules.PollDue(t0, t0.AddMinutes(15), 15, 5), "a poll is due exactly when the interval has elapsed");
        Check(!ScheduleSourceRules.PollDue(t0, t0.AddMinutes(4).AddSeconds(59), 1, 5) && ScheduleSourceRules.PollDue(t0, t0.AddMinutes(5), 1, 5), "an interval below the 5 minute minimum is raised to it");
        Check(ScheduleSourceRules.PollDue(t0, t0.AddMinutes(-3), 15, 5), "a clock that moved backwards polls again instead of waiting forever");
        Check(ScheduleSourceRules.MinHttpsPollMinutes >= 5 && ScheduleSourceRules.RequestTimeoutSeconds == 10 && ScheduleEvaluator.MaxHttpsBytes == 65536, "the documented limits are the real constants");
        var good = ScheduleEvaluator.ParseSource("{\"version\":1,\"theme\":\"Light\"}", 30, 65536, "response");
        var first = ScheduleSourceRules.Merge(null, new SourceFetch(true, good, HomeAssistantState.None, ""), t0);
        Check(!first.Offline && first.Values == good && first.LastGoodUtc == t0 && first.LastAttemptUtc == t0, "a success stores the data and the time");
        var failed = ScheduleSourceRules.Merge(first, new SourceFetch(false, null, HomeAssistantState.None, "The server could not be reached."), t0.AddMinutes(20));
        Check(failed.Offline && failed.Values == good && failed.LastGoodUtc == t0 && failed.LastAttemptUtc == t0.AddMinutes(20) && failed.Error == "The server could not be reached.", "a failure keeps the last good data, records the failure and does not lose the good time");
        var cold = ScheduleSourceRules.Merge(null, new SourceFetch(false, null, HomeAssistantState.None, ""), t0);
        Check(cold.Offline && cold.Values == null && cold.Error.Length > 0 && cold.LastGoodUtc == null, "a failure with nothing before it stays empty and still reports an error");
        var recovered = ScheduleSourceRules.Merge(failed, new SourceFetch(true, good, HomeAssistantState.None, ""), t0.AddMinutes(40));
        Check(!recovered.Offline && recovered.LastGoodUtc == t0.AddMinutes(40), "a later success clears the failure");
        var haOn = ScheduleSourceRules.Merge(null, new SourceFetch(true, null, HomeAssistantState.On, ""), t0);
        var haFail = ScheduleSourceRules.Merge(haOn, new SourceFetch(false, null, HomeAssistantState.None, "The server could not be reached."), t0.AddMinutes(5));
        Check(haFail.State == HomeAssistantState.On && haFail.Offline, "a failed Home Assistant poll keeps the last known state");
        // The point of all of it: a failed poll must not change what the schedule lends.
        var rules = new List<ScheduleRule> { Rule(ScheduleSettings.Theme, "", source: ScheduleSources.Https), Rule(ScheduleSettings.Density, "Compact", source: ScheduleSources.HomeAssistant, off: "Spacious", id: "ha") };
        var live = new UserSettings { Theme = "Dark", Density = "Comfortable" };
        var before = ScheduleEvaluator.Evaluate(rules, At("2026-11-04"), null, new ScheduleExternal(first.Values, haOn.State));
        live.ApplyScheduled(before.Values);
        var after = ScheduleEvaluator.Evaluate(rules, At("2026-11-04"), null, new ScheduleExternal(failed.Values, haFail.State));
        Check(before.Theme == "Light" && live.Theme == "Light" && live.Density == "Compact" && after.Values.SequenceEqual(before.Values) && !live.ApplyScheduled(after.Values), "a network failure changes no setting");
        var describeOnline = ScheduleSourceRules.Describe(first, t0.AddMinutes(3)); var describeOffline = ScheduleSourceRules.Describe(failed, t0.AddMinutes(25)); var describeCold = ScheduleSourceRules.Describe(cold, t0);
        Check(describeOnline == "Online. Last checked 3 minutes ago." && describeOffline.Contains("Offline") && describeOffline.Contains("25 minutes ago") && describeOffline.Contains("nothing was changed") && describeCold.Contains("Nothing from this source is applied") && ScheduleSourceRules.Describe(SourceSnapshot.Empty, t0) == "Not checked yet.", "the state line is honest about online, offline and never-checked");
    }

    static void Lending()
    {
        var s = new UserSettings { Language = "English", Theme = "Dark", Density = "Comfortable", SeedColor = "#3A6FD8", FontSize = 14.5, MotionLevel = "Standard", DisplayName = "Mine" };
        var lend = new Dictionary<string, string> { ["Density"] = "Compact", ["Accent"] = "#AA3366", ["FontSize"] = "18", ["Motion"] = "Reduced", ["DisplayName"] = "Night desk", ["Theme"] = "Light" };
        Check(s.ApplyScheduled(lend) && s.Density == "Compact" && s.SeedColor == "#AA3366" && s.FontSize == 18 && s.MotionLevel == "Reduced" && s.DisplayName == "Night desk" && s.Theme == "Light" && s.ScheduleOverrideActive, "an active schedule lends every kind of setting to the live values");
        var saved = JsonSerializer.Deserialize<UserSettings>(s.ToJson())!;
        Check(saved.Density == "Comfortable" && saved.SeedColor == "#3A6FD8" && saved.FontSize == 14.5 && saved.MotionLevel == "Standard" && saved.DisplayName == "Mine" && saved.Theme == "Dark", "the saved file keeps every base value while the schedule lends");
        Check(s.Density == "Compact" && s.FontSize == 18 && s.DisplayName == "Night desk", "saving does not disturb the live values");
        Check(!s.ApplyScheduled(lend), "an unchanged schedule reports no change");
        Check(s.ApplyScheduled(new Dictionary<string, string> { ["Density"] = "Compact", ["Theme"] = "Light" }) && s.SeedColor == "#3A6FD8" && s.FontSize == 14.5 && s.MotionLevel == "Standard" && s.DisplayName == "Mine" && s.Density == "Compact" && s.ScheduleOverrideActive, "a setting that leaves the schedule is released on its own");
        s.Density = "Spacious";
        s.ApplyScheduled(new Dictionary<string, string> { ["Density"] = "Compact", ["Theme"] = "Light" });
        Check(s.Density == "Compact", "after a manual change the schedule lends again");
        s.Density = "Comfortable"; // the person changes it by hand while it is lent
        Check(s.ApplyScheduled(new Dictionary<string, string>()) && s.Density == "Comfortable" && s.Theme == "Dark" && !s.ScheduleOverrideActive, "a manual change during a lend becomes the new base and everything else is released");
        s.ApplyScheduled(lend); s.ClearScheduledOverride();
        Check(s.Density == "Comfortable" && s.FontSize == 14.5 && s.DisplayName == "Mine" && s.Theme == "Dark" && !s.ScheduleOverrideActive, "clearing restores every base value");
        s.ApplyScheduled(new Dictionary<string, string> { ["FontSize"] = "16" }); s.FontSize = 20;
        s.ApplyScheduled(new Dictionary<string, string>());
        Check(s.FontSize == 20, "a font size changed during the lend is kept");
        var legacy = new UserSettings { Language = "English", Theme = "Dark" };
        Check(legacy.ApplyScheduled("Cantonese", "Light") && legacy.Language == "Cantonese" && legacy.Theme == "Light" && legacy.ApplyScheduled(null, null) && legacy.Language == "English" && legacy.Theme == "Dark", "the language and theme overload still works");
    }

    static void Persistence()
    {
        var s = new UserSettings { ScheduleHttpsUrl = "https://example.com/x?page=2", ScheduleHttpsPollMinutes = 30, ScheduleHaBaseUrl = "http://homeassistant.local:8123/", ScheduleHaEntity = "input_boolean.night", ScheduleHaPollMinutes = 3, SoundVolume = 40, QuietHoursEnabled = true, QuietHoursStart = "21:30", QuietHoursEnd = "06:15" };
        s.ScheduleRules.Add(Rule(ScheduleSettings.Accent, "#aa3366", "20:00", "07:00", "2026-12-01", "2026-12-31", ScheduleSources.HomeAssistant, 2, "#112233"));
        var back = JsonSerializer.Deserialize<UserSettings>(s.ToJson())!; back.Normalize();
        var r = back.ScheduleRules.Single();
        Check(back.ScheduleHttpsUrl == "https://example.com/x?page=2" && back.ScheduleHttpsPollMinutes == 30 && back.ScheduleHaBaseUrl == "http://homeassistant.local:8123" && back.ScheduleHaEntity == "input_boolean.night" && back.ScheduleHaPollMinutes == 3 && back.SoundVolume == 40 && back.QuietHoursEnabled && back.QuietHoursStart == "21:30" && back.QuietHoursEnd == "06:15", "source addresses, intervals, volume and quiet hours survive a save and load");
        Check(r.Setting == "Accent" && r.Value == "#AA3366" && r.OffValue == "#112233" && r.FromDate == "2026-12-01" && r.ToDate == "2026-12-31" && r.Source == ScheduleSources.HomeAssistant && r.Priority == 2, "a rule with every new field survives a save and load");
        var wrecked = new UserSettings { ScheduleHttpsUrl = "http://example.com/x", ScheduleHttpsPollMinutes = 1, ScheduleHaBaseUrl = "http://example.com", ScheduleHaEntity = "Bad Entity", ScheduleHaPollMinutes = 0, SoundVolume = 900, QuietHoursStart = "25:99", QuietHoursEnd = "x" };
        wrecked.Normalize();
        Check(wrecked.ScheduleHttpsUrl == "" && wrecked.ScheduleHttpsPollMinutes == 5 && wrecked.ScheduleHaBaseUrl == "" && wrecked.ScheduleHaEntity == "" && wrecked.ScheduleHaPollMinutes == 1 && wrecked.SoundVolume == 100 && wrecked.QuietHoursStart == "22:00" && wrecked.QuietHoursEnd == "07:00", "loading repairs unsafe addresses, intervals, volume and times");
        Check(new UserSettings { SoundVolume = -5 } is { } low && Normalized(low).SoundVolume == 0, "a negative volume becomes silent");
        var json = s.ToJson();
        var names = typeof(UserSettings).GetProperties().Select(p => p.Name).ToList();
        Check(!names.Any(n => n.Contains("Token", StringComparison.OrdinalIgnoreCase) || n.Contains("Password", StringComparison.OrdinalIgnoreCase) || n.Contains("Secret", StringComparison.OrdinalIgnoreCase)) && !json.Contains("Bearer", StringComparison.OrdinalIgnoreCase) && !json.Contains("token", StringComparison.OrdinalIgnoreCase), "settings.json has no place for a token or password");
        Check(ElementLocks.Properties.Values.SelectMany(v => v).All(names.Contains) && ElementLocks.Properties.ContainsKey("schedulehttps") && ElementLocks.Properties.ContainsKey("schedulehomeassistant") && ElementLocks.Properties.ContainsKey("soundvolume") && ElementLocks.Properties.ContainsKey("quiethours"), "the new rows can be locked and map to real settings");
        var defaults = new UserSettings();
        Check(defaults.SoundVolume == 100 && !defaults.QuietHoursEnabled && defaults.ScheduleHttpsPollMinutes == 15 && defaults.ScheduleHttpsUrl == "" && defaults.ScheduleHaBaseUrl == "", "defaults leave sounds at full volume, quiet hours off and no source configured");
    }

    static UserSettings Normalized(UserSettings s) { s.Normalize(); return s; }

    static void Sounds()
    {
        var at = (int h, int m) => new DateTime(2026, 11, 4, h, m, 0);
        Check(!QuietHours.IsQuiet(true, "22:00", "07:00", at(21, 59)) && QuietHours.IsQuiet(true, "22:00", "07:00", at(22, 0)) && QuietHours.IsQuiet(true, "22:00", "07:00", at(23, 59)), "cross-midnight quiet hours start at the start minute");
        Check(QuietHours.IsQuiet(true, "22:00", "07:00", at(0, 0)) && QuietHours.IsQuiet(true, "22:00", "07:00", at(6, 59)) && !QuietHours.IsQuiet(true, "22:00", "07:00", at(7, 0)) && !QuietHours.IsQuiet(true, "22:00", "07:00", at(12, 0)), "cross-midnight quiet hours continue after midnight and end exactly at the end minute");
        Check(!QuietHours.IsQuiet(true, "13:00", "15:00", at(12, 59)) && QuietHours.IsQuiet(true, "13:00", "15:00", at(13, 0)) && QuietHours.IsQuiet(true, "13:00", "15:00", at(14, 59)) && !QuietHours.IsQuiet(true, "13:00", "15:00", at(15, 0)), "same-day quiet hours include the start and exclude the end");
        Check(!QuietHours.IsQuiet(true, "09:00", "09:00", at(9, 0)) && !QuietHours.IsQuiet(true, "09:00", "09:00", at(3, 0)), "equal times mean no quiet window");
        Check(!QuietHours.IsQuiet(false, "22:00", "07:00", at(23, 0)) && !QuietHours.IsQuiet(true, "25:00", "07:00", at(23, 0)) && !QuietHours.IsQuiet(true, "22:00", null, at(23, 0)) && !QuietHours.IsQuiet(true, "", "", at(23, 0)), "a disabled switch or unreadable times never silence anything");
        Check(QuietHours.IsQuiet(true, "00:00", "23:59", at(0, 0)) && QuietHours.IsQuiet(true, "00:00", "23:59", at(23, 58)) && !QuietHours.IsQuiet(true, "00:00", "23:59", at(23, 59)), "a nearly all-day window leaves only its last minute");
        Check(QuietHours.IsQuiet(true, "23:59", "00:01", at(23, 59)) && QuietHours.IsQuiet(true, "23:59", "00:01", at(0, 0)) && !QuietHours.IsQuiet(true, "23:59", "00:01", at(0, 1)), "a two-minute window across midnight works");
        var s = new UserSettings { QuietHoursEnabled = true, QuietHoursStart = "22:00", QuietHoursEnd = "07:00" };
        Check(!SoundCueRules.MayPlay(s, false, at(23, 0)) && SoundCueRules.MayPlay(s, false, at(12, 0)), "a cue is silenced inside the quiet hours and plays outside them");
        s.SoundVolume = 0;
        Check(!SoundCueRules.MayPlay(s, false, at(12, 0)), "volume zero silences every cue");
        s.SoundVolume = 1;
        Check(SoundCueRules.MayPlay(s, false, at(12, 0)) && !SoundCueRules.MayPlay(s, true, at(12, 0)) && !SoundCueRules.MayPlay(new UserSettings { Sounds = false }, false, at(12, 0)) && !SoundCueRules.MayPlay(new UserSettings { LowStimulation = true }, false, at(12, 0)), "the older rules (switch, Low stimulation, capture) still hold");
        Check(QuietHours.Gain(100) == 1 && QuietHours.Gain(0) == 0 && QuietHours.Gain(50) == 0.5 && QuietHours.Gain(250) == 1 && QuietHours.Gain(-9) == 0, "the volume becomes a gain from 0 to 1");
    }

    static byte[] MakeWav(short bits, ushort format, byte[] data, bool listChunk = false, uint? declaredDataSize = null)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(0u); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16u); w.Write(format); w.Write((ushort)1); w.Write(22050u); w.Write((uint)(22050 * bits / 8)); w.Write((ushort)(bits / 8)); w.Write((ushort)bits);
        if (listChunk) { w.Write("LIST"u8.ToArray()); w.Write(3u); w.Write(new byte[] { 1, 2, 3, 0 }); }
        w.Write("data"u8.ToArray()); w.Write(declaredDataSize ?? (uint)data.Length); w.Write(data);
        var bytes = ms.ToArray();
        BitConverter.GetBytes((uint)(bytes.Length - 8)).CopyTo(bytes, 4);
        return bytes;
    }

    static byte[] Pcm16(params short[] samples) { var b = new byte[samples.Length * 2]; for (var i = 0; i < samples.Length; i++) BitConverter.GetBytes(samples[i]).CopyTo(b, i * 2); return b; }
    static short[] Read16(byte[] wav, int count) { var r = new short[count]; for (var i = 0; i < count; i++) r[i] = BitConverter.ToInt16(wav, 44 + i * 2); return r; }

    static void Wav()
    {
        var wav = MakeWav(16, 1, Pcm16(0, 1000, -1000, 32767, -32768, 7));
        var half = WavScaler.Scale(wav, 0.5) ?? throw new Exception("scaling failed");
        Check(half.Length == wav.Length && half.Take(44).SequenceEqual(wav.Take(44)), "scaling keeps the header and the length");
        Check(Read16(half, 6).SequenceEqual(new short[] { 0, 500, -500, 16384, -16384, 4 }), "16-bit samples are scaled, rounded and keep their sign");
        Check(Read16(WavScaler.Scale(wav, 0)!, 6).All(x => x == 0), "gain zero is silence");
        var same = WavScaler.Scale(wav, 1)!;
        Check(same.SequenceEqual(wav) && !ReferenceEquals(same, wav), "gain one is an identical copy, not the same array");
        Check(WavScaler.Scale(wav, 7)!.SequenceEqual(wav) && Read16(WavScaler.Scale(wav, -3)!, 6).All(x => x == 0), "a gain outside 0 to 1 is clamped, never louder");
        Check(WavScaler.Scale(wav, double.NaN) == null && WavScaler.Scale(wav, double.PositiveInfinity) == null, "a gain that is not a number is refused");
        var quarter = Read16(WavScaler.Scale(wav, 0.25)!, 6);
        Check(quarter[3] == 8192 && quarter[4] == -8192 && Math.Abs(quarter[1]) == 250, "quarter volume is a quarter of the amplitude");
        var peak = Read16(WavScaler.Scale(wav, 1.0)!, 6);
        Check(peak[3] == 32767 && peak[4] == -32768, "full volume never clips the extreme samples");
        var eight = MakeWav(8, 1, [128, 255, 0, 192]);
        var eightHalf = WavScaler.Scale(eight, 0.5)!;
        Check(eightHalf[44] == 128 && eightHalf[45] == 192 && eightHalf[46] == 64 && eightHalf[47] == 160, "8-bit samples scale around their centre at 128");
        var s24 = MakeWav(24, 1, [0x00, 0x00, 0x40, 0x00, 0x00, 0xC0, 0xFF, 0xFF, 0x7F]); // 4194304, -4194304, 8388607
        var s24Half = WavScaler.Scale(s24, 0.5)!;
        int Read24(byte[] b, int at) { var v = b[at] | (b[at + 1] << 8) | (b[at + 2] << 16); return (v & 0x800000) != 0 ? v | unchecked((int)0xFF000000) : v; }
        Check(Read24(s24Half, 44) == 2097152 && Read24(s24Half, 47) == -2097152 && Read24(s24Half, 50) == 4194304, "24-bit samples are scaled with their sign");
        Check(WavScaler.Scale(MakeWav(16, 3, Pcm16(1, 2)), 0.5) == null && WavScaler.Scale(MakeWav(16, 2, Pcm16(1, 2)), 0.5) == null && WavScaler.Scale(MakeWav(16, 0x55, Pcm16(1, 2)), 0.5) == null, "compressed or floating-point audio is refused so the cue stays silent");
        Check(WavScaler.Scale(MakeWav(32, 1, new byte[8]), 0.5) == null && WavScaler.Scale(MakeWav(12, 1, new byte[8]), 0.5) == null, "unsupported bit depths are refused");
        var listed = MakeWav(16, 1, Pcm16(1000, -1000), listChunk: true);
        Check(Read16Tail(WavScaler.Scale(listed, 0.5)!, listed.Length - 4).SequenceEqual(new short[] { 500, -500 }), "an odd-sized chunk before the data is padded and skipped correctly");
        var lying = MakeWav(16, 1, Pcm16(1000, -1000, 400), declaredDataSize: 4000000);
        Check(Read16(WavScaler.Scale(lying, 0.5)!, 3).SequenceEqual(new short[] { 500, -500, 200 }), "a data size larger than the file is clamped to the bytes present");
        var odd = MakeWav(16, 1, [0xE8, 0x03, 0xE8]); // 1000 and one stray byte
        var oddScaled = WavScaler.Scale(odd, 0.5)!;
        Check(BitConverter.ToInt16(oddScaled, 44) == 500 && oddScaled[46] == 0xE8, "a stray trailing byte is left alone");
        Check(WavScaler.Scale(new byte[10], 0.5) == null && WavScaler.Scale(new byte[60], 0.5) == null && WavScaler.Scale(null!, 0.5) == null && WavScaler.Scale(new byte[WavScaler.MaxBytes + 1], 0.5) == null, "garbage, empty and oversized input is refused");
        var noData = MakeWav(16, 1, Pcm16(1)); noData[36] = (byte)'x'; // the data tag is gone
        Check(WavScaler.Scale(noData, 0.5) == null, "a file with no data chunk is refused");
        var notRiff = (byte[])wav.Clone(); notRiff[0] = (byte)'X';
        Check(WavScaler.Scale(notRiff, 0.5) == null, "a file that is not RIFF is refused");
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "tada.wav");
        if (File.Exists(system) && new FileInfo(system).Length <= WavScaler.MaxBytes)
        {
            var real = File.ReadAllBytes(system); var scaled = WavScaler.Scale(real, 0.3);
            Check(scaled != null && scaled.Length == real.Length && !scaled.SequenceEqual(real), "the real Windows tada sound scales to a quieter copy of the same length");
        }
        else Check(true, "no Windows tada.wav on this machine, so only the synthetic files were scaled");
    }

    static short[] Read16Tail(byte[] wav, int offsetFromEnd) { var start = offsetFromEnd; return [BitConverter.ToInt16(wav, start), BitConverter.ToInt16(wav, start + 2)]; }

    static void Cantonese()
    {
        var prior = MaterialDockerImage.Services.Loc.Language;
        MaterialDockerImage.Services.Loc.Language = "Cantonese";
        var keys = new[] { "Scheduled settings", "Accent colour", "Font size", "Density", "First date (optional)", "HTTPS schedule source", "Home Assistant schedule source", "Store token", "Remove token", "Check now", "Not checked yet.", "Offline: {0} Nothing from this source is applied.", "Action sound volume", "Quiet hours", "Play a test sound", "The last date is before the first date.", "The server could not be reached.", "The request timed out after 10 seconds.", "fontSize must be a whole number from 12 to 22.", "Use https for a host outside your local network so the token is not sent in clear text.", "No token is stored. Create a long-lived access token in your Home Assistant profile and enter it here.", "The entity is on.", "It is quiet hours right now, so no sound plays." };
        var missing = keys.Where(k => MaterialDockerImage.Services.Loc.T(k) == k).ToList();
        Check(missing.Count == 0, "Cantonese covers the new schedule, source and sound strings" + (missing.Count == 0 ? "" : ": " + string.Join(" | ", missing)));
        var online = ScheduleSourceRules.Describe(new SourceSnapshot(null, HomeAssistantState.None, new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc), ""), new DateTime(2026, 11, 4, 12, 7, 0, DateTimeKind.Utc), MaterialDockerImage.Services.Loc.T);
        Check(online.Contains("7") && online != "Online. Last checked 7 minutes ago." && online.Contains("分鐘"), "the state line is translated and keeps its number");
        var validationKeys = new[] { "The setting is not one that can be scheduled.", "The first date must look like 2026-12-24.", "Only https addresses are accepted.", "The entity looks like input_boolean.night_mode: lower case letters, digits and underscores, a dot between." };
        Check(validationKeys.All(k => MaterialDockerImage.Services.Loc.T(k) != k), "Cantonese covers the new validation messages");
        MaterialDockerImage.Services.Loc.Language = "English";
        Check(keys.Where(k => !k.Contains("{0}")).All(k => MaterialDockerImage.Services.Loc.T(k) == k), "English leaves the new strings unchanged");
        MaterialDockerImage.Services.Loc.Language = prior;
    }
}
