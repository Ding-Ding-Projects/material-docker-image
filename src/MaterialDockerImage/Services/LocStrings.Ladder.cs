namespace MaterialDockerImage.Services;

/// <summary>Hong Kong written Cantonese for the unlock ladder games, School mode's name and PIN, and the dim sum surprise.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> LadderCantonese()
    {
        // The ladder
        yield return new("Play to shorten the wait", "玩遊戲縮短等候時間");
        yield return new("Games left this hour:", "呢個鐘頭剩返嘅遊戲次數：");
        yield return new("All the game wins for this hour are used. The clock is the way through until an hour has passed.", "呢個鐘頭嘅遊戲勝出次數用晒喇，要過一個鐘先再玩得，期間只可以等時間過去。");
        yield return new("The games are over for this wait. The clock is the way through.", "今次等候嘅遊戲已經玩完，只可以等時間過去。");
        yield return new("There is no wait to shorten.", "而家冇等候時間可以縮短。");
        yield return new("Winning only ends the wait. You still need your PIN or code, and the next wrong answer still earns a longer wait.", "贏咗只係結束等候。你仍然要輸入 PIN 或驗證碼，下一次答錯仍然會等得更耐。");
        yield return new("The wait is over. You still need your PIN or code to unlock.", "等候結束喇。解鎖仍然要輸入你嘅 PIN 或驗證碼。");
        yield return new("The wait had already ended.", "等候早已結束。");
        yield return new("That round could not be used. Here is a fresh one.", "呢一輪用唔到，轉過新嘅一輪。");
        yield return new("No game question could be made right now. The clock is the way through.", "而家整唔到遊戲題目，只可以等時間過。");
        yield return new("The game could not be shown:", "遊戲顯示唔到：");
        yield return new("Not that one. Try another question.", "唔係呢個，換條題目再試。");
        yield return new("That was the last game. The clock is the way through now.", "已經係最後一個遊戲，而家只可以等時間過去。");
        yield return new("That round is lost. Here is the next game.", "呢一輪輸咗，下一個遊戲嚟喇。");
        // Dish question
        yield return new("Name the dish", "點心叫咩名");
        yield return new("Looking for a dish picture…", "搵緊點心相片…");
        yield return new("A picture of a dim sum dish. Pick its name from the four choices.", "一張點心相片，請喺四個選項入面揀出佢嘅名。");
        yield return new("Which dish is this?", "呢個係邊款點心？");
        yield return new("No picture is available right now, so this one asks for a name instead.", "暫時冇相片，所以今次改為問名稱。");
        yield return new("Which English name matches?", "邊個英文名啱？");
        yield return new("Choice", "選項");
        // Sums
        yield return new("Ten sums", "十條數");
        yield return new("Answer to sum", "答案，第幾條數");
        yield return new("Fill in all ten sums with whole numbers.", "十條數都要填晒，而且要用整數。");
        yield return new("Every sum has to be right. One slip moves you on to the next game.", "每條數都要啱。錯一條就會去下一個遊戲。");
        yield return new("Check my answers", "核對我嘅答案");
        // Whack-a-mole
        yield return new("Whack-a-mole", "打地鼠");
        yield return new("Hits", "打中");
        yield return new("seconds left", "秒剩餘");
        yield return new("Cell", "格");
        yield return new("mole is up", "地鼠出咗嚟");
        yield return new("MOLE", "地鼠");
        yield return new("Start the round", "開始呢一輪");
        yield return new("Give up", "放棄");
        yield return new("A mole pops up in one cell at a time and stays for one second. Click it, or press its number key 1 to 9. Hit at least 8 of 14 in 20 seconds. Nothing here is animated.", "地鼠一次喺一格冒出嚟，企一秒鐘。用滑鼠㩒佢，或者㩒對應嘅數字鍵 1 至 9。要喺 20 秒內打中 14 隻入面至少 8 隻。呢度冇任何動畫。");
        // School mode
        yield return new("Name of this mode", "呢個模式嘅名稱");
        yield return new("Rename the mode. Every screen then uses your name and never the shipped one.", "幫呢個模式改名。之後每個畫面都用你改嘅名，唔會再出現原本嘅名。");
        yield return new("The name is kept in the shared School mode record, so every app shows the same one.", "名稱記錄喺共用嘅學校模式記錄入面，所以每個程式都顯示同一個名。");
        yield return new("A PIN or password is set.", "已設定 PIN 或密碼。");
        yield return new("No PIN or password is set, so the mode can be turned off freely.", "未設定 PIN 或密碼，所以可以隨時關閉呢個模式。");
        yield return new("PIN or password to turn it off", "關閉所需嘅 PIN 或密碼");
        yield return new("Set…", "設定…");
        yield return new("Change…", "更改…");
        yield return new("Your language and funny-level choices are kept and come back when School mode is turned off. Turning it off asks for the PIN or password when one is set. One shared record serves every app on this computer.", "你嘅語言同搞笑程度選擇會保留，關閉學校模式後就會返回。如果設定咗 PIN 或密碼，關閉時會要求輸入。呢部電腦上所有程式共用同一份記錄。");
        yield return new("Turn off", "關閉");
        yield return new("Enter the PIN or password you set for this mode.", "請輸入你為呢個模式設定嘅 PIN 或密碼。");
        yield return new("PIN or password", "PIN 或密碼");
        yield return new("That did not match.", "唔啱。");
        yield return new("Too many wrong answers. Wait, then try again.", "答錯太多次，請等一陣再試。");
        yield return new("Too many wrong answers. Try again at", "答錯太多次，請喺以下時間再試：");
        yield return new("The two entries do not match.", "兩次輸入唔一樣。");
        yield return new("The current PIN or password did not match.", "而家嘅 PIN 或密碼唔啱。");
        yield return new("It could not be saved: ", "儲存唔到：");
        yield return new("The PIN or password was saved. It is stored only as a salted hash.", "PIN 或密碼已儲存，只會以加鹽雜湊形式保存。");
        yield return new("PIN or password to turn off", "關閉所需嘅 PIN 或密碼");
        yield return new("A PIN is 4 to 12 digits. A password is 6 to 64 characters. It is asked for only when turning the mode off, in every app that shares this record.", "PIN 係 4 至 12 個數字，密碼係 6 至 64 個字元。只會喺關閉呢個模式時詢問，所有共用呢份記錄嘅程式都一樣。");
        yield return new("Current PIN or password", "而家嘅 PIN 或密碼");
        yield return new("New PIN or password", "新嘅 PIN 或密碼");
        yield return new("Repeat it", "再輸入一次");
        yield return new("Rename this mode", "幫呢個模式改名");
        yield return new("Every screen of every app uses this name instead of the shipped one.", "每個程式嘅每個畫面都會用呢個名稱，取代原本嘅名。");
        yield return new("Renamed.", "已改名。");
        yield return new("Enter a name.", "請輸入名稱。");
        yield return new("The name can be at most 40 characters.", "名稱最多 40 個字元。");
        yield return new("The name cannot contain control characters.", "名稱唔可以包含控制字元。");
        yield return new("Use a PIN of 4 to 12 digits, or a password of 6 to 64 characters.", "請用 4 至 12 個數字嘅 PIN，或者 6 至 64 個字元嘅密碼。");
        yield return new("This mode is a lock you set for yourself, not security. Anyone who can delete the shared application-data record can switch it off.", "呢個模式係你自己設定嘅提示鎖，並非安全保護。任何可以刪除共用應用程式資料記錄嘅人都可以將佢關閉。");
        yield return new("on", "開");
        yield return new("off", "關");
        // Dim sum surprise
        yield return new("Dim sum surprise", "點心驚喜");
        yield return new("No picture is available right now.", "暫時冇相片。");
    }
}
