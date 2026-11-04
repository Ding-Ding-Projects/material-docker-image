namespace MaterialDockerImage.Services;

/// <summary>Hong Kong written Cantonese for the School mode passkey choice and for reading an authenticator QR code from a picture.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> SchoolPasskeyCantonese()
    {
        // Windows Hello availability, said plainly
        yield return new("Windows Hello is available on this computer.", "呢部電腦用得 Windows Hello。");
        yield return new("This computer has no device that Windows Hello can use, such as a fingerprint reader or camera.", "呢部電腦冇 Windows Hello 用得著嘅裝置，例如指紋讀取器或者鏡頭。");
        yield return new("Windows Hello is not set up for this Windows account. Set it up in Windows Settings first.", "呢個 Windows 帳戶未設定 Windows Hello。請先喺 Windows 設定入面設定。");
        yield return new("Windows Hello is turned off by a policy on this computer.", "呢部電腦嘅原則已經停用咗 Windows Hello。");
        yield return new("Windows Hello is busy right now. Try again in a moment.", "Windows Hello 而家忙緊，請稍後再試。");
        yield return new("Windows Hello cannot be used here.", "呢度用唔到 Windows Hello。");
        yield return new("Enter the PIN or password instead.", "請改為輸入 PIN 或者密碼。");
        yield return new("No PIN or password is set either, so the mode turns off without a check.", "亦都冇設定 PIN 或者密碼，所以呢個模式會直接關閉，唔會驗證。");
        // Turning the mode off and changing the method
        yield return new("Windows Hello did not verify you, so the mode stays on.", "Windows Hello 驗證唔到你，所以模式保持開啟。");
        yield return new("Windows Hello did not verify you, so the choice was not changed.", "Windows Hello 驗證唔到你，所以冇更改選擇。");
        yield return new("Use Windows Hello to turn off", "用 Windows Hello 關閉");
        yield return new("Change how to turn off", "更改關閉方式");
        yield return new("Windows Hello will now turn this mode off.", "之後會用 Windows Hello 關閉呢個模式。");
        yield return new("The PIN or password will now turn this mode off.", "之後會用 PIN 或者密碼關閉呢個模式。");
        // Settings row
        yield return new("Windows Hello passkey to turn it off", "用 Windows Hello 通行密鑰關閉");
        yield return new("Choose whether Windows Hello or the PIN or password turns the mode off. Windows shows its own verification dialog; nothing it checks is stored here.", "揀用 Windows Hello 定係 PIN 或者密碼嚟關閉呢個模式。驗證視窗由 Windows 顯示，佢檢查嘅嘢唔會儲存喺呢度。");
        yield return new("Only the choice is saved in the shared record. If Windows Hello is not set up or not available when the mode is turned off, that is said plainly and the PIN or password is asked for instead; with neither, the mode turns off without a check.", "共用記錄只會儲存你嘅選擇。關閉模式時如果 Windows Hello 未設定或者用唔到，會照直講出嚟，並改為要求 PIN 或者密碼；兩樣都冇嘅話，模式會直接關閉，唔會驗證。");
        yield return new("Windows Hello turns this mode off. If it is not available, the PIN or password is asked for instead.", "用 Windows Hello 關閉呢個模式。如果用唔到，就會改為要求 PIN 或者密碼。");
        yield return new("The PIN or password turns this mode off.", "用 PIN 或者密碼關閉呢個模式。");
        yield return new("Use PIN or password", "改用 PIN 或者密碼");
        yield return new("Use Windows Hello", "改用 Windows Hello");
        yield return new("School mode is on. The other choices here come back when it is turned off in Data and privacy.", "學校模式已開啟。其他選擇會喺你喺「資料與私隱」關閉佢之後返嚟。");
        // Authenticator: from a picture
        yield return new("From an image file…", "從圖片檔案…");
        yield return new("From the clipboard", "從剪貼簿");
        yield return new("Or read the QR code from a picture. The picture is read on this computer and is not kept.", "或者從圖片讀取 QR 碼。圖片只喺呢部電腦讀取，唔會保留。");
        yield return new("Choose a picture of the QR code", "選擇 QR 碼嘅圖片");
        yield return new("That picture could not be opened.", "打開唔到呢張圖片。");
        yield return new("The clipboard has no picture of a QR code.", "剪貼簿入面冇 QR 碼圖片。");
        yield return new("The clipboard could not be read.", "讀取唔到剪貼簿。");
        yield return new("That picture is too large to read.", "呢張圖片太大，讀取唔到。");
        yield return new("Choose a picture file.", "請選擇圖片檔案。");
        yield return new("Choose a PNG, JPEG, BMP, GIF or TIFF picture.", "請選擇 PNG、JPEG、BMP、GIF 或者 TIFF 圖片。");
        yield return new("That picture is too large. Use one under 20 MB.", "呢張圖片太大，請用細過 20 MB 嘅。");
        yield return new("That picture is empty.", "呢張圖片係空嘅。");
        yield return new("That picture cannot be read.", "讀取唔到呢張圖片。");
        yield return new("No QR code was found in that picture.", "圖片入面搵唔到 QR 碼。");
        yield return new("This is an export code for several accounts, which this authenticator cannot read. Show one account's own QR code instead.", "呢個係多個帳戶嘅匯出碼，呢個驗證器讀唔到。請改為顯示單一帳戶本身嘅 QR 碼。");
        yield return new("That QR code is not an authenticator link.", "呢個 QR 碼唔係驗證器連結。");
    }
}
