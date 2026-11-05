using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Editor for a RUN step's --mount list. A "Mounts" header with an Add mount menu (types and cache presets), then one
/// compact tonal row per mount: type, target, the key option (id, source or size), the remaining options, an options
/// toggle and remove. The toggle opens every other field of that type plus move buttons. Edits write straight into the
/// list and call <c>changed</c>.
/// </summary>
public sealed class CreatorMountEditor : StackPanel
{
    private readonly List<RunMount> mounts;
    private readonly Action changed;
    private readonly StackPanel rows = new();
    /// <summary>Mounts whose option fields are open; kept across rebuilds of the rows.</summary>
    private readonly HashSet<RunMount> open = new();

    private static readonly Dictionary<string, string> TypeHelp = new()
    {
        [RunMounts.Cache] = "Keeps a directory between builds without adding it to the image. Ideal for package manager caches.",
        [RunMounts.Bind] = "Makes files from the build context, a stage or an image visible to this step only (read-only unless Read-write is on).",
        [RunMounts.Tmpfs] = "An in-memory scratch directory that disappears after the step.",
        [RunMounts.Secret] = "A secret file or environment variable passed with docker buildx build --secret. It is never stored in a layer.",
        [RunMounts.Ssh] = "Forwards your SSH agent (docker buildx build --ssh default) so the step can clone private repositories.",
    };

