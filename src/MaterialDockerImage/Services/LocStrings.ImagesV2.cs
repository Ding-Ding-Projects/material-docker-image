namespace MaterialDockerImage.Services;

/// <summary>Cantonese for the full-page image detail (design/workspace-redesign-2026-11/project/ImagesV2.dc.html).</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseImagesV2()
    {
        yield return new("Image detail", "映像詳情");
        yield return new("Back to Images", "返回映像");
        yield return new("Breadcrumb", "導覽路徑");
        yield return new("Copy image digest", "複製映像摘要");
        yield return new("More image actions", "更多映像操作");
        yield return new("layer", "層");
        yield return new("layers", "層");
        yield return new("created", "建立於");
        yield return new("Open full view", "開全頁詳情");
        yield return new("Open details", "開詳情");
        yield return new("Open details of selected image", "開所選映像嘅詳情");
        yield return new("Sort and filter layers", "排序同篩選層");
        yield return new("Oldest first", "最舊排先");
        yield return new("Newest first", "最新排先");
        yield return new("Largest first", "最大排先");
        yield return new("Bars relative to the largest layer", "長條以最大嗰層為準");
        yield return new("Layers, oldest first", "層（最舊排先）");
        yield return new("Layers, newest first", "層（最新排先）");
        yield return new("Layers, largest first", "層（最大排先）");
        yield return new("Created by", "由乜嘢建立");
        yield return new("Share of image", "佔映像比例");
        yield return new("(no instruction recorded)", "（冇記錄指令）");
        yield return new("Metadata only", "只係中繼資料");
        yield return new("Scout summary", "Scout 摘要");
        yield return new("Analyze", "分析");
        yield return new("Analyze again", "再分析一次");
        yield return new("View findings", "睇結果");
        yield return new("Reading the registry…", "讀緊 registry…");
        yield return new("Registry not read", "未讀到 registry");
        yield return new("Single manifest", "單一 manifest");
        yield return new("OCI image index", "OCI 映像索引");
        yield return new("Docker manifest list", "Docker manifest 清單");
        yield return new("This machine", "呢部機");
        yield return new("Needs emulation", "要模擬先行到");
        yield return new("This engine runs a different architecture, so running this platform needs emulation such as QEMU.", "呢個引擎嘅架構唔同，要用 QEMU 之類嘅模擬先可以行呢個平台。");
        yield return new("Local only", "淨係本機");
        yield return new("This image has not been analyzed with Docker Scout in this session.", "呢個工作階段仲未用 Docker Scout 分析過呢個映像。");
        yield return new("analyzed", "分析於");
        yield return new("No vulnerabilities found", "搵唔到漏洞");
        yield return new("Not installed", "未安裝");
        yield return new("Open the Vulnerabilities tab for the install guide.", "去「漏洞」分頁睇安裝指南。");
        yield return new("{0} vulnerabilities in {1} packages", "{1} 個套件有 {0} 個漏洞");
        yield return new("{0} with a fix available", "{0} 個有修正版本");
        yield return new("{0} without a severity rating", "{0} 個冇嚴重程度評級");
        yield return new("{0} more in the Platforms tab", "仲有 {0} 個，喺「平台」分頁");
    }
}
