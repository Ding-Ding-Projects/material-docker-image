using System.Reflection;
using MaterialDockerImage.UI;
using Loc = MaterialDockerImage.Services.Loc;

/// <summary>
/// Checks for the last lock catalogue items: tab and tab group locks, appearance value locks, the rule behind clicking a locked control, locked labels in searches and the palette,
/// and the PIN keypad. Logic only: nothing here drives the built window.
/// </summary>
public static class LockExtraChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    public static int Run()
    {
        count = 0;
        Tabs();
        BulkClose();
        SearchAndPalette();
        AppearanceValues();
        ElementGuards();
        Keypad();
        Prompt();
        Wording();
        Console.WriteLine($"Lock extra checks: {count} passed.");
        return count;
    }

    static UserSettings Locked(params string[] ids)
    {
        var settings = new UserSettings();
        foreach (var id in ids) ElementLocks.Lock(settings, id);
        return settings;
    }

    // ----- Tab and tab group locks -------------------------------------------------------------------------------

    static void Tabs()
    {
        var names = new[] { "Build", "Creator", "Images", "Containers", "Settings", "A very long destination name that would overflow any id limit by quite a lot", "Étoile ünï", "" };
        var ids = names.Select(TabLocks.TabId).ToList();
        Check(ids.All(ElementLocks.IsValidId) && ids.All(i => i.Length <= 40), "every tab lock id fits the lock id pattern and 40 characters");
        Check(TabLocks.TabId("Build") == "tab.build" && TabLocks.TabId("Build") != TabLocks.TabId("Images"), "a tab lock id comes from its destination, so each tab has its own lock");
        var groupIds = TabLocks.Groups.Select(TabLocks.GroupId).ToList();
        Check(groupIds.Count == 3 && groupIds.Distinct().Count() == 3 && groupIds.All(ElementLocks.IsValidId) && groupIds.All(g => !ids.Contains(g)), "the pinned, open and rail groups each have their own lock id, apart from every tab's");

        var none = new UserSettings();
        var every = new[] { TabAction.Activate, TabAction.Close, TabAction.Move, TabAction.Pin };
        Check(TabLocks.Groups.All(g => every.All(a => TabLocks.Blocker(none, "Build", g, a) == null)), "with no lock set every tab action is allowed");

        var tabLocked = Locked(TabLocks.TabId("Images"));
        Check(TabLocks.Groups.All(g => every.All(a => TabLocks.Blocker(tabLocked, "Images", g, a)?.Id == "tab.images")), "a locked tab refuses switching to it, closing, moving and pinning in any group");
        Check(every.All(a => TabLocks.Blocker(tabLocked, "Build", TabGroup.Open, a) == null), "locking one tab leaves every other tab alone");

        var groupLocked = Locked(TabLocks.GroupId(TabGroup.Open));
        Check(new[] { TabAction.Close, TabAction.Move, TabAction.Pin }.All(a => TabLocks.Blocker(groupLocked, "Build", TabGroup.Open, a) is { IsGroup: true, Id: "tabgroup.open" }), "a locked tab group refuses closing, moving and pinning for its members");
        Check(TabLocks.Blocker(groupLocked, "Build", TabGroup.Open, TabAction.Activate) == null, "a locked tab group still lets its members be switched to");
        Check(!TabLocks.IsTabLocked(groupLocked, "Build") && every.All(a => TabLocks.Blocker(groupLocked, "Build", TabGroup.Pinned, a) == null), "locking a group never locks its members or another group");

        var railLocked = Locked(TabLocks.GroupId(TabGroup.Rail));
        Check(TabLocks.NavigationBlocker(railLocked, "Build", true)?.Id == "tabgroup.rail" && TabLocks.NavigationBlocker(railLocked, "Build", false) == null, "a locked navigation rail refuses rail clicks but not other ways of opening a tab");
        Check(TabLocks.NavigationBlocker(tabLocked, "Images", false)?.Id == "tab.images" && TabLocks.NavigationBlocker(tabLocked, "Images", true)?.Id == "tab.images", "a locked destination is refused from the rail, a shortcut and the palette alike");

        var both = Locked(TabLocks.TabId("Images"), TabLocks.GroupId(TabGroup.Open));
        Check(TabLocks.Blocker(both, "Images", TabGroup.Open, TabAction.Close)?.Id == "tab.images", "a locked tab inside a locked group answers to its own lock first");
        ElementLocks.Unlock(both, "tab.images");
        Check(TabLocks.Blocker(both, "Images", TabGroup.Open, TabAction.Close)?.Id == "tabgroup.open", "unlocking the tab leaves the group lock standing: two locks, two separate answers");
        Check(TabLocks.GroupOf(true, false) == TabGroup.Pinned && TabLocks.GroupOf(false, true) == TabGroup.Pinned && TabLocks.GroupOf(false, false) == TabGroup.Open, "the workspace tab and pinned tabs share the pinned region");
    }

    static void BulkClose()
    {
        var tabs = new (string, TabGroup)[] { ("Build", TabGroup.Pinned), ("Images", TabGroup.Open), ("Containers", TabGroup.Open), ("Storage", TabGroup.Open) };
        var settings = Locked(TabLocks.TabId("Images"));
        Check(TabLocks.BulkCloseProtected(settings, tabs, false).SetEquals(new[] { 1 }), "a locked tab is excluded from bulk closes by default");
        Check(TabLocks.BulkCloseProtected(settings, tabs, true).Count == 0, "including locked tabs explicitly removes the exclusion");
        var groupSettings = Locked(TabLocks.GroupId(TabGroup.Open));
        Check(TabLocks.BulkCloseProtected(groupSettings, tabs, false).SetEquals(new[] { 1, 2, 3 }), "tabs in a locked group are excluded from bulk closes too");

        var query = new RegexSearchQuery("i", false, new RegexBuilderFlags());
        var candidates = tabs.Select((t, i) => new TabCloseCandidate(i, t.Item1, false, false, false)).ToList();
        var marked = TabLocks.MarkLocked(settings, candidates, tabs, false);
        var result = TabCloseSelector.Select(marked, query, true);
        Check(result.ToClose.All(c => c.Label != "Images") && result.Kept.Any(k => k.Tab.Label == "Images" && k.Reason == "locked"), "the bulk-close preview keeps a locked tab open and says why");
        Check(result.ToClose.Select(c => c.Label).Contains("Containers"), "unlocked matches still close");
        var included = TabCloseSelector.Select(TabLocks.MarkLocked(settings, candidates, tabs, true), query, true, false, true);
        Check(included.ToClose.Any(c => c.Label == "Images"), "an explicit include puts the locked tab in the preview");
    }

    // ----- Locked labels in searches and the palette ------------------------------------------------------------------

    static void SearchAndPalette()
    {
        Check(LockedResults.SearchText("Images tab", true).Contains("Locked") && LockedResults.SearchText("Images tab", false) == "Images tab", "typing the word Locked finds locked items, and unlocked items are not matched by it");
        Check(LockedResults.Label("Images", true) == "Images · Locked" && LockedResults.Label("Images", false) == "Images", "a locked result is labelled as locked in its visible name");
        var items = new[] { "a", "b", "c", "d" };
        var shown = LockedResults.Apply(items, i => i is "b" or "d", false, out var none);
        Check(shown.Count == 4 && none == 0, "locked items stay in the results by default");
        var hiddenList = LockedResults.Apply(items, i => i is "b" or "d", true, out var hidden);
        Check(hiddenList.SequenceEqual(new[] { "a", "c" }) && hidden == 2, "hiding locked items leaves the others and counts what it hid");
        Check(LockedResults.HiddenStatement(0) == "" && LockedResults.HiddenStatement(2).StartsWith("2 ", StringComparison.Ordinal) && LockedResults.HiddenStatement(2).Contains("Hide locked items in searches"), "a list that was shortened says how many it hid and how to see them");
        Check(!new UserSettings().HideLockedInSearch, "hiding locked items is off by default");

        Check(PaletteLocks.LockIdFor("go.Images") == TabLocks.TabId("Images") && PaletteLocks.LockIdFor("go.") == null, "a go-to command answers to its destination's tab lock");
        Check(PaletteLocks.LockIdFor("set.theme") == "theme" && PaletteLocks.LockIdFor("adhd.focus") == "focusmode" && PaletteLocks.LockIdFor("palette") == null, "settings commands answer to their settings row, other commands to none");
        Check(PaletteLocks.SettingCommandIds.All(c => ElementLocks.Properties.ContainsKey(PaletteLocks.LockIdFor(c)!)), "every guarded palette setting names a real lockable settings row");

        var settings = Locked(TabLocks.TabId("Images"), TabLocks.GroupId(TabGroup.Pinned), AppearanceLocks.RoleId("Primary"), "language");
        Check(LockManager.LabelOf(settings, "tabgroup.pinned") == "Tab group: Pinned tabs" && LockManager.LabelOf(settings, "tab.images") == "Tab: images", "the Locks manager names tab and tab group locks");
        Check(LockManager.LabelOf(settings, AppearanceLocks.RoleId("Primary")) == "Colour role: Primary" && LockManager.LabelOf(settings, "theme") == "Appearance: Theme", "the Locks manager names appearance locks");
        var row = new LockRow("tab.images", "Tab: images", LockPolicy.Pin, UnlockDuration.UntilRestart, true, null, 8, true);
        Check(LockManager.SearchText(row).Contains("Locked"), "the Locks manager search labels a locked tab as locked");
    }

    // ----- Appearance value locks ------------------------------------------------------------------------------------

    static void AppearanceValues()
    {
        var members = typeof(AppearanceState).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
        var covered = AppearanceLocks.Values.Select(v => v.StateMember).ToList();
        Check(members.OrderBy(x => x).SequenceEqual(covered.OrderBy(x => x)), "every appearance value of the editor has a lock and none is listed twice");
        Check(AppearanceLocks.Values.All(v => ElementLocks.IsValidId(v.Id) && ElementLocks.Properties.ContainsKey(v.Id)) && AppearanceLocks.Values.Select(v => v.Id).Distinct().Count() == AppearanceLocks.Values.Count, "each appearance value lock is a real settings-row lock with its own id");

        var styleProps = typeof(ElementStyle).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite).Select(p => p.Name).ToList();
        Check(styleProps.OrderBy(x => x).SequenceEqual(AppearanceLocks.ElementProperties.Select(p => p.Property).OrderBy(x => x)), "every property an element can override can be locked on its own");

        var roleIds = AppearanceRoles.All.Select(r => AppearanceLocks.RoleId(r.Key)).ToList();
        Check(roleIds.Count == AppearanceRoles.All.Count && roleIds.Distinct().Count() == roleIds.Count && roleIds.All(ElementLocks.IsValidId), "every colour role has its own valid lock id");
        Check(AppearanceLocks.LabelFor(roleIds[0]) == "Colour role: Primary" && AppearanceLocks.LabelFor("language") == null, "colour role locks are named and other ids are not claimed");

        var presetA = AppearanceLocks.PresetId("My look"); var presetB = AppearanceLocks.PresetId("my-look");
        Check(ElementLocks.IsValidId(presetA) && presetA != presetB && presetA == AppearanceLocks.PresetId("My look"), "a preset lock id is valid, stable and tells apart names that slug alike");
        Check(AppearanceLocks.PresetBlocker(Locked(presetA), "My look") != null && AppearanceLocks.PresetBlocker(Locked(presetA), "Other") == null && AppearanceLocks.PresetBlocker(new UserSettings(), "My look") == null, "a preset lock guards exactly its own preset");

        // A locked global value keeps its value; everything else in the same change goes through.
        var before = new AppearanceState();
        var proposed = before.Clone(); proposed.Theme = "Light"; proposed.FontSize = 18; proposed.Density = "Compact";
        var open = AppearanceLocks.GuardState(new UserSettings(), before, proposed, out var heldNone);
        Check(open.Theme == "Light" && open.FontSize == 18 && heldNone.Count == 0, "with no lock the appearance change goes through whole");
        var themeLocked = AppearanceLocks.GuardState(Locked("theme"), before, proposed, out var held);
        Check(themeLocked.Theme == before.Theme && themeLocked.FontSize == 18 && themeLocked.Density == "Compact" && held.SequenceEqual(new[] { "theme" }), "a locked theme keeps its value while the other values change");
        var fontLocked = AppearanceLocks.GuardState(Locked("fontsize", "density"), before, proposed, out var held2);
        Check(fontLocked.FontSize == before.FontSize && fontLocked.Density == before.Density && fontLocked.Theme == "Light" && held2.Count == 2, "several locked values each keep their value, separately");
        Check(AppearanceLocks.Values.Where(v => v.Id != "roles").All(v =>
        {
            var one = before.Clone(); var other = before.Clone();
            var property = typeof(AppearanceState).GetProperty(v.StateMember)!;
            property.SetValue(other, property.PropertyType switch { var t when t == typeof(string) => (object)"changed", var t when t == typeof(double) => 7.5, var t when t == typeof(int) => 900, _ => !(bool)property.GetValue(before)! });
            var guarded = AppearanceLocks.GuardState(Locked(v.Id), one, other, out var h);
            return Equals(property.GetValue(guarded), property.GetValue(one)) && h.Contains(v.Id);
        }), "every appearance value type is held back by its own lock");

        // Colour roles: one role, or the whole row.
        var roleBefore = before.Clone(); roleBefore.Roles["Surface"] = "#101010";
        var roleProposed = roleBefore.Clone(); roleProposed.Roles["Primary"] = "#FF0000"; roleProposed.Roles["Surface"] = "#202020"; roleProposed.Roles.Remove("Outline");
        var primaryLocked = AppearanceLocks.GuardState(Locked(AppearanceLocks.RoleId("Primary")), roleBefore, roleProposed, out var heldRoles);
        Check(!primaryLocked.Roles.ContainsKey("Primary") && primaryLocked.Roles["Surface"] == "#202020" && heldRoles.SequenceEqual(new[] { AppearanceLocks.RoleId("Primary") }), "a locked colour role stays as it was (even absent) while other roles change");
        var surfaceLocked = AppearanceLocks.GuardState(Locked(AppearanceLocks.RoleId("Surface")), roleBefore, roleProposed, out _);
        Check(surfaceLocked.Roles["Surface"] == "#101010" && surfaceLocked.Roles["Primary"] == "#FF0000", "a locked colour role keeps its stored value");
        var allRolesLocked = AppearanceLocks.GuardState(Locked("roles"), roleBefore, roleProposed, out var heldAll);
        Check(allRolesLocked.Roles.Count == 1 && allRolesLocked.Roles["Surface"] == "#101010" && heldAll.Count >= 2, "the whole colour roles row locked refuses every role change");

        var current = new UserSettings(); current.RoleOverrides["Primary"] = "#00FF00"; ElementLocks.Lock(current, AppearanceLocks.RoleId("Primary")); ElementLocks.Lock(current, AppearanceLocks.RoleId("Error"));
        var target = new UserSettings(); target.RoleOverrides["Primary"] = "#123456"; target.RoleOverrides["Error"] = "#654321"; target.RoleOverrides["Outline"] = "#999999";
        var kept = ElementLocks.Preserve(current, target);
        Check(target.RoleOverrides["Primary"] == "#00FF00" && !target.RoleOverrides.ContainsKey("Error") && target.RoleOverrides["Outline"] == "#999999" && kept.Contains("RoleOverrides.Primary"), "an import or reset keeps locked colour roles and changes the rest");
        Check(ElementLocks.Snapshot(current, AppearanceLocks.RoleId("Primary")) == "#00FF00", "an unlock for one action can tell when a colour role changed");

        var bulk = AppearanceLocks.PlanBulk(Locked("theme"), new[] { "theme", "seed", "seed", "BAD ID", "tint" });
        Check(bulk.ToLock.SequenceEqual(new[] { "seed", "tint" }) && bulk.Skipped.Count == 2, "a bulk lock plan skips what is locked or invalid and lists each lock once");
        var full = new UserSettings(); for (var i = 0; i < ElementLocks.MaxLocks; i++) ElementLocks.Lock(full, "x" + i);
        Check(AppearanceLocks.PlanBulk(full, new[] { "seed" }).Skipped.Single().Reason.Contains("full"), "a bulk lock plan stops when the lock list is full");
        Check(AppearanceLocks.AllRoleIds().Count == AppearanceRoles.All.Count, "the bulk colour role command covers every role");
    }

    static void ElementGuards()
    {
        const string key = "Window/Grid[0]/Button[2]";
        var existing = new ElementStyle { Background = "#112233", FontSize = 14 };
        var proposed = new ElementStyle { Background = "#445566", FontSize = 20, CornerRadius = 8 };
        var open = AppearanceLocks.Guard(new UserSettings(), key, existing, proposed, out var held0);
        Check(open.Background == "#445566" && open.FontSize == 20 && open.CornerRadius == 8 && held0.Count == 0, "an element with no lock accepts every change");

        var bgLocked = Locked(AppearanceLocks.ElementPropertyId(key, nameof(ElementStyle.Background)));
        var guarded = AppearanceLocks.Guard(bgLocked, key, existing, proposed, out var held);
        Check(guarded.Background == "#112233" && guarded.FontSize == 20 && guarded.CornerRadius == 8 && held.SequenceEqual(new[] { nameof(ElementStyle.Background) }), "a locked element property keeps its value while the others change");
        Check(AppearanceLocks.Guard(bgLocked, "Window/Other[0]", existing, proposed, out _).Background == "#445566", "a property lock on one element does not touch another element");

        var wholeLocked = Locked(AppearanceLocks.ElementId(key));
        var whole = AppearanceLocks.Guard(wholeLocked, key, existing, proposed, out var heldWhole);
        Check(whole.Background == "#112233" && whole.FontSize == 14 && whole.CornerRadius == null && heldWhole.Count == 3, "a locked element refuses every property change");

        var reset = AppearanceLocks.Guard(bgLocked, key, existing, new ElementStyle(), out var heldReset);
        Check(reset.Background == "#112233" && reset.FontSize == null && heldReset.Count == 1, "a reset cannot remove a locked property");
        var hideLocked = Locked(AppearanceLocks.ElementPropertyId(key, nameof(ElementStyle.Hidden)));
        Check(AppearanceLocks.Guard(hideLocked, key, existing, new ElementStyle { Hidden = true }, out _).Hidden == false, "a locked visibility stays as it was");

        var global = Locked("fontsize");
        Check(AppearanceLocks.Guard(global, key, existing, proposed, out var heldGlobal).FontSize == 14 && heldGlobal.Contains(nameof(ElementStyle.FontSize)), "the existing appearance row locks still hold an element's matching property");
        Check(AppearanceLocks.ElementPropertyId(key, nameof(ElementStyle.Background)) != AppearanceLocks.ElementPropertyId(key, nameof(ElementStyle.Foreground)) && AppearanceLocks.ElementPropertyId(key, "FontSize") != AppearanceLocks.ElementPropertyId("Window/Other[0]", "FontSize") && AppearanceLocks.ElementLockIds(key, "Background").All(ElementLocks.IsValidId), "each property of each element has its own valid lock id");
        Check(AppearanceLocks.ElementPropertyBlocker(bgLocked, key, nameof(ElementStyle.Background)) != null && AppearanceLocks.ElementPropertyBlocker(bgLocked, key, nameof(ElementStyle.Padding)) == null, "the blocker names the lock that refuses a property");
        Check(AppearanceLocks.LabelFor(AppearanceLocks.ElementPropertyId(key, nameof(ElementStyle.Background))) == "Customized element: Background colour" && AppearanceLocks.LabelFor(AppearanceLocks.ElementId(key)) == "Customized element (all properties)", "element locks are named in the Locks manager");
    }

    // ----- PIN keypad -------------------------------------------------------------------------------------------------------

    sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plain, byte[] entropy) => plain.ToArray();
        public byte[]? Unprotect(byte[] protectedBytes, byte[] entropy) => protectedBytes.ToArray();
    }

    static void Keypad()
    {
        var layout = PinKeypad.Layout(false);
        var flat = layout.SelectMany(r => r).ToList();
        Check(layout.Count == 4 && layout.All(r => r.Count == 3), "the keypad is four rows of three keys");
        Check(string.Concat(layout.Take(3).SelectMany(r => r).Select(k => k.Digit)) == "123456789" && layout[3][0].Kind == KeypadKind.Clear && layout[3][1].Digit == '0' && layout[3][2].Kind == KeypadKind.Backspace, "the standard layout is 1-9 then Clear, 0, Backspace");
        var all = PinKeypad.AllKeys(false);
        Check(all.Count(k => k.Kind == KeypadKind.Digit) == 10 && all.Any(k => k.Kind == KeypadKind.Cancel) && all.Any(k => k.Kind == KeypadKind.Submit) && all.Any(k => k.Kind == KeypadKind.Backspace) && all.Any(k => k.Kind == KeypadKind.Clear), "the pad offers every digit, Backspace, Clear, Cancel and Submit");
        Check(PinKeypad.Layout(false).SelectMany(r => r).Select(k => k.Digit).SequenceEqual(flat.Select(k => k.Digit)), "the standard layout is the same every time");
        var shuffled = PinKeypad.Layout(true, new Random(7)).SelectMany(r => r).ToList();
        Check(shuffled.Where(k => k.Kind == KeypadKind.Digit).Select(k => k.Digit).OrderBy(c => c).SequenceEqual("0123456789") && shuffled[9].Kind == KeypadKind.Clear && shuffled[11].Kind == KeypadKind.Backspace, "a shuffled pad still has each digit exactly once, with Clear and Backspace where hands expect them");
        Check(Enumerable.Range(0, 20).Any(seed => !PinKeypad.Layout(true, new Random(seed)).SelectMany(r => r).Select(k => k.Digit).SequenceEqual(flat.Select(k => k.Digit))), "shuffling really moves the digits");
        Check(!new UserSettings().PinKeypadShuffle, "the keypad is not shuffled unless the person chooses it");

        var pad = new PinKeypad();
        Check(!pad.Press('x') && !pad.Press(' ') && !pad.Press('٣') && pad.Length == 0, "only ASCII digits are accepted");
        foreach (var d in "739285") pad.Press(d);
        Check(pad.Length == 6 && pad.Masked == "••••••" && !pad.Masked.Any(char.IsDigit), "the display shows dots, never digits");
        Check(pad.Announcement == "6 digits entered" && !pad.Announcement.Contains('7'), "a screen reader hears how many digits, never which");
        Check(pad.Backspace() && pad.Length == 5 && pad.EntryForField() == "73928", "Backspace removes the last digit");
        pad.Clear();
        Check(pad.Length == 0 && !pad.Backspace() && pad.Announcement == "No digits entered", "Clear empties the entry and Backspace on nothing does nothing");
        for (var i = 0; i < 30; i++) pad.Press('1');
        Check(pad.Length == PinRules.MaxLength, "the entry is bounded at the PIN maximum length");
        pad.Clear();
        Check(pad.PressKeyboardText("12a3") && pad.EntryForField() == "123" && !pad.CanSubmit, "keyboard digits fill the same entry and anything else is ignored");
        pad.SetFromManual("55x5 5");
        Check(pad.EntryForField() == "5555" && pad.CanSubmit, "typing in the field and the pad always agree");
        Check(PinKeypad.AccessibleName(new KeypadKey(KeypadKind.Digit, '4', "4")) == "Digit 4" && !PinKeypad.AccessibleName(new KeypadKey(KeypadKind.Submit, '\0', "Submit")).Any(char.IsDigit), "key names are the digit or the action and reveal nothing entered");

        // One validator and one attempt budget for typing and the pad.
        var store = new LockStore(null, new XorProtector(), () => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), 10_000);
        store.Configure("pad.pin", LockPolicy.Pin, "135790");
        var typedWrong = store.Begin("pad.pin")!.Submit("000000");
        var padAttempt = store.Begin("pad.pin")!;
        pad.Clear(); foreach (var d in "000000") pad.Press(d);
        var padWrong = pad.Submit(padAttempt);
        Check(typedWrong.Outcome == UnlockOutcome.Wrong && padWrong.Outcome == UnlockOutcome.Wrong && padWrong.FreeAttemptsLeft == typedWrong.FreeAttemptsLeft - 1, "a wrong PIN on the pad spends the same attempt budget as a wrong typed PIN");
        Check(pad.Length == 0, "the pad clears itself after submitting");
        foreach (var d in "135790") pad.Press(d);
        var rightTry = pad.Submit(store.Begin("pad.pin")!);
        Check(rightTry.Unlocked, "the right PIN on the pad unlocks through the same validator");
        Check(new PinKeypad().Submit(store.Begin("pad.pin")!).Outcome == UnlockOutcome.Wrong, "an empty submit is an ordinary wrong answer, not a way around the budget");
        var typedRight = store.Begin("pad.pin")!.Submit("135790");
        Check(typedRight.Unlocked, "typing the right PIN still works beside the pad");

        // Locked out: the pad is refused exactly as typing is.
        var strict = new LockStore(null, new XorProtector(), () => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), 10_000);
        strict.Configure("strict.pin", LockPolicy.Pin, "246810");
        for (var i = 0; i < 12 && strict.WaitUntil("strict.pin") == null; i++) { pad.Clear(); pad.Press('9'); pad.Press('9'); pad.Press('9'); pad.Press('9'); pad.Submit(strict.Begin("strict.pin")!); }
        pad.Clear(); foreach (var d in "246810") pad.Press(d);
        Check(strict.WaitUntil("strict.pin") != null && pad.Submit(strict.Begin("strict.pin")!).Outcome == UnlockOutcome.Waiting, "during a wait even the right PIN on the pad does not unlock");
    }

    // ----- Clicking a locked control ------------------------------------------------------------------------------------------

    static void Prompt()
    {
        Check(UnlockPromptGate.Decide(true, false) == DisabledClickDecision.OpenPrompt, "a click on a locked control opens its unlock prompt");
        Check(UnlockPromptGate.Decide(true, true) == DisabledClickDecision.Swallow, "a second click while the prompt is open is swallowed");
        Check(UnlockPromptGate.Decide(false, false) == DisabledClickDecision.PassThrough && UnlockPromptGate.Decide(false, true) == DisabledClickDecision.PassThrough, "an unlocked control keeps its ordinary click");
        Check(new[] { false, true }.All(open => UnlockPromptGate.Decide(true, open) != DisabledClickDecision.PassThrough), "a locked control never lets a click through to its protected action");
        var gate = new UnlockPromptGate();
        Check(gate.TryBegin("a") && !gate.TryBegin("a") && gate.TryBegin("b") && gate.Click("a", true) == DisabledClickDecision.Swallow && gate.Click("c", true) == DisabledClickDecision.OpenPrompt, "each lock has at most one prompt open and another lock's prompt is independent");
        gate.End("a");
        Check(gate.Click("a", true) == DisabledClickDecision.OpenPrompt && !gate.IsOpen("a"), "closing the prompt lets the next click open it again");
    }

    // ----- Cantonese wording -------------------------------------------------------------------------------------------------------

    static bool HasCjk(string text) => text.Any(c => c >= 0x4E00 && c <= 0x9FFF);

    static void Wording()
    {
        var language = Loc.Language; var school = Loc.SchoolMode;
        Loc.SchoolMode = false; Loc.Language = "Cantonese";
        var samples = new List<string>(UiStrings);
        samples.AddRange(AppearanceLocks.Values.Select(v => AppearanceLocks.LabelFor(v.Id)!));
        samples.AddRange(AppearanceLocks.ElementProperties.Select(p => AppearanceLocks.LabelFor(AppearanceLocks.ElementPropertyId("k", p.Property))!));
        samples.AddRange(TabLocks.Groups.Select(TabLocks.GroupLabel));
        samples.Add(AppearanceLocks.LabelFor(AppearanceLocks.ElementId("k"))!);
        samples.Add(LockedResults.HiddenSuffix); samples.Add(LockedResults.Marker);
        var missing = samples.Distinct().Where(x => Loc.T(x) == x).ToList();
        foreach (var m in missing) Console.WriteLine("MISSING Cantonese: " + m);
        Check(missing.Count == 0, "every new tab, group, appearance, search and keypad string has Cantonese wording");
        Check(samples.Distinct().All(x => HasCjk(Loc.T(x))), "the Cantonese wording is real Chinese text");
        Loc.Language = "Bilingual";
        Check(Loc.T("Shuffle the keys").StartsWith("Shuffle the keys · ", StringComparison.Ordinal), "bilingual mode keeps the English sentence first");
        Loc.Language = language; Loc.SchoolMode = school;
    }

    /// <summary>Strings used directly by the tab, appearance, palette and keypad surfaces. Kept here so a missing translation turns the check red.</summary>
    static readonly string[] UiStrings =
    [
        "Locked. Click it to unlock.", "Locked. Click the switch or right-click it to unlock it.", "Locked. Select it to unlock this tab.", "locked", "Unlock…",
        "Lock this tab…", "Unlock this tab…", "Lock this tab group…", "Unlock this tab group…", "Lock this destination…", "Unlock this destination…", "Lock the navigation rail…", "Unlock the navigation rail…",
        "Lock:", "Unlock:", "Include locked tabs", "Off by default. Locked tabs still ask for their unlock before they close.", "locked tab(s) stayed open.",
        "Hide locked items in searches", "Off by default: locked tabs, settings and commands stay listed and are labelled Locked. When on, they are left out, and every list says how many it hid.",
        "Locked appearance values were kept as they were:", "Unlock them to change them.", "Locked properties kept their values. Unlock them to change them.", "Some elements keep locked properties. Unlock them to reset them.",
        "Lock this element…", "Unlock this element…", "Lock every property…", "Each lock has its own policy and credentials.", "Lock this colour role…", "Unlock this colour role…", "Lock every colour role…", "Lock this preset…",
        "Built-in presets cannot be changed, so they need no lock.",
        "Or use the keypad. The keyboard digits work too.", "PIN keypad", "Shuffle the PIN keypad", "Off by default. When on, the digits on the unlock keypad change places each time it opens.", "Shuffle the keys",
        "Off by default. Shuffling moves the digits around so the pad cannot be read from finger positions.", "Digit", "Backspace", "Clear", "Backspace, delete the last digit", "Clear all digits", "Submit PIN",
        "No digits entered", "1 digit entered", "digits entered"
    ];
}