    public CreatorMountEditor(List<RunMount> mounts, Action changed)
    {
        this.mounts = mounts; this.changed = changed;
        var add = Ui.Button("Add mount", Icons.Add, ButtonKind.Text, null);
        add.Height = add.MinHeight = 28; add.Padding = new Thickness(4, 0, 6, 0); add.FontSize = 13;
        add.ToolTip = Loc.T("Add a cache, bind, tmpfs, secret or ssh mount, or the cache mounts of a package manager.");
        var menu = new ContextMenu();
        foreach (var type in RunMounts.Types)
        {
            var item = Ui.MenuItem("Add " + type + " mount", Icons.Add, () => { var mount = RunMounts.Create(type); mounts.Add(mount); Rebuild(); changed(); });
            item.ToolTip = Loc.T(TypeHelp[type]);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var presets = new MenuItem { Header = Loc.T("Cache preset…") };
        presets.ToolTip = Loc.T("Adds the cache mounts for a package manager with its default cache paths.");
        foreach (var preset in RunMounts.CachePresets)
        {
            var name = preset.Name;
            presets.Items.Add(Ui.MenuItem(name, null, () => AddPreset(name)));
        }
        menu.Items.Add(presets);
        add.Click += (_, _) => { menu.PlacementTarget = add; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; };
        System.Windows.Automation.AutomationProperties.SetName(add, Loc.T("Add mount"));
        var label = Ui.Wrap(0, Ui.Overline("Mounts"), Ui.InfoButton("Mounts",
            "Filesystems available to this step only (--mount). cache keeps package downloads between builds, bind reads files from the context or another stage without copying them, tmpfs gives scratch space, secret and ssh pass credentials that never enter the image.",
            "type=cache,target=/root/.npm\ntype=bind,source=package.json,target=package.json\ntype=secret,id=npmrc,target=/root/.npmrc"));
        label.VerticalAlignment = VerticalAlignment.Center;
        var head = Ui.Columns(8, [1, 0], label, add);
        head.Margin = new Thickness(0, 0, 0, 6);
        Children.Add(head);
        Children.Add(rows);
        presetNote.Visibility = Visibility.Collapsed;
        presetNote.Margin = new Thickness(0, 6, 0, 0);
        Children.Add(presetNote);
        Rebuild();
    }

    private readonly TextBlock presetNote = Ui.Caption("");

    private void AddPreset(string name)
    {
        var added = RunMounts.AddPreset(mounts, name);
        var note = RunMounts.CachePresets.First(p => p.Name == name).Note;
        if (added > 0) { Rebuild(); changed(); }
        presetNote.Text = (added == 0 ? Loc.T("Those cache mounts are already present.") : $"{Loc.T("Added")} {added} {Loc.T(added == 1 ? "cache mount" : "cache mounts")} {Loc.T("for")} {name}.") + (note.Length > 0 ? " " + note : "");
        presetNote.Visibility = Visibility.Visible;
    }

    private void Rebuild()
    {
        rows.Children.Clear();
        open.RemoveWhere(m => !mounts.Contains(m));
        if (mounts.Count == 0) rows.Children.Add(Ui.Caption("No mounts. Add a cache mount for package managers, or a secret mount for credentials.").Margin(0, 0, 0, 6));
        for (var k = 0; k < mounts.Count; k++) rows.Children.Add(Row(k));
    }

    private void Move(int index, int delta)
    {
        var to = index + delta;
        if (to < 0 || to >= mounts.Count) return;
        (mounts[index], mounts[to]) = (mounts[to], mounts[index]);
        Rebuild(); changed();
    }

    private static TextBox Small(TextBox box)
    {
        box.Height = box.MinHeight = 32;
        box.FontSize = 12;
        box.Padding = new Thickness(8, 0, 8, 0);
        box.VerticalContentAlignment = VerticalAlignment.Center;
        return box;
    }

    private UIElement Row(int index)
    {
        var mount = mounts[index];
        var type = Ui.Combo(RunMounts.Types, RunMounts.Types.Contains(mount.Type) ? mount.Type : RunMounts.Cache);
        type.Width = 96; type.Height = type.MinHeight = 32; type.FontSize = 12;
        System.Windows.Automation.AutomationProperties.SetName(type, $"{Loc.T("Mount type")} {index + 1}");
        type.ToolTip = Loc.T(TypeHelp.TryGetValue(mount.Type, out var typeHelp) ? typeHelp : "");
        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedItem is not string value || value == mount.Type) return;
            var replacement = RunMounts.Create(value);
            if (open.Remove(mount)) open.Add(replacement);
            mounts[index] = replacement;
            Rebuild(); changed();
        };
        var problems = Ui.Text("", 12, null, "Error");
        var others = Small(Ui.Input(DockerfileOutline.MountOtherOptions(mount), mount.Type + " " + Loc.T("mount options"), true));
        others.IsReadOnly = true;
        others.ToolTip = Loc.T("Other options of this mount. Open the options to change them.");
        void Touch()
        {
            others.Text = DockerfileOutline.MountOtherOptions(mount);
            var list = RunMounts.Validate(mount);
            problems.Text = string.Join(Environment.NewLine, list);
            problems.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            changed();
        }
        TextBox Box(string value, string label, Action<string> set)
        {
            var box = Ui.Input(value, label, true);
            box.TextChanged += (_, _) => { set(box.Text); Touch(); };
            return box;
        }
        FrameworkElement F(string label, string value, Action<string> set, string info, string example) => Ui.Field(label, Box(value, label, set), info, example);
        CheckBox S(string label, bool value, Action<bool> set, string description) => Ui.Switch(label, value, v => { set(v); Touch(); }, description);

        // The compact row: target and the key option are edited in place.
        var (targetHint, keyHint) = mount.Type switch
        {
            RunMounts.Secret => ("Target file", "Secret id"),
            RunMounts.Ssh => ("Socket path", "SSH id"),
            RunMounts.Bind => ("Target", "Source"),
            RunMounts.Tmpfs => ("Target", "Size"),
            _ => ("Target", "Cache id"),
        };
        var target = Small(Box(mount.Target, mount.Type + " " + Loc.T("mount target"), v => mount.Target = v));
        target.ToolTip = Loc.T(targetHint);
        var keyOption = DockerfileOutline.MountKeyOption(mount.Type);
        var key = Small(Box(keyOption switch { "source" => mount.Source, "size" => mount.Size, _ => mount.Id }, mount.Type + " " + Loc.T("mount") + " " + keyOption,
            v => { if (keyOption == "source") mount.Source = v; else if (keyOption == "size") mount.Size = v; else mount.Id = v; }));
        key.ToolTip = Loc.T(keyHint);

