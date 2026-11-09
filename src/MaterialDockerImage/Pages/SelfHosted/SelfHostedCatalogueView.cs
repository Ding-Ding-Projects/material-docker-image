using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>The catalogue of quick setups: search with its regex builder, category chips and one card per app.</summary>
internal sealed class SelfHostedCatalogueView : Grid
{
    private readonly SearchFilter search = new("Search self-hosted apps", 360);
    private readonly HashSet<SelfHostedCategory> categories = [];
    private readonly UniformGrid cards = new() { Columns = 3 };
    private readonly ContentControl empty = new();
    private readonly TextBlock count = Ui.Caption("");
    private readonly Action<SelfHostedApp> setUp;

    internal SearchFilter Search => search;

    public SelfHostedCatalogueView(Action<SelfHostedApp> setUp)
    {
        this.setUp = setUp;
        search.Changed += Render;
        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(chips, Loc.T("Categories"));
        foreach (var category in SelfHostedCatalogue.Categories)
        {
            var c = category;
            var chip = Ui.Chip(SelfHostedCatalogue.CategoryName(c), false, on => { if (on) categories.Add(c); else categories.Remove(c); Render(); });
            chip.Margin = new Thickness(0, 0, 8, 8);
            chips.Children.Add(chip);
        }
        var top = Ui.VStack(0, Ui.Columns(12, [1, 0], search, count), chips);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Children.Add(top);
        var scroll = Ui.Scroll(Ui.VStack(0, empty, cards));
        scroll.SizeChanged += (_, _) => cards.Columns = Math.Clamp((int)(scroll.ActualWidth / 330), 1, 4);
        Children.Add(scroll.At(1));
        Render();
    }

    public static string CategoryIcon(SelfHostedCategory category) => category switch
    {
        SelfHostedCategory.FilesAndSync => Icons.CloudSync,
        SelfHostedCategory.Development => Icons.Code,
        SelfHostedCategory.Media => Icons.PlayArrow,
        SelfHostedCategory.HomeAndMonitoring => Icons.Monitoring,
        SelfHostedCategory.Network => Icons.Router,
        SelfHostedCategory.Productivity => Icons.Description,
        _ => Icons.Science,
    };

    private void Render()
    {
        var shown = SelfHostedCatalogue.All.Where(a => (categories.Count == 0 || categories.Contains(a.Category)) && search.MatchesFields(SelfHostedCatalogue.SearchFields(a))).ToList();
        cards.Children.Clear();
        foreach (var app in shown) cards.Children.Add(Card(app));
        count.Text = Loc.Line($"{shown.Count} of {SelfHostedCatalogue.All.Count} apps");
        count.VerticalAlignment = VerticalAlignment.Center;
        empty.Content = shown.Count > 0 ? null : Ui.EmptyState(Icons.FilterList, "No matches", search.Status.Length > 0 ? search.Status : "No app matches the search and categories.");
    }

    private Border Card(SelfHostedApp app)
    {
        var glyph = Ui.Icon(CategoryIcon(app.Category), 22, true, "OnSecondaryContainer");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        var tile = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(12), Child = glyph, VerticalAlignment = VerticalAlignment.Top };
        tile.SetResourceReference(Border.BackgroundProperty, "SecondaryContainer");
        var title = Ui.VStack(4, Ui.Text(app.Name, 15, FontWeights.SemiBold, "OnSurface", true, false), Ui.Badge(app.CategoryText, Tone.Neutral));
        var head = Ui.Columns(12, [0, 1], tile, title);
        var needs = Ui.Wrap(6,
            Ui.Badge(Loc.T("Memory") + " " + SelfHostedRules.MemoryText(app.MinMemoryMb), Tone.Neutral, false),
            Ui.Badge(Loc.T("Disk") + " " + app.MinDiskGb.ToString(CultureInfo.InvariantCulture) + " GB", Tone.Neutral, false),
            Ui.Badge(Loc.T("Port") + " " + string.Join(", ", app.Ports.Select(p => p.DefaultHostPort.ToString(CultureInfo.InvariantCulture))), Tone.Neutral, false));
        var setUpButton = Ui.Button("Set up", Icons.RocketLaunch, ButtonKind.Filled, () => setUp(app));
        System.Windows.Automation.AutomationProperties.SetName(setUpButton, Loc.T("Set up") + " " + app.Name);
        var docs = Ui.IconButton(Icons.MenuBook, "Upstream documentation", () => PageParts.OpenExternal(app.DocsUrl));
        docs.ToolTip = app.DocsUrl;
        var actions = Ui.HStack(8, setUpButton, docs);
        var body = Ui.VStack(10, head, Ui.Text(app.Description, 13, null, "OnSurfaceVariant"), needs, actions);
        var card = Ui.Card(body, 16, "SurfaceContainer", 16);
        card.Margin = new Thickness(0, 0, 12, 12);
        return card;
    }
}
