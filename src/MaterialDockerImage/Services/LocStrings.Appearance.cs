namespace MaterialDockerImage.Services;

/// <summary>Hong Kong written Cantonese for the per-element appearance editor and the general authenticator.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> ElementAppearanceCantonese()
    {
        // Entry points
        yield return new("Customize this element…", "自訂呢個元素…");
        yield return new("Customize the focused element…", "自訂而家揀咗嘅元素…");
        yield return new("Pick an element to customize…", "點選一個元素嚟自訂…");
        yield return new("Show customized elements", "顯示已自訂嘅元素");
        yield return new("Reset all customized elements", "重設所有已自訂嘅元素");
        yield return new("Focus an element first, or use Pick an element to customize.", "請先揀好一個元素，或者用「點選一個元素嚟自訂」。");
        yield return new("All customized elements were reset.", "所有已自訂嘅元素已重設。");
        yield return new("Click the element to customize. Press Escape to cancel.", "請撳一下想自訂嘅元素。撳 Esc 取消。");
        yield return new("That spot has no element that can be customized.", "嗰個位置冇可以自訂嘅元素。");
        // Editor
        yield return new("Customize this element", "自訂呢個元素");
        yield return new("Element", "元素");
        yield return new("Element identity", "元素識別碼");
        yield return new("Close element editor", "關閉元素編輯器");
        yield return new("Override the background colour", "覆寫背景顏色");
        yield return new("Override the text colour", "覆寫文字顏色");
        yield return new("Override the corner radius", "覆寫圓角半徑");
        yield return new("Override the font size", "覆寫字體大小");
        yield return new("Override the font weight", "覆寫字體粗幼");
        yield return new("Override the padding", "覆寫內距");
        yield return new("Hide this element (it keeps its space)", "隱藏呢個元素（佢嘅位置會保留）");
        yield return new("Reset it from the Overrides tab or with Reset element if it disappears.", "如果佢消失咗，可以喺「覆寫清單」分頁或者用「重設元素」還原。");
        yield return new("Reset element", "重設元素");
        yield return new("Revert changes", "還原更改");
        yield return new("Changes apply to this element at once and are saved. Escape closes the editor. The element is found again by its place in the window, so the override survives restarts.", "更改會即時套用喺呢個元素並自動儲存。撳 Esc 關閉編輯器。元素係靠佢喺視窗入面嘅位置再搵返出嚟，所以覆寫重新啟動後都會保留。");
        yield return new("Search customized elements", "搜尋已自訂嘅元素");
        yield return new("No element has been customized yet. Right-click an element, choose Customize this element…, and its overrides will be listed here.", "未有元素被自訂。喺元素上撳右鍵，揀「自訂呢個元素…」，佢嘅覆寫就會列喺呢度。");
        yield return new("No customized element matches this search.", "冇已自訂嘅元素符合呢個搜尋。");
        yield return new("Reset all customized elements?", "重設所有已自訂嘅元素？");
        yield return new("{0} element(s) return to the theme's look. This cannot be undone.", "{0} 個元素會還原做主題嘅外觀，呢個動作冇得還原。");
        yield return new("Reset all", "全部重設");
        yield return new("No element has been customized.", "未有元素被自訂。");
        yield return new("Text and background contrast is {0}:1, which is hard to read. Aim for at least 4.5:1.", "文字同背景嘅對比只有 {0}:1，好難睇清楚。請至少做到 4.5:1。");
        yield return new("That element cannot be identified.", "認唔到呢個元素。");
        yield return new("At most 200 elements can be customized. Reset one first.", "最多可以自訂 200 個元素，請先重設其中一個。");
        yield return new("element-appearance.json could not be read ({0}). Move or fix the file first.", "讀唔到 element-appearance.json（{0}），請先移走或者修復個檔案。");
        yield return new("The element appearance could not be saved: {0}", "儲存唔到元素外觀：{0}");
    }

    private static IEnumerable<KeyValuePair<string, string>> AuthenticatorCantonese()
    {
        // Sections and layout
        yield return new("My accounts", "我嘅帳戶");
        yield return new("App locks", "應用程式鎖");
        yield return new("Add an account", "新增帳戶");
        yield return new("Paste a link", "貼上連結");
        yield return new("Enter manually", "手動輸入");
        yield return new("Search accounts", "搜尋帳戶");
        yield return new("Keep the time-based one-time codes for your own accounts here. Everything stays on this computer: secrets are protected for your Windows account and are never sent anywhere.", "喺呢度保管你自己帳戶嘅限時一次性驗證碼。所有嘢都留喺呢部電腦：密鑰受你嘅 Windows 帳戶保護，唔會傳去任何地方。");
        yield return new("Sample accounts, not your real ones", "示範帳戶，唔係你真正嘅帳戶");
        yield return new("The authenticator needs Windows secret protection, which is not available here.", "驗證器需要 Windows 密鑰保護，但呢部電腦用唔到。");
        yield return new("Nothing is changed while the file cannot be read.", "個檔案讀唔到嘅時候，唔會改動任何嘢。");
        yield return new("The authenticator file is larger than 512 KiB.", "驗證器檔案超過 512 KiB。");
        yield return new("The authenticator file cannot be opened by this Windows account.", "呢個 Windows 帳戶開唔到驗證器檔案。");
        yield return new("The authenticator file is not valid.", "驗證器檔案無效。");
        yield return new("The authenticator file has an unsupported version.", "驗證器檔案嘅版本唔支援。");
        yield return new("The authenticator file holds too many accounts.", "驗證器檔案入面嘅帳戶太多。");
        // Add form
        yield return new("otpauth:// link", "otpauth:// 連結");
        yield return new("Paste the link a service shows next to its QR code. Its issuer, algorithm, digits and period are used as given.", "貼上服務喺 QR 碼旁邊顯示嘅連結。連結入面嘅發行者、演算法、位數同週期會照用。");
        yield return new("Add account", "新增帳戶");
        yield return new("Issuer", "發行者");
        yield return new("The service the codes are for.", "驗證碼所屬嘅服務。");
        yield return new("Account name", "帳戶名稱");
        yield return new("Your account or e-mail at that service.", "你喺嗰個服務嘅帳戶或電郵。");
        yield return new("Base32 secret", "Base32 密鑰");
        yield return new("The setup key the service shows. Spaces and lower case are fine.", "服務顯示嘅設定金鑰。有空格同細楷都冇問題。");
        yield return new("Algorithm", "演算法");
        yield return new("Most services use SHA1.", "大部分服務用 SHA1。");
        yield return new("Digits", "位數");
        yield return new("Most services use 6.", "大部分服務用 6 位。");
        yield return new("Period (seconds)", "週期（秒）");
        yield return new("Most services use 30.", "大部分服務用 30 秒。");
        yield return new("The period must be a whole number of seconds.", "週期必須係整數秒。");
        yield return new("Account added: {0}", "已新增帳戶：{0}");
        // Rows
        yield return new("No accounts yet", "未有帳戶");
        yield return new("Add an account above with its otpauth:// link or its setup key, and its code appears here.", "喺上面用 otpauth:// 連結或者設定金鑰新增帳戶，驗證碼就會顯示喺呢度。");
        yield return new("No account matches this search.", "冇帳戶符合呢個搜尋。");
        yield return new("Current code for", "目前驗證碼：");
        yield return new("Time left for", "剩餘時間：");
        yield return new("Copy code for", "複製驗證碼：");
        yield return new("digits", "位");
        yield return new("Next:", "下一個：");
        yield return new("Unreadable secret", "讀唔到密鑰");
        yield return new("Code copied. It stays on the clipboard until you copy something else.", "驗證碼已複製。喺你複製其他嘢之前，佢會一直留喺剪貼簿。");
        yield return new("Rename account", "帳戶改名");
        yield return new("Delete this account?", "刪除呢個帳戶？");
        yield return new("Its secret is removed from this computer. If you have not kept its setup key or another authenticator, you may lose access to the account it protects.", "佢嘅密鑰會喺呢部電腦移除。如果你冇保留設定金鑰或者另一個驗證器，可能會登入唔到佢保護嘅帳戶。");
        yield return new("Account deleted.", "帳戶已刪除。");
        // Backup
        yield return new("Export encrypted backup…", "匯出加密備份…");
        yield return new("Import encrypted backup…", "匯入加密備份…");
        yield return new("A backup is encrypted with a passphrase you choose. Without that passphrase it cannot be opened, so keep it safe. Importing skips accounts you already have.", "備份會用你揀嘅密碼短語加密。冇呢個密碼短語就開唔到，所以請妥善保管。匯入時會略過你已有嘅帳戶。");
        yield return new("Passphrase", "密碼短語");
        yield return new("Repeat the passphrase", "重複密碼短語");
        yield return new("The two passphrases are different.", "兩個密碼短語唔一樣。");
        yield return new("Enter the passphrase.", "請輸入密碼短語。");
        yield return new("Encrypt the backup", "加密備份");
        yield return new("Choose a passphrase of at least 8 characters. It is not stored anywhere, and a backup cannot be opened without it.", "請揀一個至少 8 個字元嘅密碼短語。佢唔會儲存喺任何地方，冇佢就開唔到備份。");
        yield return new("There are no accounts to back up.", "冇帳戶可以備份。");
        yield return new("Save the encrypted backup", "儲存加密備份");
        yield return new("Encrypted backup saved.", "加密備份已儲存。");
        yield return new("The backup could not be saved: {0}", "儲存唔到備份：{0}");
        yield return new("Open an encrypted backup", "開啟加密備份");
        yield return new("That file is too large to be an authenticator backup.", "個檔案太大，唔會係驗證器備份。");
        yield return new("The file could not be read: {0}", "讀唔到個檔案：{0}");
        yield return new("Open the backup", "開啟備份");
        yield return new("Enter the passphrase this backup was encrypted with.", "請輸入加密呢個備份用嘅密碼短語。");
        yield return new("Imported {0} account(s). {1} already present or skipped.", "已匯入 {0} 個帳戶，{1} 個已存在或者被略過。");
        // Messages from the rules
        yield return new("Paste an otpauth:// link.", "請貼上 otpauth:// 連結。");
        yield return new("That link is too long to be an authenticator link.", "個連結太長，唔會係驗證器連結。");
        yield return new("That is not an otpauth:// link.", "呢個唔係 otpauth:// 連結。");
        yield return new("Counter-based (HOTP) accounts are not supported. Only time-based (TOTP) accounts are.", "唔支援計數式（HOTP）帳戶，只支援限時式（TOTP）帳戶。");
        yield return new("The link must be of the form otpauth://totp/…", "連結格式必須係 otpauth://totp/…");
        yield return new("The link repeats the \"{0}\" parameter.", "連結重複咗「{0}」參數。");
        yield return new("The link asks for an algorithm that is not supported. Use SHA1, SHA256 or SHA512.", "連結要求嘅演算法唔支援，請用 SHA1、SHA256 或 SHA512。");
        yield return new("The digits value in the link is not a number.", "連結入面嘅位數唔係數字。");
        yield return new("The period value in the link is not a number.", "連結入面嘅週期唔係數字。");
        yield return new("The link has no secret.", "連結冇密鑰。");
        yield return new("The issuer must be at most 60 characters without control characters.", "發行者最多 60 個字元，而且唔可以有控制字元。");
        yield return new("The account name must be at most 100 characters without control characters.", "帳戶名稱最多 100 個字元，而且唔可以有控制字元。");
        yield return new("Enter an issuer or an account name.", "請輸入發行者或者帳戶名稱。");
        yield return new("The secret must be Base32 text (letters A to Z and digits 2 to 7).", "密鑰必須係 Base32 文字（字母 A 至 Z 同數字 2 至 7）。");
        yield return new("The secret is too short to be a real authenticator secret.", "密鑰太短，唔似真正嘅驗證器密鑰。");
        yield return new("The secret is too long.", "密鑰太長。");
        yield return new("The algorithm must be SHA1, SHA256 or SHA512.", "演算法必須係 SHA1、SHA256 或 SHA512。");
        yield return new("Codes have 6 to 8 digits.", "驗證碼有 6 至 8 位。");
        yield return new("The period must be between 1 and 300 seconds.", "週期必須喺 1 至 300 秒之間。");
        yield return new("The account id is not valid.", "帳戶編號無效。");
        yield return new("That account is already stored.", "呢個帳戶已經儲存咗。");
        yield return new("At most 200 accounts can be stored.", "最多只可以儲存 200 個帳戶。");
        yield return new("That account no longer exists.", "呢個帳戶已經唔存在。");
        yield return new("Importing would exceed the limit of 200 accounts.", "匯入會超過 200 個帳戶嘅上限。");
        yield return new("Use a passphrase of at least 8 characters.", "請用至少 8 個字元嘅密碼短語。");
        yield return new("Use a passphrase of at most 256 characters.", "請用最多 256 個字元嘅密碼短語。");
        yield return new("The file is larger than 512 KiB, so it is not an authenticator backup.", "個檔案超過 512 KiB，所以唔係驗證器備份。");
        yield return new("The file is not an authenticator backup.", "呢個檔案唔係驗證器備份。");
        yield return new("The file is not a supported authenticator backup.", "呢個檔案係唔支援嘅驗證器備份。");
        yield return new("The backup uses an unsupported key setting.", "備份用咗唔支援嘅金鑰設定。");
        yield return new("The backup is damaged.", "備份已損毀。");
        yield return new("The passphrase is wrong, or the file was changed.", "密碼短語唔啱，或者個檔案被改過。");
        yield return new("The backup holds too many accounts.", "備份入面嘅帳戶太多。");
    }
}
