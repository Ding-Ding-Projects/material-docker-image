namespace MaterialDockerImage.Services;

/// <summary>Hong Kong written Cantonese for the extended schedules (more settings, date ranges, https and Home Assistant sources) and for sound volume and quiet hours.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> SchedulesExtendedCantonese()
    {
        // Rules and editor
        yield return new("Scheduled settings", "排程設定");
        yield return new("Change the language, theme, density, accent colour, font size, motion or display name automatically for set times of day, on chosen weekdays and within an optional date range.", "喺指定時段、指定星期同可選日期範圍內，自動轉換語言、主題、密度、強調色、字體大小、動態或顯示名稱。");
        yield return new("A schedule only lends its value while its window is open. Your own saved settings are never overwritten, and they come back when the window ends. Each setting is decided on its own: the highest priority rule wins, and the later rule wins a tie. Every rule add, change and delete is recorded in local history.", "排程只會喺時段內暫借數值，你自己儲存嘅設定唔會被覆寫，時段完咗就會還原。每項設定獨立決定：優先次序最高嘅規則勝出，同級就由較後嘅規則勝出。每次新增、更改同刪除規則都會記入本機歷史。");
        yield return new("No schedules yet. Add a rule to change the language, theme, density, accent colour, font size, motion or display name at set times.", "未有排程。新增規則，就可以喺指定時間轉換語言、主題、密度、強調色、字體大小、動態或顯示名稱。");
        yield return new("No schedule is applying right now. Your own settings are in effect.", "而家冇排程生效，用緊你自己嘅設定。");
        yield return new("The schedule ended, so your own settings are back.", "排程完咗，已還原你自己嘅設定。");
        yield return new("Accent colour", "強調色");
        yield return new("Font size", "字體大小");
        yield return new("Density", "密度");
        yield return new("Motion", "動態");
        yield return new("Also apply a value while the entity is off", "實體關閉時亦套用一個數值");
        yield return new("Scheduled value while off", "關閉時嘅排程數值");
        yield return new("Local uses the value below. File and HTTPS read the setting from that source. Home Assistant applies the value while its entity is on.", "「本機」用下面嘅數值。「檔案」同 HTTPS 由該來源讀取設定。Home Assistant 會喺實體開啟時套用數值。");
        yield return new("HTTPS", "HTTPS");
        yield return new("Home Assistant", "Home Assistant");
        yield return new("First date", "開始日期");
        yield return new("Last date", "結束日期");
        yield return new("First date (optional)", "開始日期（可選）");
        yield return new("Last date (optional)", "結束日期（可選）");
        yield return new("Dates look like 2026-12-24. Both days are included. Leave both empty for every date. A window that crosses midnight belongs to the date it starts.", "日期格式似 2026-12-24，頭尾兩日都計入。兩格都留空就係每一日。跨午夜嘅時段屬於開始嗰一日。");
        // Validation reasons
        yield return new("The setting is not one that can be scheduled.", "呢項設定唔可以排程。");
        yield return new("The source must be Local, File, Https or HomeAssistant.", "來源必須係本機、檔案、HTTPS 或 Home Assistant。");
        yield return new("The value for off is not allowed for this setting.", "呢項設定唔接受關閉時嘅數值。");
        yield return new("The first date must look like 2026-12-24.", "開始日期格式要似 2026-12-24。");
        yield return new("The last date must look like 2026-12-31.", "結束日期格式要似 2026-12-31。");
        yield return new("The last date is before the first date.", "結束日期早過開始日期。");
        yield return new("The schedule file must hold one JSON object.", "排程檔案必須係一個 JSON 物件。");
        yield return new("The response must hold one JSON object.", "回應必須係一個 JSON 物件。");
        yield return new("The response is empty.", "回應係空嘅。");
        yield return new("The response is larger than 64 KiB.", "回應大過 64 KiB。");
        yield return new("version must be a number.", "version 必須係數字。");
        yield return new("language is not one of English, Cantonese or Bilingual.", "language 唔係 English、Cantonese 或 Bilingual 其中一個。");
        yield return new("theme is not one of Dark, Light or System.", "theme 唔係 Dark、Light 或 System 其中一個。");
        yield return new("density is not one of Compact, Comfortable or Spacious.", "density 唔係 Compact、Comfortable 或 Spacious 其中一個。");
        yield return new("accent must look like #3A6FD8.", "accent 格式要似 #3A6FD8。");
        yield return new("fontSize must be a whole number from 12 to 22.", "fontSize 必須係 12 至 22 嘅整數。");
        yield return new("motion is not one of Off, Reduced, Standard or Expressive.", "motion 唔係 Off、Reduced、Standard 或 Expressive 其中一個。");
        yield return new("displayName must be 1 to 80 characters without control characters.", "displayName 要有 1 至 80 個字元，唔可以有控制字元。");
        // HTTPS source
        yield return new("HTTPS schedule source", "HTTPS 排程來源");
        yield return new("An https address that returns a small JSON document. Rules whose source is HTTPS read their value from it.", "一個回傳細小 JSON 文件嘅 https 位址。來源為 HTTPS 嘅規則會由度讀取數值。");
        yield return new("Same format as the schedule file (up to 64 KiB). The app sends only a GET request, waits 10 seconds at most, never sends cookies or credentials, never follows a redirect to plain http, and checks no more often than every 5 minutes. Addresses with a user name, password or a secret-looking query parameter are refused so nothing secret is saved in plain text. If the server cannot be reached or answers badly, the last good answer stays in use, nothing changes by itself and the state line says so. Nothing is requested unless an enabled rule uses this source.", "格式同排程檔案一樣（最多 64 KiB）。應用程式只會發出 GET 請求，最多等 10 秒，唔會傳送 cookie 或憑證，唔會跟隨轉去純 http 嘅重新導向，而且最密都係每 5 分鐘先查一次。含用戶名稱、密碼或似密碼參數嘅位址會被拒絕，確保冇機密以明文儲存。伺服器連唔到或者回應有問題時，會繼續用最後一次正常嘅回應，設定唔會自己變，狀態行會如實講明。冇啟用嘅規則用呢個來源時，完全唔會發出請求。");
        yield return new("HTTPS address", "HTTPS 位址");
        yield return new("Check every", "檢查間隔");
        yield return new("Check now", "即刻檢查");
        yield return new("No address set. Rules whose source is HTTPS stay paused.", "未設定位址。來源為 HTTPS 嘅規則會暫停。");
        yield return new("The address is saved but no enabled rule uses the HTTPS source, so nothing is requested.", "位址已儲存，但冇啟用嘅規則用 HTTPS 來源，所以唔會發出請求。");
        yield return new("Enter an https address.", "請輸入 https 位址。");
        yield return new("The address is longer than 500 characters.", "位址超過 500 個字元。");
        yield return new("The address cannot contain spaces or control characters.", "位址唔可以有空格或控制字元。");
        yield return new("Only https addresses are accepted.", "只接受 https 位址。");
        yield return new("The address has no host.", "位址冇主機名稱。");
        yield return new("Remove the user name and password from the address. Credentials are never stored in plain text.", "請移除位址入面嘅用戶名稱同密碼。憑證絕不以明文儲存。");
        yield return new("Remove the user name and password from the address.", "請移除位址入面嘅用戶名稱同密碼。");
        yield return new("Remove the part after # from the address.", "請移除位址入面 # 之後嘅部分。");
        yield return new("The address has a query parameter that looks like a secret. Secrets are never stored in plain text.", "位址有個似機密嘅查詢參數。機密絕不以明文儲存。");
        // Home Assistant source
        yield return new("Home Assistant schedule source", "Home Assistant 排程來源");
        yield return new("Rules whose source is Home Assistant apply their value while a chosen entity (for example an input_boolean) is on, and optionally another value while it is off.", "來源為 Home Assistant 嘅規則，會喺所揀實體（例如 input_boolean）開啟時套用數值，亦可以喺關閉時套用另一個數值。");
        yield return new("The address is the base address of Home Assistant, such as http://homeassistant.local:8123. Plain http is accepted only on your own network; anything else must use https. The token is encrypted with Windows data protection for your account in its own file, is never saved in settings.json, never exported, logged or shown again. The app sends only a GET request for the one entity, waits 10 seconds at most and reads 64 KiB at most. Only the states on and off decide anything. If Home Assistant cannot be reached, the last good state stays in use and nothing changes by itself.", "位址係 Home Assistant 嘅基本位址，例如 http://homeassistant.local:8123。純 http 只限你自己嘅網絡；其他一律要用 https。令牌會用 Windows 資料保護為你嘅帳戶加密，存放喺獨立檔案，絕不存入 settings.json，亦唔會匯出、記錄或再顯示。應用程式只會為呢個實體發出 GET 請求，最多等 10 秒，最多讀 64 KiB。只有 on 同 off 兩個狀態會有作用。連唔到 Home Assistant 時，會繼續用最後一次正常嘅狀態，設定唔會自己變。");
        yield return new("Home Assistant address", "Home Assistant 位址");
        yield return new("Home Assistant entity", "Home Assistant 實體");
        yield return new("Home Assistant long-lived access token", "Home Assistant 長期存取令牌");
        yield return new("Address", "位址");
        yield return new("Entity", "實體");
        yield return new("Token", "令牌");
        yield return new("Store token", "儲存令牌");
        yield return new("Remove token", "移除令牌");
        yield return new("A token is stored, encrypted for your Windows account. It cannot be shown again; enter a new one to replace it.", "已儲存令牌，並用你嘅 Windows 帳戶加密。佢唔會再顯示；輸入新令牌即可取代。");
        yield return new("No token is stored. Create a long-lived access token in your Home Assistant profile and enter it here.", "未儲存令牌。請喺 Home Assistant 個人檔案建立長期存取令牌，然後輸入喺度。");
        yield return new("Set the address and the entity. Rules whose source is Home Assistant stay paused.", "請設定位址同實體。來源為 Home Assistant 嘅規則會暫停。");
        yield return new("Saved, but no enabled rule uses Home Assistant, so nothing is requested.", "已儲存，但冇啟用嘅規則用 Home Assistant，所以唔會發出請求。");
        yield return new("The entity is on.", "實體開啟中。");
        yield return new("The entity is off.", "實體關閉中。");
        yield return new("The entity is neither on nor off, so nothing is applied.", "實體既唔係開亦唔係關，所以唔會套用任何數值。");
        yield return new("The entity looks like input_boolean.night_mode: lower case letters, digits and underscores, a dot between.", "實體格式似 input_boolean.night_mode：細楷字母、數字同底線，中間一個點。");
        yield return new("That does not look like a Home Assistant token. It must be printable ASCII without spaces, up to 512 characters.", "呢個唔似 Home Assistant 令牌。必須係無空格嘅可列印 ASCII，最多 512 個字元。");
        yield return new("The token could not be stored securely on this computer.", "呢部電腦無法安全儲存令牌。");
        yield return new("Enter the Home Assistant address.", "請輸入 Home Assistant 位址。");
        yield return new("Use an http or https address.", "請用 http 或 https 位址。");
        yield return new("Use only the base address, for example http://homeassistant.local:8123.", "只可以用基本位址，例如 http://homeassistant.local:8123。");
        yield return new("Use https for a host outside your local network so the token is not sent in clear text.", "本地網絡以外嘅主機請用 https，避免令牌以明文傳送。");
        // Poll results and states
        yield return new("Not checked yet.", "仲未檢查。");
        yield return new("Online. Last checked {0}.", "連線正常。上次檢查喺{0}。");
        yield return new("Offline: {0} Nothing from this source is applied.", "離線：{0} 呢個來源嘅數值一概唔套用。");
        yield return new("Offline: {0} The last good answer from {1} stays in use and nothing was changed.", "離線：{0} 繼續用{1}最後一次正常嘅回應，冇改動任何設定。");
        yield return new("never", "從未");
        yield return new("less than a minute ago", "不足一分鐘前");
        yield return new("{0} minutes ago", "{0} 分鐘前");
        yield return new("{0} hours ago", "{0} 小時前");
        yield return new("{0} days ago", "{0} 日前");
        yield return new("The server could not be reached.", "連唔到伺服器。");
        yield return new("The answer could not be read.", "讀唔到回應。");
        yield return new("The request timed out after 10 seconds.", "請求等咗 10 秒仲未有回應，已逾時。");
        yield return new("The answer is larger than 64 KiB.", "回應大過 64 KiB。");
        yield return new("The source could not be reached.", "連唔到來源。");
        yield return new("The stored Home Assistant token could not be read. Enter it again.", "讀唔到已儲存嘅 Home Assistant 令牌，請重新輸入。");
        yield return new("No Home Assistant token is stored. Enter one to use this source.", "未儲存 Home Assistant 令牌。輸入一個先可以用呢個來源。");
        yield return new("The Home Assistant address or entity is not valid.", "Home Assistant 位址或實體無效。");
        yield return new("Home Assistant sent an empty answer.", "Home Assistant 回應係空嘅。");
        yield return new("The Home Assistant answer is larger than 64 KiB.", "Home Assistant 回應大過 64 KiB。");
        yield return new("The Home Assistant answer is not one JSON object.", "Home Assistant 回應唔係一個 JSON 物件。");
        yield return new("The Home Assistant answer is for a different entity.", "Home Assistant 回應係另一個實體嘅。");
        yield return new("The Home Assistant answer has no state.", "Home Assistant 回應冇狀態。");
        yield return new("The Home Assistant answer is not valid JSON.", "Home Assistant 回應唔係有效 JSON。");
        yield return new("The server refused the request (HTTP 401).", "伺服器拒絕咗請求（HTTP 401）。");
        yield return new("The server refused the request (HTTP 403).", "伺服器拒絕咗請求（HTTP 403）。");
        yield return new("The server answered HTTP 404.", "伺服器回應 HTTP 404。");
        yield return new("The server answered HTTP 500.", "伺服器回應 HTTP 500。");
        yield return new("The server answered HTTP 502.", "伺服器回應 HTTP 502。");
        yield return new("The server answered HTTP 503.", "伺服器回應 HTTP 503。");
        // Sound volume and quiet hours
        yield return new("Action sound volume", "操作音效音量");
        yield return new("How loud action sounds are, from 0 (silent) to 100 percent of the Windows sound's own level. It applies to the next sound at once.", "操作音效嘅大細聲，由 0（靜音）至 Windows 音效本身音量嘅 100%，下一個音效即時生效。");
        yield return new("Below 100 percent the sound's own WAV file is scaled in memory and played through the app; nothing is written to disk and the Windows volume is never changed. If the file cannot be read or is not plain PCM audio, the cue stays silent instead of playing at full volume. Sounds never block an action and are never the only sign of a result.", "低於 100% 時，會喺記憶體入面縮放音效自己嘅 WAV 檔，再由應用程式播放；唔會寫入磁碟，亦唔會改 Windows 音量。讀唔到檔案或者唔係一般 PCM 音訊時，音效會保持靜音，而唔係全音量播放。音效唔會阻擋操作，亦絕不係結果嘅唯一提示。");
        yield return new("Play a test sound", "播放測試音效");
        yield return new("No sound will play right now: sounds are off, Low stimulation is on, the volume is 0 or it is inside the quiet hours.", "而家唔會有聲：音效已關、低刺激模式已開、音量係 0，或者喺靜音時段內。");
        yield return new("Quiet hours", "靜音時段");
        yield return new("Silences every action sound between two times of day, even when sounds are on. The window may cross midnight, for example 22:00 to 07:00.", "喺一日內兩個時間之間，即使音效已開都會全部靜音。時段可以跨午夜，例如 22:00 至 07:00。");
        yield return new("Silence sounds during quiet hours", "靜音時段內唔播放音效");
        yield return new("Times use this computer's clock and time zone and follow daylight saving changes. The end time is not included. A start later than the end crosses midnight; equal times mean no quiet window. Quiet hours only silence sounds: notifications, narration and visible results are unchanged.", "時間以呢部電腦嘅時鐘同時區為準，並跟隨日光節約時間轉變。結束時間唔計入。開始時間遲過結束時間即跨午夜；兩個時間相同代表冇靜音時段。靜音時段只會靜音音效：通知、朗讀同畫面上嘅結果都冇改變。");
        yield return new("Quiet hours start hour", "靜音開始時");
        yield return new("Quiet hours start minute", "靜音開始分");
        yield return new("Quiet hours end hour", "靜音結束時");
        yield return new("Quiet hours end minute", "靜音結束分");
        yield return new("The start and end are the same, so there is no quiet window.", "開始同結束時間相同，所以冇靜音時段。");
        yield return new("It is quiet hours right now, so no sound plays.", "而家係靜音時段，所以唔會播放音效。");
    }
}
