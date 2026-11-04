namespace MaterialDockerImage.Services;

/// <summary>Cantonese for row actions that open a dialog (labels ending in an ellipsis) and for short field captions.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> ActionAndFieldLabels()
    {
        // Compact layouts and the window-wide personal wording drop.
        // Container confirmations with real singular and plural forms.
        yield return new("Actions for the selected row", "所選行嘅動作");
        yield return new("Stop 1 container?", "停止 1 個容器？");
        yield return new("Stop {0} containers?", "停止 {0} 個容器？");
        yield return new("Restart 1 container?", "重新啟動 1 個容器？");
        yield return new("Restart {0} containers?", "重新啟動 {0} 個容器？");
        yield return new("Pause 1 container?", "暫停 1 個容器？");
        yield return new("Pause {0} containers?", "暫停 {0} 個容器？");
        yield return new("Unpause 1 container?", "恢復 1 個容器？");
        yield return new("Unpause {0} containers?", "恢復 {0} 個容器？");
        yield return new("Remove 1 container", "移除 1 個容器");
        yield return new("Remove {0} containers", "移除 {0} 個容器");
        // Shell and operation panel.
        yield return new("Workspace", "工作區");
        yield return new("No operation is running.", "目前冇進行中嘅操作。");
        yield return new("elapsed", "已用時間");
        yield return new("Operation output", "操作輸出");
        yield return new("Output copied.", "已複製輸出。");
        yield return new("Copy output", "複製輸出");
        yield return new("Clear output", "清除輸出");
        yield return new("{0} selected of {1} matching", "已揀 {0} 項（{1} 項符合）");
        yield return new("filters:", "篩選：");
        yield return new("search active", "搜尋中");
        yield return new("Show details", "顯示詳情");
        yield return new("Hide details", "隱藏詳情");
        yield return new("Selected item summary", "已選項目摘要");
        yield return new("Nothing selected", "未有揀選任何項目");
        yield return new("Personal wording applied", "已套用個人用字");
        yield return new("Only a personal wording .json file can be dropped here. Nothing was changed.", "呢度只可以拖入個人用字 .json 檔，乜都冇改到。");
        yield return new("The dropped file was refused and nothing was changed: ", "拖入嘅檔案被拒絕，乜都冇改到：");
        yield return new("Personal wording applied from the dropped file. Clear it in Settings › Language to restore the original wording.", "已套用拖入檔案嘅個人用字。去「設定 › 語言」清除就可以還原原本用字。");
        yield return new("Label…", "標籤…");
        yield return new("Rename…", "重新命名…");
        yield return new("Kill…", "強制停止…");
        yield return new("Remove…", "移除…");
        yield return new("Export…", "匯出…");
        yield return new("Import…", "匯入…");
        yield return new("Save…", "儲存…");
        yield return new("Tag…", "加標籤…");
        yield return new("Push…", "上載…");
        yield return new("Pull…", "下載…");
        yield return new("Run…", "執行…");
        yield return new("Restore…", "還原…");
        yield return new("Kick…", "踢出…");
        yield return new("Pause…", "暫停…");
        yield return new("Drain…", "排空…");
        yield return new("Logs…", "記錄…");
        yield return new("Scale…", "調整規模…");
        yield return new("Delete…", "刪除…");
        yield return new("Browse…", "瀏覽…");
        yield return new("Clone…", "複製…");
        yield return new("Review…", "檢視…");
        yield return new("Prune…", "清理…");
        yield return new("Detecting…", "偵測中…");
        yield return new("Connecting…", "連線中…");
        yield return new("Overrides:", "覆寫：");
        yield return new("SSH keys", "SSH 金鑰");
        yield return new("OCI archive path", "OCI 封存檔路徑");
        yield return new("CACHED", "已快取");
        yield return new("Build:", "建立：");
        yield return new("Invalid:", "無效：");
        yield return new("None.", "無。");
        yield return new("stderr only", "只顯示 stderr");
        yield return new("Auto-refresh", "自動重新整理");
        yield return new("TLS (tcp:// endpoints)", "TLS（tcp:// 端點）");
        yield return new("CLI plugins", "CLI 外掛");
        yield return new("Read-only", "唯讀");
        yield return new("CPU limit", "CPU 上限");
        yield return new("GPUs", "GPU");
        yield return new("tmpfs mounts", "tmpfs 掛載");
        yield return new("IP range", "IP 範圍");
        yield return new("User-defined", "用戶自訂");
        yield return new("IPv4 address (optional)", "IPv4 位址（選填）");
        yield return new("Context:", "環境：");
        yield return new("Folder:", "資料夾：");
        yield return new("Result:", "結果：");
        yield return new("Stopped:", "已停止：");
        yield return new("MIME type", "MIME 類型");
        yield return new("data: URL", "data: 網址");
        yield return new("Copied.", "已複製。");
        yield return new("docker compose images failed:", "docker compose images 失敗：");
        yield return new("docker stack services failed:", "docker stack services 失敗：");
        yield return new("docker image inspect failed:", "docker image inspect 失敗：");
        yield return new("A starter replaces the draft. The previous draft stays in local history (Activity) and can be restored.", "範本會取代草稿。上一份草稿會保留喺本機歷史（活動記錄），可以還原。");
        yield return new("I have read and accept the Minecraft End User License Agreement", "我已閱讀並接受 Minecraft 最終用戶授權協議");
        yield return new("A container named {0} already exists on this engine. Choose another server name.", "呢個引擎已經有名為 {0} 嘅容器。請選擇另一個伺服器名稱。");
        yield return new("RCON is off, so saving could not be paused. The archive is taken from the live volume.", "RCON 已關閉，所以無法暫停儲存。封存檔會直接由運行中嘅磁碟區取得。");
        yield return new("save-on failed. Run save-on in the console so the server keeps saving.", "save-on 失敗。請喺主控台執行 save-on，令伺服器繼續儲存。");
        yield return new("save-all flush failed; the export may miss the most recent changes.", "save-all flush 失敗；匯出可能欠缺最近嘅變更。");
        yield return new("docker buildx ls returned no builders, or buildx is not installed. Diagnostics checks buildx availability.", "docker buildx ls 冇傳回建立器，或者未安裝 buildx。「診斷」會檢查 buildx 是否可用。");
        yield return new("docker search asks the engine to query Docker Hub. Results are not cached.", "docker search 會請引擎查詢 Docker Hub。結果不會快取。");
        yield return new("docker image import creates a single-layer image from a root filesystem tarball. It is different from Load archive, which restores images saved with docker image save.", "docker image import 由根檔案系統 tarball 建立單層映像。同「載入封存檔」唔同，後者用嚟還原由 docker image save 儲存嘅映像。");
        yield return new("docker info reports swarm mode as inactive. Services, stacks, nodes, secrets and configs exist only in swarm mode. Starting or joining a swarm changes this engine.", "docker info 報告 swarm 模式未啟用。服務、堆疊、節點、機密同設定只存在於 swarm 模式。開始或加入 swarm 會改變呢個引擎。");
    }
}
