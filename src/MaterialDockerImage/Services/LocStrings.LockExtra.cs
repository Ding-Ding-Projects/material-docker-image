namespace MaterialDockerImage.Services;

/// <summary>Hong Kong written Cantonese for tab and tab group locks, appearance value locks, the unlock prompt opened by clicking a locked control, locked labels in searches and the PIN keypad.</summary>
public static partial class Loc
{
    private static IEnumerable<KeyValuePair<string, string>> LockExtraCantonese()
    {
        // Clicking a locked control
        yield return new("Locked. Click it to unlock.", "已鎖住。撳一下就可以解鎖。");
        yield return new("Locked. Click the switch or right-click it to unlock it.", "已鎖住。撳開關或者按右鍵就可以解鎖。");
        yield return new("Locked. Select it to unlock this tab.", "已鎖住。揀呢個分頁就可以解鎖。");
        yield return new("locked", "已鎖住");
        yield return new("Unlock…", "解鎖…");
        // Tabs and tab groups
        yield return new("Lock this tab…", "鎖住呢個分頁…");
        yield return new("Unlock this tab…", "解鎖呢個分頁…");
        yield return new("Lock this tab group…", "鎖住呢個分頁群組…");
        yield return new("Unlock this tab group…", "解鎖呢個分頁群組…");
        yield return new("Lock this destination…", "鎖住呢個目的地…");
        yield return new("Unlock this destination…", "解鎖呢個目的地…");
        yield return new("Lock the navigation rail…", "鎖住導覽列…");
        yield return new("Unlock the navigation rail…", "解鎖導覽列…");
        yield return new("Tab group: Pinned tabs", "分頁群組：已釘選分頁");
        yield return new("Tab group: Open tabs", "分頁群組：開啟中嘅分頁");
        yield return new("Tab group: Navigation rail", "分頁群組：導覽列");
        yield return new("Lock:", "鎖住：");
        yield return new("Unlock:", "解鎖：");
        yield return new("Include locked tabs", "包括已鎖住嘅分頁");
        yield return new("Off by default. Locked tabs still ask for their unlock before they close.", "預設關閉。已鎖住嘅分頁關閉之前，都會先要求解鎖。");
        yield return new("locked tab(s) stayed open.", "個已鎖住嘅分頁保持開啟。");
        // Locked items in searches
        yield return new("Hide locked items in searches", "搜尋時隱藏已鎖住嘅項目");
        yield return new("Off by default: locked tabs, settings and commands stay listed and are labelled Locked. When on, they are left out, and every list says how many it hid.", "預設關閉：已鎖住嘅分頁、設定同指令仍然會列出，並標示「已鎖住」。開啟後會唔顯示，而每個清單都會講明隱藏咗幾多個。");
        yield return new("locked item(s) are hidden from these results. Turn off Hide locked items in searches in Settings to see them.", "個已鎖住嘅項目喺呢個結果入面被隱藏。想睇返，請喺設定關閉「搜尋時隱藏已鎖住嘅項目」。");
        // Appearance value locks
        yield return new("Locked appearance values were kept as they were:", "已鎖住嘅外觀數值維持原狀：");
        yield return new("Unlock them to change them.", "解鎖之後先可以更改。");
        yield return new("Locked properties kept their values. Unlock them to change them.", "已鎖住嘅屬性保持原本嘅數值。解鎖之後先可以更改。");
        yield return new("Some elements keep locked properties. Unlock them to reset them.", "有啲元素仍然有已鎖住嘅屬性。解鎖之後先可以重設。");
        yield return new("Appearance: Theme", "外觀：主題");
        yield return new("Appearance: Seed colour", "外觀：種子顏色");
        yield return new("Appearance: Colour roles (all)", "外觀：所有顏色角色");
        yield return new("Appearance: Corner radius scale", "外觀：圓角比例");
        yield return new("Appearance: Density", "外觀：密度");
        yield return new("Appearance: Interface font", "外觀：介面字體");
        yield return new("Appearance: Code font", "外觀：程式碼字體");
        yield return new("Appearance: Font size", "外觀：字體大小");
        yield return new("Appearance: Heading weight", "外觀：標題粗幼");
        yield return new("Appearance: Rail icon style", "外觀：導覽列圖示樣式");
        yield return new("Appearance: Elevation", "外觀：浮起程度");
        yield return new("Appearance: Surface tint", "外觀：表面色調");
        yield return new("Appearance: Rainbow accent", "外觀：彩虹強調色");
        yield return new("Appearance: Motion level", "外觀：動態程度");
        yield return new("Appearance: Motion speed", "外觀：動態速度");
        yield return new("Customized element (all properties)", "已自訂元素（所有屬性）");
        yield return new("Customized element: Background colour", "已自訂元素：背景顏色");
        yield return new("Customized element: Text colour", "已自訂元素：文字顏色");
        yield return new("Customized element: Corner radius", "已自訂元素：圓角");
        yield return new("Customized element: Font size", "已自訂元素：字體大小");
        yield return new("Customized element: Font weight", "已自訂元素：字體粗幼");
        yield return new("Customized element: Padding", "已自訂元素：內距");
        yield return new("Customized element: Visibility", "已自訂元素：是否顯示");
        yield return new("Lock this element…", "鎖住呢個元素…");
        yield return new("Unlock this element…", "解鎖呢個元素…");
        yield return new("Lock every property…", "鎖住每個屬性…");
        yield return new("Each lock has its own policy and credentials.", "每個鎖都有自己嘅鎖定方式同憑證。");
        yield return new("Lock this colour role…", "鎖住呢個顏色角色…");
        yield return new("Unlock this colour role…", "解鎖呢個顏色角色…");
        yield return new("Lock every colour role…", "鎖住所有顏色角色…");
        yield return new("Lock this preset…", "鎖住呢個預設組合…");
        yield return new("Built-in presets cannot be changed, so they need no lock.", "內置預設組合本身唔可以更改，所以唔需要鎖。");
        // PIN keypad
        yield return new("Or use the keypad. The keyboard digits work too.", "或者用數字鍵盤。鍵盤上嘅數字都用得。");
        yield return new("PIN keypad", "PIN 碼數字鍵盤");
        yield return new("Shuffle the PIN keypad", "打亂 PIN 碼數字鍵盤");
        yield return new("Off by default. When on, the digits on the unlock keypad change places each time it opens.", "預設關閉。開啟後，解鎖數字鍵盤每次打開，數字都會轉位置。");
        yield return new("Shuffle the keys", "打亂按鍵");
        yield return new("Off by default. Shuffling moves the digits around so the pad cannot be read from finger positions.", "預設關閉。打亂數字位置之後，就唔可以靠手指位置估到按咗邊啲數字。");
        yield return new("Digit", "數字");
        yield return new("Backspace", "退格");
        yield return new("Backspace, delete the last digit", "退格，刪除最後一個數字");
        yield return new("Clear all digits", "清除所有數字");
        yield return new("Submit PIN", "提交 PIN 碼");
        yield return new("No digits entered", "未輸入任何數字");
        yield return new("1 digit entered", "已輸入 1 個數字");
        yield return new("digits entered", "個數字已輸入");
    }
}
