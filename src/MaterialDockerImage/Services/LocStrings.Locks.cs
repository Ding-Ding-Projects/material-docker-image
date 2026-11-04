namespace MaterialDockerImage.Services;

/// <summary>Hong Kong written Cantonese for lock policies, the unlock ladder, recovery codes and the Authenticator tool.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> LocksCantonese()
    {
        // Menu and shared
        yield return new("Lock this control…", "鎖住呢個控制項…");
        yield return new("Unlock this control…", "解鎖呢個控制項…");
        yield return new("Lock this control", "鎖住呢個控制項");
        yield return new("Unlock this control", "解鎖呢個控制項");
        yield return new("Change lock policy…", "更改鎖定方式…");
        yield return new("Remove lock policy…", "移除鎖定方式…");
        yield return new("Remove this lock policy?", "移除呢個鎖定方式？");
        yield return new("Its PIN or authenticator secret and its recovery codes are deleted. The control stays unlocked.", "佢嘅 PIN 或驗證器密鑰同還原碼會被刪除，控制項維持未鎖住。");
        yield return new("Lock policy removed.", "鎖定方式已移除。");
        yield return new("Remove", "移除");
        yield return new("Unlock this control before changing its lock policy.", "更改鎖定方式之前，請先解鎖呢個控制項。");
        yield return new("Unlock this control before removing its lock policy.", "移除鎖定方式之前，請先解鎖呢個控制項。");
        yield return new("Locked. Right-click and choose Unlock this control.", "已鎖住。撳右鍵再揀「解鎖呢個控制項」。");
        // Policy wizard
        yield return new("Choose what unlocking should ask for.", "揀選解鎖時要問啲咩。");
        yield return new("None (confirm only)", "無（只需確認）");
        yield return new("PIN", "PIN 碼");
        yield return new("Authenticator app", "驗證器應用程式");
        yield return new("Unlocking will only ask you to confirm. Pick a PIN or an authenticator app for something harder to click through.", "解鎖時只會要求你確認。想難啲撳過，就揀 PIN 碼或驗證器應用程式。");
        yield return new("PIN (4 to 12 digits)", "PIN 碼（4 至 12 位數字）");
        yield return new("Repeat the PIN", "再輸入一次 PIN 碼");
        yield return new("The PIN is stored as a salted PBKDF2 hash, never as typed.", "PIN 碼以加鹽 PBKDF2 雜湊儲存，唔會儲存原文。");
        yield return new("A PIN is 4 to 12 digits, numbers only.", "PIN 碼係 4 至 12 位數字，只可以用數字。");
        yield return new("The two PINs do not match.", "兩次輸入嘅 PIN 碼唔一樣。");
        yield return new("Authenticator locks need Windows secret protection, which is not available here.", "驗證器鎖需要 Windows 密鑰保護，但呢部電腦用唔到。");
        yield return new("That code did not match. Check the app and try the current code.", "驗證碼唔脗合。請睇返應用程式，輸入而家顯示嘅驗證碼。");
        yield return new("The lock could not be saved: ", "鎖定方式儲存唔到：");
        yield return new("Lock", "鎖住");
        yield return new("Show secret and QR code", "顯示密鑰同 QR 碼");
        yield return new("Type this instead if you cannot scan. SHA-1, 6 digits, 30 seconds.", "掃描唔到就改為輸入呢串密鑰。SHA-1，6 位數字，30 秒。");
        yield return new("Authenticator secret", "驗證器密鑰");
        yield return new("Copy secret", "複製密鑰");
        yield return new("Copy otpauth link", "複製 otpauth 連結");
        yield return new("This secret is shown only now. It is stored protected for your Windows account and never written to a log, export or history.", "密鑰只會喺而家顯示一次。佢以你嘅 Windows 帳戶保護儲存，唔會寫入記錄、匯出檔或歷史。");
        yield return new("Code from your authenticator", "驗證器顯示嘅驗證碼");
        yield return new("Scan the QR code with an authenticator app, then type the 6-digit code it shows to arm the lock.", "用驗證器應用程式掃描 QR 碼，再輸入佢顯示嘅 6 位數字驗證碼，鎖先會生效。");
        yield return new("QR code that pairs an authenticator app with the lock for {0}. Scan it, or type the text secret beside it.", "用嚟配對驗證器應用程式同 {0} 嘅鎖嘅 QR 碼。請掃描，或者輸入旁邊嘅文字密鑰。");
        yield return new("Copied to the clipboard.", "已複製到剪貼簿。");
        yield return new("The clipboard is busy. Try again.", "剪貼簿暫時忙緊，請再試。");
        yield return new("Copy folder path", "複製資料夾路徑");
        yield return new("Open folder", "開啟資料夾");
        yield return new("Locked out completely? Close the app and delete the lock file locks.json in the data folder (or the whole folder) to clear every lock:", "完全鎖死？請關閉應用程式，刪除資料夾入面嘅 locks.json（或者成個資料夾），就會清走所有鎖：");
        // Recovery codes
        yield return new("Recovery codes", "還原碼");
        yield return new("Save recovery codes", "儲存還原碼");
        yield return new("Recovery codes saved.", "還原碼已儲存。");
        yield return new("Each code clears this lock once if you lose the PIN or authenticator. They are shown only now and stored only as hashes, so nobody can read them back.", "如果你唔記得 PIN 碼或者遺失驗證器，每個還原碼可以解除呢個鎖一次。還原碼只會喺而家顯示，而且只以雜湊儲存，冇人讀返出嚟。");
        yield return new("I have saved these recovery codes", "我已經儲好呢啲還原碼");
        yield return new("Save to file…", "儲存為檔案…");
        yield return new("Copy all", "全部複製");
        yield return new("Done", "完成");
        // Ladder
        yield return new("Confirm that you want to unlock this control.", "請確認你想解鎖呢個控制項。");
        yield return new("Enter your PIN.", "請輸入你嘅 PIN 碼。");
        yield return new("Enter the 6-digit code from your authenticator app.", "請輸入驗證器應用程式顯示嘅 6 位數字驗證碼。");
        yield return new("Too many wrong answers. Wait it out, or use a recovery code.", "答錯次數太多。請等候時間完結，或者使用還原碼。");
        yield return new("Enter one of your recovery codes. Each works once.", "請輸入其中一個還原碼。每個只可以用一次。");
        yield return new("Unlock", "解鎖");
        yield return new("Recovery code", "還原碼");
        yield return new("That did not match.", "唔脗合。");
        yield return new("1 more try before a wait starts.", "仲有 1 次機會，之後就要等候。");
        yield return new("{0} more tries before a wait starts.", "仲有 {0} 次機會，之後就要等候。");
        yield return new("The stored authenticator secret could not be read on this account. Use a recovery code.", "喺呢個帳戶讀取唔到已儲存嘅驗證器密鑰，請使用還原碼。");
        yield return new("That recovery code did not match, or it was already used.", "還原碼唔脗合，或者已經用過。");
        yield return new("Recovery codes left:", "剩餘還原碼：");
        yield return new("Use a recovery code", "使用還原碼");
        yield return new("You can try again in", "你可以喺");
        yield return new("at", "之後，時間係");
        yield return new("Each wrong answer after that doubles the wait, up to one hour.", "之後每答錯一次，等候時間加倍，最長一個鐘。");
        yield return new("Waiting does not unlock anything: when the time is up you still need your PIN or code. A recovery code works at any time.", "等候本身唔會解鎖：時間到咗你仲係需要 PIN 碼或驗證碼。還原碼隨時都用得。");
        // Authenticator tool
        yield return new("Authenticator", "驗證器");
        yield return new("Authenticator for locks", "鎖用驗證器");
        yield return new("Authenticator for this app's locks", "本應用程式鎖用嘅驗證器");
        yield return new("Pair an authenticator app with one of this app's locks. The secret is made here, shown once as a QR code and text, and stored protected for your Windows account. Nothing is sent anywhere.", "將驗證器應用程式配對到本應用程式嘅其中一個鎖。密鑰喺度生成，以 QR 碼同文字顯示一次，並以你嘅 Windows 帳戶保護儲存，唔會傳去任何地方。");
        yield return new("This only unlocks controls inside this app. A lock is a speed bump you set for yourself, and an authenticator for a lock on this same computer is mostly for fun.", "呢個只可以解鎖本應用程式內嘅控制項。鎖係你自己設嘅減速帶，喺同一部電腦用驗證器鎖，主要係鬧住玩。");
        yield return new("Lock to pair", "要配對嘅鎖");
        yield return new("Pick a settings row or type the id of any lock, for example language or btn.build.build-image. The lock is not turned on by this; lock the control afterwards and it will ask for the code.", "揀一個設定項目，或者輸入任何鎖嘅識別碼，例如 language 或 btn.build.build-image。呢個步驟唔會鎖住控制項；之後再鎖住佢，就會要求驗證碼。");
        yield return new("Set up authenticator for this lock", "為呢個鎖設定驗證器");
        yield return new("Check a code", "檢查驗證碼");
        yield return new("Checks a code for the lock above without using it up and without counting as an unlock attempt.", "檢查上面嗰個鎖嘅驗證碼，唔會用咗佢，亦唔計作解鎖嘗試。");
        yield return new("Code to check", "要檢查嘅驗證碼");
        yield return new("Check code", "檢查驗證碼");
        yield return new("Locks with a policy", "已設定鎖定方式嘅鎖");
        yield return new("Standards check", "標準測試");
        yield return new("Computes the published RFC 6238 test vectors for SHA-1, SHA-256 and SHA-512 with this build's own code.", "用本版本自己嘅程式碼，計算 RFC 6238 公佈嘅 SHA-1、SHA-256 同 SHA-512 測試向量。");
        yield return new("Run RFC 6238 self-test", "執行 RFC 6238 自我測試");
        yield return new("All {0} RFC 6238 vectors match.", "{0} 個 RFC 6238 測試向量全部脗合。");
        yield return new("{0} of {1} vectors match.", "{1} 個測試向量當中有 {0} 個脗合。");
        yield return new("A lock id uses lower-case letters, digits, dots, hyphens and underscores, up to 40 characters.", "鎖識別碼只可以用小寫英文字母、數字、點、連字號同底線，最多 40 個字元。");
        yield return new("Unlock this control before pairing a new authenticator.", "配對新驗證器之前，請先解鎖呢個控制項。");
        yield return new("Replace this lock's policy?", "取代呢個鎖嘅鎖定方式？");
        yield return new("Its current PIN or authenticator and its recovery codes will stop working.", "佢現有嘅 PIN 碼或驗證器同還原碼會失效。");
        yield return new("Replace", "取代");
        yield return new("Pair an authenticator", "配對驗證器");
        yield return new("Arm authenticator", "啟用驗證器");
        yield return new("Authenticator armed for this lock.", "呢個鎖已啟用驗證器。");
        yield return new("This lock has no authenticator yet.", "呢個鎖未有驗證器。");
        yield return new("That code is valid right now.", "呢個驗證碼而家有效。");
        yield return new("That code does not match. It may be old, or this computer's clock may be off.", "驗證碼唔脗合。可能已經過期，或者呢部電腦嘅時鐘唔準。");
        yield return new("No lock has a PIN or authenticator yet. Lock a control from its right-click menu to choose one.", "暫時冇鎖設定 PIN 碼或驗證器。喺控制項嘅右鍵選單鎖住佢，就可以揀選。");
        yield return new("Remove policy", "移除鎖定方式");
        yield return new("Locked", "已鎖住");
        yield return new("Unlocked", "已解鎖");
    }
}
