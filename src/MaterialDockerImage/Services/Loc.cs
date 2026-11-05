using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

/// <summary>Language modes (English, Cantonese, bilingual), funny levels and the local personal-vocabulary overlay.</summary>
public static partial class Loc
{
    public static string Language { get; set; } = "English";
    public static int FunnyEnglish { get; set; } = 5;
    public static int FunnyCantonese { get; set; } = 5;
    public static bool DialogEmoji { get; set; } = true;
    public static bool SchoolMode { get; set; }
    private static Dictionary<string, string> overrides = new(StringComparer.Ordinal);
    public static int OverrideCount => overrides.Count;
    public static event Action? Changed;
    public static void RaiseChanged() => Changed?.Invoke();

    private static readonly Dictionary<string, string> Cantonese = new(StringComparer.Ordinal)
    {
        ["Build"] = "建立映像", ["Creator"] = "Dockerfile 工房", ["Images"] = "映像", ["Containers"] = "容器", ["Compose"] = "Compose 專案",
        ["Canvas"] = "畫布", ["Bake"] = "Bake 建置", ["Security"] = "安全", ["Contexts"] = "引擎環境", ["Swarm"] = "Swarm 叢集", ["Minecraft"] = "Minecraft 伺服器", ["Storage"] = "儲存與網絡", ["Volumes"] = "磁碟區", ["Networks"] = "網絡", ["Monitor"] = "即時監察",
        ["System"] = "系統", ["Registry"] = "映像倉庫", ["Diagnostics"] = "診斷", ["Tools"] = "工具", ["Activity"] = "活動記錄",
        ["Settings"] = "設定", ["Setup"] = "初次設定", ["Refresh"] = "重新整理", ["Browse"] = "瀏覽", ["Save"] = "儲存", ["Save as…"] = "另存新檔…",
        ["Load"] = "載入", ["Cancel"] = "取消", ["Close"] = "關閉", ["Back"] = "返回", ["Next"] = "下一步", ["Finish"] = "完成",
        ["Skip for now"] = "遲啲先", ["Pull image"] = "下載映像", ["Run"] = "執行", ["Tag"] = "標籤", ["Inspect"] = "檢視詳情", ["Remove"] = "移除",
        ["Start"] = "啟動", ["Stop"] = "停止", ["Restart"] = "重新啟動", ["Pause"] = "暫停", ["Resume"] = "繼續", ["Kill"] = "強制停止",
        ["Logs"] = "記錄", ["Statistics"] = "統計", ["Create"] = "建立", ["Push"] = "上載", ["Export"] = "匯出", ["Import"] = "匯入",
        ["Search"] = "搜尋", ["Filter"] = "篩選", ["Regex"] = "正規表示式", ["New tab"] = "新增分頁", ["Pin tab"] = "釘選分頁",
        ["Build context"] = "建立內容資料夾", ["Tags"] = "標籤", ["Target stage"] = "目標階段", ["Platform"] = "平台",
        ["Build arguments"] = "建立參數", ["Ignore build cache"] = "唔用快取", ["Pull newer base images"] = "更新基礎映像",
        ["Build image"] = "建立映像", ["Build and run"] = "建立再執行", ["Check Dockerfile"] = "檢查 Dockerfile",
        ["Dockerfile"] = "Dockerfile", ["Build output"] = "建立輸出", ["Destination"] = "目的地", ["Load locally"] = "載入本機",
        ["OCI archive"] = "OCI 封存檔", ["Ready"] = "準備就緒", ["Working"] = "處理中", ["Completed"] = "已完成", ["Failed"] = "失敗咗",
        ["Theme"] = "主題", ["Language"] = "語言", ["Font size"] = "字體大小", ["Reduced motion"] = "減少動畫", ["High contrast"] = "高對比",
        ["Appearance"] = "外觀", ["Accessibility"] = "無障礙", ["Focus modes"] = "專注模式", ["Sounds and narration"] = "聲音與朗讀",
        ["Engine"] = "引擎", ["Data and privacy"] = "資料與私隱", ["About"] = "關於", ["Notifications"] = "通知",
        ["Version"] = "版本", ["Updated"] = "更新時間", ["Selected"] = "已選", ["Select all"] = "全選", ["Clear selection"] = "取消選取",
        ["Command palette"] = "指令面板", ["No results"] = "搵唔到結果", ["Running"] = "運行中", ["Stopped"] = "已停止",
        ["Dangling"] = "懸空", ["Unused"] = "未使用", ["In use"] = "使用中", ["Prune"] = "清理", ["Disk usage"] = "磁碟用量",
        ["Sign in"] = "登入", ["Sign out"] = "登出", ["Builders"] = "建立器", ["History"] = "歷史", ["Restore"] = "還原",
        ["Copy"] = "複製", ["Open"] = "開啟", ["Edit"] = "編輯", ["Add"] = "新增", ["Delete"] = "刪除", ["Details"] = "詳情",
        ["Documentation"] = "說明文件", ["Changelog"] = "更新日誌", ["Base64"] = "Base64", ["Encode"] = "編碼", ["Decode"] = "解碼",
        ["Emergency exit"] = "緊急退出", ["Slide to confirm"] = "拉到盡確認", ["Key 1"] = "鎖匙一", ["Key 2"] = "鎖匙二",
        ["Live"] = "即時", ["Paused"] = "已暫停", ["Display name"] = "顯示名稱", ["Reset"] = "重設", ["Stages"] = "階段",
        ["Add stage"] = "新增階段", ["Add instruction"] = "新增指令", ["Live preview"] = "即時預覽", ["Checks"] = "檢查",
        ["Open in Build"] = "開去建立", ["Start from"] = "由範本開始", ["Base image"] = "基礎映像", ["Stage name"] = "階段名稱",
        ["Events"] = "事件", ["Run all checks"] = "全部檢查", ["Export report"] = "匯出報告"
    };

