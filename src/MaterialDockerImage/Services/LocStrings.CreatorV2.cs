namespace MaterialDockerImage.Services;

/// <summary>Cantonese for the wording added when the Dockerfile Creator and the editor suggestions were matched to the second design boards.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseCreatorV2()
    {
        yield return new("More Creator actions", "更多 Creator 動作");
        yield return new("Runs the checks on the draft and lists the results with their fixes.", "檢查草稿，列出結果同修正方法。");
        yield return new("Saves the Dockerfile to the file it was last saved to, or asks where to save it.", "將 Dockerfile 儲存去上次儲存嘅檔案；未儲存過就問你存去邊。");
        yield return new("Opens the draft in Build, where you start the build.", "喺「建立映像」打開草稿，喺嗰度開始建立。");
        yield return new("Preview", "預覽");
        yield return new("Click or press Enter to edit this step.", "撳一下或者撳 Enter 編輯呢個步驟。");
        yield return new("Previous step", "上一步");
        yield return new("Next step", "下一步");
        yield return new("Collapse instruction (Esc)", "收埋指令（Esc）");
        yield return new("More instruction actions", "更多指令動作");
        yield return new("More step actions", "更多步驟動作");
        yield return new("Run commands", "執行指令");
        yield return new("Add files", "加入檔案");
        yield return new("Build argument", "建立參數");
        yield return new("Default command", "預設指令");
        yield return new("Entry point", "進入點");
        yield return new("Stop signal", "停止訊號");
        yield return new("Instruction for child images", "俾子映像用嘅指令");
        yield return new("Preview files", "預覽檔案");
        yield return new(".dockerignore presets", ".dockerignore 預設");
        yield return new("Listed. Click to remove these patterns:", "已經列咗。撳一下移除呢啲規則：");
        yield return new("Removed", "已移除");
        yield return new("Undo restores them.", "復原可以攞返。");
        yield return new("Add a cache, bind, tmpfs, secret or ssh mount, or the cache mounts of a package manager.", "加入 cache、bind、tmpfs、secret 或 ssh 掛載，或者某個套件管理器嘅快取掛載。");
        yield return new("mount options", "掛載選項");
        yield return new("Other options of this mount. Open the options to change them.", "呢個掛載嘅其他選項。打開選項先可以改。");
        yield return new("mount target", "掛載目標");
        yield return new("mount", "掛載");
        yield return new("Hide mount options", "收埋掛載選項");
        yield return new("Show mount options", "顯示掛載選項");
        yield return new("Mount", "掛載");
        yield return new("Add cache mount", "加入 cache 掛載");
        yield return new("Add bind mount", "加入 bind 掛載");
        yield return new("Add tmpfs mount", "加入 tmpfs 掛載");
        yield return new("Add secret mount", "加入 secret 掛載");
        yield return new("Add ssh mount", "加入 ssh 掛載");
        yield return new("Heredoc script", "Heredoc 腳本");
        yield return new("Write several commands as one RUN script instead of joining them with && and backslashes.", "將幾條指令寫成一個 RUN 腳本，唔使用 && 同反斜線駁埋。");
        yield return new("Shell command. For several lines end each line with a backslash, join commands with &&, or switch on Heredoc script.", "Shell 指令。多行就每行尾加反斜線、用 && 駁指令，或者開 Heredoc 腳本。");
        yield return new("Evidence", "證據");
    }
}