        var details = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var isOpen = open.Contains(mount);
        details.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        var toggle = Ui.IconButton(Icons.Tune, isOpen ? "Hide mount options" : "Show mount options");
        var remove = Ui.IconButton(Icons.Delete, "Remove mount", () => { mounts.RemoveAt(index); open.Remove(mount); Rebuild(); changed(); });
        foreach (var b in new[] { toggle, remove }) { b.Width = b.Height = b.MinHeight = 28; b.FontSize = 16; }
        toggle.Click += (_, _) =>
        {
            var show = details.Visibility != Visibility.Visible;
            details.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) open.Add(mount); else open.Remove(mount);
            toggle.ToolTip = Loc.T(show ? "Hide mount options" : "Show mount options");
            System.Windows.Automation.AutomationProperties.SetName(toggle, Loc.T(show ? "Hide mount options" : "Show mount options"));
        };
        var top = new Grid();
        foreach (var width in new[] { GridLength.Auto, new GridLength(1.3, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        top.ColumnDefinitions[1].MinWidth = 60; top.ColumnDefinitions[2].MinWidth = 50; top.ColumnDefinitions[3].MinWidth = 50;
        UIElement[] cells = [type, target, key, others, toggle, remove];
        for (var c = 0; c < cells.Length; c++)
        {
            if (cells[c] is FrameworkElement fe) { fe.Margin = new Thickness(c == 0 ? 0 : 6, 0, 0, 0); fe.VerticalAlignment = VerticalAlignment.Center; }
            top.Children.Add(cells[c].At(0, c));
        }

        var fields = new List<FrameworkElement>();
        switch (mount.Type)
        {
            case RunMounts.Cache:
                var sharing = Ui.Combo(RunMounts.SharingModes.Select(m => m.Length == 0 ? "(default: shared)" : m), mount.Sharing.Length == 0 ? "(default: shared)" : mount.Sharing);
                sharing.SelectionChanged += (_, _) => { mount.Sharing = sharing.SelectedItem is string s && !s.StartsWith('(') ? s : ""; Touch(); };
                fields.Add(Ui.Columns(10, [1, 1], Ui.Field("Sharing", sharing, "shared lets parallel builds use the cache together, private gives each writer its own copy, locked makes builds wait (apt needs locked).", "locked"),
                    F("From", mount.From, v => mount.From = v, "Stage, context or image whose content seeds the cache. Empty starts with an empty directory.", "deps")));
                fields.Add(Ui.Columns(10, [1, 1, 1, 1], F("Source", mount.Source, v => mount.Source = v, "Subpath of From to mount.", "/cache"), F("Mode", mount.Mode, v => mount.Mode = v, "Octal mode for a new cache directory (default 0755).", "0755"),
                    F("UID", mount.Uid, v => mount.Uid = v, "Owner user id of a new cache directory (default 0).", "1000"), F("GID", mount.Gid, v => mount.Gid = v, "Owner group id (default 0).", "1000")));
                fields.Add(S("Read-only", mount.ReadOnly, v => mount.ReadOnly = v, "The step can read the cache but not change it."));
                break;
            case RunMounts.Bind:
                fields.Add(F("From", mount.From, v => mount.From = v, "Stage, named context or image to read from. Empty uses the build context.", "build"));
                fields.Add(S("Read-write", mount.ReadWrite, v => mount.ReadWrite = v, "Allows writes; they are discarded when the step finishes."));
                break;
            case RunMounts.Secret:
                fields.Add(F("Environment variable", mount.Env, v => mount.Env = v, "Expose the secret as this environment variable instead of (or as well as) a file. Needs docker/dockerfile:1.10 or later.", "NPM_TOKEN"));
                fields.Add(Ui.Columns(10, [1, 1, 1], F("Mode", mount.Mode, v => mount.Mode = v, "Octal mode of the secret file (default 0400).", "0400"),
                    F("UID", mount.Uid, v => mount.Uid = v, "Owner user id (default 0).", "1000"), F("GID", mount.Gid, v => mount.Gid = v, "Owner group id (default 0).", "1000")));
                fields.Add(S("Required", mount.Required, v => mount.Required = v, "Fail the step when the build does not provide this secret."));
                break;
            case RunMounts.Ssh:
                fields.Add(Ui.Columns(10, [1, 1, 1], F("Mode", mount.Mode, v => mount.Mode = v, "Octal mode of the socket (default 0600).", "0600"),
                    F("UID", mount.Uid, v => mount.Uid = v, "Owner user id (default 0).", "1000"), F("GID", mount.Gid, v => mount.Gid = v, "Owner group id (default 0).", "1000")));
                fields.Add(S("Required", mount.Required, v => mount.Required = v, "Fail the step when no SSH agent or key is forwarded."));
                break;
        }
        var up = Ui.Button("Move up", Icons.ExpandLess, ButtonKind.Text, () => Move(index, -1));
        var down = Ui.Button("Move down", Icons.ExpandMore, ButtonKind.Text, () => Move(index, 1));
        foreach (var b in new[] { up, down }) { b.Height = b.MinHeight = 32; b.Padding = new Thickness(6, 0, 10, 0); b.FontSize = 13; }
        up.IsEnabled = index > 0; down.IsEnabled = index < mounts.Count - 1;
        Ui.Add(details, Ui.Caption(TypeHelp.TryGetValue(mount.Type, out var help) ? help : ""), 0);
        foreach (var f in fields) Ui.Add(details, f, 10);
        Ui.Add(details, Ui.Wrap(4, up, down), 8);

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(details);
        problems.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(problems);
        var initial = RunMounts.Validate(mount);
        problems.Text = string.Join(Environment.NewLine, initial);
        problems.Visibility = initial.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var card = Ui.Card(stack, 8, "SurfaceContainer", 12);
        card.Margin = new Thickness(0, 0, 0, 6);
        System.Windows.Automation.AutomationProperties.SetName(card, $"{Loc.T("Mount")} {index + 1}: {RunMounts.Describe(mount)}");
        return card;
    }
}

/// <summary>Chips for a list of short values (patterns, tags) with an input to add more and a remove button per chip.</summary>
public sealed class CreatorChipsInput : StackPanel
{
    private readonly List<string> values;
    private readonly Action changed;
    private readonly Func<string, string?>? validate;
    private readonly WrapPanel chips = new();
    private readonly TextBlock error = Ui.Text("", 12, null, "Error");

    public CreatorChipsInput(List<string> values, string placeholder, Action changed, Func<string, string?>? validate = null)
    {
        this.values = values; this.changed = changed; this.validate = validate;
        var input = Ui.Input("", placeholder, true);
        var add = Ui.Button("Add", Icons.Add, ButtonKind.Tonal, () => Add(input));
        input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Add(input); e.Handled = true; } };
        Children.Add(chips);
        Children.Add(Ui.Columns(8, [1, 0], input, add));
        error.Visibility = Visibility.Collapsed;
        Children.Add(error);
        Rebuild();
    }

    private void Add(TextBox input)
    {
        var value = input.Text.Trim();
        if (value.Length == 0) return;
        var problem = validate?.Invoke(value) ?? (values.Contains(value) ? Loc.T("Already listed.") : null);
        error.Text = problem ?? ""; error.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        if (problem != null) return;
        values.Add(value);
        input.Clear();
        Rebuild(); changed();
    }

    private void Rebuild()
    {
        chips.Children.Clear();
        for (var k = 0; k < values.Count; k++)
        {
            var index = k;
            var remove = Ui.IconButton(Icons.Close, Loc.T("Remove") + " " + values[k], () => { values.RemoveAt(index); Rebuild(); changed(); });
            remove.Width = remove.Height = remove.MinHeight = 24; remove.FontSize = 14;
            var chip = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 2, 2, 2), Margin = new Thickness(0, 0, 6, 6), Child = Ui.HStack(4, Ui.Mono(values[k], 12, "OnSecondaryContainer"), remove) };
            ((FrameworkElement)((StackPanel)chip.Child).Children[0]).VerticalAlignment = VerticalAlignment.Center;
            chip.SetResourceReference(Border.BackgroundProperty, "SecondaryContainer");
            chips.Children.Add(chip);
        }
        chips.Visibility = values.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
