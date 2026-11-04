namespace MaterialDockerImage.Services;

/// <summary>Hong Kong written Cantonese for container health checks, device mappings and GPU requests.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseContainers()
    {
        yield return new("GPU capabilities", "GPU 功能");
        yield return new("Start interval", "啟動期間隔");
        yield return new("Device cgroup rules", "裝置 cgroup 規則");
        yield return new("Health check", "健康檢查");
        yield return new("Failing streak", "連續失敗次數");
        yield return new("Last probes", "最近檢查");
        yield return new("Devices and GPUs", "裝置同 GPU");
        yield return new("Device mappings", "裝置對應");
        yield return new("GPU requests", "GPU 要求");
        yield return new("No output.", "冇輸出。");
        yield return new("not reported yet", "未有回報");
        yield return new("No probe has finished yet.", "未有檢查完成。");
        yield return new("This container has no health check. Add one when you run it, or define HEALTHCHECK in the image.", "呢個容器冇健康檢查。執行時可以加入一個，或者喺映像入面定義 HEALTHCHECK。");
        yield return new("The health check is disabled for this container.", "呢個容器已停用健康檢查。");
        yield return new("No device mappings, device rules or GPU requests.", "冇裝置對應、裝置規則或者 GPU 要求。");
        yield return new("A GPU request is only honoured when the engine host has a GPU driver such as the NVIDIA Container Toolkit. Otherwise the container does not start.", "只有引擎主機有 GPU 驅動程式（例如 NVIDIA Container Toolkit）時，GPU 要求先會生效，否則容器唔會啟動。");
        yield return new("This engine has no GPU support for the requested GPUs. GPU access needs a driver on the engine host, for example the NVIDIA Container Toolkit or WSL GPU support. Remove the GPU request, or run on an engine that provides one.", "呢個引擎唔支援所要求嘅 GPU。GPU 存取需要引擎主機有驅動程式，例如 NVIDIA Container Toolkit 或者 WSL GPU 支援。請移除 GPU 要求，或者轉用有支援嘅引擎。");
        yield return new("A host device path was not found on the engine host. Device paths belong to the machine running the engine, which may not be this computer.", "引擎主機搵唔到呢個主機裝置路徑。裝置路徑屬於執行引擎嘅機器，未必係呢部電腦。");
        yield return new("--gpus requests GPU access: all, a count, or device IDs. Requires GPU support on the engine host; an engine without it refuses to start the container.", "--gpus 要求 GPU 存取：all、數量或者裝置 ID。需要引擎主機支援 GPU；冇支援嘅引擎會拒絕啟動容器。");
        yield return new("Optional capabilities for the GPU request, separated by spaces or commas. Leave empty for the driver default.", "GPU 要求嘅可選功能，用空格或者逗號分隔。留空就用驅動程式預設值。");
        yield return new("--device maps a host device into the container with read (r), write (w) and mknod (m) permissions. Device paths belong to the engine host.", "--device 將主機裝置對應入容器，附帶讀取 (r)、寫入 (w) 同 mknod (m) 權限。裝置路徑屬於引擎主機。");
        yield return new("--device-cgroup-rule allows access to devices by type and major:minor numbers, one rule per line, for example c 189:* rwm. Rules apply to devices that appear later, such as hot-plugged USB.", "--device-cgroup-rule 按類型同 major:minor 編號允許存取裝置，每行一條規則，例如 c 189:* rwm。規則亦適用於之後出現嘅裝置，例如熱插拔 USB。");
        yield return new("--health-interval: time between checks, as a Go duration such as 30s or 1m30s.", "--health-interval：每次檢查之間嘅時間，用 Go 時長格式，例如 30s 或 1m30s。");
        yield return new("--health-retries: consecutive failures before the container is unhealthy. A positive whole number.", "--health-retries：連續失敗幾次先判定容器唔健康。必須係正整數。");
        yield return new("--health-timeout: maximum time one check may take.", "--health-timeout：單次檢查最長可以用幾耐。");
        yield return new("--health-start-period: initial grace period during which failures do not count.", "--health-start-period：啟動初期嘅寬限期，期間失敗唔計算。");
        yield return new("--health-start-interval: time between checks during the start period. Needs Docker Engine 25 or later.", "--health-start-interval：啟動期內每次檢查之間嘅時間。需要 Docker Engine 25 或以上。");
        yield return new("{0} must be longer than zero, or left empty to use the engine default.", "{0} 必須長過零，或者留空使用引擎預設值。");
        yield return new("{0} must be at least 1ms.", "{0} 最少要 1ms。");
        yield return new("{0} must be a positive whole number.", "{0} 必須係正整數。");
        yield return new("Disable the image health check cannot be combined with a health command or timing options.", "停用映像健康檢查唔可以同健康檢查指令或者時間選項一齊用。");
        yield return new("Device paths must be absolute (for example /dev/ttyUSB0) and cannot contain colons, commas or spaces.", "裝置路徑必須係絕對路徑（例如 /dev/ttyUSB0），而且唔可以包含冒號、逗號或者空格。");
        yield return new("A device mapping needs a host device path.", "裝置對應需要主機裝置路徑。");
        yield return new("Device permissions are a combination of r, w and m, each used at most once, for example rw.", "裝置權限係 r、w、m 嘅組合，每個最多用一次，例如 rw。");
        yield return new("Container device path {0} is mapped more than once.", "容器裝置路徑 {0} 被對應咗多過一次。");
        yield return new("A cgroup device rule looks like \"c 189:* rwm\": type a, b or c, major:minor (or *), then r, w and m permissions.", "cgroup 裝置規則格式似「c 189:* rwm」：類型 a、b 或 c，major:minor（或 *），然後係 r、w、m 權限。");
        yield return new("GPUs must be all, a positive count, or device IDs such as device=0,1.", "GPU 必須係 all、正整數數量，或者裝置 ID，例如 device=0,1。");
        yield return new("GPU capability \"{0}\" must be lowercase letters, such as compute, utility or video.", "GPU 功能「{0}」必須係小寫字母，例如 compute、utility 或者 video。");
        yield return new("Each GPU capability can be listed once.", "每個 GPU 功能只可以列出一次。");
        yield return new("GPU capabilities need a GPU request: enter all, a count or device IDs.", "GPU 功能需要配合 GPU 要求：請輸入 all、數量或者裝置 ID。");
    }
}
