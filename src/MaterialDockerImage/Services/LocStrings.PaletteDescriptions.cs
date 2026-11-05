namespace MaterialDockerImage.Services;

/// <summary>Cantonese for the one-line command descriptions the command palette shows under each title (UI/CommandDescriptions.cs).</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantonesePaletteDescriptions()
    {
        // Shell
        yield return new("Search every command, destination and setting in one place", "喺同一個地方搵晒所有指令、目的地同設定");
        yield return new("Open the catalogue of destinations, tools and settings in a new tab", "喺新分頁打開目的地、工具同設定目錄");
        yield return new("Close the current tab", "閂咗而家呢個分頁");
        yield return new("Show the selected image full page with its layers, platforms and vulnerabilities", "用全頁顯示揀咗嘅映像，包括分層、平台同漏洞");
        yield return new("Switch to the tab on the right, wrapping around at the end", "轉去右邊嗰個分頁，去到尾會兜返轉頭");
        yield return new("Find an open tab by its title and jump to it", "用標題搵返打開咗嘅分頁，一下跳過去");
        yield return new("Close every tab whose title contains the text or pattern you enter", "閂晒標題包含你輸入文字或模式嘅分頁");
        yield return new("Close every tab whose title does not contain the text or pattern you enter", "閂晒標題唔包含你輸入文字或模式嘅分頁");
        yield return new("Review recent messages, warnings and finished operations", "睇返最近嘅訊息、警告同完成咗嘅操作");
        yield return new("Text size, contrast, motion and narration in one quick menu", "字體大小、對比、動畫同朗讀，一個快捷選單搞掂");
        yield return new("Open or close the docked panel with command output and logs", "打開或者閂埋底部嘅指令輸出同記錄面板");
        yield return new("Reload engine contexts and check which ones are reachable", "重新載入引擎環境，睇吓邊個連得到");
        // Settings rows
        yield return new("Follow Windows, or always use light or dark colours", "跟 Windows 設定，或者一直用淺色或深色");
        yield return new("Shorten or remove animations across the app", "成個程式嘅動畫縮短或者乾脆唔要");
        yield return new("Stronger outlines and text colours for readability", "加強外框同文字顏色，睇得清楚啲");
        yield return new("English, Hong Kong Cantonese, or both side by side", "英文、香港粵語，或者兩樣並排");
        yield return new("Short sounds when an action finishes or fails", "操作完成或者失敗時播短聲效");
        yield return new("Keep the output panel open at the bottom of the window", "視窗底部嘅輸出面板保持打開");
        yield return new("Change settings automatically by time, date or schedule", "按時間、日期或排程自動改設定");
        yield return new("Add, change or remove PIN, password and authenticator locks", "新增、更改或者移除 PIN、密碼同驗證器鎖");
        yield return new("Hide nonessential chrome and narrow the navigation rail", "收埋唔重要嘅介面，導航列收窄啲");
        yield return new("Turn off action sounds and every animation", "熄晒操作聲效同所有動畫");
        yield return new("Count operations finished in a row this session", "數吓今次連續完成咗幾多個操作");
        yield return new("Show a clock in the title bar", "喺標題列顯示時鐘");
        yield return new("Opening a destination reuses the current tab", "開目的地會用返而家個分頁");
        yield return new("Local help desk for getting back into a lock you cannot open", "本機服務台，幫你開返打唔開嘅鎖");
        // Pages
        yield return new("Browse saved versions of your files and restore one", "睇吓檔案儲存過嘅版本，揀一個還原");
        yield return new("Open the local history folder in File Explorer", "喺檔案總管打開本機歷史資料夾");
        yield return new("Load a Dockerfile from disk into the Build editor", "由磁碟載入 Dockerfile 去建立編輯器");
        yield return new("Write the Build editor's Dockerfile back to its file", "將建立編輯器入面嘅 Dockerfile 寫返落原本個檔");
        yield return new("Run docker buildx build --check and list the findings", "執行 docker buildx build --check，列出搵到嘅問題");
        yield return new("Build the image from the Dockerfile and context folder", "用 Dockerfile 同內容資料夾建立映像");
        yield return new("Open the current Dockerfile in Visual Studio Code", "用 Visual Studio Code 打開而家個 Dockerfile");
        yield return new("Follow each BuildKit step with its status and duration", "跟住每個 BuildKit 步驟睇狀態同用時");
        yield return new("Previous builds recorded by buildx history", "buildx history 記低咗嘅舊建立");
        yield return new("Stop the build started from the Build page", "停咗由建立頁開始嘅建立");
        yield return new("Choose a folder and find its bake or Compose files", "揀個資料夾，搵入面嘅 bake 或 Compose 檔");
        yield return new("Open a docker-bake.hcl, JSON or Compose file", "打開 docker-bake.hcl、JSON 或 Compose 檔");
        yield return new("Resolve the targets and groups with docker buildx bake --print", "用 docker buildx bake --print 解析目標同群組");
        yield return new("Build the targets ticked in the bake plan", "建立 bake 計劃入面剔咗嘅目標");
        yield return new("Stop the bake that is running", "停咗正在跑嘅 bake");
        yield return new("Zoom the graph so every stage fits the view", "縮放圖表，令所有階段都擺得入畫面");
        yield return new("Draw the stage graph of the Dockerfile open in Build", "畫出建立頁打開緊嗰個 Dockerfile 嘅階段圖");
        yield return new("Load a compose.yaml or docker-compose.yml file", "載入 compose.yaml 或 docker-compose.yml 檔");
        yield return new("Reload Compose projects from docker compose ls", "由 docker compose ls 重新載入 Compose 專案");
        yield return new("Start a new container from a local image", "用本機映像開一個新容器");
        yield return new("Reload the container list from the engine", "由引擎重新載入容器清單");
        yield return new("Delete every stopped container after you confirm", "你確認之後刪晒所有已停止嘅容器");
        yield return new("Restart the container focused in the Containers list", "重新啟動容器清單入面揀咗嗰個容器");
        yield return new("Add an engine endpoint over SSH, TCP or TLS", "經 SSH、TCP 或 TLS 加一個引擎端點");
        yield return new("Add a context from an exported archive", "由匯出咗嘅封存檔加返一個引擎環境");
        yield return new("Parse a Dockerfile from disk into editable stages", "將磁碟上嘅 Dockerfile 拆成可以編輯嘅階段");
        yield return new("Find the language and build files in a folder and draft a Dockerfile", "認出資料夾用咩語言同建立檔，起個 Dockerfile 草稿");
        yield return new("Copy the docker buildx build command for this draft", "複製呢份草稿嘅 docker buildx build 指令");
        yield return new("Name the open Creator step; the name is kept as a # step: comment above it", "幫工房打開咗嘅步驟改名；個名會以 # step: 註解留喺佢上面");
        yield return new("Send the Creator draft to the Build page", "將工房草稿送去建立頁");
        yield return new("Check the engine connection, versions and health", "檢查引擎連線、版本同健康狀況");
        yield return new("Preview and save a report with secrets removed", "預覽同儲存一份已經剷走機密嘅報告");
        yield return new("Follow docker events from the engine as they happen", "即時跟住引擎嘅 docker events");
        yield return new("Download an image from a registry by name and tag", "用名同標籤由映像倉庫下載映像");
        yield return new("Load images from a docker save tar archive", "由 docker save 嘅 tar 封存檔載入映像");
        yield return new("Create an image from a filesystem tarball", "用檔案系統 tarball 整一個映像");
        yield return new("Delete untagged images that no container uses", "刪走冇標籤又冇容器用緊嘅映像");
        yield return new("Add several tags to the selected image at once", "一次過幫揀咗嘅映像加幾個標籤");
        yield return new("Upload every tag of the selected repository", "上載揀咗嗰個倉庫嘅所有標籤");
        yield return new("Copy a docker pull command for the selected image", "複製揀咗嗰個映像嘅 docker pull 指令");
        yield return new("Reload the local image list from the engine", "由引擎重新載入本機映像清單");
        yield return new("Guided setup for a Java or Bedrock server in a container", "一步步喺容器入面開 Java 或 Bedrock 伺服器");
        yield return new("Reload Minecraft servers and their status", "重新載入 Minecraft 伺服器同佢哋嘅狀態");
        yield return new("Open the folder that holds world backups", "打開放世界備份嘅資料夾");
        yield return new("Stop or restart the live CPU, memory and network charts", "停止或者重開即時 CPU、記憶體同網絡圖表");
        yield return new("Read container statistics once, right now", "即刻讀一次容器統計");
        yield return new("Store registry credentials with docker login", "用 docker login 儲存映像倉庫登入資料");
        yield return new("Find public images on Docker Hub", "喺 Docker Hub 搵公開映像");
        yield return new("List buildx builders and prune their build cache", "列出 buildx 建立器，清理佢哋嘅建立快取");
        yield return new("Check every local image for known vulnerabilities", "幫每個本機映像查已知漏洞");
        yield return new("Check the selected image for known vulnerabilities", "幫揀咗嘅映像查已知漏洞");
        yield return new("Save the vulnerability findings as JSON or CSV", "將漏洞結果存做 JSON 或 CSV");
        yield return new("Stop the Docker Scout scan that is running", "停咗正在跑嘅 Docker Scout 掃描");
        yield return new("Reload images and saved findings", "重新載入映像同儲存咗嘅結果");
        yield return new("Save every setting to a JSON file", "將所有設定存做 JSON 檔");
        yield return new("Restore settings from an exported JSON file", "由匯出咗嘅 JSON 檔還原設定");
        yield return new("English-only wording; turning it off needs the PIN or password you set", "只用英文字眼；要用你設定嘅 PIN 或密碼先熄得");
        yield return new("Walk through engine detection and first-run choices again", "再行一次引擎偵測同初次設定");
        yield return new("Open Storage on the list of volumes", "打開儲存頁嘅磁碟區清單");
        yield return new("Make a named volume with a driver and options", "用驅動程式同選項整一個具名磁碟區");
        yield return new("Make a bridge or overlay network with its subnet", "連子網一齊整一個 bridge 或 overlay 網絡");
        yield return new("Show completions at the caret in a Dockerfile or Compose editor", "喺 Dockerfile 或 Compose 編輯器游標位置顯示建議");
        yield return new("Open a picker for the value under the caret", "幫游標下面嘅值打開選擇器");
        yield return new("Show completions automatically as you type", "打字嗰陣自動顯示建議");
        yield return new("Reload swarm services, stacks and nodes", "重新載入 swarm 服務、堆疊同節點");
        yield return new("Create a swarm service with its image and replicas", "用映像同副本數整一個 swarm 服務");
        yield return new("Show what images, containers, volumes and cache use on disk", "睇吓映像、容器、磁碟區同快取佔幾多磁碟");
        yield return new("Choose unused images, containers, volumes and cache to remove", "揀要移除嘅未使用映像、容器、磁碟區同快取");
        yield return new("Encode or decode text, files and data URLs", "編碼或解碼文字、檔案同 data URL");
        yield return new("Read the bundled guides without a network connection", "冇網絡都睇到附帶嘅指南");
        yield return new("Enrol and read authenticator codes for locks", "登記同讀取鎖用嘅驗證器代碼");
        yield return new("This build's version and its release history", "呢個版本嘅版本號同發佈歷史");
        // Appearance
        yield return new("Change the colour, shape, size or visibility of the focused element", "改焦點元素嘅顏色、形狀、大小或者顯示");
        yield return new("Click any element to customize how it looks", "撳任何元素嚟自訂佢個樣");
        yield return new("List every element you customized and reset any of them", "列出所有自訂過嘅元素，邊個都可以重設");
        yield return new("Remove every element customization", "移除所有元素自訂");
        yield return new("Colours, fonts, shape, motion, density and presets", "顏色、字型、形狀、動畫、密度同預設");
        yield return new("Pick colour roles and style for the focused element", "幫焦點元素揀顏色角色同樣式");
        yield return new("Step back one appearance change", "退返一步外觀變更");
        yield return new("Reapply the appearance change you undid", "重新套用你復原咗嘅外觀變更");
        yield return new("Off, reduced, standard or expressive transitions", "過場動畫：關、減少、標準或者誇張");
        yield return new("Compact, comfortable or spacious spacing", "緊湊、舒適或者寬鬆嘅間距");
        yield return new("Slowly cycle the accent colour through the hues", "強調色慢慢轉匀所有色相");
        // Creator starters
        yield return new("Three-stage Node.js Dockerfile with a non-root production runtime", "三階段 Node.js Dockerfile，正式運行唔用 root");
        yield return new("Two-stage Python Dockerfile that installs wheels from requirements.txt", "兩階段 Python Dockerfile，由 requirements.txt 裝 wheel");
        yield return new("Two-stage Python Dockerfile using uv sync and a virtual environment", "兩階段 Python Dockerfile，用 uv sync 同虛擬環境");
        yield return new("Two-stage .NET Dockerfile with the SDK build and the ASP.NET runtime", "兩階段 .NET Dockerfile，SDK 建立、ASP.NET 運行");
        yield return new("Two-stage Go Dockerfile with a static build on distroless", "兩階段 Go Dockerfile，靜態建立再放落 distroless");
        yield return new("Two-stage Java Dockerfile with a Maven build and a JRE runtime", "兩階段 Java Dockerfile，Maven 建立、JRE 運行");
        yield return new("Two-stage Rust Dockerfile with a release build on Debian slim", "兩階段 Rust Dockerfile，release 建立放落 Debian slim");
        yield return new("Two-stage static site Dockerfile served by unprivileged nginx", "兩階段靜態網站 Dockerfile，由冇特權嘅 nginx 提供");
        yield return new("One empty Alpine stage to start from scratch", "一個空嘅 Alpine 階段，由零開始");
        // Appearance presets
        yield return new("The standard dark colours and shapes", "標準深色同形狀");
        yield return new("The standard light colours and shapes", "標準淺色同形狀");
        yield return new("Black surfaces, white text and a yellow accent", "黑色底、白色字、黃色強調色");
        yield return new("Dark teal accent with softer, rounder corners", "深青強調色，角位圓啲軟啲");
        yield return new("Dark green accent with tighter corners", "深綠強調色，角位收細啲");
        yield return new("Light orange accent, round corners and bold headings", "淺色配橙色強調、圓角同粗體標題");
        yield return new("Dark grey accent with square corners and flat surfaces", "深灰強調色、方角同平面");
        // Data table checkbox column
        yield return new("Select row", "揀呢行");
    }
}
