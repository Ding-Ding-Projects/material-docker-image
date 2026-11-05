namespace MaterialDockerImage.Services;

/// <summary>Cantonese for named Creator steps and the Values line of the suggestion documentation.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> PageCantoneseCreatorSteps()
    {
        // Suggestion documentation: the fixed values of an option.
        yield return new("Values:", "可選值：");
        yield return new("(default)", "（預設）");

        // Named steps.
        yield return new("Rename step", "幫步驟改名");
        yield return new("Step name", "步驟名稱");
        yield return new("Step renamed. The name is saved as a # step: comment above the instruction.", "步驟改好名喇。個名會以 # step: 註解儲存喺指令上面。");
        yield return new("Step name removed.", "已經剷走步驟名稱。");
        yield return new("Double-click or press F2 to rename", "撳兩下或者按 F2 改名");
        yield return new("Double-click or press F2 to name this step", "撳兩下或者按 F2 幫呢個步驟改個名");
        yield return new("Enter saves, Escape cancels. Leave it empty to remove the name.", "Enter 儲存，Escape 取消。留空就會剷走個名。");
        yield return new("Open a step in the Creator first, then rename it.", "請先喺工房打開一個步驟，然後再改名。");
        yield return new("Creator: rename the open step", "工房：幫打開咗嘅步驟改名");
    }
}
