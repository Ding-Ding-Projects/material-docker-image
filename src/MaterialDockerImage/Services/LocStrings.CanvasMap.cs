namespace MaterialDockerImage.Services;

/// <summary>Cantonese for the Canvas minimap and the stage line spans.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseCanvasMap()
    {
        yield return new("Minimap", "縮圖");
        yield return new("Hide minimap (M)", "隱藏縮圖 (M)");
        yield return new("Show minimap (M)", "顯示縮圖 (M)");
        yield return new("Minimap: click or drag to move the view", "縮圖：撳一下或者拖動就可以移動畫面");
        yield return new("Overview of the whole graph. The outlined box is the visible area. Click or drag to move the view; arrow keys pan and Home returns to the start.",
            "成個圖嘅總覽。有框嗰格係而家睇到嘅範圍。撳一下或者拖動就可以移動畫面；方向鍵平移，Home 返去起點。");
        yield return new("Lines", "行");
    }
}
