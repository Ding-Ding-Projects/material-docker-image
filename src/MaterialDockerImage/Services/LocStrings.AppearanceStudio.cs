namespace MaterialDockerImage.Services;

/// <summary>Cantonese for the appearance studio in Settings, Appearance.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseAppearanceStudio()
    {
        yield return new("Appearance studio", "外觀工作室");
        yield return new("Every change applies live and is saved in settings.json. Undo keeps the last 100 appearance changes.", "每個改動即刻生效，並儲存喺 settings.json。復原會保留最近 100 個外觀改動。");
        yield return new("Search appearance", "搜尋外觀");
        yield return new("Nothing in Appearance matches.", "外觀入面冇嘢符合。");
        yield return new("matching panel(s) and setting(s)", "個符合嘅面板同設定");
        yield return new("Export theme", "匯出主題");
        yield return new("My appearance", "我嘅外觀");
        yield return new("Saves the current appearance as a preset file you can import here or on another computer.", "將目前嘅外觀儲存做預設檔案，可以喺呢度或者另一部電腦匯入。");
        yield return new("Save as preset", "儲存做預設");
        yield return new("saved on this computer", "儲存喺呢部電腦");
        yield return new("Apply preset", "套用預設");
        yield return new("Applies the colours, shape, density, fonts and effects of this preset. Motion stays as it is. Undo returns to the current look.", "套用呢個預設嘅顏色、形狀、密度、字型同效果。動畫維持不變。復原會返返目前嘅樣。");
        yield return new("Import theme file", "匯入主題檔案");
        yield return new("appearance-presets.json could not be read, so saving presets is paused:", "讀唔到 appearance-presets.json，所以暫停儲存預設：");
        yield return new("All roles pass AA", "所有角色都過 AA");
        yield return new("role(s) below AA", "個角色未達 AA");
        yield return new("Replay the motion demo", "重播動畫示範");
        yield return new("Reset to default", "重設做預設值");
        yield return new("Undo last change", "復原上一個改動");
        yield return new("Example content drawn with your current appearance. Nothing in it runs.", "用你目前外觀畫出嚟嘅示例內容，入面乜都唔會執行。");
        yield return new("On primary container", "主要容器上嘅文字");
        yield return new("Secondary text", "次要文字");
        yield return new("Emphasized decelerate", "強調減速");
        yield return new("Emphasized accelerate", "強調加速");
        yield return new("Standard decelerate", "標準減速");
        yield return new("Open the full seed colour picker", "打開完整嘅種子顏色選擇器");
        yield return new("Open the full seed colour picker (any format, contrast and rainbow) further down this section.", "打開呢部分下面完整嘅種子顏色選擇器（任何格式、對比度同彩虹）。");
        yield return new("Seed hue", "種子色相");
        yield return new("Seed chroma", "種子彩度");
        yield return new("Seed tone", "種子色調");
        yield return new("Chroma", "彩度");
        yield return new("Tone", "色調");
        yield return new("That hue and chroma cannot be shown at this tone, so the nearest displayable colour is used.", "呢個色調顯示唔到呢個色相同彩度，所以用最接近而顯示得到嘅顏色。");
        yield return new("Animated rainbow accent is on, so the accent cycles around this seed.", "動態彩虹強調色已開，強調色會圍住呢個種子循環變色。");
        yield return new("Generated colour roles · from the seed and theme", "生成嘅顏色角色 · 來自種子同主題");
        yield return new("contrast", "對比度");
        yield return new("to 1 against", "比 1，對比");
        yield return new("passes", "通過");
        yield return new("fails", "唔通過");
        yield return new("Colour", "顏色");
        yield return new("Speed", "速度");
        yield return new("Easing curve", "緩動曲線");
        yield return new("A medium transition now lasts", "而家一個中等過渡維持");
        yield return new("Motion is not running, so changes snap to their final state.", "動畫冇運行，所以改動會即刻跳到最終狀態。");
        yield return new("Shows this Material easing token on the curve. The motion level decides where each token is used.", "喺曲線上顯示呢個 Material 緩動標記。用邊個標記由動畫程度決定。");
        yield return new("Easing tokens", "緩動標記");
        yield return new("Square corners (0.5x)", "方角（0.5x）");
        yield return new("Default corners (1x)", "預設圓角（1x）");
        yield return new("Round corners (1.5x)", "圓角（1.5x）");
        yield return new("Small text (13 px)", "細字（13 px）");
        yield return new("Default text (14 px)", "預設字（14 px）");
        yield return new("Large text (16 px)", "大字（16 px）");
        yield return new("Corner shape", "角形狀");
        yield return new("Type scale", "字體比例");
        yield return new("Example progress", "示例進度");
        yield return new("Exited (1)", "已結束（1）");
        yield return new("Notifications arrive with the chosen motion", "通知會用揀咗嘅動畫出現");
        yield return new("Playing on the live preview in the studio above.", "喺上面工作室嘅即時預覽播緊。");
        yield return new("Plays a page entrance, a staggered list, a progress bar and a notification on the studio's live preview with the current motion settings.", "用目前嘅動畫設定，喺工作室嘅即時預覽播頁面進場、逐個出現嘅清單、進度條同通知。");
        yield return new("Compact", "緊湊");
        yield return new("Comfortable", "舒適");
        yield return new("Spacious", "寬鬆");
        yield return new("Default dark", "預設深色");
        yield return new("Default light", "預設淺色");
        yield return new("Ocean", "海洋");
        yield return new("Forest", "森林");
        yield return new("Sunset", "日落");
        yield return new("Mono", "單色");
        yield return new("Surface (window background)", "表面（視窗背景）");
        yield return new("Surface, high", "表面，高");
        yield return new("Surface, highest (toasts)", "表面，最高（提示）");
        yield return new("Surface container", "表面容器");
        yield return new("On primary", "主要色上嘅文字");
        yield return new("Primary container", "主要容器");
        yield return new("Secondary container (selection, tonal buttons)", "次要容器（選取、色調按鈕）");
        yield return new("On secondary container", "次要容器上嘅文字");
        yield return new("Tertiary container", "第三容器");
        yield return new("On tertiary container", "第三容器上嘅文字");
    }
}
