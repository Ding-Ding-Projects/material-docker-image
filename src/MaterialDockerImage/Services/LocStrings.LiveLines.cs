namespace MaterialDockerImage.Services;

/// <summary>Cantonese for the live palette second lines and the Canvas build-result footers.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseLiveLines()
    {
        // Command palette live second lines (UI/PaletteLiveLines.cs)
        yield return new("{0} total", "共 {0} 個");
        yield return new("{0} project", "{0} 個項目");
        yield return new("{0} projects", "{0} 個項目");
        yield return new("Current context: {0}", "目前 context：{0}");
        yield return new("Context list unavailable", "攞唔到 context 清單");
        yield return new("Sampling every {0} s", "每 {0} 秒取樣一次");
        yield return new("Sampling paused", "取樣暫停咗");
        yield return new("{0} container sampled", "取樣咗 {0} 個容器");
        yield return new("{0} containers sampled", "取樣咗 {0} 個容器");
        yield return new("Last sample {0}", "上次取樣 {0}");
        yield return new("{0} of {1} images analyzed", "{1} 個映像入面分析咗 {0} 個");
        yield return new("Swarm active", "Swarm 啟用中");
        yield return new("Swarm inactive", "Swarm 未啟用");
        yield return new("{0} stopped container", "{0} 個已停容器");
        yield return new("{0} stopped containers", "{0} 個已停容器");
        yield return new("Nothing stopped to remove", "冇已停嘅容器要清");
        yield return new("Swarm state unavailable", "攞唔到 Swarm 狀態");
        // Palette commands added with the live lines
        yield return new("Restart the selected container", "重新啟動已選容器");
        // Canvas build-result footers (Pages/Canvas/CanvasBuildFooters.cs)
        yield return new("cached", "已快取");
        yield return new("failed at step {0}", "喺第 {0} 步失敗");
        yield return new("canceled", "已取消");
        yield return new("{0} of {1} steps cached", "{1} 個步驟入面 {0} 個已快取");
        yield return new("{0} built image", "建好嘅映像 {0}");
        yield return new("from the build at {0}", "來自 {0} 嗰次建立");
        yield return new("Results BuildKit reported for this stage in the build at {0}. The Dockerfile text has not changed since that build.", "呢個係 BuildKit 喺 {0} 嗰次建立報告呢個階段嘅結果。之後 Dockerfile 文字冇改過。");
    }
}
