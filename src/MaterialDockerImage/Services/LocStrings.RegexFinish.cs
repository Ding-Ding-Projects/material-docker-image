namespace MaterialDockerImage.Services;

/// <summary>
/// Hong Kong written Cantonese for the last wave of regex builder features: panel layout and preferences, tree editing, code templates, the folder search extras,
/// action files, the keyboard map, engine versions, and the generated wording that used to stay English (help pages, style tips, comparison verdicts, scope and
/// hex reports, growth sentences and the messages of the editing logic). Sentences with numbers or pieces of the user's pattern are templates ({0}, {1}, ...).
/// </summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseRegexFinish()
    {
        // ----- importing the output of an external search tool -----
        yield return new("Import tool output…", "匯入工具輸出…");
        yield return new("Import tool output", "匯入工具輸出");
        yield return new("Import from clipboard", "由剪貼簿匯入");
        yield return new("Tool output format", "工具輸出格式");
        yield return new("Auto-detect", "自動偵測");
        yield return new("The clipboard has no text to import.", "剪貼簿入面冇文字可以匯入。");
        yield return new("Reads the saved output of grep -n, grep -nH, ripgrep --vimgrep or ripgrep --json from a file into the result list. Nothing is run and no file named in the output is read.", "將已儲存嘅 grep -n、grep -nH、ripgrep --vimgrep 或 ripgrep --json 輸出由檔案讀入結果清單。唔會執行任何嘢，輸出入面提到嘅檔案亦唔會被讀取。");
        yield return new("Reads the output of grep -n, grep -nH, ripgrep --vimgrep or ripgrep --json that you copied, into the result list. Nothing is run.", "將你複製咗嘅 grep -n、grep -nH、ripgrep --vimgrep 或 ripgrep --json 輸出讀入結果清單。唔會執行任何嘢。");
        yield return new("How the output of an external search tool is read when you import it. Auto-detect picks the form that fits the most lines; choose one when lines of text could be mistaken for a column.", "匯入外部搜尋工具輸出時按咩格式讀取。自動偵測會揀最啱最多行嘅格式；如果文字行可能俾人當成欄號，就自己揀一種。");
        // ----- sections, labels and messages of the new panels -----
        yield return new("Preferences and layout", "偏好設定同排列");
        yield return new("Code templates", "程式碼範本");
        yield return new("Edit through the tree", "用樹狀說明編輯");
        yield return new("Action file and command line", "動作檔同命令列");
        yield return new("Keyboard shortcuts", "鍵盤快速鍵");
        yield return new("Keyboard shortcuts of the builder", "建構器嘅鍵盤快速鍵");
        yield return new("Panels", "面板");
        yield return new("Panels shown, in order:", "有顯示嘅面板（按次序）：");
        yield return new("always shown", "一直顯示");
        yield return new("Default flags for new search fields", "新搜尋欄位嘅預設旗標");
        yield return new("Time limit per match (ms)", "每次比對嘅時間限制（毫秒）");
        yield return new("Font size of the pattern box", "樣式輸入框嘅字體大小");
        yield return new("Default explanation detail", "預設說明詳細程度");
        yield return new("Save preferences", "儲存偏好設定");
        yield return new("Reset preferences", "重設偏好設定");
        yield return new("Preferences saved.", "偏好設定已儲存。");
        yield return new("Preferences were reset.", "偏好設定已重設。");
        yield return new("Layout saved.", "排列已儲存。");
        yield return new("Template saved.", "範本已儲存。");
        yield return new("The built-in template is used again.", "已經用返內建範本。");
        yield return new("Template preview copied.", "範本預覽已複製。");
        yield return new("Command line copied.", "命令列已複製。");
        yield return new("The editor was started.", "編輯器已啟動。");
        yield return new("Opening the editor…", "正在開啟編輯器…");
        yield return new("Choose which panels are shown and in what order, and the defaults for new search fields. Everything is remembered for this user on this computer. The pattern and these two panels cannot be hidden.", "揀邊啲面板要顯示同點樣排次序，同埋新搜尋欄位嘅預設值。全部都會記喺呢部電腦呢個使用者名下。樣式同呢兩個面板唔可以隱藏。");
        yield return new("Between {0} and {1}. A match that takes longer is stopped and reported.", "喺 {0} 同 {1} 之間。比對超過呢個時間就會停止並報告。");
        yield return new("Applies to the pattern box of this builder.", "套用於呢個建構器嘅樣式輸入框。");
        yield return new("Used by the explanation of a builder opened from now on.", "由而家起開啟嘅建構器，說明會用呢個詳細程度。");
        yield return new("Remembers these preferences for every search field, from the next search field that is created. The match time limit applies to patterns compiled from now on.", "記住呢啲偏好設定，由下一個建立嘅搜尋欄位開始套用。比對時間限制只對之後編譯嘅樣式有效。");
        yield return new("Goes back to the built-in defaults, including the panel layout, and removes the saved preferences.", "返回內建預設值（包括面板排列），並刪除已儲存嘅偏好設定。");
        yield return new("NonBacktracking and RightToLeft cannot both be default flags.", "NonBacktracking 同 RightToLeft 唔可以同時做預設旗標。");
        yield return new("The time limit must be a whole number between {0} and {1}.", "時間限制要係 {0} 至 {1} 之間嘅整數。");
        yield return new("The preferences could not be saved:", "偏好設定儲存唔到：");
        yield return new("The saved preferences could not be removed:", "移除唔到已儲存嘅偏好設定：");
        yield return new("Saved preferences could not be read, so the defaults are used:", "讀唔到已儲存嘅偏好設定，所以用返預設值：");
        yield return new("Saved templates could not be read, so the built-in ones are used:", "讀唔到已儲存嘅範本，所以用返內建範本：");
        yield return new("Select a row, then change the pattern from here. Each edit rewrites the pattern text, checks that it still compiles, and keeps your selection.", "先揀一行，再喺度改樣式。每次編輯都會重寫樣式文字、檢查佢仲編譯得到，並保留你嘅選取。");
        yield return new("Quantifier", "量詞");
        yield return new("Quantifier for the selected row", "所選行嘅量詞");
        yield return new("One of ? * + {n} {n,} {n,m}, optionally followed by ? for lazy. Leave it empty and press Set to remove the quantifier.", "可以係 ? * + {n} {n,} {n,m} 其中一個，後面加 ? 就係懶惰。留空再㩒「設定量詞」就會移除量詞。");
        yield return new("Optional (zero or one)", "可有可無（零個或一個）");
        yield return new("Zero or more", "零個或以上");
        yield return new("One or more", "一個或以上");
        yield return new("Set quantifier", "設定量詞");
        yield return new("Applies the quantifier typed above to the selected row.", "將上面輸入嘅量詞套用到所選嗰一行。");
        yield return new("Remove quantifier", "移除量詞");
        yield return new("Removes the quantifier of the selected row.", "移除所選行嘅量詞。");
        yield return new("Wrap in", "包裹成");
        yield return new("Wraps the selected row, with its quantifier, in a group or lookaround.", "將所選行（連量詞）包入群組或者前後查找。");
        yield return new("Wrap selected row", "包裹所選行");
        yield return new("Wraps the selected row in the chosen kind of group.", "將所選行包入揀咗嘅群組類型。");
        yield return new("Unwrap group", "拆開群組");
        yield return new("Removes the selected group and keeps what is inside it.", "移除所選群組，但保留入面嘅內容。");
        yield return new("Delete row", "刪除呢一行");
        yield return new("Deletes the selected row from the pattern.", "將所選嗰一行由樣式入面刪走。");
        yield return new("Lookahead", "前行查找");
        yield return new("Lookbehind", "後行查找");
        yield return new("Negative lookahead", "負向前行查找");
        yield return new("Negative lookbehind", "負向後行查找");
        yield return new("Turn on regular expression mode to edit the pattern through the tree.", "請先開啟正規表示式模式，先可以用樹狀說明編輯樣式。");
        yield return new("Select a row of the explanation first.", "請先揀說明入面其中一行。");
        yield return new("The shape of the code the Convert section generates for C#, PowerShell, JavaScript, Python, Java, Go and PHP. Your template replaces the built-in one for that language and kind; the pattern is still quoted for the language, and a pattern the language cannot express is still refused.", "「轉換」部分為 C#、PowerShell、JavaScript、Python、Java、Go 同 PHP 產生嘅程式碼形狀。你嘅範本會取代嗰個語言同類型嘅內建範本；樣式仍然會按該語言加引號，語言表達唔到嘅樣式一樣會被拒絕。");
        yield return new("Template text", "範本內容");
        yield return new("Plain text with placeholders. At least one placeholder is needed, usually {{pattern}}.", "附有佔位符嘅純文字。最少要有一個佔位符，通常係 {{pattern}}。");
        yield return new("Save template", "儲存範本");
        yield return new("Stores this template for the language and kind, replacing the built-in one.", "為呢個語言同類型儲存呢個範本，取代內建嗰個。");
        yield return new("Reset to built-in", "重設為內建");
        yield return new("Removes your template for the language and kind.", "移除你為呢個語言同類型設定嘅範本。");
        yield return new("Copy preview", "複製預覽");
        yield return new("Preview with the current pattern", "用目前樣式預覽");
        yield return new("Preview of the template with the current pattern", "用目前樣式預覽範本");
        yield return new("Language", "語言");
        yield return new("Modified after", "修改時間晚於");
        yield return new("Modified after (optional)", "修改時間晚於（可選）");
        yield return new("Modified after:", "修改時間晚於：");
        yield return new("Modified before", "修改時間早於");
        yield return new("Modified before (optional)", "修改時間早於（可選）");
        yield return new("Modified before:", "修改時間早於：");
        yield return new("Context lines", "上下文行數");
        yield return new("Include hidden files and folders", "包括隱藏嘅檔案同資料夾");
        yield return new("Include read-only files", "包括唯讀檔案");
        yield return new("Include system files and folders", "包括系統檔案同資料夾");
        yield return new("Encoding for files without a byte order mark", "冇位元組次序標記嘅檔案編碼");
        yield return new("Auto (UTF-8)", "自動（UTF-8）");
        yield return new("UTF-16 little endian", "UTF-16 小端序");
        yield return new("UTF-16 big endian", "UTF-16 大端序");
        yield return new("Windows-1252", "Windows-1252");
        yield return new("Latin-1 (ISO 8859-1)", "Latin-1（ISO 8859-1）");
        yield return new("How a file with no byte order mark is read and written back. A byte order mark (UTF-8, UTF-16 or UTF-32) always wins. Auto means UTF-8. A replacement writes each file back in the encoding it was read in. A read-only file is never edited, whatever this says.", "冇位元組次序標記嘅檔案點樣讀取同寫返。有位元組次序標記（UTF-8、UTF-16 或者 UTF-32）嘅話，一定跟標記。自動即係 UTF-8。取代時每個檔案都會用讀入時嘅編碼寫返。唔理呢度點揀，唯讀檔案永遠唔會被編輯。");
        yield return new("The encoding for files without a byte order mark is not one of the supported choices.", "冇位元組次序標記嘅檔案編碼唔屬於支援嘅選項。");
        yield return new("Only files last written at or after this local time are searched. Write 2026-11-04 or 2026-11-04 13:30. Empty means no limit.", "只會搜尋最後修改時間喺呢個本地時間或者之後嘅檔案。寫法係 2026-11-04 或者 2026-11-04 13:30。留空就冇限制。");
        yield return new("Only files last written at or before this local time. A date without a time covers that whole day.", "只會搜尋最後修改時間喺呢個本地時間或者之前嘅檔案。冇寫時間嘅日期代表嗰一整日。");
        yield return new("Lines shown before and after each match when you select a result, and written to saved result lists. At most 5.", "揀一個結果時會顯示每個比對前後嘅行，儲存結果清單時亦會寫入。最多 5 行。");
        yield return new("Save results…", "儲存結果…");
        yield return new("Save results", "儲存結果");
        yield return new("Saves the result list as JSON that can be loaded again, as text with context lines, or as CSV.", "將結果清單儲存做可以再載入嘅 JSON、附上下文行嘅文字，或者 CSV。");
        yield return new("Load results…", "載入結果…");
        yield return new("Load results", "載入結果");
        yield return new("Shows a result list that was saved earlier. The files are not read again.", "顯示之前儲存嘅結果清單。唔會再讀取嗰啲檔案。");
        yield return new("Open selected result", "開啟所選結果");
        yield return new("Opens the selected result in the external editor from Settings, or in Visual Studio Code when none is set, at its line when the editor is known. Double-click or Enter on a result does the same.", "用「設定」入面嘅外部編輯器開啟所選結果；冇設定就用 Visual Studio Code；如果認得嗰個編輯器，就會跳去嗰一行。對結果撳兩下或者㩒 Enter 都得。");
        yield return new("Select a result first.", "請先揀一個結果。");
        yield return new("There are no results to save. Run Search files first.", "冇結果可以儲存。請先執行「搜尋檔案」。");
        yield return new("Saved result list:", "已儲存嘅結果清單：");
        yield return new("save time unknown", "不明儲存時間");
        yield return new("An action file describes this search (pattern, flags, replacement, folder and file patterns) so it can be kept, shared and reopened. Opening one only fills in the builder: nothing is searched or replaced until you press the buttons.", "動作檔記錄一個搜尋（樣式、旗標、取代文字、資料夾同檔案樣式），方便保存、分享同再開啟。開啟動作檔只會填好建構器：喺你㩒按鈕之前，唔會搜尋或者取代任何嘢。");
        yield return new("Save action file…", "儲存動作檔…");
        yield return new("Save action file", "儲存動作檔");
        yield return new("Saves this search as a small JSON file.", "將呢個搜尋儲存做一個細細嘅 JSON 檔。");
        yield return new("Open action file…", "開啟動作檔…");
        yield return new("Open action file", "開啟動作檔");
        yield return new("Fills in the builder from an action file.", "由動作檔填好建構器。");
        yield return new("Copy command line", "複製命令列");
        yield return new("Copies the arguments that start the application with this search already in the builder.", "複製啟動參數，用佢啟動應用程式時，建構器入面已經填好呢個搜尋。");
        yield return new("version", "版本");
        yield return new("Copies the comparison as text, at the engine versions chosen above.", "將比較結果複製成文字，用上面揀咗嘅引擎版本。");
        yield return new("Correct the expression to filter the library.", "請改正表示式先可以篩選資料庫。");
        yield return new("Correct the expression to filter my patterns.", "請改正表示式先可以篩選我嘅樣式。");
        yield return new("The filter is not a valid pattern, so nothing is listed.", "篩選唔係有效樣式，所以乜都唔列出。");

        // ----- keyboard map -----
        yield return new("Open the builder from a search field, or move into it when it is open.", "喺搜尋欄位開啟建構器；如果已經開咗就移入去。");
        yield return new("Close the builder and return to the search field.", "關閉建構器並返去搜尋欄位。");
        yield return new("Move through the controls of the builder; focus stays inside until it is closed.", "喺建構器嘅控制項之間移動；焦點會留喺入面，直到關閉。");
        yield return new("Jump to the next panel that is shown, and wrap from the last to the first.", "跳去下一個有顯示嘅面板，到最尾就返去第一個。");
        yield return new("Jump to the previous panel that is shown.", "跳去上一個有顯示嘅面板。");
        yield return new("Jump to the first ten panels that are shown, in the order they appear.", "跳去頭十個有顯示嘅面板，按出現次序。");
        yield return new("Go back to the pattern box.", "返去樣式輸入框。");
        yield return new("Select the next or previous match in the sample text.", "喺範例文字入面揀下一個或者上一個比對。");
        yield return new("Press the focused button, toggle the focused option.", "㩒落有焦點嘅按鈕，或者切換有焦點嘅選項。");

        // ----- template placeholders and refusals -----
        yield return new("The pattern as a quoted literal for the language (for Go and PHP, with the options folded in as that language writes them).", "樣式用該語言嘅引號字面值寫出（Go 同 PHP 會按該語言寫法將選項併入）。");
        yield return new("The options expression for the language, for example RegexOptions.IgnoreCase or re.IGNORECASE | re.MULTILINE.", "該語言嘅選項表示式，例如 RegexOptions.IgnoreCase 或者 re.IGNORECASE | re.MULTILINE。");
        yield return new("The options as a trailing argument including its comma, or nothing when there are none.", "選項作為尾隨參數（連逗號），冇選項就乜都唔填。");
        yield return new("The replacement text as a quoted literal for the language.", "取代文字用該語言嘅引號字面值寫出。");
        yield return new("The pattern exactly as typed, not quoted.", "樣式原樣，唔加引號。");
        yield return new("The replacement exactly as typed, not quoted.", "取代文字原樣，唔加引號。");
        yield return new("The kind of code: IsMatch, Matches, Replace or Split.", "程式碼類型：IsMatch、Matches、Replace 或者 Split。");
        yield return new("The name of the language.", "語言名稱。");
        yield return new("A template cannot be empty. Use Reset to go back to the built-in one.", "範本唔可以留空。㩒「重設為內建」就可以返去內建嘅範本。");
        yield return new("A template can be at most {0} characters.", "範本最多 {0} 個字元。");
        yield return new("Unknown placeholder {0}. Known placeholders: {1}.", "未知佔位符 {0}。已知佔位符：{1}。");
        yield return new("A template needs at least one placeholder, usually {{pattern}}; otherwise the generated code would not contain the pattern.", "範本最少要有一個佔位符，通常係 {{pattern}}；否則產生嘅程式碼入面會冇樣式。");
        yield return new("Rendered with your template for {0} {1}; the notes below describe the built-in snippet.", "已用你為 {0} {1} 設定嘅範本產生；下面嘅備註係描述內建範本。");

        // ----- messages of the editing, date, layout and action logic -----
        yield return new("The quantifier is now {0}.", "量詞而家係 {0}。");
        yield return new("The quantifier was removed.", "量詞已移除。");
        yield return new("Added the quantifier {0}.", "已加上量詞 {0}。");
        yield return new("The part was wrapped in a non-capturing group so the quantifier repeats all of it.", "已將該部分包入非擷取群組，令量詞重複成個部分。");
        yield return new("A quantifier is ?, *, +, {n}, {n,} or {n,m}, optionally followed by ? for lazy.", "量詞可以係 ?、*、+、{n}、{n,} 或者 {n,m}，後面可以加 ? 變成懶惰。");
        yield return new("The repeat count is too large.", "重複次數太大。");
        yield return new("The maximum repeat count is smaller than the minimum.", "最多重複次數細過最少重複次數。");
        yield return new("This token has no quantifier to remove.", "呢個部分冇量詞可以移除。");
        yield return new("This kind of token cannot be repeated.", "呢種部分唔可以重複。");
        yield return new("A branch cannot carry a quantifier. Select the whole alternation, or wrap the branch in a group first.", "分支唔可以帶量詞。請揀成個或者運算，或者先將分支包入群組。");
        yield return new("That part of the pattern is no longer a node of the tree. Select a row again.", "樣式嗰部分已經唔係樹狀說明入面嘅節點。請重新揀一行。");
        yield return new("An invalid token cannot be wrapped. Delete or correct it first.", "無效部分唔可以包裹。請先刪除或者改正。");
        yield return new("Wrapped the selection in a capturing group.", "已將選取內容包入擷取群組。");
        yield return new("Wrapped the selection in a non-capturing group.", "已將選取內容包入非擷取群組。");
        yield return new("Wrapped the selection in an atomic group.", "已將選取內容包入原子群組。");
        yield return new("Wrapped the selection in a positive lookahead.", "已將選取內容包入正向前行查找。");
        yield return new("Wrapped the selection in a negative lookahead.", "已將選取內容包入負向前行查找。");
        yield return new("Wrapped the selection in a positive lookbehind.", "已將選取內容包入正向後行查找。");
        yield return new("Wrapped the selection in a negative lookbehind.", "已將選取內容包入負向後行查找。");
        yield return new("Capturing groups after this point are numbered one higher; check backreferences and replacements.", "呢個位置之後嘅擷取群組編號會加一；請檢查反向參照同取代文字。");
        yield return new("Capturing groups after this point are numbered one lower; check backreferences and replacements.", "呢個位置之後嘅擷取群組編號會減一；請檢查反向參照同取代文字。");
        yield return new("The group was removed.", "群組已移除。");
        yield return new("Only a group can be unwrapped.", "只有群組先可以拆開。");
        yield return new("This group is not closed, so it cannot be unwrapped. Correct it first.", "呢個群組未有閂括號，所以拆唔到。請先改正。");
        yield return new("The group has a quantifier and more than one item inside. Removing the group would make the quantifier repeat only the last item. Remove the quantifier first.", "呢個群組有量詞，入面又有多過一個項目。移除群組會令量詞只重複最後一個項目。請先移除量詞。");
        yield return new("The group holds alternatives, and removing it would let the alternation reach the text around it. Only a group that is the whole pattern can be unwrapped.", "呢個群組包含或者選項，移除佢會令或者運算影響到周圍嘅文字。只有成個樣式就係呢個群組時先可以拆開。");
        yield return new("The lookaround was removed and its contents now consume text.", "前後查找已移除，入面嘅內容而家會消耗文字。");
        yield return new("The part was deleted.", "該部分已刪除。");
        yield return new("Deleting this would empty the pattern. Clear the pattern box instead.", "刪除呢個會令樣式變成空白。請改為清空樣式輸入框。");
        yield return new("The edit would make the pattern invalid, so it was not applied: {0}", "呢個編輯會令樣式無效，所以冇套用：{0}");
        yield return new("The edited pattern would be longer than {0} characters.", "編輯後嘅樣式會超過 {0} 個字元。");
        yield return new("Write the date as 2026-11-04 or 2026-11-04 13:30.", "日期請寫成 2026-11-04 或者 2026-11-04 13:30。");
        yield return new("The earliest modified time is later than the latest one, so no file can match.", "最早修改時間遲過最遲修改時間，所以冇檔案符合。");
        yield return new("{0} cannot be hidden: it holds the pattern or the controls that restore the layout.", "{0} 唔可以隱藏：佢包含樣式，或者用嚟還原排列嘅控制項。");
        yield return new("There is no panel called {0}.", "冇叫做 {0} 嘅面板。");
        yield return new("The action has no pattern.", "動作冇樣式。");
        yield return new("The pattern is longer than {0} characters.", "樣式超過 {0} 個字元。");
        yield return new("The pattern is not valid: {0}", "樣式無效：{0}");
        yield return new("The description is longer than {0} characters.", "描述超過 {0} 個字元。");
        yield return new("The replacement is too long.", "取代文字太長。");
        yield return new("The folder or file patterns are too long.", "資料夾或者檔案樣式太長。");
        yield return new("Context lines must be between {0} and {1}.", "上下文行數要喺 {0} 至 {1} 之間。");

        // ----- engine versions -----
        yield return new("added in Python 3.11", "Python 3.11 先加入");
        yield return new("added in ECMAScript 2018", "ECMAScript 2018 先加入");
        yield return new("added in ECMAScript 2018 together with lookbehind", "ECMAScript 2018 連同後行查找一齊加入");
        yield return new("named groups were added in ECMAScript 2018", "命名群組係 ECMAScript 2018 先加入");
        yield return new("property escapes were added in ECMAScript 2018", "屬性跳脫係 ECMAScript 2018 先加入");
        yield return new("needs the u flag", "需要 u 旗標");
        yield return new("added in Python 3.14; write \\Z, which Python reads as the very end", "Python 3.14 先加入；請寫 \\Z，Python 會當成真正嘅結尾");
        yield return new("the s flag was added in ECMAScript 2018", "s 旗標係 ECMAScript 2018 先加入");
        yield return new(".NET 10 (the one this builder runs)", ".NET 10（呢個建構器用緊嗰個）");
        yield return new("ECMAScript 2017 and earlier (older browsers and Node)", "ECMAScript 2017 同更早（舊版瀏覽器同 Node）");
        yield return new("ECMAScript 2018 and later (current browsers and Node)", "ECMAScript 2018 同之後（而家嘅瀏覽器同 Node）");
        yield return new("Python 3.9 and 3.10", "Python 3.9 同 3.10");
        yield return new("Python 3.11 to 3.13", "Python 3.11 至 3.13");
        yield return new("Go 1.22 and later", "Go 1.22 同之後");
        yield return new("the regex crate used by ripgrep", "ripgrep 用嘅 regex crate");

        // ----- style tips -----
        yield return new("The pattern has {0} capturing groups and no backreference. If you do not need their text, (?:...) or the ExplicitCapture option keeps the match results smaller.", "樣式有 {0} 個擷取群組，但冇用到反向參照。如果唔需要佢哋嘅文字，用 (?:...) 或者 ExplicitCapture 選項可以令比對結果細啲。");
        yield return new("A pattern that starts with .* is unnecessary for a search, because a search already finds the match anywhere. It only makes a failing search slower. Remove it, or anchor the pattern with ^.", "搜尋本身已經會喺任何位置搵比對，所以樣式以 .* 開頭係多餘嘅，只會令搜尋失敗時更慢。請移除佢，或者用 ^ 錨定樣式。");
        yield return new("A trailing .* matches the rest of the line but adds nothing to whether the pattern matches. Remove it unless you need the rest of the line in the match.", "尾部嘅 .* 會比對行尾餘下部分，但對樣式有冇比對到冇影響。除非你要將行尾餘下部分包含喺比對入面，否則請移除。");
        yield return new("An unescaped . between letters matches any character, so {0}.{1} also matches {2}-{3}. Write \\. if you meant a literal full stop.", "字母之間冇跳脫嘅 . 會比對任何字元，所以 {0}.{1} 亦會比對 {2}-{3}。如果你想要字面嘅句號，請寫 \\.。");
        yield return new("An alternation with an empty branch, such as (a|), is an optional item written the long way. Use a? instead.", "有空分支嘅或者運算，例如 (a|)，其實係可有可無項目嘅長寫法。改用 a? 就得。");
        yield return new("One branch of this alternation is empty, so it can match nothing at all. If that is not intended, remove the stray |.", "呢個或者運算有一個分支係空嘅，所以可以乜都唔比對。如果唔係有意嘅，請移除多出嚟嘅 |。");
        yield return new("An alternation of single characters is shorter and faster as a set: [{0}].", "由單一字元組成嘅或者運算，改用字元集更短更快：[{0}]。");
        yield return new("Two branches of this alternation are identical. The second can never add a match.", "呢個或者運算有兩個分支完全一樣，第二個永遠都唔會多比對到嘢。");
        yield return new("A non-capturing group around a single item adds nothing unless a quantifier follows it. Remove the (?: ) around {0}.", "只包住單一項目嘅非擷取群組，除非後面有量詞，否則冇用。請移除 {0} 周圍嘅 (?: )。");
        yield return new("IgnoreCase is on, so [A-Za-z] is the same as [a-z]. Keep one range.", "已開 IgnoreCase，所以 [A-Za-z] 同 [a-z] 一樣。保留一個範圍就得。");

        // ----- comparison verdicts and growth -----
        yield return new("Stopped at the time budget; later patterns were not compared.", "時間用晒就停咗，之後嘅樣式冇比較。");
        yield return new("No pattern gave the expected answer for every subject, so none can be called the fastest.", "冇一個樣式喺所有測試文字都答啱，所以搵唔到最快嗰個。");
        yield return new("Fastest of the {0} pattern that are right for every subject: {1} ({2} ms per pass).", "喺 {0} 個所有測試文字都答啱嘅樣式之中，最快係：{1}（每次 {2} 毫秒）。");
        yield return new("Fastest of the {0} patterns that are right for every subject: {1} ({2} ms per pass).", "喺 {0} 個所有測試文字都答啱嘅樣式之中，最快係：{1}（每次 {2} 毫秒）。");
        yield return new("{0} other pattern is wrong for some subject, invalid or timed out.", "另外有 {0} 個樣式喺某啲測試文字答錯、無效或者超時。");
        yield return new("{0} other patterns are wrong for some subject, invalid or timed out.", "另外有 {0} 個樣式喺某啲測試文字答錯、無效或者超時。");
        yield return new("These are wall-clock times on this machine and change from run to run.", "呢啲係喺呢部電腦量度嘅實際時間，每次執行都會有啲唔同。");
        yield return new("about linear", "大約線性");
        yield return new("between linear and quadratic", "介乎線性同二次方之間");
        yield return new("about quadratic", "大約二次方");
        yield return new("worse than quadratic", "差過二次方");
        yield return new("Not enough sample probes to estimate growth.", "範例探測不足，估算唔到增長。");
        yield return new("Not enough a-run probes to estimate growth.", "a 連串探測不足，估算唔到增長。");
        yield return new("The sample probes finished in under a millisecond, too fast to estimate how time grows.", "範例探測喺一毫秒內完成，太快，估算唔到時間點樣增長。");
        yield return new("The a-run probes finished in under a millisecond, too fast to estimate how time grows.", "a 連串探測喺一毫秒內完成，太快，估算唔到時間點樣增長。");
        yield return new("Sample probes: time grew {0} (exponent about {1}) from {2} to {3} characters.", "範例探測：由 {2} 個字元到 {3} 個字元，時間增長{0}（指數約 {1}）。");
        yield return new("A-run probes: time grew {0} (exponent about {1}) from {2} to {3} characters.", "a 連串探測：由 {2} 個字元到 {3} 個字元，時間增長{0}（指數約 {1}）。");

        // ----- scope and hex reports -----
        yield return new("at {0}", "喺位置 {0}");
        yield return new("Line {0}: {1} match:", "第 {0} 行：{1} 個比對：");
        yield return new("Line {0}: {1} matches:", "第 {0} 行：{1} 個比對：");
        yield return new("Page {0}: {1} match:", "第 {0} 頁：{1} 個比對：");
        yield return new("Page {0}: {1} matches:", "第 {0} 頁：{1} 個比對：");
        yield return new("Text: {0} match:", "文字：{0} 個比對：");
        yield return new("Text: {0} matches:", "文字：{0} 個比對：");
        foreach (var (singular, unit) in new[] { ("line", "行"), ("page", "頁"), ("text", "段文字") })
        {
            foreach (var matchWord in new[] { "match", "matches" })
                foreach (var plural in new[] { "", "s" })
                    yield return new($"{{0}} {matchWord} in {{1}} of {{2}} {singular}{plural}.", $"{{2}} {unit}之中有 {{1}} {unit}有比對，共 {{0}} 個比對。");
            yield return new($"Stopped on a {singular} that exceeded the {{0}} ms limit.", $"喺一{(singular == "line" ? "行" : singular == "page" ? "頁" : "段文字")}停咗，因為超過 {{0}} 毫秒嘅限制。");
        }
        yield return new("The result was cut at the bounds.", "結果去到上限就截斷咗。");
        yield return new("{0} bytes in {1}", "{0} 個位元組（{1}）");
        yield return new("the first {0} shown", "顯示頭 {0} 個位元組");
        yield return new("No line breaks.", "冇換行。");
        yield return new("{0} CRLF, {1} LF, {2} CR.", "{0} 個 CRLF、{1} 個 LF、{2} 個 CR。");
        yield return new("{0} CRLF, {1} LF, {2} CR (mixed line endings).", "{0} 個 CRLF、{1} 個 LF、{2} 個 CR（換行方式混合）。");

        // ----- help pages -----
        yield return new("Written as: {0}", "寫法：{0}");
        yield return new("Example: {0} finds {1} in {2}", "例子：{0} 喺 {2} 入面搵到 {1}");
        yield return new("Shorthand class", "簡寫類別");
        yield return new("Escaped character", "跳脫字元");
        yield return new("Character set", "字元集");
        yield return new("Any character", "任何字元");
        yield return new("Line or text anchor", "行或者文字錨點");
        yield return new("Word boundary", "字詞邊界");
        yield return new("Greedy quantifier", "貪婪量詞");
        yield return new("Lazy quantifier", "懶惰量詞");
        yield return new("Alternative", "分支");
        yield return new("Invalid token", "無效部分");
        yield return new("Ordinary characters match themselves. A character that has a special meaning must be escaped with a backslash to be read literally.", "普通字元只會比對自己。有特別意思嘅字元要加反斜線跳脫，先會當字面意思。");
        yield return new("Case is respected unless IgnoreCase is on.", "除非開咗 IgnoreCase，否則會分大細楷。");
        yield return new("\\d is a digit, \\w a word character, \\s white space; the capital letter is the opposite. They stand for one character.", "\\d 係數字，\\w 係字詞字元，\\s 係空白；大楷字母代表相反。每個代表一個字元。");
        yield return new(".NET reads \\d and \\w as Unicode, so \\d also matches digits of other scripts. RegexOptions.ECMAScript would make them ASCII but cannot be combined with most other options.", ".NET 將 \\d 同 \\w 當作 Unicode，所以 \\d 亦會比對其他文字嘅數字。RegexOptions.ECMAScript 可以令佢哋只限 ASCII，但唔可以同大部分其他選項一齊用。");
        yield return new("A backslash before a symbol makes it literal; before a letter it can name a control character, a hexadecimal code or a class.", "符號前面加反斜線就變成字面意思；喺字母前面就可以代表控制字元、十六進位碼或者類別。");
        yield return new("Matches one character from the list. A leading ^ negates the set, a hyphen between two characters is a range, and a set can subtract another set.", "比對清單入面其中一個字元。開頭嘅 ^ 代表排除，兩個字元之間嘅連字號代表範圍，字元集亦可以減去另一個字元集。");
        yield return new("Inside a set most symbols are literal; only \\, ], ^ at the start and - between characters need care.", "字元集入面大部分符號都係字面意思；淨係 \\、]、開頭嘅 ^ 同字元之間嘅 - 要小心。");
        yield return new("Matches any one character except a newline. With Singleline on it matches the newline too.", "比對任何一個字元，換行符除外。開咗 Singleline 就連換行符都比對。");
        yield return new(".NET has categories and blocks but no script properties.", ".NET 有類別同區塊，但冇文字系統（script）屬性。");
        yield return new("\\p{...} matches a character of a Unicode general category or block, \\P{...} everything else.", "\\p{...} 比對屬於某個 Unicode 一般類別或者區塊嘅字元，\\P{...} 比對其餘所有字元。");
        yield return new("^ and $ match at the start and end of the text, or of every line when Multiline is on. \\A and \\z always mean the whole text.", "^ 同 $ 比對文字嘅開頭同結尾；開咗 Multiline 就係每一行嘅開頭同結尾。\\A 同 \\z 永遠係指成段文字。");
        yield return new("$ also matches just before a final newline; \\z does not.", "$ 亦會喺最尾嘅換行符之前配對；\\z 就唔會。");
        yield return new("\\b is the position between a word character and a non-word character, \\B any other position, \\G the end of the previous match.", "\\b 係字詞字元同非字詞字元之間嘅位置，\\B 係其他位置，\\G 係上一個比對嘅結尾。");
        yield return new("A word boundary is a position, so it has no width.", "字詞邊界係一個位置，所以冇闊度。");
        yield return new("Groups items together and saves what they matched as a numbered group, which a backreference or a replacement can use.", "將項目組合埋一齊，並將比對到嘅內容儲存做有編號嘅群組，反向參照或者取代文字都可以用。");
        yield return new("Groups are numbered by their opening parenthesis, left to right. Named groups are numbered after the unnamed ones.", "群組按開括號由左至右編號。命名群組排喺無名群組之後。");
        yield return new("A capturing group with a name. Use it as \\k<name> in the pattern and ${name} in a replacement.", "有名嘅擷取群組。喺樣式入面用 \\k<name>，喺取代文字入面用 ${name} 去引用。");
        yield return new("A name starts with a letter or underscore.", "名稱要以字母或者底線開頭。");
        yield return new("Groups items for a quantifier or an alternation without saving the match, which keeps group numbers stable.", "將項目組合起嚟畀量詞或者或者運算使用，但唔會儲存比對內容，所以群組編號唔會郁。");
        yield return new("Once the group has matched, the engine never backtracks into it. This prevents catastrophic backtracking and stands in for the possessive quantifiers that .NET lacks.", "群組一比對成功，引擎就唔會再回溯入去。咁可以避免災難性回溯，亦可以代替 .NET 冇嘅佔有式量詞。");
        yield return new("Uses the backtracking engine.", "使用回溯引擎。");
        yield return new("Switches options on or off from here, or only inside the group when a colon and a part follow. The letters are i, m, n, s and x.", "由呢度開始開關選項；如果後面跟住冒號同內容，就只喺群組入面生效。字母係 i、m、n、s 同 x。");
        yield return new("Pops one capture of the pop group and pushes the text between onto the push group. This is how .NET matches nested pairs.", "彈出 pop 群組嘅一個擷取，並將中間嘅文字推入 push 群組。.NET 就係咁比對巢狀配對。");
        yield return new("Checks what follows without consuming it: positive (?=) requires a match, negative (?!) forbids one.", "檢查後面係咩而唔會消耗字元：正向 (?=) 要求有比對，負向 (?!) 就唔准有比對。");
        yield return new("Checks what precedes the position. .NET allows a lookbehind of variable length.", "檢查呢個位置前面係咩。.NET 容許長度可變嘅後行查找。");
        yield return new("Matches the first alternative that fits, tried from left to right. Put the longer alternative first when one is the start of another.", "由左至右試，比對第一個啱嘅選項。如果一個選項係另一個嘅開頭，就將較長嘅放前面。");
        yield return new("Repeats the previous item. A greedy quantifier takes as many repetitions as it can and gives some back if the rest of the pattern needs them.", "重複前一個項目。貪婪量詞會盡量重複，如果樣式其餘部分需要，就交還一啲。");
        yield return new("The same repetition counts, but as few repetitions as possible are taken first.", "重複次數規則一樣，但會先取盡量少嘅重複。");
        yield return new("Matches the same text that an earlier group matched, not the same pattern.", "比對同前面某個群組比對到嘅相同文字，而唔係相同嘅樣式。");
        yield return new("A reference to a group that does not exist is an error.", "引用一個唔存在嘅群組會出錯。");
        yield return new("Chooses between two alternatives depending on whether a group has captured, or on a lookaround.", "按群組有冇擷取到，或者按前後查找嘅結果，喺兩個分支之間揀一個。");
        yield return new("Text the engine ignores. After the x option, white space is ignored too and a # starts a comment up to the end of the line.", "引擎會忽略嘅文字。用咗 x 選項之後，空白亦會被忽略，# 開始嘅註解會去到行尾。");
        yield return new("One of the choices of an alternation. A branch may be empty, which makes the whole alternation optional.", "或者運算入面其中一個選項。選項可以留空，咁成個或者運算就變成可有可無。");
        yield return new("The framework reports a pattern as invalid when it is not well-formed; this row marks the place where the explanation could not continue. The message beside the pattern says what is wrong.", "樣式格式唔正確時，框架會報告無效；呢一行標示說明去到邊度無法繼續。樣式旁邊嘅訊息會講明錯咩。");
    }
}
