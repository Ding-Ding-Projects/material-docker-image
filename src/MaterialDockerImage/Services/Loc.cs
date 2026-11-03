using System.Globalization;
using System.Text;
using System.Text.Json;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

/// <summary>Language modes (English, Cantonese, bilingual), funny levels and the local personal-vocabulary overlay.</summary>
public static class Loc
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
        ["Canvas"] = "畫布", ["Storage"] = "儲存與網絡", ["Volumes"] = "磁碟區", ["Networks"] = "網絡", ["Monitor"] = "即時監察",
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

    public static string T(string text)
    {
        if (overrides.TryGetValue(text, out var personal)) return personal;
        var language = SchoolMode ? "English" : Language;
        if (language == "English" || !Cantonese.TryGetValue(text, out var translated)) return text;
        return language == "Cantonese" ? translated : text + " · " + translated;
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
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1) throw new InvalidDataException("Unsupported vocabulary version. Use \"version\": 1.");
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
    public static void ClearVocabulary() { overrides = new(StringComparer.Ordinal); Changed?.Invoke(); }
}
