namespace MaterialDockerImage.Services;

/// <summary>
/// Hong Kong written Cantonese for the second wave of regex builder features: folder search, match navigation, explanation export,
/// replacement explanation, parameters, remembered experiments and the flavor comparison. Messages built from numbers or from engine and
/// parser text (for example "3 replacements in 2 files" and the reasons a conversion was refused) stay in English.
/// </summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseRegexTools()
    {
        // Explanation
        yield return new("Each token of the pattern in plain English. Select a row to highlight that token in the pattern, or move the caret in the pattern to select its row.", "用淺白文字說明樣式每個部分。選一行就會喺樣式入面標示嗰部分；喺樣式入面移動游標，亦會揀返對應嗰行。");
        yield return new("Detail", "詳細程度");
        yield return new("Full", "完整");
        yield return new("Overview shows the top-level tokens only, Standard three levels, Full every level. The export uses the same level.", "概覽只顯示最上層部分，標準顯示三層，完整顯示所有層次。匯出都用同一個程度。");
        yield return new("Export format", "匯出格式");
        yield return new("Text, Markdown or JSON. The export holds the pattern, the options and the tokens at the chosen detail.", "純文字、Markdown 或 JSON。匯出內容包括樣式、選項同所揀程度嘅各部分。");
        yield return new("Copy explanation", "複製說明");
        yield return new("Copies the explanation in the chosen format.", "按所揀格式複製說明。");
        yield return new("Save explanation…", "儲存說明…");
        yield return new("Saves the explanation in the chosen format to a file you choose.", "按所揀格式將說明儲存到你揀嘅檔案。");
        yield return new("Save explanation", "儲存說明");
        yield return new("Explanation copied.", "已複製說明。");
        yield return new("There is no pattern to explain.", "冇樣式可以說明。");
        // Match navigation
        yield return new("Previous match", "上一個比對");
        yield return new("Go to the previous match and wrap at the start. Shift+F3.", "跳去上一個比對，去到開頭就轉返去最後一個。Shift+F3。");
        yield return new("Next match", "下一個比對");
        yield return new("Go to the next match and wrap at the end. F3.", "跳去下一個比對，去到最後就轉返去第一個。F3。");
        yield return new("No match to go to.", "冇比對可以跳去。");
        yield return new("Use Next match or F3 to step through the matches.", "用「下一個比對」或 F3 逐個睇比對。");
        yield return new("Folder search results", "資料夾搜尋結果");
        // Replacement
        yield return new("Use $1 or ${name} for a group, $0 or $& for the whole match, $` and $' for the text before and after, $+ for the last group, $_ for the whole text and $$ for a dollar sign.", "用 $1 或 ${name} 取群組，$0 或 $& 取整個比對，$` 同 $' 取比對前後嘅文字，$+ 取最後一組，$_ 取整段文字，$$ 代表美元符號。");
        yield return new("Insert a reference at the caret:", "喺游標位置插入參照：");
        yield return new("Characters to insert", "要插入嘅字元");
        yield return new("Type escapes such as \\n \\t \\\\ \\xHH \\uHHHH \\u{1F600}. .NET replacement text has no escapes of its own, so they are turned into the characters themselves. The second button encodes ordinary text so a $ stays a dollar sign.", "輸入 \\n \\t \\\\ \\xHH \\uHHHH \\u{1F600} 呢類跳脫序列。.NET 取代文字本身冇跳脫，所以會轉成字元本身。第二個掣會編碼一般文字，令 $ 保持係美元符號。");
        yield return new("Insert characters", "插入字元");
        yield return new("Insert as literal text", "當作原文插入");
        yield return new("Inserts the text with every $ doubled, so it is replaced as typed.", "插入文字時將每個 $ 變成 $$，令取代時照原樣輸出。");
        yield return new("Replacement explained", "取代文字說明");
        yield return new("Type a replacement to see it explained.", "輸入取代文字就會見到說明。");
        // Posix and More tab
        yield return new("Convert POSIX classes in the pattern", "轉換樣式入面嘅 POSIX 類別");
        yield return new("Rewrites [[:alpha:]] and the other POSIX classes in the whole pattern as .NET sets. Letters, digits and case follow Unicode; punctuation, graph, print and control follow ASCII.", "將整個樣式入面嘅 [[:alpha:]] 同其他 POSIX 類別改寫成 .NET 字元集。字母、數字同大細楷跟 Unicode；標點、圖形、可列印同控制字元跟 ASCII。");
        yield return new("The pattern has no POSIX classes.", "樣式入面冇 POSIX 類別。");
        yield return new("POSIX classes converted:", "已轉換 POSIX 類別：");
        yield return new("The pattern could not be converted.", "樣式轉換唔到。");
        // Parameters
        yield return new("Parameters", "參數");
        yield return new("A parameter is written {{name}} or {{name:default}} in a pattern. The pattern still works as it stands; filling it replaces each placeholder with the value, escaped as literal text unless you choose otherwise.", "樣式入面嘅參數寫成 {{name}} 或 {{name:預設值}}。樣式照樣可以用；填寫時每個佔位符會換成你嘅值，除非另外揀，否則會當原文跳脫。");
        yield return new("Insert values as pattern fragments (not escaped)", "將值當作樣式片段插入（唔跳脫）");
        yield return new("Fill the pattern", "填寫樣式");
        yield return new("Replaces the placeholders in the pattern with the values.", "用你嘅值取代樣式入面嘅佔位符。");
        yield return new("Make the selection a parameter", "將選取部分變成參數");
        yield return new("Replaces the selected part of the pattern with {{name}} or {{name:default}}, so the pattern can be reused with other values. Saved patterns keep their placeholders.", "將樣式入面選取嘅部分換成 {{name}} 或 {{name:預設值}}，令樣式可以用其他值重用。已儲存嘅樣式會保留佔位符。");
        yield return new("Parameter name", "參數名稱");
        yield return new("Default (optional)", "預設值（可選）");
        yield return new("Turn selection into parameter", "選取部分變參數");
        yield return new("Select the part of the pattern to make a parameter first.", "請先喺樣式入面揀要變成參數嘅部分。");
        yield return new("This pattern has no parameters.", "呢個樣式冇參數。");
        yield return new("Default:", "預設：");
        yield return new("Unsupported:", "不支援：");
        yield return new("Different:", "讀法不同：");
        yield return new("Skipped:", "被略過：");
        yield return new("Parameters filled.", "已填寫參數。");
        yield return new("parameters to fill in the Parameters tab.", "個參數要喺「參數」分頁填寫。");
        yield return new("A parameter name starts with a letter or _ and has up to 40 letters, digits or _.", "參數名稱以字母或 _ 開頭，最多 40 個字母、數字或 _。");
        yield return new("A default has at most 200 characters and no braces.", "預設值最多 200 個字元，唔可以有大括號。");
        // History
        yield return new("Remembering stores the pattern, flags, sample text, replacement and test cases in regex-experiments.json in the app data folder, at most 50 experiments and 1 MiB. Nothing is stored until you press Remember.", "記住會將樣式、旗標、範例文字、取代文字同測試案例存入應用程式資料夾嘅 regex-experiments.json，最多 50 個實驗、1 MiB。未撳「記住」之前唔會儲存任何嘢。");
        yield return new("Experiment name", "實驗名稱");
        yield return new("Experiment name (optional)", "實驗名稱（可選）");
        yield return new("Optional. The pattern is used when empty.", "可選。留空就用樣式作名稱。");
        yield return new("Remember this experiment", "記住呢個實驗");
        yield return new("Stores the current pattern, flags, sample, replacement and test cases.", "儲存目前嘅樣式、旗標、範例、取代文字同測試案例。");
        yield return new("Forget all", "全部忘記");
        yield return new("Deletes every remembered experiment.", "刪除所有已記住嘅實驗。");
        yield return new("No remembered experiments yet.", "仲未有記住嘅實驗。");
        yield return new("Restores the pattern, flags, sample, replacement and test cases.", "還原樣式、旗標、範例、取代文字同測試案例。");
        yield return new("sample characters", "個範例字元");
        yield return new("Experiment remembered.", "已記住實驗。");
        yield return new("All remembered experiments were forgotten.", "已忘記所有實驗。");
        yield return new("Experiment loaded:", "已載入實驗：");
        yield return new("Could not save the experiment history:", "儲存唔到實驗歷史：");
        yield return new("The experiment history could not be read:", "讀取唔到實驗歷史：");
        yield return new("Enter a pattern to remember.", "請輸入要記住嘅樣式。");
        yield return new("Also save the replacement and the test cases", "同時儲存取代文字同測試案例");
        yield return new("Saving is the only way the builder stores anything here. The sample text is never saved with a pattern; the replacement and test cases are saved only when the box below is ticked.", "儲存係建構器喺呢度儲存嘢嘅唯一方法。範例文字永遠唔會跟樣式一齊儲存；只有剔咗下面個格，先會儲存取代文字同測試案例。");
        yield return new("Stores the pattern and flags in regex-patterns.json in the app data folder (at most 200). With the box below ticked it also stores the replacement and the test cases.", "將樣式同旗標存入應用程式資料夾嘅 regex-patterns.json（最多 200 個）。剔咗下面個格，仲會儲存取代文字同測試案例。");
        // Flavors
        yield return new("Flavor comparison", "引擎比較");
        yield return new("How the constructs this pattern uses are read by other engines. It comes from a hand-written table, checked against each engine while it was written; the pattern itself is never run in another engine.", "呢個樣式用到嘅語法，其他引擎點樣解讀。內容來自手寫對照表，編寫時逐個引擎核對過；樣式本身唔會喺其他引擎執行。");
        yield return new("Copy comparison", "複製比較");
        yield return new("Copies the comparison as text.", "將比較複製成文字。");
        yield return new("Comparison copied.", "已複製比較。");
        yield return new("Type a pattern to compare it across engines.", "輸入樣式就可以喺各引擎之間比較。");
        yield return new("Regular expression mode is off, so the field is plain text and every engine reads it the same way.", "正則表達式模式已關閉，欄位係純文字，所有引擎讀法一樣。");
        yield return new("Choose at least one engine.", "請至少揀一個引擎。");
        yield return new("The pattern uses no construct the table tracks, so every engine reads it the same way.", "樣式冇用到對照表記錄嘅語法，所以所有引擎讀法一樣。");
        yield return new("every tracked construct is supported", "所有已記錄嘅語法都支援");
        yield return new(@"\d digit class", @"\d 數字類別");
        yield return new(@"\w \W \s \S classes", @"\w \W \s \S 類別");
        yield return new(@"\b \B boundaries", @"\b \B 邊界");
        yield return new("Lazy quantifier (*? +? ??)", "惰性量詞 (*? +? ??)");
        yield return new("Possessive quantifier (*+ ++ ?+)", "佔有式量詞 (*+ ++ ?+)");
        yield return new("Atomic group (?>...)", "原子群組 (?>...)");
        yield return new("Lookahead (?=...) (?!...)", "前瞻 (?=...) (?!...)");
        yield return new("Lookbehind (?<=...) (?<!...)", "後顧 (?<=...) (?<!...)");
        yield return new("Variable-length lookbehind", "不定長度後顧");
        yield return new("Named group (?<name>...)", "命名群組 (?<name>...)");
        yield return new("Named group (?P<name>...)", "命名群組 (?P<name>...)");
        yield return new("Non-capturing group (?:...)", "非擷取群組 (?:...)");
        yield return new(@"Numbered backreference \1", @"編號反向參照 \1");
        yield return new(@"Named backreference \k<name>", @"命名反向參照 \k<name>");
        yield return new("Inline options (?i) (?i:...)", "行內選項 (?i) (?i:...)");
        yield return new("Conditional (?(1)yes|no)", "條件 (?(1)是|否)");
        yield return new("Balancing group (?<a-b>...)", "平衡群組 (?<a-b>...)");
        yield return new("Recursion (?R) (?1)", "遞迴 (?R) (?1)");
        yield return new("Class subtraction [a-z-[aeiou]]", "字元集相減 [a-z-[aeiou]]");
        yield return new(@"Unicode category \p{L}", @"Unicode 類別 \p{L}");
        yield return new(@"Unicode block \p{IsGreek}", @"Unicode 區塊 \p{IsGreek}");
        yield return new(@"\A start of text", @"\A 文字開頭");
        yield return new(@"\z end of text", @"\z 文字結尾");
        yield return new(@"\Z end, or before a final newline", @"\Z 結尾，或最後換行之前");
        yield return new(@"\G end of previous match", @"\G 上一個比對嘅結尾");
        yield return new(@"\xHH escape", @"\xHH 跳脫");
        yield return new(@"\uHHHH escape", @"\uHHHH 跳脫");
        yield return new(@"\Q...\E literal span", @"\Q...\E 原文範圍");
        yield return new("POSIX class [[:alpha:]]", "POSIX 類別 [[:alpha:]]");
        yield return new("Comment (?#...)", "註解 (?#...)");
        yield return new(@"\R line break, \K reset, \X cluster", @"\R 換行、\K 重設、\X 字元簇");
        yield return new("Branch reset (?|...)", "分支重設 (?|...)");
        yield return new("Option x: ignore white space and # comments", "選項 x：忽略空白同 # 註解");
        yield return new("Option s: the dot matches a newline", "選項 s：句點比對換行");
        yield return new("Option m: ^ and $ at every line", "選項 m：^ 同 $ 比對每一行");
        yield return new("Option n: only named groups capture", "選項 n：只有命名群組擷取");
        yield return new("Right-to-left matching", "由右至左比對");
        yield return new("matches any Unicode decimal digit", "會比對任何 Unicode 十進位數字");
        yield return new("ASCII digits only unless Unicode properties are on", "除非開咗 Unicode 屬性，否則只係 ASCII 數字");
        yield return new("ASCII digits only", "只係 ASCII 數字");
        yield return new("use [0-9]", "用 [0-9]");
        yield return new("GNU extension, not POSIX", "GNU 擴充，唔係 POSIX");
        yield return new(@"\w is ASCII only unless Unicode properties are on", @"除非開咗 Unicode 屬性，否則 \w 只係 ASCII");
        yield return new(@"\w is ASCII only", @"\w 只係 ASCII");
        yield return new("word characters are ASCII only unless Unicode properties are on", "除非開咗 Unicode 屬性，否則字詞字元只係 ASCII");
        yield return new("word characters are ASCII only", "字詞字元只係 ASCII");
        yield return new("accepted, but it means an optional repeat, not lazy", "可以輸入，但意思係可選重複，唔係惰性");
        yield return new("Python 3.11 and later", "Python 3.11 或以後");
        yield return new("accepted, but read as a repeated repeat, not possessive", "可以輸入，但當作重複嘅重複，唔係佔有式");
        yield return new("the length must be fixed or bounded", "長度必須固定或有上限");
        yield return new("the length must be fixed", "長度必須固定");
        yield return new("Python writes (?P<name>...)", "Python 寫成 (?P<name>...)");
        yield return new("Python writes (?P=name)", "Python 寫成 (?P=name)");
        yield return new("the global form must start the pattern", "全域形式必須放喺樣式開頭");
        yield return new(@"the re module has no \p", @"re 模組冇 \p");
        yield return new(@"PCRE2 names scripts, for example \p{Greek}", @"PCRE2 用文字系統名稱，例如 \p{Greek}");
        yield return new("Python 3.14 and later", "Python 3.14 或以後");
        yield return new(@"means the very end, like \z", @"表示真正嘅結尾，同 \z 一樣");
        yield return new(@"PCRE2 writes \x{41}", @"PCRE2 寫成 \x{41}");
        yield return new(@"Go writes \x{41}", @"Go 寫成 \x{41}");
        yield return new("compiles, but as a different set", "可以編譯，但意思係另一個字元集");
        yield return new("as the s flag, not inline", "以 s 旗標表示，唔係行內");
        yield return new("as the m flag, not inline", "以 m 旗標表示，唔係行內");
        yield return new("grep matches one line at a time", "grep 一次只比對一行");
        yield return new("grep is already line by line", "grep 本身已經逐行比對");
        yield return new("reads it as the Greek script", "當作 Greek 文字系統");
        yield return new("a nested set is a union, so the vowels match too; Rust writes subtraction as [a-z--[aeiou]]", "巢狀字元集係聯集，所以母音都會比對；Rust 嘅相減寫成 [a-z--[aeiou]]");
        // Folder search
        yield return new("Search in files", "喺檔案入面搜尋");
        yield return new("Searches the files of a folder with this pattern. Replacing is a dry run first; applying saves an undo snapshot. Limits: 2 MiB per file by default, 5000 files, 1000 results, 15 seconds. Binary files and links are skipped.", "用呢個樣式搜尋資料夾入面嘅檔案。取代會先試行；套用時會儲存復原快照。限制：每個檔案預設 2 MiB、5000 個檔案、1000 個結果、15 秒。二進位檔案同連結會被略過。");
        yield return new("Folder to search", "要搜尋嘅資料夾");
        yield return new("A folder on this computer. A whole drive is refused. .git and node_modules are skipped.", "呢部電腦上嘅資料夾。整個磁碟機會被拒絕。.git 同 node_modules 會被略過。");
        yield return new("File patterns", "檔案樣式");
        yield return new("Patterns separated by ; or ,. * and ? match within a name; ** crosses folders. A leading ! excludes. A pattern with a slash matches the path from the folder.", "用 ; 或 , 分隔嘅樣式。* 同 ? 比對名稱入面；** 可以跨資料夾。開頭嘅 ! 代表排除。有斜線嘅樣式比對由資料夾起計嘅路徑。");
        yield return new("Largest file (KiB)", "最大檔案（KiB）");
        yield return new("Files larger than this are skipped and listed. The ceiling is 16384 KiB.", "大過呢個數字嘅檔案會被略過並列出。上限係 16384 KiB。");
        yield return new("Include subfolders", "包括子資料夾");
        yield return new("Search files", "搜尋檔案");
        yield return new("Finds the pattern in the files and lists file, line and column.", "喺檔案入面搜尋樣式，列出檔案、行同欄。");
        yield return new("Preview replacement", "預覽取代");
        yield return new("Shows what the replacement would change, file by file. Nothing is written.", "逐個檔案顯示取代會改變啲乜。唔會寫入任何嘢。");
        yield return new("Apply…", "套用…");
        yield return new("Asks for confirmation, saves a byte-exact undo snapshot and then writes the files. Only available after a preview.", "先要求確認，儲存逐位元組一致嘅復原快照，然後寫入檔案。預覽之後先可以用。");
        yield return new("Replace in files", "取代檔案內容");
        yield return new("Writes the previewed replacements. An undo snapshot is saved first.", "寫入預覽過嘅取代。會先儲存復原快照。");
        yield return new("Cancel search", "取消搜尋");
        yield return new("Replacement plan", "取代計劃");
        yield return new("Undo snapshots", "復原快照");
        yield return new("No undo snapshots yet.", "仲未有復原快照。");
        yield return new("Restores the original bytes of every file in this snapshot that has not been edited since.", "還原呢個快照入面、之後冇再被編輯嘅每個檔案嘅原始位元組。");
        yield return new("Searching…", "搜尋緊…");
        yield return new("Previewing…", "預覽緊…");
        yield return new("Writing…", "寫入緊…");
        yield return new("Restoring…", "還原緊…");
        yield return new("Search cancelled.", "已取消搜尋。");
        yield return new("Nothing to replace in the searched files.", "搜尋過嘅檔案入面冇嘢要取代。");
        yield return new("Nothing has been written. Use Apply to write them.", "未寫入任何嘢。用「套用」先會寫入。");
        yield return new("The plan was cut at its limit.", "計劃去到上限就停咗。");
        yield return new("The pattern, replacement or folder changed since the preview. Preview again.", "預覽之後樣式、取代文字或資料夾改變咗。請再預覽。");
        yield return new("A byte-exact undo snapshot is saved first, and Undo restores the files that have not been edited since.", "會先儲存逐位元組一致嘅復原快照；「復原」會還原之後冇再被編輯嘅檔案。");
        yield return new("The files already written were restored.", "已寫入嘅檔案已經還原。");
        yield return new("Snapshot:", "快照：");
        yield return new("Choose a folder to search.", "請揀要搜尋嘅資料夾。");
        yield return new("The folder does not exist.", "資料夾唔存在。");
        yield return new("Choose a folder, not a whole drive.", "請揀資料夾，唔好揀整個磁碟機。");
        yield return new("The folder path is not valid.", "資料夾路徑無效。");
        yield return new("Enter a pattern to search for.", "請輸入要搜尋嘅樣式。");
        yield return new("Use at most 32 file patterns.", "最多用 32 個檔案樣式。");
        yield return new("A file pattern is longer than 200 characters.", "有檔案樣式長過 200 個字元。");
        // Conversions
        yield return new("The same pattern as C# or PowerShell code, a JSON string, a command line for grep, ripgrep or Select-String (using the folder and file patterns of Search in files), or code for JavaScript, Python, Java, Go or PHP. Anything the target cannot express is reported, never guessed.", "同一個樣式可以轉成 C# 或 PowerShell 程式碼、JSON 字串、grep、ripgrep 或 Select-String 嘅指令（用「喺檔案入面搜尋」嘅資料夾同檔案樣式），或者 JavaScript、Python、Java、Go、PHP 嘅程式碼。目標表達唔到嘅嘢會報告，唔會估。");
        yield return new("Quoting is exact for the target. The command lines and the other languages are checked against the flavor table: unsupported constructs stop the conversion and different readings are listed.", "引號處理對目標而言係準確嘅。指令同其他語言會對照引擎對照表：不支援嘅語法會令轉換停止，讀法不同嘅會列出。");
    }
}