    /// <summary>Exact phrases plus sentence templates (keys containing {0}, {1}…) built once from every dictionary file.</summary>
    private sealed record Template(string Prefix, string Longest, int Literal, Regex Pattern, string Value);
    private sealed record Lookup(Dictionary<string, string> Exact, List<Template> Templates);
    private static readonly Lazy<Lookup> Lookups = new(BuildLookup);
    private static readonly ConcurrentDictionary<string, byte> TemplateMisses = new(StringComparer.Ordinal);

    private static Lookup BuildLookup()
    {
        var exact = new Dictionary<string, string>(Cantonese, StringComparer.Ordinal);
        foreach (var (key, value) in PageCantonese()) exact[key] = value;
        var templates = new List<Template>();
        var marker = new Regex(@"\{\d+\}", RegexOptions.CultureInvariant);
        foreach (var key in exact.Keys.ToList())
        {
            if (!marker.IsMatch(key)) continue;
            var literals = marker.Split(key);
            var pattern = new System.Text.StringBuilder("^");
            var holes = marker.Matches(key);
            // A template with little fixed text ("{0} of {1}", "{0} s") would otherwise rewrite ordinary sentences;
            // its holes may only capture single tokens such as counts, sizes and names.
            var hole = string.Concat(literals).Trim().Length <= 6 ? @"\S{1,60}?" : ".+?";
            for (var i = 0; i < literals.Length; i++)
            {
                pattern.Append(Regex.Escape(literals[i]));
                if (i < holes.Count) pattern.Append("(?<g").Append(int.Parse(holes[i].Value.Trim('{', '}'), CultureInfo.InvariantCulture)).Append('>').Append(hole).Append(')');
            }
            pattern.Append('$');
            var longest = literals.OrderByDescending(l => l.Length).First();
            templates.Add(new Template(literals[0], longest, string.Concat(literals).Length, new Regex(pattern.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)), exact[key]));
            exact.Remove(key);
        }
        // The most specific template wins: more fixed text first, then the longest single piece.
        return new Lookup(exact, templates.OrderByDescending(t => t.Literal).ThenByDescending(t => t.Longest.Length).ToList());
    }

    private static string? Translate(string text)
    {
        var lookup = Lookups.Value;
        if (lookup.Exact.TryGetValue(text, out var found)) return found;
        if (text.Length < 4 || text.Length > 1200 || TemplateMisses.ContainsKey(text)) return null;
        foreach (var template in lookup.Templates)
        {
            if (template.Prefix.Length > 0 && !text.StartsWith(template.Prefix, StringComparison.Ordinal)) continue;
            if (template.Longest.Length > 0 && !text.Contains(template.Longest, StringComparison.Ordinal)) continue;
            Match match;
            try { match = template.Pattern.Match(text); } catch (RegexMatchTimeoutException) { continue; }
            if (!match.Success) continue;
            return Regex.Replace(template.Value, @"\{(\d+)\}", m =>
            {
                var group = match.Groups["g" + m.Groups[1].Value];
                return group.Success ? (lookup.Exact.TryGetValue(group.Value, out var inner) ? inner : group.Value) : m.Value;
            });
        }
        if (TemplateMisses.Count < 6000) TemplateMisses[text] = 0;
        return null;
    }

    private static readonly Lazy<Dictionary<string, string>> ReverseLookup = new(() =>
    {
        var reverse = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (english, cantonese) in Lookups.Value.Exact) reverse.TryAdd(cantonese, english);
        return reverse;
    });

    /// <summary>
    /// The English wording behind a label that may already be translated, so an id built from a label does not change when the language does.
    /// Bilingual labels use their English half. Sentence templates with numbers in them are not reversed, and a label with no known translation is returned as it is.
    /// </summary>
    public static string Original(string? text)
    {
        var value = text ?? "";
        var join = value.IndexOf(" · ", StringComparison.Ordinal);
        if (join > 0) value = value[..join];
        return ReverseLookup.Value.TryGetValue(value, out var english) ? english : value;
    }

    public static string T(string text)
    {
        if (overrides.TryGetValue(text, out var personal)) return personal;
        var language = SchoolMode ? "English" : Language;
        if (language == "English") return text;
        var translated = Translate(text);
        if (translated == null) return text;
        return language == "Cantonese" ? translated : text + " · " + translated;
    }

    /// <summary>Translates a status line made of " · " separated phrases one phrase at a time, so counts and context names stay literal.</summary>
    public static string Line(string composed)
    {
        if (SchoolMode || Language == "English" && overrides.Count == 0) return composed;
        return string.Join(" · ", composed.Split(" · ").Select(T));
    }

    /// <summary>Translates the leading phrase of a title such as "Inspect nginx" or "Logs · web" and keeps the identifier that follows it.</summary>
    public static string Lead(string text)
    {
        if (SchoolMode || Language == "English" && overrides.Count == 0) return text;
        var whole = T(text);
        if (whole != text) return whole;
        var words = text.Split(' ');
        for (var count = Math.Min(words.Length - 1, 4); count >= 1; count--)
        {
            var head = string.Join(' ', words.Take(count));
            var translated = T(head);
            if (translated != head) return translated + " " + string.Join(' ', words.Skip(count));
        }
        return text;
    }

    /// <summary>Changes the voice of a message, never its facts. Level 1-2 states the fact only; higher levels add a lead-in.</summary>
    public static string Voice(string fact, string englishLeadIn, string cantoneseLeadIn = "")
    {
        var cantonese = !SchoolMode && Language == "Cantonese";
        var level = cantonese ? FunnyCantonese : FunnyEnglish;
        if (SchoolMode || level <= 2) return fact;
        var lead = cantonese && cantoneseLeadIn.Length > 0 ? cantoneseLeadIn : englishLeadIn;
        if (level == 3) return fact;
        return lead.Length == 0 ? fact : lead + " " + fact;
    }

    public static string Emoji(string category) => !DialogEmoji ? "" : category switch
    {
        "success" => "🎉", "error" => "🧯", "warning" => "⚠️", "danger" => "🧨", "info" => "💡", "question" => "🤔", _ => ""
    };

    public sealed record VocabularyResult(bool Success, int Count, string Message);

    /// <summary>Validates a personal-vocabulary file in full before applying it; a rejected file never applies partially.</summary>
    public static VocabularyResult LoadVocabulary(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new(false, 0, "The file does not exist.");
            if (info.Length > 256 * 1024) return new(false, 0, "The file is larger than 256 KiB.");
            var bytes = File.ReadAllBytes(path);
            var text = new UTF8Encoding(false, true).GetString(bytes);
            var parsed = ParseVocabulary(text);
            overrides = parsed;
            Changed?.Invoke();
            return new(true, parsed.Count, $"Loaded {parsed.Count} personal wording entries.");
        }
        catch (DecoderFallbackException) { return new(false, 0, "The file is not valid UTF-8."); }
        catch (Exception e) when (e is JsonException or InvalidDataException) { return new(false, 0, e.Message); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(false, 0, "The file could not be read: " + e.Message); }
    }

    public static Dictionary<string, string> ParseVocabulary(string text)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The vocabulary must be a JSON object.");
        // The shared generator writes "schemaVersion"; files made by hand for this app may say "version". Either must be 1, and a
        // file that gives both must not disagree.
        var hasSchema = root.TryGetProperty("schemaVersion", out var schemaVersion);
        var hasVersion = root.TryGetProperty("version", out var version);
        static bool IsOne(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) && n == 1;
        if ((!hasSchema && !hasVersion) || (hasSchema && !IsOne(schemaVersion)) || (hasVersion && !IsOne(version)))
            throw new InvalidDataException("Unsupported vocabulary version. Use \"schemaVersion\": 1.");
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The vocabulary needs an \"entries\" object.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String) throw new InvalidDataException($"Entry \"{Short(entry.Name)}\" must be a string.");
            var value = entry.Value.GetString() ?? "";
            if (new StringInfo(entry.Name).LengthInTextElements > 160) throw new InvalidDataException("A key is longer than 160 characters.");
            if (new StringInfo(value).LengthInTextElements > 1000) throw new InvalidDataException("A value is longer than 1,000 characters.");
            if (!result.TryAdd(entry.Name, value)) throw new InvalidDataException($"Duplicate key \"{Short(entry.Name)}\".");
        }
        return result;
    }
    private static string Short(string s) => s.Length > 40 ? s[..40] + "…" : s;
    /// <summary>Reads the imported personal value for an exact ordinary phrase, without falling back to a translation.</summary>
    public static bool TryPersonal(string text, out string value) => overrides.TryGetValue(text, out value!);
    public static void ClearVocabulary() { overrides = new(StringComparer.Ordinal); Changed?.Invoke(); }
}
