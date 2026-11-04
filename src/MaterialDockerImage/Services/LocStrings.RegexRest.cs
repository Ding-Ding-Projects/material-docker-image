namespace MaterialDockerImage.Services;

/// <summary>
/// Hong Kong written Cantonese for the generated regex builder wording that used to stay English: the reasons and notes of the code and command-line conversions, the
/// descriptions of the built-in snippets and of the guided palette, the rows and verdicts of the pattern comparison and timing tables. Sentences with numbers or pieces
/// of the user's pattern are templates ({0}, {1}, ...). Error text that the .NET framework itself writes for an invalid pattern is deliberately not here: it is shown as
/// the framework wrote it, after a translated lead-in.
/// </summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseRegexRest()
    {
        foreach (var pair in RegexRestConversionLeads()) yield return pair;
        foreach (var pair in RegexRestConversionReasons()) yield return pair;
        foreach (var pair in RegexRestConversionNotes()) yield return pair;
        foreach (var pair in RegexRestSnippets()) yield return pair;
        foreach (var pair in RegexRestTables()) yield return pair;
    }

    // ----- the lead-ins and frames that wrap the comparison sentences of a conversion -----
    private static IEnumerable<KeyValuePair<string, string>> RegexRestConversionLeads()
    {
        yield return new("Not convertible:", "唔能夠轉換：");
        yield return new("With -P (PCRE2):", "用 -P（PCRE2）嘅話：");
        yield return new("ripgrep's default engine cannot read this pattern", "ripgrep 嘅預設引擎讀唔到呢個樣式");
        yield return new("so -P is added, which needs a ripgrep built with PCRE2.", "所以加咗 -P，而 -P 需要支援 PCRE2 嘅 ripgrep。");
        yield return new("(checked against PCRE2, the nearest column; Perl has no column of its own).", "（對照 PCRE2 檢查，因為 PCRE2 係最接近嘅一欄；Perl 冇自己嘅一欄）。");
        yield return new("(checked against PCRE2, the closest column; Ruby's Onigmo engine has no column of its own).", "（對照 PCRE2 檢查，因為 PCRE2 係最貼近嘅一欄；Ruby 嘅 Onigmo 引擎冇自己嘅一欄）。");
        yield return new("(checked against PCRE2, the closest column; Java has no column of its own).", "（對照 PCRE2 檢查，因為 PCRE2 係最貼近嘅一欄；Java 冇自己嘅一欄）。");
        yield return new("{0} is not supported by {1}.", "{0} 喺 {1} 唔支援。");
        yield return new("{0} is not supported by {1} ({2}).", "{0} 喺 {1} 唔支援（{2}）。");
        yield return new("Rendered with your template for {0} {1}; the notes below describe the built-in snippet.", "已用你嘅 {0} {1} 範本顯示；下面嘅附註係描述內建範本。");
    }

    // ----- why a pattern cannot be converted -----
    private static IEnumerable<KeyValuePair<string, string>> RegexRestConversionReasons()
    {
        yield return new("Singleline has no meaning in grep, which matches one line at a time.", "grep 逐行比對，所以 Singleline 喺 grep 入面冇意思。");
        yield return new("grep has no right-to-left matching.", "grep 冇由右至左比對。");
        yield return new("grep does not ignore white space or support # comments in a pattern.", "grep 唔會忽略樣式入面嘅空白，亦唔支援 # 註解。");
        yield return new("The pattern ends with a lone backslash.", "樣式以一個單獨嘅反斜線結尾。");
        yield return new("\\{0} has no equivalent in grep -E (grep matches one line at a time and has no escape sequences for characters).", "\\{0} 喺 grep -E 冇對應寫法（grep 逐行比對，亦冇用嚟表示字元嘅跳脫序列）。");
        yield return new("\\{0} has no equivalent in grep -E.", "\\{0} 喺 grep -E 冇對應寫法。");
        yield return new("Unterminated character set.", "字元集合冇結束。");
        yield return new("Unterminated group name.", "群組名稱冇結束。");
        yield return new("Lookarounds, atomic groups, conditionals and inline options have no equivalent in grep -E. GNU grep -P (PCRE) supports some of them, but with different semantics.", "前後查找、原子群組、條件式同行內選項喺 grep -E 都冇對應寫法。GNU grep -P（PCRE）支援其中一部分，不過意思有啲唔同。");
        yield return new("Lazy quantifiers have no equivalent in grep -E (it always takes the longest match).", "惰性量詞喺 grep -E 冇對應寫法（佢永遠取最長嘅比對）。");
        yield return new("Possessive quantifiers have no equivalent in grep -E.", "佔有式量詞喺 grep -E 冇對應寫法。");
        yield return new("The pattern contains a line break; grep patterns match within one line.", "樣式包含換行；grep 嘅樣式只會喺同一行入面比對。");
        yield return new("Character class subtraction is .NET-specific and has no equivalent in grep -E.", "字元類別相減係 .NET 專有嘅寫法，喺 grep -E 冇對應寫法。");
        yield return new("\\{0} inside a character set has no equivalent in grep -E, where a backslash in a set is literal.", "字元集合入面嘅 \\{0} 喺 grep -E 冇對應寫法，因為 grep -E 集合入面嘅反斜線只係字面字元。");
        yield return new("ripgrep has no right-to-left matching.", "ripgrep 冇由右至左比對。");
        yield return new("Select-String tests one line at a time, so Singleline cannot apply.", "Select-String 逐行測試，所以 Singleline 用唔到。");
        yield return new("Select-String has no right-to-left switch.", "Select-String 冇由右至左嘅開關。");
        yield return new("JavaScript has no right-to-left matching.", "JavaScript 冇由右至左比對。");
        yield return new("Python's re module has no right-to-left matching.", "Python 嘅 re 模組冇由右至左比對。");
        yield return new("java.util.regex has no right-to-left matching.", "java.util.regex 冇由右至左比對。");
        yield return new("Go's regexp package has no right-to-left matching.", "Go 嘅 regexp 套件冇由右至左比對。");
        yield return new("Go's regexp package has no option to ignore white space and # comments.", "Go 嘅 regexp 套件冇忽略空白同 # 註解嘅選項。");
        yield return new("PHP's PCRE functions have no right-to-left matching.", "PHP 嘅 PCRE 函式冇由右至左比對。");
        yield return new("Perl has no right-to-left matching.", "Perl 冇由右至左比對。");
        yield return new("Ruby has no right-to-left matching.", "Ruby 冇由右至左比對。");
    }

    // ----- what to know about a converted pattern -----
    private static IEnumerable<KeyValuePair<string, string>> RegexRestConversionNotes()
    {
        yield return new("(?: ) becomes ( ): grep -E has no non-capturing group, so group numbers for backreferences can shift.", "(?: ) 會變成 ( )：grep -E 冇非擷取群組，所以反向參照用嘅群組編號可能會移位。");
        yield return new("Backreferences in ERE are a GNU grep extension.", "ERE 入面嘅反向參照係 GNU grep 嘅擴充功能。");
        yield return new("Group names are dropped; refer to the group by number.", "群組名稱會被捨棄；請用編號去引用群組。");
        yield return new("\\d matches only ASCII digits in grep, but Unicode digits in .NET.", "\\d 喺 grep 只比對 ASCII 數字，但喺 .NET 會比對 Unicode 數字。");
        yield return new("\\D is converted to [^0-9] (ASCII digits only).", "\\D 會轉成 [^0-9]（只限 ASCII 數字）。");
        yield return new("\\w becomes [[:alnum:]_], which depends on the locale.", "\\w 會變成 [[:alnum:]_]，結果視乎地區設定。");
        yield return new("\\W becomes [^[:alnum:]_], which depends on the locale.", "\\W 會變成 [^[:alnum:]_]，結果視乎地區設定。");
        yield return new("\\b is a GNU grep extension (not POSIX).", "\\b 係 GNU grep 嘅擴充功能（唔屬於 POSIX）。");
        yield return new("\\B is a GNU grep extension (not POSIX).", "\\B 係 GNU grep 嘅擴充功能（唔屬於 POSIX）。");
        yield return new("\\A becomes ^, which matches at the start of every line in grep.", "\\A 會變成 ^，喺 grep 入面會比對每一行嘅開頭。");
        yield return new("\\z becomes $, which matches at the end of every line in grep.", "\\z 會變成 $，喺 grep 入面會比對每一行嘅結尾。");
        yield return new("\\d inside a set becomes 0-9 (ASCII only).", "集合入面嘅 \\d 會變成 0-9（只限 ASCII）。");
        yield return new("\\w inside a set becomes [:alnum:]_.", "集合入面嘅 \\w 會變成 [:alnum:]_。");
        yield return new("ExplicitCapture is dropped: grep -E groups always capture, which only matters for backreferences.", "ExplicitCapture 會被捨棄：grep -E 嘅群組一律擷取，只會影響反向參照。");
        yield return new("CultureInvariant has no equivalent; grep uses the locale.", "CultureInvariant 冇對應功能；grep 使用地區設定。");
        yield return new("CultureInvariant has no equivalent.", "CultureInvariant 冇對應功能。");
        yield return new("NonBacktracking is an engine choice and is dropped.", "NonBacktracking 係引擎選擇，會被捨棄。");
        yield return new("NonBacktracking is already how Go matches.", "Go 本身就係用非回溯方式比對。");
        yield return new("Go's regexp package runs in time linear in the text, like the non-backtracking engine.", "Go 嘅 regexp 套件按文字長度線性運行，同非回溯引擎一樣。");
        yield return new("grep -P is a GNU grep option and needs a grep built with PCRE2. Quoting is for a POSIX shell.", "grep -P 係 GNU grep 嘅選項，需要支援 PCRE2 嘅 grep。引號寫法係為 POSIX shell 而設。");
        yield return new("Multiline is implied: ripgrep matches ^ and $ at the start and end of every line, unless --multiline is used.", "Multiline 已隱含：除非用 --multiline，否則 ripgrep 會喺每行嘅開頭同結尾比對 ^ 同 $。");
        yield return new("Singleline needs --multiline, so the whole file is searched as one text and a match may span lines.", "Singleline 需要 --multiline，所以成個檔案會當作一段文字搜尋，比對結果可以跨行。");
        yield return new("ExplicitCapture is dropped: ripgrep groups always capture, which only matters for replacements.", "ExplicitCapture 會被捨棄：ripgrep 嘅群組一律擷取，只會影響取代。");
        yield return new("-s makes the search case sensitive, which is what the IgnoreCase flag being off means here (ripgrep's default is smart case).", "-s 令搜尋分大細楷，即係呢度關閉 IgnoreCase 旗標嘅意思（ripgrep 預設係智能大細楷）。");
        yield return new("Quoting is for a POSIX shell. In PowerShell, use single quotes and double each single quote inside the pattern.", "引號寫法係為 POSIX shell 而設。喺 PowerShell 請用單引號，並將樣式入面每個單引號寫兩次。");
        yield return new("Select-String uses .NET regular expressions, so the pattern means the same thing.", "Select-String 用 .NET 正規表示式，所以樣式意思完全一樣。");
        yield return new("Multiline is implied: Select-String tests one line at a time, so ^ and $ match at the start and end of every line.", "Multiline 已隱含：Select-String 逐行測試，所以 ^ 同 $ 會比對每行嘅開頭同結尾。");
        yield return new("CultureInvariant is dropped; Select-String uses the current culture for case folding.", "CultureInvariant 會被捨棄；Select-String 用目前嘅文化設定嚟處理大細楷。");
        yield return new("NonBacktracking is dropped; Select-String cannot set regular expression options.", "NonBacktracking 會被捨棄；Select-String 設定唔到正規表示式選項。");
        yield return new("Get-ChildItem -Include and -Exclude match file names only.", "Get-ChildItem 嘅 -Include 同 -Exclude 只會比對檔案名稱。");
        yield return new("Select-String ignores case by default, which matches the IgnoreCase flag.", "Select-String 預設唔分大細楷，同 IgnoreCase 旗標一致。");
        yield return new("The u flag is added so that Unicode escapes and \\p{...} work; it also makes unknown escapes an error.", "已加上 u 旗標，令 Unicode 跳脫同 \\p{...} 可以運作；不過未知嘅跳脫亦會因此變成錯誤。");
        yield return new("ExplicitCapture is dropped: JavaScript groups always capture.", "ExplicitCapture 會被捨棄：JavaScript 嘅群組一律擷取。");
        yield return new("JavaScript replacements use $1, $<name>, $&, $` and $'; ${name} is not understood, write $<name> instead.", "JavaScript 嘅取代文字用 $1、$<name>、$&、$` 同 $'；唔識讀 ${name}，請改寫成 $<name>。");
        yield return new("ExplicitCapture is dropped: Python groups always capture.", "ExplicitCapture 會被捨棄：Python 嘅群組一律擷取。");
        yield return new("Python replacements use \\1 and \\g<name>; $1 and ${name} are not understood. Rewrite the replacement before use.", "Python 嘅取代文字用 \\1 同 \\g<name>；唔識讀 $1 同 ${name}。使用前請改寫取代文字。");
        yield return new("Python 3.14 was the version the table was checked against.", "對照表係以 Python 3.14 版本檢查。");
        yield return new("ExplicitCapture is dropped: Java groups always capture.", "ExplicitCapture 會被捨棄：Java 嘅群組一律擷取。");
        yield return new("Java replacements use $1 and ${name}; \\ and $ must be escaped with a backslash.", "Java 嘅取代文字用 $1 同 ${name}；\\ 同 $ 必須用反斜線跳脫。");
        yield return new("The comparison table has no Java column. PCRE2 was used as the nearest engine, so differences are not fully known. Test the pattern in Java.", "對照表冇 Java 一欄。已用 PCRE2 做最接近嘅引擎，所以差異未必完全清楚。請喺 Java 入面測試樣式。");
        yield return new("ExplicitCapture is dropped: Go groups always capture.", "ExplicitCapture 會被捨棄：Go 嘅群組一律擷取。");
        yield return new("Go replacements use $1 and ${name}; a name followed by a letter must be written ${name}.", "Go 嘅取代文字用 $1 同 ${name}；名稱後面緊接字母嘅話，必須寫成 ${name}。");
        yield return new("PHP uses PCRE2. The u modifier is added so that the pattern and text are read as UTF-8.", "PHP 用 PCRE2。已加上 u 修飾詞，令樣式同文字都按 UTF-8 讀取。");
        yield return new("PHP replacements use $1, ${1} and \\1; named references are not available, use numbers.", "PHP 嘅取代文字用 $1、${1} 同 \\1；唔支援命名參照，請用編號。");
        yield return new("The comparison table has no Perl column. PCRE2 was used as the nearest engine, so differences that are not in the table are not known. Test the pattern in Perl.", "對照表冇 Perl 一欄。已用 PCRE2 做最接近嘅引擎，所以表入面冇列出嘅差異並不清楚。請喺 Perl 入面測試樣式。");
        yield return new("The n flag needs Perl 5.22 or later.", "n 旗標需要 Perl 5.22 或以上。");
        yield return new("The pattern is kept in a single-quoted string and interpolated by qr, so $ and @ inside it are never read as variables.", "樣式放喺單引號字串入面，再由 qr 嵌入，所以入面嘅 $ 同 @ 唔會被當作變數。");
        yield return new("Perl replacements use $1 and $&; a named group is $+{name}, so ${name} is not understood, and $$ is not a dollar sign. Rewrite those before use.", "Perl 嘅取代文字用 $1 同 $&；命名群組寫成 $+{name}，所以唔識讀 ${name}，而 $$ 亦唔係金錢符號。使用前請改寫。");
        yield return new("Perl's split drops trailing empty pieces, which .NET keeps.", "Perl 嘅 split 會捨棄尾部嘅空片段，而 .NET 會保留。");
        yield return new("The comparison table has no Ruby column. PCRE2 was used as the closest engine, so differences that are not in the table are not known. Test the pattern in Ruby.", "對照表冇 Ruby 一欄。已用 PCRE2 做最貼近嘅引擎，所以表入面冇列出嘅差異並不清楚。請喺 Ruby 入面測試樣式。");
        yield return new("In Ruby ^ and $ always match at the start and end of every line (use \\A and \\z for the whole text), and the Regexp::MULTILINE option means what Singleline means in .NET.", "喺 Ruby 入面 ^ 同 $ 永遠比對每行嘅開頭同結尾（要比對成段文字請用 \\A 同 \\z），而 Regexp::MULTILINE 選項嘅意思等同 .NET 嘅 Singleline。");
        yield return new("ExplicitCapture is dropped: Ruby groups capture unless they are named, in which case the unnamed ones stop capturing.", "ExplicitCapture 會被捨棄：Ruby 嘅群組預設擷取，但一旦有命名群組，冇命名嘅群組就唔再擷取。");
        yield return new("Ruby replacements use \\1 and \\k<name>; $1 and ${name} are not understood. Rewrite the replacement before use.", "Ruby 嘅取代文字用 \\1 同 \\k<name>；唔識讀 $1 同 ${name}。使用前請改寫取代文字。");
        yield return new("A complete script: save it as a .ps1 file and pass the text file to read. The match timeout of 250 ms is reported as an error instead of hanging.", "完整嘅指令碼：儲存成 .ps1 檔案，再傳入要讀取嘅文字檔。比對逾時 250 毫秒會當作錯誤報告，而唔會卡住。");
        yield return new("The pattern contains a line break. A single-quoted PowerShell string may span lines, but check it survives your editor.", "樣式包含換行。PowerShell 嘅單引號字串可以跨行，不過請確認你嘅編輯器唔會改動佢。");
        yield return new("PowerShell converts the option names in the string to [System.Text.RegularExpressions.RegexOptions]. The -match operator is case-insensitive unless you use -cmatch, so the [regex] form is used to keep the flags exact.", "PowerShell 會將字串入面嘅選項名稱轉成 [System.Text.RegularExpressions.RegexOptions]。-match 運算子預設唔分大細楷，除非用 -cmatch，所以用 [regex] 寫法嚟確保旗標準確。");
        yield return new("In the replacement, $1 and ${name} are expanded by .NET because the replacement is in single quotes.", "取代文字用單引號，所以 $1 同 ${name} 會由 .NET 展開。");
    }

    // ----- built-in snippets and the guided palette -----
    private static IEnumerable<KeyValuePair<string, string>> RegexRestSnippets()
    {
        yield return new("Image tag", "映像標籤");
        yield return new("Digest (sha256)", "摘要（sha256）");
        yield return new("Semantic version", "語意化版本");
        yield return new("IPv4 address", "IPv4 位址");
        yield return new("IPv6 address", "IPv6 位址");
        yield return new("Port number", "埠號");
        yield return new("Container or image ID", "容器或映像 ID");
        yield return new("ISO 8601 date and time", "ISO 8601 日期同時間");
        yield return new("Email address", "電郵地址");
        yield return new("URL", "URL 網址");
        yield return new("Windows path", "Windows 路徑");
        yield return new("Environment variable NAME=value", "環境變數 NAME=value");
        yield return new("Log level word", "記錄等級字詞");
        yield return new("Optional registry host and port, repository path, optional tag and optional sha256 digest. Broad: combine with ^ and $ or Multiline to match whole values.", "可有可無嘅映像倉庫主機同埠、倉庫路徑、可有可無嘅標籤同 sha256 摘要。範圍較闊：配合 ^ 同 $ 或者 Multiline 就可以比對完整數值。");
        yield return new("A tag after a colon: starts with a letter, digit or _, then up to 127 letters, digits, _, . or -.", "冒號後面嘅標籤：以字母、數字或 _ 開頭，之後最多 127 個字母、數字、_、. 或者 -。");
        yield return new("A content digest: sha256: followed by 64 lowercase hexadecimal digits.", "內容摘要：sha256: 後面跟 64 個細楷十六進位數字。");
        yield return new("major.minor.patch with optional v prefix, pre-release and build metadata.", "major.minor.patch，可以加 v 前綴、預發行標記同建置中繼資料。");
        yield return new("Four numbers from 0 to 255 separated by dots.", "四個 0 至 255 嘅數字，用點分隔。");
        yield return new("Full or compressed (::) IPv6 address without a zone or embedded IPv4 part.", "完整或者壓縮（::）嘅 IPv6 位址，唔包含區域識別或內嵌 IPv4 部分。");
        yield return new("A port from 1 to 65535 after a colon.", "冒號後面 1 至 65535 嘅埠號。");
        yield return new("A full 64-digit or short 12-digit hexadecimal ID.", "完整 64 位或者簡短 12 位嘅十六進位 ID。");
        yield return new("A date such as 2026-11-03 with an optional time and time zone.", "例如 2026-11-03 嘅日期，可以附加時間同時區。");
        yield return new("A common email address shape (not a full validation).", "常見嘅電郵地址形式（並非完整驗證）。");
        yield return new("An http or https address up to the next space or quote.", "http 或 https 網址，直至下一個空格或引號為止。");
        yield return new("A drive-letter path such as C:\\Projects\\app\\Dockerfile.", "以磁碟機代號開頭嘅路徑，例如 C:\\Projects\\app\\Dockerfile。");
        yield return new("A NAME=value line; turn on Multiline to match every line of a block.", "NAME=value 嘅一行；開啟 Multiline 就可以比對一整段入面嘅每一行。");
        yield return new("A common log level keyword.", "常見嘅記錄等級關鍵字。");
        yield return new("A digit.", "一個數字。");
        yield return new("Not a digit.", "唔係數字。");
        yield return new("A word character: letter, digit or _.", "字詞字元：字母、數字或者 _。");
        yield return new("Not a word character.", "唔係字詞字元。");
        yield return new("White space.", "空白。");
        yield return new("Not white space.", "唔係空白。");
        yield return new("Any character except newline (Singleline includes newline).", "除換行符以外嘅任何字元（開咗 Singleline 就連換行符都包括）。");
        yield return new("Start of text (or line with Multiline).", "文字開頭（開咗 Multiline 就係每行開頭）。");
        yield return new("End of text (or line with Multiline).", "文字結尾（開咗 Multiline 就係每行結尾）。");
        yield return new("Word boundary.", "字詞邊界。");
        yield return new("Not a word boundary.", "唔係字詞邊界。");
        yield return new("Start of text only.", "只限文字開頭。");
        yield return new("End of text only.", "只限文字結尾。");
        yield return new("Capturing group around the selection.", "包住選取部分嘅擷取群組。");
        yield return new("Non-capturing group around the selection.", "包住選取部分嘅非擷取群組。");
        yield return new("Positive lookahead (uses the backtracking engine).", "正向前行查找（會用回溯引擎）。");
        yield return new("Negative lookahead (uses the backtracking engine).", "負向前行查找（會用回溯引擎）。");
        yield return new("Positive lookbehind (uses the backtracking engine).", "正向後行查找（會用回溯引擎）。");
        yield return new("Negative lookbehind (uses the backtracking engine).", "負向後行查找（會用回溯引擎）。");
        yield return new("Atomic group (uses the backtracking engine).", "原子群組（會用回溯引擎）。");
        yield return new("Or: either the left or the right side.", "或者：左邊或者右邊其中一邊。");
        yield return new("Optional: zero or one time.", "可有可無：零次或者一次。");
        yield return new("Zero or more times, greedy.", "零次或以上，貪婪。");
        yield return new("One or more times, greedy.", "一次或以上，貪婪。");
        yield return new("Zero or one time, lazy.", "零次或者一次，惰性。");
        yield return new("Zero or more times, lazy.", "零次或以上，惰性。");
        yield return new("One or more times, lazy.", "一次或以上，惰性。");
        yield return new("A character in the Greek Unicode block. .NET also offers other Is blocks, such as \\p{IsCyrillic}.", "希臘文 Unicode 區塊入面嘅字元。.NET 亦提供其他 Is 區塊，例如 \\p{IsCyrillic}。");
    }

    // ----- the rows of the comparison and timing tables, and the verdicts of the timing probe -----
    private static IEnumerable<KeyValuePair<string, string>> RegexRestTables()
    {
        yield return new("invalid:", "無效：");
        yield return new("ms", "毫秒");
        yield return new("timed out", "逾時");
        yield return new("must match {0}/{1}, must not match {2}/{3}", "應該符合 {0}/{1}，應該唔符合 {2}/{3}");
        yield return new("probe characters", "個字元");
        yield return new("probe matched", "有符合");
        yield return new("probe did not match", "冇符合");
        yield return new("sample x{0}", "樣本 ×{0}");
        yield return new("a-run {0}", "連續 a ×{0}");
        yield return new("Warning: time grew more than six times when the probe doubled.", "警告：探測長度加倍時，所需時間增加咗六倍以上。");
        yield return new("That is super-linear growth.", "即係超線性增長。");
        yield return new("Warning: {0} ({1} characters) exceeded the {2} ms limit.", "警告：{0}（{1} 個字元）超過 {2} 毫秒限制。");
        yield return new("The pattern may backtrack catastrophically on similar text.", "對相似嘅文字，樣式可能會災難性回溯。");
        yield return new("No super-linear growth was seen on these inputs.", "喺呢啲輸入入面睇唔到超線性增長。");
        yield return new("This is a measurement on a few inputs, not a proof.", "呢個只係針對少量輸入嘅量度，並非證明。");
    }
}
