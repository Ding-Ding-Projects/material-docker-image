namespace MaterialDockerImage.Services;

/// <summary>Hong Kong written Cantonese for setting locks, schedules, attention modes, sound cues and collapsible filters.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> SettingsSafetyCantonese()
    {
        // Element locks
        yield return new("Lock this setting: ", "鎖住呢項設定：");
        yield return new("Unlock this setting: ", "解鎖呢項設定：");
        yield return new("Locked. Select to unlock.", "已鎖住。撳一下解鎖。");
        yield return new("Lock this setting against changes", "鎖住呢項設定，防止更改");
        yield return new("Unlock this setting?", "解鎖呢項設定？");
        yield return new("Unlock", "解鎖");
        yield return new("Locked. Unlock this setting to change it. A lock is a speed bump you set for yourself, not security. It does not protect anything from another person who has this computer.", "已鎖住。解鎖後先可以更改。鎖只係你自己設嘅減速帶，並非保安措施，唔會保護電腦免受其他使用者影響。");
        yield return new("A lock is a speed bump you set for yourself, not security. It does not protect anything from another person who has this computer.", "鎖只係你自己設嘅減速帶，並非保安措施，唔會保護電腦免受其他使用者影響。");
        yield return new("Locked in Settings. Unlock it there to change it.", "已喺設定中鎖住。請去嗰度解鎖先可以更改。");
        yield return new("This setting could not be locked because the lock list is full.", "鎖定清單已滿，所以鎖唔到呢項設定。");
        yield return new("All unlocked settings already have their built-in defaults. Locked settings were left alone.", "所有未鎖住嘅設定已經係內建預設值，鎖住嘅設定冇改動。");
        // Schedules
        yield return new("Schedules", "排程");
        yield return new("Scheduled language and theme", "排程語言同主題");
        yield return new("Switch the language or the theme automatically for set times of day, every day or on chosen weekdays.", "喺指定時段自動轉換語言或主題，可以每日或只係揀咗嘅星期幾。");
        yield return new("A schedule only lends its value while its window is open. Your own saved language and theme are never overwritten, and they come back when the window ends. Every rule add, change and delete is recorded in local history.", "排程只會喺時段內暫借數值，你自己儲存嘅語言同主題唔會被覆寫，時段完咗就會還原。每次新增、更改同刪除規則都會記入本機歷史。");
        yield return new("Schedule source file", "排程來源檔案");
        yield return new("A local JSON file that supplies the value for rules whose source is File.", "本機 JSON 檔案，為來源設為「檔案」嘅規則提供數值。");
        yield return new("No schedules yet. Add a rule to switch the language or theme at set times.", "未有排程。新增規則，就可以喺指定時間轉換語言或主題。");
        yield return new("No schedule is applying right now. Your own language and theme are in effect.", "而家冇排程生效，用緊你自己嘅語言同主題。");
        yield return new("Applying now: ", "目前生效：");
        yield return new("(your saved choices are unchanged).", "（你儲存嘅選擇冇改變）。");
        yield return new("Rule enabled", "規則已啟用");
        yield return new("Edit rule", "編輯規則");
        yield return new("Delete rule", "刪除規則");
        yield return new("New rule", "新增規則");
        yield return new("Add rule", "新增規則");
        yield return new("Save rule", "儲存規則");
        yield return new("Rule label", "規則標籤");
        yield return new("Setting", "設定");
        yield return new("Source", "來源");
        yield return new("Value", "數值");
        yield return new("Local", "本機");
        yield return new("File", "檔案");
        yield return new("Every day", "每日");
        yield return new("From", "由");
        yield return new("until", "至");
        yield return new("Start hour", "開始時");
        yield return new("Start minute", "開始分");
        yield return new("End hour", "結束時");
        yield return new("End minute", "結束分");
        yield return new("Scheduled value", "排程數值");
        yield return new("Rule priority", "規則優先次序");
        yield return new("Priority", "優先次序");
        yield return new("priority", "優先次序");
        yield return new("Times use this computer's time zone", "時間以呢部電腦嘅時區為準");
        yield return new("They follow daylight saving changes. The end time is not included. Equal times mean the whole day. A window that ends earlier than it starts continues past midnight and belongs to the day it starts.", "會跟隨夏令時間轉變。結束時間唔包括在內。開始同結束時間相同即係成日。結束時間早過開始時間嘅時段會過咗午夜，並屬於開始嗰日。");
        yield return new("Rules follow daylight saving changes. A later rule wins over an earlier one unless an earlier rule has a higher priority.", "規則會跟隨夏令時間轉變。除非較前嘅規則優先次序較高，否則後面嘅規則優先。");
        yield return new("Up to 50 rules are allowed.", "最多可以有 50 條規則。");
        yield return new("The setting must be Language or Theme.", "設定必須係語言或主題。");
        yield return new("The source must be Local or File.", "來源必須係本機或檔案。");
        yield return new("The value is not allowed for this setting.", "呢項設定唔接受呢個數值。");
        yield return new("The start time must look like 08:30.", "開始時間格式要似 08:30。");
        yield return new("The end time must look like 21:00.", "結束時間格式要似 21:00。");
        yield return new("Choose at least one weekday.", "請至少揀一個星期幾。");
        yield return new("The label is longer than 60 characters.", "標籤超過 60 個字元。");
        yield return new("No file chosen. Rules whose source is File stay paused.", "未揀檔案。來源為「檔案」嘅規則會暫停。");
        yield return new("Not used: ", "未使用：");
        yield return new("In use. The file is watched, so edits apply within a moment.", "使用中。檔案有被監察，改動會好快生效。");
        yield return new("Choose a schedule source file", "揀選排程來源檔案");
        yield return new("The schedule source file does not exist.", "排程來源檔案唔存在。");
        yield return new("The schedule file is empty.", "排程檔案係空嘅。");
        yield return new("The schedule file is larger than 4 KiB.", "排程檔案大過 4 KiB。");
        yield return new("Only version 1 is supported.", "只支援第 1 版。");
        yield return new("A schedule is now applying {0}. Your saved choices are unchanged.", "排程而家套用緊{0}。你儲存嘅選擇冇改變。");
        yield return new("The schedule ended, so your own language and theme are back.", "排程完咗，已還原你自己嘅語言同主題。");
        yield return new("Open schedules", "開啟排程");
        yield return new("Scheduled language and theme", "排程語言同主題");
        // Attention modes
        yield return new("Exit focus", "離開專注模式");
        yield return new("Focus mode", "專注模式");
        yield return new("Momentum streak", "動力連勝");
        yield return new("Time awareness clock", "時間意識時鐘");
        yield return new("1 step done", "完成 1 步");
        yield return new("{0} steps in a row", "連續完成 {0} 步");
        yield return new("Good start.", "開局唔錯。");
        yield return new("You are on a roll.", "你好順手。");
        yield return new("Steady and strong.", "穩陣又強勁。");
        yield return new("Nothing is removed that you cannot bring back in one step. The command palette (Ctrl+Shift+F) also has Focus.", "冇嘢係一步搵唔返嘅。指令面板（Ctrl+Shift+F）都有專注模式。");
        yield return new("The streak lives only in this session and is never saved, so it cannot nag you across days. Low stimulation hides the chip.", "連勝只喺今次使用期間存在，唔會儲存，所以唔會跨日煩住你。低刺激模式會隱藏呢個標籤。");
        // Sounds
        yield return new("What each sound means", "每個聲音嘅意思");
        yield return new("Low stimulation turns sounds off even when this is on. Sounds never block an action and are never the only sign of a result.", "即使呢項開咗，低刺激模式都會關閉聲音。聲音唔會阻住操作，亦唔會係結果嘅唯一提示。");
        // Collapsible filters
        yield return new("Show filters", "顯示篩選");
        yield return new("Hide filters", "隱藏篩選");
        yield return new("Show ", "顯示");
        yield return new("Hide ", "隱藏");
        yield return new("Statistics", "統計");
        yield return new("Action filters", "操作篩選");
        yield return new("Collapsed. Select to expand.", "已收合。撳一下展開。");
        yield return new("Expanded. Select to collapse.", "已展開。撳一下收合。");
        yield return new("Filters · {0} on", "篩選 · {0} 項已開");
        yield return new("Filters · none on", "篩選 · 冇開");
        yield return new("Filters · {0} filters", "篩選 · {0} 個篩選");
        yield return new("Filters · 1 filter", "篩選 · 1 個篩選");
    }
}
