using System.Collections.Generic;

namespace MaterialDockerImage.UI;

public static class UiText
{
    private static readonly Dictionary<string, string> Cantonese = new()
    {
        ["Build"] = "建立映像", ["Images"] = "映像", ["Containers"] = "容器", ["Compose"] = "Compose 專案",
        ["Canvas"] = "畫布", ["Volumes"] = "磁碟區", ["Networks"] = "網絡", ["Diagnostics"] = "診斷", ["Settings"] = "設定",
        ["Setup"] = "初次設定", ["Refresh"] = "重新整理", ["Browse"] = "瀏覽", ["Save"] = "儲存", ["Load"] = "載入",
        ["Cancel"] = "取消", ["Close"] = "關閉", ["Back"] = "返回", ["Next"] = "下一步", ["Finish"] = "完成",
        ["Pull image"] = "下載映像", ["Run"] = "執行", ["Tag"] = "標籤", ["Inspect"] = "檢視詳情", ["Remove"] = "移除",
        ["Start"] = "啟動", ["Stop"] = "停止", ["Restart"] = "重新啟動", ["Logs"] = "記錄", ["Statistics"] = "統計",
        ["Create"] = "建立", ["Save archive"] = "儲存封存檔", ["Load archive"] = "載入封存檔", ["New tab"] = "新增分頁",
        ["Build context"] = "建立內容資料夾", ["Image tag"] = "映像標籤", ["Target stage"] = "目標階段", ["Platforms"] = "平台",
        ["Build arguments"] = "建立參數", ["No cache"] = "不使用快取", ["Pull base images"] = "更新基礎映像", ["Build image"] = "建立映像",
        ["Load Dockerfile"] = "載入 Dockerfile", ["Save Dockerfile"] = "儲存 Dockerfile", ["Dockerfile editor"] = "Dockerfile 編輯器",
        ["Ready"] = "準備就緒", ["Not checked"] = "尚未檢查", ["Detect Docker"] = "偵測 Docker", ["Refresh contexts"] = "重新整理環境",
        ["Fit graph"] = "縮放至全圖", ["Zoom in"] = "放大", ["Zoom out"] = "縮小", ["Parse Dockerfile"] = "分析 Dockerfile",
        ["Container topology"] = "容器拓撲", ["Apply stage changes"] = "套用階段變更", ["Version"] = "版本", ["Updated"] = "更新時間",
        ["Theme"] = "主題", ["Language"] = "語言", ["Font size"] = "字體大小", ["Reduced motion"] = "減少動畫",
        ["Operation output"] = "操作輸出", ["Clear output"] = "清除輸出", ["Working"] = "處理中", ["Completed"] = "已完成"
    };
    public static string Get(string text, string language)
    {
        if (language == "English" || !Cantonese.TryGetValue(text, out var translated)) return text;
        return language == "Cantonese" ? translated : text + " · " + translated;
    }
}
