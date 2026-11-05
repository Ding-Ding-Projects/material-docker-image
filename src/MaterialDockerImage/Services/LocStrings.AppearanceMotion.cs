namespace MaterialDockerImage.Services;

/// <summary>Cantonese for the appearance studio's transition patterns, spring bounce, wallpaper seed and A/B comparison.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseAppearanceMotion()
    {
        // Transition patterns.
        yield return new("Shared axis", "共用軸");
        yield return new("Fade through", "淡入淡出");
        yield return new("Container transform", "容器變形");
        yield return new("Slide", "滑入");
        yield return new("Transition", "過場");
        yield return new("Transition pattern", "過場效果");
        yield return new("Appearance: Transition pattern", "外觀：過場效果");
        yield return new("Appearance: Spring bounce", "外觀：彈簧回彈");
        yield return new("transition pattern", "過場效果");
        yield return new("Page and tab transition", "頁面同分頁過場");
        yield return new("The new page fades in while growing slightly.", "新頁面一路淡入一路微微放大。");
        yield return new("The new page grows out of a slightly smaller container.", "新頁面由一個細少少嘅容器度展開。");
        yield return new("The new page slides in from the side it comes from.", "新頁面由佢嚟嘅方向滑入。");
        yield return new("The new page moves along the axis of travel: right for later destinations, left for earlier ones.", "新頁面沿住移動方向郁：去後面嘅目的地向右，返前面嘅向左。");
        yield return new("Choosing it plays it on the live preview.", "揀咗就會喺即時預覽播一次。");
        yield return new("How pages and tabs change: shared axis, fade through, container transform or slide.", "頁面同分頁點樣切換：共用軸、淡入淡出、容器變形或者滑入。");
        yield return new("Shared axis moves the new page along the direction of travel, fade through fades it in while it grows slightly, container transform grows it out of a smaller container and slide brings it in from the side. With motion Off nothing animates; with Reduced, Reduced motion or Low stimulation nothing moves and pages change with a short fade only. Presets never change it.", "共用軸令新頁面沿住移動方向郁，淡入淡出令佢一路淡入一路微微放大，容器變形由細啲嘅容器展開，滑入就由側邊滑入。動態效果關咗就乜都唔郁；揀「減少」、開咗減少動態效果或者低刺激模式，就乜都唔會移動，頁面切換只會短短淡入。預設組合永遠唔會改佢。");

        // Spring bounce.
        yield return new("Spring", "彈簧");
        yield return new("Spring bounce", "彈簧回彈");
        yield return new("spring bounce", "彈簧回彈");
        yield return new("Bounce", "回彈");
        yield return new("bounce", "回彈");
        yield return new("overshoot", "衝過頭");
        yield return new("Spring easing", "彈簧緩動");
        yield return new("Used only at the Expressive level; this shows what it would do.", "只喺「表現力」級別先會用；呢度顯示佢會點郁。");
        yield return new("Shows the spring curve Expressive motion uses for movement, with the current spring bounce.", "顯示「表現力」動態效果移動時用嘅彈簧曲線，用緊而家嘅回彈設定。");
        yield return new("Spring bounce only applies when motion is Expressive. Choose Expressive above to use it.", "彈簧回彈只喺動態效果係「表現力」時先生效。喺上面揀「表現力」就用得。");
        yield return new("Spring bounce is paused because Reduced motion, Low stimulation or the Windows animation setting turns movement off.", "因為減少動態效果、低刺激模式或者 Windows 動畫設定停咗移動，彈簧回彈暫停咗。");
        yield return new("How far Expressive movement overshoots before it settles, from none to about 20%.", "「表現力」動態效果停定之前衝過頭幾多，由完全唔衝到大約 20%。");
        yield return new("How far Expressive movement overshoots before it settles, from 0% (no overshoot) to 100% (about 20% past the target).", "「表現力」動態效果停定之前衝過頭幾多，由 0%（唔衝過頭）到 100%（衝過目標大約 20%）。");
        yield return new("Only Expressive motion uses the spring: it replaces the emphasized easing for movement and scale (opacity never bounces). At every other level, and whenever Reduced motion, Low stimulation or the Windows animation setting stops movement, the slider is disabled and explains why.", "只有「表現力」動態效果會用彈簧：移動同縮放會用佢代替強調緩動（透明度永遠唔會彈）。其他級別，或者減少動態效果、低刺激模式、Windows 動畫設定停咗移動嘅時候，滑桿會停用並講明原因。");
        yield return new("Expressive movement overshoots by up to", "「表現力」動態效果最多會衝過頭");
        yield return new("before it settles.", "先停定。");

        // Motion notes.
        yield return new("Reduced motion is on: nothing moves, and pages change with a short fade only.", "減少動態效果開咗：乜都唔會移動，頁面切換只會短短淡入。");
        yield return new("Low stimulation is on: nothing moves, and pages change with a short fade only.", "低刺激模式開咗：乜都唔會移動，頁面切換只會短短淡入。");
        yield return new("Reduced: nothing moves, and pages change with a short fade only.", "減少：乜都唔會移動，頁面切換只會短短淡入。");
        yield return new("Turns off movement: the interface snaps to each final state and pages change with a short fade only.", "關閉移動：介面直接跳去最終狀態，頁面切換只會短短淡入。");
        yield return new("Pages change with the chosen transition", "頁面用揀咗嘅過場切換");

        // From wallpaper.
        yield return new("From wallpaper", "跟桌面背景");
        yield return new("Seed from the Windows desktop", "由 Windows 桌面攞種子色");
        yield return new("Apply the seed colour from the desktop wallpaper", "套用桌面背景嘅種子色");
        yield return new("The wallpaper is not read in documentation captures.", "製作文件截圖時唔會讀桌面背景。");
        yield return new("Reading the desktop wallpaper…", "讀緊桌面背景…");
        yield return new("Taken from the desktop picture.", "由桌面圖片攞嘅。");
        yield return new("The desktop picture could not be read, so no colour was taken from it.", "讀唔到桌面圖片，所以冇由佢度攞顏色。");
        yield return new("No desktop picture is set, so the solid background colour is used.", "冇設定桌面圖片，所以用純色背景。");
        yield return new("No desktop picture or background colour was found.", "搵唔到桌面圖片或者背景顏色。");
        yield return new("The seed colour was not changed.", "種子色冇改到。");
        yield return new("Reads the current desktop picture (or the solid background colour) and uses its most prominent colour as the seed. Nothing in Windows changes. Undo returns to the previous seed.", "讀而家嘅桌面圖片（或者純色背景），用最突出嘅顏色做種子色。Windows 入面乜都唔會改。復原可以返去之前嘅種子色。");

        // Compare A/B.
        yield return new("Compare A/B", "A/B 比較");
        yield return new("Shows your appearance beside a candidate preset or seed without changing anything until you apply it.", "將你嘅外觀同候選預設或者種子色並排顯示，你套用之前乜都唔會改。");
        yield return new("Wallpaper colour", "桌面背景顏色");
        yield return new("Typed seed colour", "自己輸入嘅種子色");
        yield return new("Candidate for B", "B 嘅候選");
        yield return new("Seed colour for B", "B 嘅種子色");
        yield return new("B is", "B 係");
        yield return new("A · current", "A · 而家");
        yield return new("Your appearance now", "你而家嘅外觀");
        yield return new("B ·", "B ·");
        yield return new("Apply B", "套用 B");
        yield return new("Makes B your appearance. Motion settings stay as they are, locked values keep their value, and Undo returns to A.", "將 B 變成你嘅外觀。動態效果設定唔變，鎖咗嘅值保持原樣，復原可以返去 A。");
        yield return new("Type a hex colour such as #0B7A99 for B.", "幫 B 輸入一個十六進位顏色，例如 #0B7A99。");
        yield return new("Choose From wallpaper in Presets first, so there is a wallpaper colour to compare.", "請先喺預設組合揀「跟桌面背景」，咁先有桌面背景顏色可以比較。");
        yield return new("B is computed without changing your appearance. Both previews are examples: nothing in them runs.", "計 B 嘅時候唔會改你嘅外觀。兩個預覽都係示範：入面乜都唔會執行。");
        yield return new("Close the comparison", "關閉比較");
    }
}
