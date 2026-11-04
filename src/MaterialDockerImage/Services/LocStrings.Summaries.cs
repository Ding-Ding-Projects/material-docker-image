namespace MaterialDockerImage.Services;

/// <summary>
/// Cantonese for the status lines under page titles and for counted phrases. Each English noun is listed in its singular and plural form
/// because the pages build them with an English plural before translating; Cantonese has one form for both.
/// </summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseSummaries()
    {
        foreach (var (singular, plural, value) in CountedPhrases())
        {
            yield return new(singular, value);
            if (plural != singular) yield return new(plural, value);
        }
        foreach (var pair in StatusPhrases()) yield return pair;
    }

    private static IEnumerable<(string Singular, string Plural, string Value)> CountedPhrases()
    {
        yield return ("{0} image", "{0} images", "{0} 個映像");
        yield return ("{0} container", "{0} containers", "{0} 個容器");
        yield return ("{0} server", "{0} servers", "{0} 個伺服器");
        yield return ("{0} context", "{0} contexts", "{0} 個引擎環境");
        yield return ("{0} network", "{0} networks", "{0} 個網絡");
        yield return ("{0} volume", "{0} volumes", "{0} 個磁碟區");
        yield return ("{0} config", "{0} configs", "{0} 個設定");
        yield return ("{0} node", "{0} nodes", "{0} 個節點");
        yield return ("{0} manager", "{0} managers", "{0} 個管理節點");
        yield return ("{0} secret", "{0} secrets", "{0} 個機密");
        yield return ("{0} service", "{0} services", "{0} 個服務");
        yield return ("{0} stack", "{0} stacks", "{0} 個堆疊");
        yield return ("{0} task including history", "{0} tasks including history", "{0} 個工作（包括歷史）");
        yield return ("{0} step", "{0} steps", "{0} 個步驟");
        yield return ("{0} platform", "{0} platforms", "{0} 個平台");
        yield return ("{0} attestation", "{0} attestations", "{0} 個證明");
        yield return ("{0} finding", "{0} findings", "{0} 項結果");
        yield return ("{0} finding · {1} fixable", "{0} findings · {1} fixable", "{0} 項結果 · {1} 項可修正");
        yield return ("{0} event", "{0} events", "{0} 個事件");
        yield return ("{0} plugin found by the docker CLI", "{0} plugins found by the docker CLI", "docker CLI 搵到 {0} 個外掛");
        yield return ("{0} project reported by the engine", "{0} projects reported by the engine", "引擎報告 {0} 個專案");
        yield return ("{0} recent file", "{0} recent files", "{0} 個最近檔案");
        yield return ("{0} running container", "{0} running containers", "{0} 個運行中嘅容器");
        yield return ("{0} tagged image", "{0} tagged images", "{0} 個已加標籤嘅映像");
        yield return ("{0} engine warning", "{0} engine warnings", "{0} 個引擎警告");
        yield return ("{0} category selected", "{0} categories selected", "已選 {0} 個類別");
        yield return ("docker compose images · {0} container", "docker compose images · {0} containers", "docker compose images · {0} 個容器");
        yield return ("peak {0} over {1} sample", "peak {0} over {1} samples", "喺 {1} 個取樣中峰值 {0}");
        yield return ("{0} cache mount", "{0} cache mounts", "{0} 個快取掛載");
        yield return ("{0} pattern", "{0} patterns", "{0} 個樣式");
        yield return ("{0} instruction", "{0} instructions", "{0} 個指令");
        yield return ("{0} stage", "{0} stages", "{0} 個階段");
        yield return ("{0} group", "{0} groups", "{0} 個群組");
        yield return ("{0} target", "{0} targets", "{0} 個目標");
        yield return ("{0} link", "{0} links", "{0} 個連結");
        yield return ("{0} error", "{0} errors", "{0} 個錯誤");
        yield return ("{0} match", "{0} matches", "{0} 個符合項");
        yield return ("{0} attachment", "{0} attachments", "{0} 個附加項目");
        yield return ("{0} arg", "{0} args", "{0} 個參數");
    }

    private static IEnumerable<KeyValuePair<string, string>> StatusPhrases()
    {
        yield return new("{0} paused", "{0} 個已暫停");
        yield return new("context {0}", "環境 {0}");
        yield return new("refreshed {0}", "更新於 {0}");
        yield return new("this window uses {0}", "呢個視窗使用 {0}");
        yield return new("CLI current: {0}", "CLI 目前：{0}");
        yield return new("{0} failed to load", "{0} 個載入失敗");
        yield return new("all loaded", "全部已載入");
        yield return new("{0} built-in", "{0} 個內建");
        yield return new("{0} user-defined without containers", "{0} 個用戶自訂且冇容器");
        yield return new("sizes not reported by this engine", "引擎冇報告大小");
        yield return new("{0} not used by any container", "{0} 個冇被任何容器使用");
        yield return new("{0} used", "已用 {0}");
        yield return new("{0} reclaimable", "可回收 {0}");
        yield return new("{0} at desired replicas", "{0} 個已達期望副本數");
        yield return new("{0} failed or rejected", "{0} 個失敗或被拒絕");
        yield return new("{0} analyzed this session", "今次工作階段已分析 {0} 個");
        yield return new("window {0}", "視窗 {0}");
        yield return new("docker stats every {0} s", "docker stats 每 {0} 秒");
        yield return new("Average {0}", "平均 {0}");
        yield return new("100% equals one full CPU core", "100% 等於一個完整 CPU 核心");
        yield return new("Swarm mode {0}", "Swarm 模式 {0}");
        yield return new("Worker node", "工作節點");
        yield return new("Engine {0} on {1}", "引擎 {0}，環境 {1}");
        yield return new("docker compose ls failed:", "docker compose ls 失敗：");
        yield return new("docker context ls failed:", "docker context ls 失敗：");
        yield return new("Docker Scout unavailable", "Docker Scout 無法使用");
        yield return new("parsed from the Build editor, not executed", "解析自建立編輯器，並未執行");
        yield return new("last saved to", "最後儲存到");
        yield return new("draft kept automatically", "已自動保留草稿");
        yield return new("unsaved changes", "未儲存嘅變更");
        yield return new("saved", "已儲存");
        yield return new("loaded", "已載入");
        yield return new("Ln", "行");
        yield return new("Col", "列");
        yield return new("current", "目前");
        yield return new("context", "環境");
        yield return new("group", "群組");
        yield return new("groups", "群組");
        yield return new("target", "目標");
        yield return new("targets", "目標");
        yield return new("stage", "階段");
        yield return new("stages", "階段");
        yield return new("step", "步驟");
        yield return new("steps", "步驟");
        yield return new("link", "連結");
        yield return new("links", "連結");
        yield return new("container", "容器");
        yield return new("volume", "磁碟區");
        yield return new("volumes", "磁碟區");
        yield return new("network", "網絡");
        yield return new("networks", "網絡");
        yield return new("instruction", "指令");
        yield return new("instructions", "指令");
        yield return new("match", "個符合項");
        yield return new("matches", "個符合項");
        yield return new("error", "個錯誤");
        yield return new("errors", "個錯誤");
        yield return new("arg", "個參數");
        yield return new("args", "個參數");
        yield return new("attachment", "個附加項目");
        yield return new("attachments", "個附加項目");
        yield return new("pattern", "個樣式");
        yield return new("patterns", "個樣式");
        yield return new("cache mount", "個快取掛載");
        yield return new("cache mounts", "個快取掛載");
        yield return new("no tags", "冇標籤");
        yield return new("target selected", "個目標已選");
        yield return new("targets selected", "個目標已選");
        yield return new("waiting for export", "等待匯出");
        yield return new("no image digest reported", "未報告映像摘要");
        yield return new("Expand output", "展開輸出");
        yield return new("Collapse output", "收合輸出");
        yield return new("Container", "容器");
        yield return new("This list was loaded from engine context {0}, which is no longer the selected context. Refresh before acting.", "呢個清單載入自引擎環境 {0}，而佢已經唔係目前選擇嘅環境。操作前請先重新整理。");
        yield return new("Logs:", "記錄：");
        foreach (var pair in ActionAndFieldLabels()) yield return pair;
        yield return new("Environment variables", "環境變數");
        yield return new("docker exited with code {0}.", "docker 結束，結束碼 {0}。");
        yield return new("…and {0} more.", "…仲有 {0} 項。");
    }
}
