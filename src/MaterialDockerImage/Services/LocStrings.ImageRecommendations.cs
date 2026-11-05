namespace MaterialDockerImage.Services;

/// <summary>Cantonese for the base image recommendation panel and the Dockerfile source notes of the image Layers table.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseImageRecommendations()
    {
        yield return new("Base image recommendation", "基底映像建議");
        yield return new("Get recommendations", "攞建議");
        yield return new("Runs docker scout recommendations for this image. It contacts Docker's service and can take a minute.", "為呢個映像執行 docker scout recommendations。佢會連去 Docker 嘅服務，可能要等一分鐘。");
        yield return new("Not checked in this session. Docker Scout looks up newer tags for the base image it detects; this contacts Docker's service.", "今次工作階段未檢查過。Docker Scout 會幫佢偵測到嘅基底映像搵較新嘅標籤；呢個會連去 Docker 嘅服務。");
        yield return new("Docker Scout found no base image for this image, so there is nothing to refresh or update.", "Docker Scout 搵唔到呢個映像嘅基底映像，所以冇嘢可以刷新或者更新。");
        yield return new("The output did not name a base image.", "輸出冇講明基底映像。");
        yield return new("{0} more tags in the full output (Output panel or Copy output)", "完整輸出仲有 {0} 個標籤（睇輸出面板或者複製輸出）");
        yield return new("The base image was auto-detected. Builds with provenance attestations give exact results.", "基底映像係自動偵測嘅。用來源證明（provenance attestation）建置會有準確結果。");
        yield return new("No newer base image is recommended. The current base image is up to date.", "冇建議較新嘅基底映像。而家嘅基底映像已經係最新。");
        yield return new("Refresh base image {0} to the newer {1}", "將基底映像 {0} 刷新做較新嘅 {1}");
        yield return new("Update base image {0} to {1}", "將基底映像 {0} 更新做 {1}");
        yield return new("Same tag, newer build", "同一標籤，較新建置");
        yield return new("Other tag", "其他標籤");
        yield return new("Current base", "而家嘅基底");
        yield return new("Also known as:", "又叫做：");
        yield return new("pushed", "推送於");
        yield return new("{0} fewer {1}", "{1}級少 {0} 個");
        yield return new("{0} more {1}", "{1}級多 {0} 個");
        yield return new("unrated", "未評級");
        yield return new("same size", "大小差唔多");
        yield return new("Copy image reference", "複製映像參照");
        yield return new("the image reference", "映像參照");
        yield return new("the command output", "指令輸出");
        yield return new("Checked", "檢查於");
        yield return new("Docker Scout could not give recommendations", "Docker Scout 俾唔到建議");
        yield return new("Asking Docker Scout…", "問緊 Docker Scout…");
        yield return new("Unsaved Dockerfile draft in Build", "建置頁未儲存嘅 Dockerfile 草稿");
        yield return new("Base:", "基底：");
        yield return new("Your Dockerfile, line {0}", "你嘅 Dockerfile，第 {0} 行");
        yield return new("from stage {0}", "來自階段 {0}");
        yield return new("from image {0}", "來自映像 {0}");
        yield return new("This step comes from the base image named in the first FROM of the Dockerfile.", "呢一步來自 Dockerfile 第一個 FROM 指明嘅基底映像。");
        yield return new("Matched exactly to this Dockerfile:", "同呢個 Dockerfile 完全對得上：");
        yield return new("Show this line in Build", "喺建置頁顯示呢一行");
    }
}
