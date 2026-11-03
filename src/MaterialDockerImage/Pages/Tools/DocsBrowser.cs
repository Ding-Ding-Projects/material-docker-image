using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

public sealed record DocArticle(string Resource, string FileName, string Title, string Body, string Plain);

/// <summary>Offline documentation browser over the embedded docs/*.md resources, with search across titles and bodies.</summary>
internal sealed class DocsBrowser
{
    private static AppHost Host => AppHost.Current;
    private readonly List<DocArticle> articles;
    private readonly SearchFilter search = new("Search documentation", 280);
    private readonly ListBox list = new() { MinHeight = 200 };
    private readonly MarkdownView view = new();
    private readonly TextBlock count = Ui.Caption("");
    private readonly ScrollViewer reader;
    public FrameworkElement Root { get; }

    public DocsBrowser()
    {
        articles = Load();
        System.Windows.Automation.AutomationProperties.SetName(list, Loc.T("Documentation articles"));
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: DocArticle a }) Show(a); };
        search.Changed += Filter;
        view.LinkClicked += OnLink;
        reader = Ui.Scroll(view.Margin(4, 0, 12, 12));
        var left = new DockPanel();
        left.Children.Add(Ui.VStack(8, search, count).Margin(0, 0, 0, 8).Dock(Dock.Top));
        left.Children.Add(list);
        var leftCard = Ui.Card(left, 12, "SurfaceLow", 16);
        var rightCard = Ui.Card(reader, 20, "SurfaceLow", 16);
        Root = Ui.Columns(16, [-320, 1], leftCard, rightCard);
        Filter();
        if (articles.Count > 0) Select(articles[0]);
        else view.Children.Add(Ui.EmptyState(Icons.MenuBook, "No documentation embedded", "This build contains no docs/*.md resources. The project's documentation folder is docs/features in the source repository."));
    }

    private static List<DocArticle> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var result = new List<DocArticle>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("docs/", StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null) continue;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var body = reader.ReadToEnd();
            var file = name[5..];
            var title = body.Replace("\r\n", "\n").Split('\n').FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim() ?? Path.GetFileNameWithoutExtension(file);
            result.Add(new(name, file, title, body, MarkdownView.PlainText(body)));
        }
        return result;
    }

    private void Filter()
    {
        var selected = (list.SelectedItem as ListBoxItem)?.Tag as DocArticle;
        list.Items.Clear();
        foreach (var a in articles.Where(a => search.Matches(a.Title + " " + a.FileName + " " + a.Plain)))
        {
            var snippet = Snippet(a);
            var item = new ListBoxItem { Tag = a, Content = Ui.VStack(2, Ui.Text(a.Title, 14, FontWeights.SemiBold, "OnSurface", true, false), Ui.Caption(snippet)), Padding = new Thickness(10, 8, 10, 8) };
            System.Windows.Automation.AutomationProperties.SetName(item, a.Title);
            list.Items.Add(item);
            if (a == selected) list.SelectedItem = item;
        }
        count.Text = search.Status.Length > 0 ? search.Status : $"Offline · {list.Items.Count} of {articles.Count} articles";
    }

    private string Snippet(DocArticle a)
    {
        if (!search.IsActive || search.RegexMode) return a.FileName;
        var index = a.Plain.IndexOf(search.Query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return a.FileName;
        var start = Math.Max(0, index - 30);
        var text = a.Plain.Substring(start, Math.Min(90, a.Plain.Length - start));
        return (start > 0 ? "…" : "") + System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim() + "…";
    }

    private void Select(DocArticle a)
    {
        foreach (ListBoxItem item in list.Items) if (ReferenceEquals(item.Tag, a)) { list.SelectedItem = item; return; }
        Show(a);
    }

    private void Show(DocArticle a)
    {
        view.Render(a.Body);
        reader.ScrollToTop();
    }

    public bool Open(string fileName)
    {
        var a = articles.FirstOrDefault(x => x.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (a == null) return false;
        if (search.IsActive) search.Query = "";
        Select(a);
        return true;
    }

    private async void OnLink(string href)
    {
        try
        {
            if (href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) { Host.Notify.Warning("The link is not a valid address."); return; }
                if (await Host.Overlay.ConfirmAsync("Open in your browser?", $"This leaves the app and opens {uri.Host} in your default browser:\n{uri.AbsoluteUri}", "Open browser"))
                    PageParts.OpenExternal(uri.AbsoluteUri);
                return;
            }
            if (href.Contains("://", StringComparison.Ordinal) || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                Host.Notify.Warning("Only https links open from the documentation.");
                return;
            }
            var file = Path.GetFileName(href.Split('#')[0]);
            if (!Open(file)) Host.Notify.Info($"\"{file}\" is not part of the embedded documentation.");
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { Host.Notify.Error(e.Message); }
    }
}
