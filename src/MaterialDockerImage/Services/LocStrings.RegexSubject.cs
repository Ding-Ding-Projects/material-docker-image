namespace MaterialDockerImage.Services;

/// <summary>
/// Hong Kong written Cantonese for the third wave of regex builder features: the Unicode block picker, per-token help, the subject tools
/// (scopes, hex view, line endings, files), pattern comparison and saved pattern descriptions. The help page bodies, the style tips, the comparison
/// verdicts and the reports built from numbers stay in English.
/// </summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseRegexSubject()
    {
        // Token help
        yield return new("Select a row of the explanation to read the full help for that token.", "喺說明入面選一行，就可以睇嗰個部分嘅完整說明。");
        yield return new("Help for the selected token", "所選部分嘅說明");
        // Unicode blocks
        yield return new("Unicode block", "Unicode 區塊");
        yield return new("Filter the blocks", "篩選區塊");
        yield return new("Not in this block", "唔喺呢個區塊");
        yield return new("Matches a character of one named block such as Greek or Cyrillic. .NET knows the blocks of the Basic Multilingual Plane by name.", "配對屬於某個命名區塊（例如希臘文或西里爾文）嘅字元。.NET 認得基本多文種平面入面各個區塊嘅名。");
        yield return new(".NET supports Unicode blocks only. It has no script properties such as \\p{Greek}, and its blocks stop at the Basic Multilingual Plane.", ".NET 只支援 Unicode 區塊，冇 \\p{Greek} 呢類文字系統屬性，區塊亦只去到基本多文種平面。");
        yield return new("Insert block", "插入區塊");
        yield return new("No block matches the filter.", "冇區塊符合篩選。");
        // Subject tools
        yield return new("Subject tools", "測試文字工具");
        yield return new("Test the sample line by line or page by page, look at its bytes in an encoding, change its line endings, or load a text file into it and save it back. A file is read up to the sample limit.", "可以逐行或者逐頁測試範例文字、用某種編碼睇佢嘅位元組、更改換行方式，或者載入文字檔案再存返出去。檔案最多讀到範例上限。");
        yield return new("Scope", "範圍");
        yield return new("Encoding", "編碼");
        yield return new("Line ending", "換行方式");
        yield return new("Each line or each page is tested on its own, so ^ and $ anchor there and no match crosses the boundary. A page ends at a form feed.", "逐行或者逐頁各自測試，所以 ^ 同 $ 會喺每行或者每頁定位，配對亦唔會跨過邊界。換頁字元就係一頁嘅結尾。");
        yield return new("Used by the hex view, by Load and by Save. Auto reads a byte order mark, then valid UTF-8, then Latin-1, and saves as UTF-8.", "十六進位檢視、載入同儲存都會用。自動會先睇位元組順序標記，再睇係咪有效 UTF-8，否則當 Latin-1，儲存時用 UTF-8。");
        yield return new("The line ending that Convert and Save write.", "轉換同儲存時寫入嘅換行方式。");
        yield return new("Whole text", "整段文字");
        yield return new("Each line", "每一行");
        yield return new("Each page", "每一頁");
        yield return new("Auto", "自動");
        yield return new("Test by scope", "按範圍測試");
        yield return new("Runs the pattern on every line or page of the sample and lists where it matches.", "喺範例嘅每一行或者每一頁行一次樣式，並列出配對位置。");
        yield return new("Show hex view", "顯示十六進位檢視");
        yield return new("Shows the first 4 KiB of the sample as bytes in the chosen encoding.", "用所揀編碼，將範例頭 4 KiB 顯示成位元組。");
        yield return new("Convert line endings", "轉換換行方式");
        yield return new("Rewrites every line break in the sample as the chosen line ending.", "將範例入面每個換行改成所揀嘅換行方式。");
        yield return new("Load file…", "載入檔案…");
        yield return new("Replaces the sample with the first 64 KiB of a text file.", "用文字檔案頭 64 KiB 取代範例文字。");
        yield return new("Save sample…", "儲存範例…");
        yield return new("Writes the sample to a file in the chosen encoding and line ending.", "用所揀編碼同換行方式，將範例寫入檔案。");
        yield return new("Matches by scope", "按範圍嘅配對");
        yield return new("Hex view of the sample", "範例嘅十六進位檢視");
        yield return new("Enter a pattern to test.", "請輸入要測試嘅樣式。");
        yield return new("Load a file as the sample", "載入檔案作為範例");
        yield return new("Save the sample", "儲存範例");
        // Comparison
        yield return new("Compare patterns", "比較樣式");
        yield return new("Runs several patterns over the test cases above: each + line is a subject that must match and each - line a subject that must not. Every pattern is checked for the right answers and then timed.", "用上面嘅測試個案跑幾個樣式：每行 + 係必須配對嘅文字，每行 - 係必須唔配對嘅文字。每個樣式會先核對答案，再計時。");
        yield return new("Patterns", "樣式");
        yield return new("One pattern per line, at most 8. They use the flags chosen above.", "每行一個樣式，最多 8 個，用上面所揀嘅旗標。");
        yield return new("Add the current pattern", "加入目前樣式");
        yield return new("Adds the pattern of this field as a new line.", "將呢個欄位嘅樣式加做新一行。");
        yield return new("Compare", "比較");
        yield return new("Checks and times every pattern on the test cases. The times are measurements on this computer and change from run to run.", "核對每個樣式喺測試個案上嘅答案並計時。時間係喺呢部電腦量度，每次都可能唔同。");
        yield return new("Patterns to compare, one per line", "要比較嘅樣式，每行一個");
        yield return new("Pattern comparison", "樣式比較");
        yield return new("Comparing…", "比較緊…");
        // Saved pattern description
        yield return new("Description", "描述");
        yield return new("Description (optional)", "描述（可選）");
        yield return new("What the pattern is for, shown under its name and searched by the filter. Optional; at most 300 characters.", "呢個樣式嘅用途，會顯示喺名稱下面，篩選時亦會搜尋。可選，最多 300 個字元。");
    }
}
