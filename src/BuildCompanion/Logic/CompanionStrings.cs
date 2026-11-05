using System.Collections.Generic;

namespace BuildCompanion;

public enum CompanionLanguage { English, Cantonese, Both }

/// <summary>Tiny local string table. Missing keys fall back to the key so a gap is visible rather than blank.</summary>
public static class CompanionStrings
{
    static readonly Dictionary<string, (string En, string Yue)> Table = new()
    {
        ["title"] = ("Material Docker Image build companion", "Material Docker Image 建置助手"),
        ["repo"] = ("Repository", "專案資料夾"),
        ["change"] = ("Change folder", "更改資料夾"),
        ["language"] = ("Language", "語言"),
        ["setup"] = ("Setup check", "環境檢查"),
        ["setupNote"] = ("The batch files download and verify the pinned .NET SDK and Node.js on first use. No administrator rights are requested.", "批次檔會喺第一次使用時下載並驗證指定版本嘅 .NET SDK 同 Node.js，唔會要求管理員權限。"),
        ["present"] = ("found", "已有"),
        ["missing"] = ("missing", "冇"),
        ["prepared"] = ("created by the first build", "第一次建置時會建立"),
        ["version"] = ("Version (x.y.z)", "版本 (x.y.z)"),
        ["versionHint"] = ("Optional for Build, required for the installer", "建置可留空，安裝程式必須填寫"),
        ["versionBad"] = ("Version must look like 1.2.3", "版本格式必須似 1.2.3"),
        ["build"] = ("Build", "建置"),
        ["buildRun"] = ("Build and run", "建置並執行"),
        ["installer"] = ("Build installer", "建置安裝程式"),
        ["cancel"] = ("Cancel", "取消"),
        ["copyLog"] = ("Copy log", "複製記錄"),
        ["log"] = ("Output", "輸出"),
        ["idle"] = ("Ready", "就緒"),
        ["running"] = ("Running", "執行中"),
        ["succeeded"] = ("Succeeded", "成功"),
        ["failed"] = ("Failed", "失敗"),
        ["cancelled"] = ("Cancelled", "已取消"),
        ["launchFailed"] = ("Could not start", "無法啟動"),
        ["exitCode"] = ("Exit code", "結束碼"),
        ["elapsed"] = ("Elapsed", "經過時間"),
        ["phase"] = ("Phase", "階段"),
        ["phase0"] = ("Starting", "開始"),
        ["phase1"] = ("Preparing dependencies", "準備相依項目"),
        ["phase2"] = ("Compiling and publishing", "編譯及發佈"),
        ["phase3"] = ("Packaging installer", "封裝安裝程式"),
        ["copied"] = ("Log copied", "已複製記錄"),
        ["pickRepo"] = ("Use another copy of the source: choose the folder that contains build.bat", "用另一份原始碼：請揀選有 build.bat 嘅資料夾"),
        ["noRepo"] = ("No repository selected", "未揀選專案資料夾"),
        ["embeddedSource"] = ("Using the source packed inside this program, unpacked to", "用緊呢個程式入面附帶嘅原始碼，已經解壓到"),
        ["shortcuts"] = ("Ctrl+B build, Ctrl+R build and run, Ctrl+I installer, Esc cancel, Ctrl+Shift+C copy log", "Ctrl+B 建置、Ctrl+R 建置並執行、Ctrl+I 安裝程式、Esc 取消、Ctrl+Shift+C 複製記錄"),
        ["installerNote"] = ("Installer builds are unsigned and may trigger an unknown-publisher warning.", "安裝程式冇簽署，可能會出現未知發佈者警告。"),
    };

    public static string Get(string key, CompanionLanguage language)
    {
        if (!Table.TryGetValue(key, out var pair)) return key;
        return language switch
        {
            CompanionLanguage.English => pair.En,
            CompanionLanguage.Cantonese => pair.Yue,
            _ => pair.En + " · " + pair.Yue,
        };
    }

    public static IEnumerable<string> Keys => Table.Keys;

    public static bool IsComplete(string key) => Table.TryGetValue(key, out var p) && p.En.Length > 0 && p.Yue.Length > 0;

    public static CompanionLanguage Next(CompanionLanguage current) => current switch
    {
        CompanionLanguage.English => CompanionLanguage.Cantonese,
        CompanionLanguage.Cantonese => CompanionLanguage.Both,
        _ => CompanionLanguage.English,
    };
}
