namespace MaterialDockerImage.Services;

/// <summary>Hong Kong written Cantonese for the password and combined lock policies, unlock durations, the Locks manager and the Support Tickets desk.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> LockCatalogueCantonese()
    {
        // Policies and factors
        yield return new("Password", "密碼");
        yield return new("PIN plus password", "PIN 碼加密碼");
        yield return new("Password plus authenticator app", "密碼加驗證器應用程式");
        yield return new("PIN plus authenticator app", "PIN 碼加驗證器應用程式");
        yield return new("Password plus PIN plus authenticator app", "密碼加 PIN 碼加驗證器應用程式");
        yield return new("Lock policy", "鎖定方式");
        yield return new("Set up the factors in order.", "請按次序設定每一道驗證。");
        yield return new("Unlocking will only ask you to confirm. Pick a PIN, a password or an authenticator app for something harder to click through.", "解鎖時只會要求你確認。想難啲撳過，就揀 PIN 碼、密碼或者驗證器應用程式。");
        yield return new("Password (8 to 128 characters)", "密碼（8 至 128 個字元）");
        yield return new("Repeat the password", "再輸入一次密碼");
        yield return new("The password is stored as a salted PBKDF2 hash, never as typed.", "密碼以加鹽 PBKDF2 雜湊儲存，唔會儲存原文。");
        yield return new("The two passwords do not match.", "兩次輸入嘅密碼唔一樣。");
        yield return new("A password is 8 to 128 characters with no control characters.", "密碼要有 8 至 128 個字元，而且唔可以有控制字元。");
        yield return new("Enter your password.", "請輸入你嘅密碼。");
        yield return new("Step {0} of {1}", "第 {0} 步，共 {1} 步");
        // Unlock durations
        yield return new("How long should unlocking last?", "解鎖想維持幾耐？");
        yield return new("This action only", "只限今次操作");
        yield return new("5 minutes", "5 分鐘");
        yield return new("1 hour", "1 個鐘");
        yield return new("Until the app closes", "直到關閉應用程式");
        yield return new("Until I lock it again", "直到我再鎖住佢");
        yield return new("Unlocked until", "解鎖到");
        yield return new("Unlocked for this action only.", "只為今次操作解鎖。");
        yield return new("Unlocked until the app closes.", "解鎖到應用程式關閉為止。");
        yield return new("Unlocked.", "已解鎖。");
        yield return new("Locked again.", "已重新鎖住。");
        yield return new("A timed unlock ended, so the lock is closed again.", "限時解鎖已完結，鎖已經重新鎖住。");
        // Locks manager
        yield return new("Locks", "鎖");
        yield return new("Locks manager", "鎖管理");
        yield return new("Locks manager…", "鎖管理…");
        yield return new("Search locks", "搜尋鎖");
        yield return new("No locks yet.", "暫時未有任何鎖。");
        yield return new("Right-click any button, switch, option, field or slider and choose Lock this control… to add one.", "喺任何按鈕、開關、選項、輸入欄或滑桿撳右鍵，再揀「鎖住呢個控制項…」就可以新增一個鎖。");
        yield return new("Every lock the app knows about, whether it guards a settings row, a button, a switch, a field, a slider or a drop-down. Each lock has its own policy and its own credentials; unlocking one never unlocks another.", "應用程式知道嘅每一個鎖，不論守住設定項目、按鈕、開關、輸入欄、滑桿定下拉選單。每個鎖都有自己嘅鎖定方式同憑證；解鎖一個，唔會解鎖另一個。");
        yield return new("What it is", "係咩");
        yield return new("Policy", "鎖定方式");
        yield return new("Unlock lasts", "解鎖維持");
        yield return new("Locked only", "只睇已鎖住");
        yield return new("Has a credential", "有憑證");
        yield return new("Lock again", "再鎖住");
        yield return new("Remove lock", "移除鎖");
        yield return new("Remove selected locks", "移除已選嘅鎖");
        yield return new("Its PIN, password or authenticator secret and its recovery codes are deleted. The control stays unlocked.", "佢嘅 PIN 碼、密碼或驗證器密鑰同還原碼會被刪除，控制項維持未鎖住。");
        yield return new("Lock removed.", "鎖已移除。");
        yield return new("Locks removed.", "鎖已移除。");
        yield return new("Skipped because they are locked:", "因為仲鎖住，所以略過：");
        yield return new("Unlock this lock before removing it.", "移除之前，請先解鎖呢個鎖。");
        yield return new("Setting: {0}", "設定：{0}");
        yield return new("No lock has a PIN, password or authenticator yet. Lock a control from its right-click menu to choose one.", "暫時冇鎖設定 PIN 碼、密碼或驗證器。喺控制項嘅右鍵選單鎖住佢，就可以揀選。");
        // Support Tickets
        yield return new("Support Tickets", "支援服務單");
        yield return new("Support Tickets…", "支援服務單…");
        yield return new("Forgotten your password?", "唔記得密碼？");
        yield return new("Search tickets", "搜尋服務單");
        yield return new("No tickets yet.", "暫時未有服務單。");
        yield return new("File a ticket above. It stays on this computer.", "請喺上面提交服務單。佢只會留喺呢部電腦。");
        yield return new("File a ticket", "提交服務單");
        yield return new("Ticket", "服務單");
        yield return new("Category", "類別");
        yield return new("Describe what happened", "請描述發生咗咩事");
        yield return new("The data folder", "資料夾");
        yield return new("Open the data folder", "開啟資料夾");
        yield return new("The desk opens the folder and stands back. Deleting it is your own act.", "服務台只會開啟資料夾，然後退後。刪除係你自己動手。");
        yield return new("Advance status", "推進狀態");
        yield return new("Resolve", "處理完成");
        yield return new("Remove ticket", "移除服務單");
        yield return new("Ticket filed on this computer.", "服務單已經提交，只存放喺呢部電腦。");
        yield return new("Ticket removed.", "服務單已移除。");
        yield return new("Tickets removed.", "服務單已移除。");
        yield return new("Waiting for a reply that is not coming. Advance the status to see what the desk says.", "喺度等一個唔會嚟嘅回覆。推進狀態，睇下服務台點講。");
        yield return new("The only way back into a locked control is to delete the app's data folder yourself.", "重新進入已鎖控制項嘅唯一方法，就係自己刪除應用程式嘅資料夾。");
        yield return new("Welcome to the Department of Forgotten Things.", "歡迎嚟到遺忘事務部。");
        yield return new("Say what happened first. The desk cannot help with a blank page.", "請先講發生咗咩事。服務台對住白紙幫唔到手。");
        yield return new("The ticket list is full. Remove old tickets first.", "服務單清單滿咗，請先移除舊嘅服務單。");
        yield return new("Nothing is sent anywhere. A ticket exists only on this computer, no network request is made, no data is collected, and nobody is reading it.", "完全冇嘢會傳去任何地方。服務單只存在於呢部電腦，唔會發出任何網絡請求，亦唔會收集資料，冇人會睇。");
        yield return new("Severity is written down and then treated exactly like every other severity.", "嚴重程度會被記低，然後同其他嚴重程度一模一樣咁處理。");
        // Ticket categories and statuses
        yield return new("Forgot my PIN", "唔記得 PIN 碼");
        yield return new("Forgot my password", "唔記得密碼");
        yield return new("Lost my authenticator", "遺失咗驗證器");
        yield return new("Lost my recovery codes", "遺失咗還原碼");
        yield return new("Something else", "其他問題");
        yield return new("New", "新");
        yield return new("Acknowledged", "已收到");
        yield return new("Investigating", "調查中");
        yield return new("Resolved", "已解決");
        // The desk's replies
        yield return new("Your ticket has been logged on this computer. Please do not wait for a reply: no person will write one.", "你嘅服務單已經記錄喺呢部電腦。請唔好等回覆：冇人會寫。");
        yield return new("The desk has read the manual once. The manual says the only way back in is to delete the app's data folder yourself.", "服務台睇過一次手冊。手冊話，重新進入嘅唯一方法就係自己刪除應用程式嘅資料夾。");
        yield return new("Resolution: open the data folder below and delete it in your file manager. The app never deletes it for you.", "處理方法：開啟下面嘅資料夾，然後喺檔案總管自己刪除。應用程式永遠唔會代你刪除。");
        yield return new("Thank you for contacting the Department of Forgotten Things.", "多謝你聯絡遺忘事務部。");
        yield return new("Our specialists have convened (it was one specialist, and it was the app).", "我哋嘅專家已經開會（其實得一位專家，就係個應用程式）。");
        yield return new("Case closed with great ceremony.", "個案以隆重儀式結案。");
        // Words used by the check list and by smaller labels
        yield return new("Lock labels", "鎖標籤");
        yield return new("Never", "從不");
    }
}
