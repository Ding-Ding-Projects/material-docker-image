using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Editor for a RUN step's --mount list: one tonal row per mount with a type selector, the fields that type
/// accepts, and move/remove buttons. Edits write straight into the list and call <c>changed</c>.
/// </summary>
public sealed class CreatorMountEditor : StackPanel
{
    private readonly List<RunMount> mounts;
    private readonly Action changed;
    private readonly StackPanel rows = new();

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
        Children.Add(rows);
        var add = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var type in RunMounts.Types)
        {
            var b = Ui.Button(type, Icons.Add, ButtonKind.Outlined, () => { mounts.Add(RunMounts.Create(type)); Rebuild(); changed(); }, false);
            b.MinHeight = 32; b.Height = 32; b.Padding = new Thickness(10, 0, 12, 0); b.Margin = new Thickness(0, 0, 6, 6);
            b.ToolTip = Loc.T("Add a " + type + " mount. ") + Loc.T(TypeHelp[type]);
            System.Windows.Automation.AutomationProperties.SetName(b, Loc.T("Add " + type + " mount"));
            add.Children.Add(b);
        }
        var presets = Ui.Combo(new[] { Loc.T("Cache preset…") }.Concat(RunMounts.CachePresets.Select(p => p.Name)), null);
        presets.SelectedIndex = 0;
        presets.HorizontalAlignment = HorizontalAlignment.Left; presets.MinWidth = 200;
        System.Windows.Automation.AutomationProperties.SetName(presets, Loc.T("Add cache mounts for a package manager"));
        presets.ToolTip = Loc.T("Adds the cache mounts for a package manager with its default cache paths.");
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedIndex <= 0 || presets.SelectedItem is not string name) return;
            var added = RunMounts.AddPreset(mounts, name);
            var note = RunMounts.CachePresets.First(p => p.Name == name).Note;
            presets.SelectedIndex = 0;
            if (added > 0) { Rebuild(); changed(); }
            presetNote.Text = (added == 0 ? Loc.T("Those cache mounts are already present.") : $"{Loc.T("Added")} {added} {Loc.T(added == 1 ? "cache mount" : "cache mounts")} {Loc.T("for")} {name}.") + (note.Length > 0 ? " " + note : "");
            presetNote.Visibility = Visibility.Visible;
        };
        Children.Add(add);
        Children.Add(presets);
        presetNote.Visibility = Visibility.Collapsed;
        presetNote.Margin = new Thickness(0, 6, 0, 0);
        Children.Add(presetNote);
        Rebuild();
    }

    private readonly TextBlock presetNote = Ui.Caption("");

    private void Rebuild()
    {
        rows.Children.Clear();
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

    private UIElement Row(int index)
    {
        var mount = mounts[index];
        var type = Ui.Combo(RunMounts.Types, RunMounts.Types.Contains(mount.Type) ? mount.Type : RunMounts.Cache);
        type.MinWidth = 110;
        System.Windows.Automation.AutomationProperties.SetName(type, $"{Loc.T("Mount type")} {index + 1}");
        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedItem is not string value || value == mount.Type) return;
            mounts[index] = RunMounts.Create(value);
            Rebuild(); changed();
        };
        var summary = Ui.Mono(RunMounts.Describe(mount), 12, "OnSurfaceVariant", true);
        summary.VerticalAlignment = VerticalAlignment.Center;
        var up = Ui.IconButton(Icons.ExpandLess, "Move mount up", () => Move(index, -1));
        var down = Ui.IconButton(Icons.ExpandMore, "Move mount down", () => Move(index, 1));
        var remove = Ui.IconButton(Icons.Delete, "Remove mount", () => { mounts.RemoveAt(index); Rebuild(); changed(); });
        up.IsEnabled = index > 0; down.IsEnabled = index < mounts.Count - 1;
        foreach (var b in new[] { up, down, remove }) { b.Width = b.Height = b.MinHeight = 32; b.FontSize = 18; }
        var top = Ui.Columns(8, [0, 1, 0, 0, 0], type, summary, up, down, remove);
        var problems = Ui.Text("", 12, null, "Error");
        void Touch()
        {
            summary.Text = RunMounts.Describe(mount);
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

        var fields = new List<FrameworkElement>();
        switch (mount.Type)
        {
            case RunMounts.Cache:
                fields.Add(Ui.Columns(10, [1, 1], F("Target", mount.Target, v => mount.Target = v, "Directory the step sees the cache at.", "/root/.npm"),
                    F("Cache id", mount.Id, v => mount.Id = v, "Separates caches; defaults to the target path. Use the same id in several steps to share one cache.", "npm")));
                var sharing = Ui.Combo(RunMounts.SharingModes.Select(m => m.Length == 0 ? "(default: shared)" : m), mount.Sharing.Length == 0 ? "(default: shared)" : mount.Sharing);
                sharing.SelectionChanged += (_, _) => { mount.Sharing = sharing.SelectedItem is string s && !s.StartsWith('(') ? s : ""; Touch(); };
                fields.Add(Ui.Columns(10, [1, 1], Ui.Field("Sharing", sharing, "shared lets parallel builds use the cache together, private gives each writer its own copy, locked makes builds wait (apt needs locked).", "locked"),
                    F("From", mount.From, v => mount.From = v, "Stage, context or image whose content seeds the cache. Empty starts with an empty directory.", "deps")));
                fields.Add(Ui.Columns(10, [1, 1, 1, 1], F("Source", mount.Source, v => mount.Source = v, "Subpath of From to mount.", "/cache"), F("Mode", mount.Mode, v => mount.Mode = v, "Octal mode for a new cache directory (default 0755).", "0755"),
                    F("UID", mount.Uid, v => mount.Uid = v, "Owner user id of a new cache directory (default 0).", "1000"), F("GID", mount.Gid, v => mount.Gid = v, "Owner group id (default 0).", "1000")));
                fields.Add(S("Read-only", mount.ReadOnly, v => mount.ReadOnly = v, "The step can read the cache but not change it."));
                break;
            case RunMounts.Bind:
                fields.Add(Ui.Columns(10, [1, 1], F("Target", mount.Target, v => mount.Target = v, "Path the files appear at during the step. Relative paths are inside WORKDIR.", "package.json"),
                    F("Source", mount.Source, v => mount.Source = v, "Path in the build context (or in From). Empty mounts the root.", "package.json")));
                fields.Add(F("From", mount.From, v => mount.From = v, "Stage, named context or image to read from. Empty uses the build context.", "build"));
                fields.Add(S("Read-write", mount.ReadWrite, v => mount.ReadWrite = v, "Allows writes; they are discarded when the step finishes."));
                break;
            case RunMounts.Tmpfs:
                fields.Add(Ui.Columns(10, [1, 1], F("Target", mount.Target, v => mount.Target = v, "Path of the in-memory directory.", "/tmp"),
                    F("Size", mount.Size, v => mount.Size = v, "Upper size limit with a unit.", "64m")));
                break;
            case RunMounts.Secret:
                fields.Add(Ui.Columns(10, [1, 1], F("Secret id", mount.Id, v => mount.Id = v, "Name passed with docker buildx build --secret id=…; defaults to the target file name.", "npmrc"),
                    F("Target file", mount.Target, v => mount.Target = v, "Where the secret file appears; defaults to /run/secrets/<id>.", "/root/.npmrc")));
                fields.Add(F("Environment variable", mount.Env, v => mount.Env = v, "Expose the secret as this environment variable instead of (or as well as) a file. Needs docker/dockerfile:1.10 or later.", "NPM_TOKEN"));
                fields.Add(Ui.Columns(10, [1, 1, 1], F("Mode", mount.Mode, v => mount.Mode = v, "Octal mode of the secret file (default 0400).", "0400"),
                    F("UID", mount.Uid, v => mount.Uid = v, "Owner user id (default 0).", "1000"), F("GID", mount.Gid, v => mount.Gid = v, "Owner group id (default 0).", "1000")));
                fields.Add(S("Required", mount.Required, v => mount.Required = v, "Fail the step when the build does not provide this secret."));
                break;
            case RunMounts.Ssh:
                fields.Add(Ui.Columns(10, [1, 1], F("SSH id", mount.Id, v => mount.Id = v, "Agent socket or key id passed with --ssh; defaults to default.", "default"),
                    F("Socket path", mount.Target, v => mount.Target = v, "Where the agent socket appears; defaults to /run/buildkit/ssh_agent.N.", "/run/buildkit/ssh_agent.0")));
                fields.Add(Ui.Columns(10, [1, 1, 1], F("Mode", mount.Mode, v => mount.Mode = v, "Octal mode of the socket (default 0600).", "0600"),
                    F("UID", mount.Uid, v => mount.Uid = v, "Owner user id (default 0).", "1000"), F("GID", mount.Gid, v => mount.Gid = v, "Owner group id (default 0).", "1000")));
                fields.Add(S("Required", mount.Required, v => mount.Required = v, "Fail the step when no SSH agent or key is forwarded."));
                break;
        }
        var stack = Ui.VStack(10, top, Ui.Caption(TypeHelp.TryGetValue(mount.Type, out var help) ? help : ""));
        foreach (var f in fields) Ui.Add(stack, f, 10);
        Ui.Add(stack, problems, 6);
        var initial = RunMounts.Validate(mount);
        problems.Text = string.Join(Environment.NewLine, initial);
        problems.Visibility = initial.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var card = Ui.Card(stack, 12, "SurfaceContainer", 12);
        card.Margin = new Thickness(0, 0, 0, 8);
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
            var remove = Ui.IconButton(Icons.Close, "Remove " + values[k], () => { values.RemoveAt(index); Rebuild(); changed(); });
            remove.Width = remove.Height = remove.MinHeight = 24; remove.FontSize = 14;
            var chip = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 2, 2, 2), Margin = new Thickness(0, 0, 6, 6), Child = Ui.HStack(4, Ui.Mono(values[k], 12, "OnSecondaryContainer"), remove) };
            ((FrameworkElement)((StackPanel)chip.Child).Children[0]).VerticalAlignment = VerticalAlignment.Center;
            chip.SetResourceReference(Border.BackgroundProperty, "SecondaryContainer");
            chips.Children.Add(chip);
        }
        chips.Visibility = values.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
