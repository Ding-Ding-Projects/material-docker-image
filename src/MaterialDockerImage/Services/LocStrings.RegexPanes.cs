namespace MaterialDockerImage.Services;

/// <summary>Cantonese for the three-pane arrangement of the regular expression builder.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseRegexPanes()
    {
        yield return new("Tokens and library", "記號同範本庫");
        yield return new("Pattern and test", "模式同測試");
        yield return new("Captures and actions", "擷取同動作");
        yield return new("Panes", "窗格");
        yield return new("Captures", "擷取");
        yield return new("Shows or hides the pane with the insert palette, the library and my patterns.", "顯示或者收埋放住插入調色盤、範本庫同我嘅模式嘅窗格。");
        yield return new("Shows the pane with the pattern, its flags, the explanation and the tests. On a wide builder it is always shown.", "顯示放住模式、旗標、解釋同測試嘅窗格。建構器夠闊嘅時候佢會一直顯示。");
        yield return new("Shows or hides the pane with the captures, the replace preview, conversion and the actions.", "顯示或者收埋放住擷取、取代預覽、轉換同動作嘅窗格。");
        yield return new("Every group of every match in the sample. Groups appear here when the pattern has capture groups and the sample matches.", "樣本入面每個配對嘅每個群組。模式有擷取群組而且樣本有配對嘅時候，群組就會喺度出現。");
        yield return new("The builder is narrower than three panes, so the two side panes take turns beside the pattern. Use the pane buttons to switch.", "建構器窄過三個窗格，所以兩邊嘅窗格輪流喺模式旁邊顯示。用窗格掣切換。");
        yield return new("The builder is narrow, so one pane is shown at a time. Use the pane buttons to switch.", "建構器太窄，所以一次只顯示一個窗格。用窗格掣切換。");
        yield return new("The Insert panel is hidden. Show it again under Preferences and layout.", "插入面板收埋咗。喺「偏好設定同版面」度可以再顯示佢。");
    }
}
