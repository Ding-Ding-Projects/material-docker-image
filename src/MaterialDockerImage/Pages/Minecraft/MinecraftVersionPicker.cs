using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Version chooser: keyword chips (LATEST, SNAPSHOT or PREVIEW), a validated typed version, and for Java a searchable list
/// loaded on request from Mojang's public manifest through a short-lived curl container on the selected engine.
/// </summary>
internal sealed class MinecraftVersionPicker : StackPanel
{
    private static AppHost Host => AppHost.Current;
    private static MinecraftManifest? cached;
    private static DateTimeOffset cachedAt;
    private readonly MinecraftEdition edition;
    private readonly Action<string> changed;
    private readonly TextBox box;
    private readonly TextBlock error = Ui.Text("", 12, null, "Error", true, false);
    private readonly TextBlock runtime = Ui.Caption("");
    private readonly TextBlock listStatus = Ui.Caption("");
    private readonly SearchFilter search = new("Search versions", 260);
    private readonly ObservableCollection<MinecraftVersionEntry> shown = new();
    private readonly ListBox list = new() { MaxHeight = 260, MinHeight = 120 };
    private bool releases = true, snapshots;
    private bool settingText;

    public MinecraftVersionPicker(MinecraftEdition edition, string value, Func<string?> context, Action<string> changed)
    {
        this.edition = edition; this.changed = changed;
        var keywords = edition == MinecraftEdition.Java ? MinecraftVersions.JavaKeywords : MinecraftVersions.BedrockKeywords;
        box = Ui.Input(value, "Version", true);
        box.Width = 260; box.HorizontalAlignment = HorizontalAlignment.Left;
        box.TextChanged += (_, _) => { if (!settingText) Validate(); };
        var chips = new WrapPanel();
        foreach (var k in keywords) { var b = Ui.Button(k, null, ButtonKind.Outlined, () => Set(k), false); b.Margin = new Thickness(0, 0, 8, 6); chips.Children.Add(b); }
        var info = edition == MinecraftEdition.Java
            ? "LATEST follows the newest release each time the container starts; SNAPSHOT follows the newest snapshot. A typed version pins the server to exactly that version. Pinning is safer: worlds cannot be downgraded."
            : "LATEST follows the newest Bedrock Dedicated Server release; PREVIEW follows the preview channel. A typed version such as 1.21.44.01 pins it.";
        Children.Add(Ui.Field("Version", Ui.VStack(8, chips, box), info, edition == MinecraftEdition.Java ? "1.21.4\n1.20.1\n24w14a" : "1.21.44.01"));
        Children.Add(error);
        if (edition == MinecraftEdition.Java)
        {
            Children.Add(runtime.Margin(0, 4, 0, 0));
            var refresh = Ui.AsyncButton("Refresh versions", Icons.CloudDownload, ButtonKind.Tonal, () => FetchAsync(context()));
            refresh.ToolTip = Loc.T("Runs docker run --rm " + MinecraftVersions.CurlImage + " to download Mojang's public version manifest on the selected engine.");
            var relChip = Ui.Chip("Releases", true, v => { releases = v; Render(); });
            var snapChip = Ui.Chip("Snapshots", false, v => { snapshots = v; Render(); });
            var bar = new WrapPanel { Margin = new Thickness(0, 14, 0, 6) };
            foreach (var e in new FrameworkElement[] { search, relChip, snapChip, refresh }) { e.Margin = new Thickness(0, 0, 8, 6); e.VerticalAlignment = VerticalAlignment.Center; bar.Children.Add(e); }
            Children.Add(bar);
            list.ItemsSource = shown;
            list.DisplayMemberPath = null;
            list.ItemTemplate = Template();
            System.Windows.Automation.AutomationProperties.SetName(list, Loc.T("Available versions"));
            list.SelectionChanged += (_, _) => { if (list.SelectedItem is MinecraftVersionEntry v) Set(v.Id); };
            Children.Add(Ui.Card(list, 4, "SurfaceLowest", 12));
            Children.Add(listStatus.Margin(2, 6, 0, 0));
            search.Changed += Render;
            Render();
        }
        Validate();
    }

    private static DataTemplate Template()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var id = new FrameworkElementFactory(typeof(TextBlock));
        id.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(MinecraftVersionEntry.Id)));
        id.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        id.SetValue(FrameworkElement.WidthProperty, 180.0);
        var type = new FrameworkElementFactory(typeof(TextBlock));
        type.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(MinecraftVersionEntry.Type)));
        type.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        type.SetValue(FrameworkElement.WidthProperty, 90.0);
        var date = new FrameworkElementFactory(typeof(TextBlock));
        date.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(MinecraftVersionEntry.DateText)));
        date.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        panel.AppendChild(id); panel.AppendChild(type); panel.AppendChild(date);
        return new DataTemplate { VisualTree = panel };
    }

    private void Set(string value) { settingText = true; box.Text = value; settingText = false; Validate(); }

    private void Validate()
    {
        var v = box.Text.Trim();
        var problem = MinecraftVersions.Validate(edition, v);
        error.Text = problem ?? "";
        if (edition == MinecraftEdition.Java)
        {
            var tag = MinecraftVersions.JavaRuntimeTag(v);
            runtime.Text = problem != null ? "" : tag == "latest"
                ? "Image itzg/minecraft-server:latest (the image picks its default Java runtime)."
                : $"Image itzg/minecraft-server:{tag} (Java {tag[4..]} runtime for this version).";
        }
        changed(problem == null ? (MinecraftVersions.JavaKeywords.Concat(MinecraftVersions.BedrockKeywords).Contains(v.ToUpperInvariant()) ? v.ToUpperInvariant() : v) : v);
    }

    private void Render()
    {
        shown.Clear();
        if (cached == null)
        {
            listStatus.Text = Loc.T("No version list loaded. Use Refresh versions to download Mojang's manifest, or type a version above.");
            return;
        }
        foreach (var v in cached.Versions)
        {
            if (v.IsRelease ? !releases : !(snapshots && v.Type == "snapshot")) continue;
            if (!search.Matches(v.Id + " " + v.Type + " " + v.DateText)) continue;
            shown.Add(v);
        }
        listStatus.Text = (search.Status.Length > 0 ? search.Status + " · " : "") + $"{shown.Count} of {cached.Versions.Count} versions shown · latest release {cached.LatestRelease ?? "unknown"}, latest snapshot {cached.LatestSnapshot ?? "unknown"} · downloaded {cachedAt.ToLocalTime():HH:mm}";
    }

    private async Task FetchAsync(string? context)
    {
        listStatus.Text = Loc.T("Downloading the version manifest… The first run also pulls the") + " " + MinecraftVersions.CurlImage + " image.";
        var r = await Host.Read(MinecraftVersions.ManifestFetchArguments(), context);
        if (!r.Success) { listStatus.Text = Loc.T("The version manifest could not be downloaded:") + " " + MinecraftOps.Trim(r) + " You can still type a version."; return; }
        try { cached = MinecraftVersions.ParseManifest(r.StandardOutput); cachedAt = DateTimeOffset.Now; Render(); }
        catch (FormatException e) { listStatus.Text = e.Message; }
    }
}
