namespace MaterialDockerImage.Services;

/// <summary>Hong Kong written Cantonese for the changelog, table exports, info-icon guidance and update checks.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseRecords()
    {
        // Table exports and copy-as
        foreach (var format in new[] { "CSV", "TSV", "JSON", "Markdown", "HTML" })
        {
            yield return new("Export visible rows as " + format, "將顯示嘅行匯出做 " + format);
            yield return new("Copy visible rows as " + format, "將顯示嘅行複製做 " + format);
        }
        yield return new("Copied {0} rows as {1}.", "已將 {0} 行複製做 {1}。");
        yield return new("The clipboard is busy. Try again.", "剪貼簿忙緊，請再試一次。");
        yield return new("Exported {0} rows as {1} (UTF-8, header row included). Cells hold the text shown in the table.", "已將 {0} 行匯出做 {1}（UTF-8，包含標題行）。儲存格係表格入面見到嘅文字。");
        yield return new("Exported {0} rows as {1} (UTF-8, schema version 1). Cells hold the text shown in the table.", "已將 {0} 行匯出做 {1}（UTF-8，結構版本 1）。儲存格係表格入面見到嘅文字。");

        // Changelog
        yield return new("Release history", "發佈記錄");
        yield return new("Search the changelog", "搜尋更新記錄");
        yield return new("Changelog from date", "更新記錄開始日期");
        yield return new("Changelog to date", "更新記錄結束日期");
        yield return new("From date", "由日期");
        yield return new("To date", "到日期");
        yield return new("Only changes committed on or after this day. Typed dates are checked as you type and your text is never discarded.", "只顯示呢日或之後提交嘅更改。輸入嘅日期會即時檢查，你打嘅字唔會被清走。");
        yield return new("Only changes committed on or before this day.", "只顯示呢日或之前提交嘅更改。");
        yield return new("YYYY-MM-DD, or a date in your regional format. Leave empty for no limit.", "YYYY-MM-DD，或者你地區格式嘅日期。留空就係冇限制。");
        yield return new("Copy as Markdown", "複製做 Markdown");
        yield return new("Export Markdown…", "匯出 Markdown…");
        yield return new("the filtered changelog", "篩選後嘅更新記錄");
        yield return new("{0} of {1} changes shown in {2} versions", "顯示 {2} 個版本入面 {1} 項更改中嘅 {0} 項");
        yield return new("No change matches the search and date range. Clear them to see the full history.", "冇更改符合搜尋同日期範圍，清除之後就見到完整記錄。");
        yield return new("This build does not include a changelog, so no history is shown here. Builds made with build.bat include it.", "呢個版本冇附帶更新記錄，所以呢度唔會顯示歷史。用 build.bat 建置嘅版本會有。");
        yield return new("The start date is not a valid date. Use YYYY-MM-DD.", "開始日期唔係有效日期，請用 YYYY-MM-DD。");
        yield return new("The end date is not a valid date. Use YYYY-MM-DD.", "結束日期唔係有效日期，請用 YYYY-MM-DD。");
        yield return new("The start date is after the end date.", "開始日期喺結束日期之後。");
        yield return new("Open this commit on the forge", "喺代碼平台開啟呢個提交");
        yield return new("Commit", "提交");
        yield return new("Exported {0} changes as Markdown (UTF-8). The range and search are written at the top of the file.", "已將 {0} 項更改匯出做 Markdown（UTF-8）。日期範圍同搜尋條件寫喺檔案最頂。");
        yield return new("This build reads its version and build time from its own assembly metadata. The history below was bundled when the build was made; this page does not contact the internet.", "呢個版本由自己嘅組件資料讀取版本同建置時間。下面嘅歷史係建置時一齊打包，呢頁唔會連去互聯網。");
        yield return new("Newer releases may exist. Check the releases page for anything published after this build.", "可能已有更新嘅版本。建置之後發佈嘅內容，請去發佈頁面睇。");

        // Info-icon guidance
        yield return new("{0} The built-in default is {1}. A change is saved to settings.json on this computer straight away, and Reset all settings in Data and privacy restores the default.", "{0} 內置預設值係 {1}。更改會即時儲存到呢部電腦嘅 settings.json，喺「資料同私隱」揀「重設所有設定」就會還原預設值。");
        yield return new("Setup re-runs the Docker detection and the engine context and project folder choices. It does not delete settings, history or any Docker resource.", "設定精靈會重新偵測 Docker，並重新揀引擎環境同專案資料夾。唔會刪除設定、歷史或者任何 Docker 資源。");
        yield return new("Every preference returns to its built-in default, including appearance, language and the personal wording file path. Local history and your Docker resources are not touched, and you are asked to confirm first.", "所有偏好都會返回內置預設值，包括外觀、語言同個人用字檔案路徑。本機歷史同你嘅 Docker 資源唔會受影響，而且會先問你確認。");
        yield return new("Only the title bar and the window title change. The shipped product name stays visible in About and in the diagnostics report.", "只會改標題列同視窗標題。關於同診斷報告仍然會顯示出廠產品名稱。");
        yield return new("The version and build time are read from this program's own metadata, so they describe the copy you are running. Tools has the changelog.", "版本同建置時間係由程式自己嘅資料讀取，所以描述緊你而家行緊嗰個版本。更新記錄喺「工具」。");
        yield return new("The original text of this instruction. Edits here replace it exactly as typed.", "呢個指令嘅原文。喺呢度改嘅內容會照你打嘅字取代。");
        yield return new("Transport protocol the port uses. Documentation only, like the port itself.", "呢個埠使用嘅傳輸協定。同埠本身一樣，只係說明用途。");
        yield return new("Dark, Light, or follow the Windows setting. Applies at once and is saved.", "深色、淺色，或者跟 Windows 設定。即時生效並會儲存。");
        yield return new("Comfortable leaves more space between controls; compact fits more on screen.", "舒適會喺控制項之間留多啲空位；緊湊可以喺畫面放多啲內容。");
        yield return new("Drag the slider all the way across to confirm. This extra step stops a destructive action from happening by accident.", "將滑桿拖到最尾就會確認。多呢一步係防止有破壞性嘅操作誤觸。");
        yield return new("Changes the text size of the whole window from 12 to 22 points and is saved with your settings.", "更改整個視窗嘅文字大小（12 至 22 點），並會同設定一齊儲存。");
        yield return new("Copy a file or folder out of the service to this computer, or from this computer into the service.", "將檔案或資料夾由服務複製到呢部電腦，或者由呢部電腦複製入服務。");
        yield return new("Simple splits the text into words. Argv JSON passes an exact list of strings with no splitting.", "簡單模式會將文字拆成字詞。Argv JSON 會原封不動傳遞一個字串清單，唔會拆開。");
        yield return new("The name a Dockerfile RUN --mount=type=secret,id=NAME step uses to read this secret. The value never enters the image.", "Dockerfile 入面 RUN --mount=type=secret,id=NAME 步驟讀取呢個機密時用嘅名稱。機密值唔會寫入映像。");
        yield return new("The file on this computer that holds the secret. Only its path is passed to the build; this app does not read or store its contents.", "呢部電腦上存放機密嘅檔案。只會將路徑傳去建置；呢個程式唔會讀取或者儲存檔案內容。");
        yield return new("The image this container is created from. Change it by choosing a different image in the Images list.", "呢個容器所用嘅映像。要改嘅話，請喺映像清單揀另一個映像。");

        // Update checks
        yield return new("Check for updates", "檢查更新");
        yield return new("Looks for a newer release in the background and stages it. Nothing restarts until you choose to.", "喺背景搵新版本並預先下載。喺你自己揀之前，乜都唔會重新啟動。");
        yield return new("Only an installed copy updates itself; a portable or development run never does. The check reads the public RELEASES file of this project's GitHub releases and sends no personal data. Releases are unsigned, so Windows may warn about an unknown publisher. A staged update opens the next time you start the app, or when you press Restart to update.", "只有安裝版會自行更新；可攜版或者開發版本唔會。檢查只會讀取本專案 GitHub 發佈嘅公開 RELEASES 檔，唔會傳送個人資料。發佈版本未簽署，所以 Windows 可能會提示發行者不明。預先下載嘅更新會喺你下次開啟程式，或者撳「重新啟動以更新」時啟用。");
        yield return new("Update status", "更新狀態");
        yield return new("Checks the release feed once, even when background checks are off.", "即使背景檢查已關閉，都會檢查一次發佈來源。");
        yield return new("Check now", "立即檢查");
        yield return new("A check compares this build's version with the newest full package in the feed. If a newer one exists and this copy is installed, it is downloaded and staged; the running app is never restarted for you.", "檢查會將呢個版本同來源入面最新嘅完整套件比較。如果有新版本而且呢個係安裝版，就會下載並預先安排；執行緊嘅程式唔會自動重新啟動。");
        yield return new("No update check has run in this session.", "今次使用期間未檢查過更新。");
        yield return new("Automatic update checks are turned off in Settings.", "自動檢查更新已喺設定入面關閉。");
        yield return new("This copy was not installed by the installer (portable or development run), so it does not update itself.", "呢個副本唔係經安裝程式安裝（可攜版或者開發版本），所以唔會自行更新。");
        yield return new("Checking for a newer version…", "正在檢查新版本…");
        yield return new("This build is up to date.", "呢個版本已經係最新。");
        yield return new("The feed lists no full package for this app.", "來源入面冇呢個程式嘅完整套件。");
        yield return new("The running version is not a recognised version number, so no comparison was made.", "目前版本號碼無法識別，所以冇作比較。");
        yield return new("Version {0} is available.", "有新版本 {0}。");
        yield return new("Downloading version {0} in the background…", "正在背景下載版本 {0}…");
        yield return new("Version {0} is ready. Restart when you are done with your work.", "版本 {0} 已準備好，做完手頭嘅嘢先再重新啟動。");
        yield return new("The update check did not finish: {0} The current version keeps working.", "更新檢查未完成：{0} 目前版本仍然可以正常使用。");
        yield return new("Update ready", "更新已準備好");
        yield return new("Restart to update", "重新啟動以更新");
        yield return new("Later", "遲啲再講");
        yield return new("Version", "版本");
        yield return new("is ready.", "已準備好。");
        yield return new("It will start the next time you open the app, or you can restart now. This build is unsigned, so Windows may show an unknown publisher warning.", "下次開啟程式時就會用新版本，或者你可以而家重新啟動。呢個版本未簽署，所以 Windows 可能會提示發行者不明。");
        yield return new("Restart to finish updating?", "重新啟動以完成更新？");
        yield return new("This closes the app and opens version", "呢個動作會關閉程式並開啟版本");
        yield return new("Save or finish any work first. Nothing is restarted unless you confirm.", "請先儲存或者完成手頭工作。你冇確認嘅話，乜都唔會重新啟動。");
        yield return new("Restart now", "立即重新啟動");
        yield return new("The updated version could not be started. The current version keeps working.", "未能啟動更新後嘅版本。目前版本仍然可以正常使用。");
    }
}
