namespace MaterialDockerImage.Services;

/// <summary>Hong Kong written Cantonese for registering an authenticator account with the camera.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> CameraQrCantonese()
    {
        yield return new("From the camera…", "用鏡頭掃描…");
        yield return new("Or read the QR code from a picture or the camera. It is read on this computer and nothing is kept.", "或者由圖片或者鏡頭讀取 QR 碼。讀取工作喺呢部電腦入面完成，唔會保留任何嘢。");
        yield return new("Scan a QR code with the camera", "用鏡頭掃描 QR 碼");
        yield return new("Hold the QR code the service shows in front of the camera. It is read on this computer, nothing from the camera is saved, and the camera is turned off as soon as this window closes.", "將服務顯示嘅 QR 碼對住鏡頭。讀取工作喺呢部電腦入面完成，鏡頭拍到嘅嘢一律唔會儲存，而且一關閉呢個視窗鏡頭就會即刻停用。");
        yield return new("Sample, no camera is used", "示範，冇使用鏡頭");
        yield return new("Sample camera", "示範鏡頭");
        yield return new("This is a sample. A real camera is not opened here.", "呢個只係示範，呢度唔會開啟真正嘅鏡頭。");
        yield return new("Choose another camera if the picture is dark or empty.", "如果畫面太暗或者空白，可以揀另一個鏡頭。");
        yield return new("Camera", "鏡頭");
        yield return new("Camera preview", "鏡頭預覽");
        yield return new("Camera scan status", "鏡頭掃描狀態");
        yield return new("Scanning for a QR code", "正在搜尋 QR 碼");
        yield return new("Looking for cameras…", "搜尋緊鏡頭…");
        yield return new("Starting the camera…", "啟動緊鏡頭…");
        yield return new("QR code read.", "已讀取 QR 碼。");
        yield return new("Looking for a QR code. Hold it steady in front of the camera.", "搜尋緊 QR 碼。請將佢穩定咁對住鏡頭。");
        yield return new("Camera access is turned off for this app. Allow it in Windows Settings under Privacy and security, then Camera, and try again.", "呢個應用程式嘅鏡頭存取權已經關閉。請喺 Windows 設定嘅「私隱權與安全性」入面，揀「相機」並允許存取，然後再試。");
        yield return new("No camera was found on this computer.", "呢部電腦搵唔到鏡頭。");
        yield return new("The camera is being used by another app. Close that app and try again.", "鏡頭正被另一個應用程式使用。請關閉嗰個應用程式再試。");
        yield return new("No authenticator QR code was found in time. Check the lighting and try again, or use a picture instead.", "喺限時內搵唔到驗證器嘅 QR 碼。請檢查光線再試，或者改用圖片。");
        yield return new("The camera could not be started.", "啟動唔到鏡頭。");
    }
}
