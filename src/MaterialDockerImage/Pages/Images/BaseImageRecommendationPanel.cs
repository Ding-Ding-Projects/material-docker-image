using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Core;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>One run of docker scout recommendations for one image, kept for this session only.</summary>
internal sealed record BaseImageRecommendationResult(ScoutState State, ScoutRecommendationReport? Report, string Output, string Command, DateTime CheckedUtc);

/// <summary>Recommendation results in memory, keyed by engine context and image ID. Nothing is written to disk.</summary>
internal static class BaseImageRecommendationCache
{
    private static readonly Dictionary<string, BaseImageRecommendationResult> Results = new(StringComparer.Ordinal);
    private static string Key(string? context, string imageId) => (context ?? "") + "\n" + imageId;
    public static BaseImageRecommendationResult? Get(string? context, string imageId) => Results.TryGetValue(Key(context, imageId), out var r) ? r : null;
    public static void Put(string? context, string imageId, BaseImageRecommendationResult result) => Results[Key(context, imageId)] = result;
}

/// <summary>
/// "Base image recommendation" block of the Scout summary card (design/workspace-redesign-2026-11/project/ImagesV2.dc.html).
/// It runs docker scout recommendations only when asked, because the command contacts Docker's service, and shows the
/// detected base image with the refresh and update tags Docker Scout printed: their vulnerability counts, the change
/// against the current base and the size. Every value comes from the command output; failures keep the exact output.
/// </summary>
internal sealed class BaseImageRecommendationPanel : Border
{
    private const int MaxRows = 3;
    private static AppHost Host => AppHost.Current;
    private readonly ImageDetailContext ctx;
    private readonly ContentControl body = new();
    private readonly Button run;

    public BaseImageRecommendationPanel(ImageDetailContext context)
    {
        ctx = context;
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(12);
        SetResourceReference(BackgroundProperty, "SurfaceContainer");
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("Base image recommendation"));

        var icon = Ui.Icon(Icons.NewReleases, 20, true, "Success");
        icon.VerticalAlignment = VerticalAlignment.Top;
        var title = Ui.Text("Base image recommendation", 14, FontWeights.SemiBold, "OnSurface", true);
        run = Ui.AsyncButton("Get recommendations", Icons.Refresh, ButtonKind.Text, RecommendAsync);
        run.ToolTip = Loc.T("Runs docker scout recommendations for this image. It contacts Docker's service and can take a minute.");
        run.HorizontalAlignment = HorizontalAlignment.Left;
        run.Margin = new Thickness(-12, 0, 0, 0);
        var stack = Ui.VStack(6, title, body, run);
        Child = Ui.Columns(10, [0, 1], icon, stack);
        Show(BaseImageRecommendationCache.Get(ctx.Context, ctx.Image.Id));
    }

    private string Command => "docker scout recommendations " + ctx.Image.Reference;

    private void Show(BaseImageRecommendationResult? result)
    {
        SetLabel(result == null ? "Get recommendations" : "Check again");
        if (result == null)
        {
            body.Content = Caption("Not checked in this session. Docker Scout looks up newer tags for the base image it detects; this contacts Docker's service.");
            return;
        }
        if (result.State != ScoutState.Ready || result.Report == null)
        {
            body.Content = Problem(result.State == ScoutState.Ready ? ScoutState.Failed : result.State, result.Output, result.Command);
            return;
        }
        var report = result.Report;
        if (report.NoBaseImage)
        {
            body.Content = Ui.VStack(4, Caption("Docker Scout found no base image for this image, so there is nothing to refresh or update."), Checked(result));
            return;
        }
        if (report.BaseImage is not { } baseImage)
        {
            body.Content = Problem(ScoutState.Failed, Loc.T("The output did not name a base image.") + "\n" + result.Output, result.Command);
            return;
        }

        var stack = Ui.VStack(6);
        var pick = report.Refresh.FirstOrDefault() ?? report.Updates.FirstOrDefault();
        stack.Children.Add(Headline(baseImage, pick));
        stack.Children.Add(BaseLine(baseImage));
        var rows = report.Refresh.Concat(report.Updates).ToList();
        foreach (var recommendation in rows.Take(MaxRows)) stack.Children.Add(Row(baseImage, recommendation));
        if (rows.Count > MaxRows)
            stack.Children.Add(Caption(Format("{0} more tags in the full output (Output panel or Copy output)", rows.Count - MaxRows), false));
        if (report.BaseAutoDetected) stack.Children.Add(Caption("The base image was auto-detected. Builds with provenance attestations give exact results."));
        stack.Children.Add(Checked(result));
        body.Content = stack;
    }

    /// <summary>The sentence of the design: "Update node:22-alpine to node:22.11-alpine3.20" plus the printed change.</summary>
    private static FrameworkElement Headline(ScoutBaseImage baseImage, ScoutRecommendation? pick)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "OnSurfaceVariant");
        if (pick == null)
        {
            text.Text = Loc.T("No newer base image is recommended. The current base image is up to date.");
            return text;
        }
        var target = Repository(baseImage.Reference) + ":" + pick.Tag;
        var template = Loc.T(pick.Kind == ScoutRecommendation.RefreshKind ? "Refresh base image {0} to the newer {1}" : "Update base image {0} to {1}");
        // The template is split at its placeholders so both references can be shown in the monospace font.
        var first = template.IndexOf("{0}", StringComparison.Ordinal);
        var second = template.IndexOf("{1}", StringComparison.Ordinal);
        if (first >= 0 && second > first && template.IndexOf("{0}", first + 3, StringComparison.Ordinal) < 0)
        {
            text.Inlines.Add(new System.Windows.Documents.Run(template[..first]));
            text.Inlines.Add(MonoRun(baseImage.Reference));
            text.Inlines.Add(new System.Windows.Documents.Run(template[(first + 3)..second]));
            text.Inlines.Add(MonoRun(target));
            text.Inlines.Add(new System.Windows.Documents.Run(template[(second + 3)..]));
        }
        else text.Inlines.Add(new System.Windows.Documents.Run(string.Format(CultureInfo.CurrentCulture, template, baseImage.Reference, target)));
        var change = DeltaText(pick.Delta);
        text.Inlines.Add(new System.Windows.Documents.Run(change.Length > 0 ? ": " + change : ""));
        System.Windows.Automation.AutomationProperties.SetName(text, string.Format(CultureInfo.CurrentCulture, template, baseImage.Reference, target) + (change.Length > 0 ? ": " + change : ""));
        return text;
    }

    private static System.Windows.Documents.Run MonoRun(string text)
    {
        var run = new System.Windows.Documents.Run(text);
        run.SetResourceReference(System.Windows.Documents.TextElement.FontFamilyProperty, "MonoFont");
        run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "OnSurface");
        return run;
    }

    private static FrameworkElement BaseLine(ScoutBaseImage baseImage)
    {
        var parts = new List<string> { Loc.T("Current base") + " " + baseImage.Reference };
        if (baseImage.Counts is { } c) parts.Add(Counts(c));
        if (baseImage.Fact("Size") is { Length: > 0 } size) parts.Add(size);
        var line = Ui.Text(string.Join(" · ", parts), 12, null, "OnSurfaceVariant", false, false);
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        line.ToolTip = string.Join("\n", baseImage.Facts.Select(f => f.Key + ": " + f.Value));
        return line;
    }

    private static FrameworkElement Row(ScoutBaseImage baseImage, ScoutRecommendation recommendation)
    {
        var target = Repository(baseImage.Reference) + ":" + recommendation.Tag;
        var tag = Ui.Mono(target, 12, "OnSurface");
        tag.TextTrimming = TextTrimming.CharacterEllipsis;
        tag.ToolTip = recommendation.AlsoKnownAs.Count > 0 ? Loc.T("Also known as:") + " " + string.Join(", ", recommendation.AlsoKnownAs) : target;
        var kind = Ui.Badge(recommendation.Kind == ScoutRecommendation.RefreshKind ? "Same tag, newer build" : "Other tag", recommendation.Kind == ScoutRecommendation.RefreshKind ? Tone.Success : Tone.Neutral);
        var facts = new List<string>();
        if (recommendation.Counts is { } counts) facts.Add(Counts(counts));
        if (recommendation.Detail("Size") is { Length: > 0 } size) facts.Add(size + SizeChange(baseImage.Fact("Size"), size));
        if (recommendation.Pushed.Length > 0) facts.Add(Loc.T("pushed") + " " + recommendation.Pushed);
        var factLine = Ui.Text(string.Join(" · ", facts), 12, null, "OnSurfaceVariant", false, false);
        factLine.TextTrimming = TextTrimming.CharacterEllipsis;
        var change = DeltaText(recommendation.Delta);
        var stack = Ui.VStack(2, Ui.Columns(8, [1, 0], tag, kind), factLine);
        if (change.Length > 0) stack.Children.Add(Ui.Text(change, 12, null, "OnSurfaceVariant", true, false));
        stack.ToolTip = string.Join("\n", recommendation.Reasons.Concat(recommendation.Benefits.Select(b => "• " + b)));
        var card = Ui.Card(stack, 8, "SurfaceHigh", 8);
        card.ContextMenu = new ContextMenu();
        card.ContextMenu.Items.Add(Ui.MenuItem("Copy image reference", Icons.ContentCopy, () => PageKit.Copy(target, "the image reference")));
        System.Windows.Automation.AutomationProperties.SetName(card, target + ", " + string.Join(", ", facts) + (change.Length > 0 ? ", " + change : ""));
        return card;
    }

    /// <summary>0C 3H 0M 9L, plus unrated findings when Docker Scout printed any.</summary>
    private static string Counts(ScoutVulnerabilityCounts c) =>
        string.Create(CultureInfo.CurrentCulture, $"{c.Critical}C {c.High}H {c.Medium}M {c.Low}L") + (c.Unknown > 0 ? string.Create(CultureInfo.CurrentCulture, $" {c.Unknown}?") : "");

    /// <summary>"1 fewer High, 1 more Medium, 17 more Low", from the printed change line only.</summary>
    private static string DeltaText(ScoutVulnerabilityDelta? delta)
    {
        if (delta is null || !delta.Any) return "";
        var parts = new List<string>();
        void Add(int? value, string label)
        {
            if (value is not { } n || n == 0) return;
            parts.Add(string.Format(CultureInfo.CurrentCulture, Loc.T(n < 0 ? "{0} fewer {1}" : "{0} more {1}"), Math.Abs(n), Loc.T(label)));
        }
        Add(delta.Critical, "Critical"); Add(delta.High, "High"); Add(delta.Medium, "Medium"); Add(delta.Low, "Low"); Add(delta.Unknown, "unrated");
        return string.Join(", ", parts);
    }

    /// <summary>" (+19 MB)" when both sizes were printed in readable units; empty otherwise.</summary>
    private static string SizeChange(string current, string recommended)
    {
        if (ScoutRecommendationParser.SizeBytes(current) is not { } a || ScoutRecommendationParser.SizeBytes(recommended) is not { } b) return "";
        var difference = b - a;
        if (Math.Abs(difference) < 50_000) return " (" + Loc.T("same size") + ")";
        return " (" + (difference > 0 ? "+" : "−") + (Math.Abs(difference) / 1_000_000).ToString("0.#", CultureInfo.CurrentCulture) + " MB)";
    }

    /// <summary>docker.io/library/node:22 → docker.io/library/node; keeps a registry port.</summary>
    internal static string Repository(string reference)
    {
        var at = reference.IndexOf('@');
        if (at >= 0) reference = reference[..at];
        var slash = reference.LastIndexOf('/');
        var colon = reference.LastIndexOf(':');
        return colon > slash ? reference[..colon] : reference;
    }

    private FrameworkElement Checked(BaseImageRecommendationResult result)
    {
        var copy = Ui.Button("Copy output", Icons.ContentCopy, ButtonKind.Text, () => PageKit.Copy(result.Output, "the command output"));
        var when = Ui.Text(Loc.T("Checked") + " " + result.CheckedUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) + " · " + result.Command, 12, null, "OnSurfaceVariant", false, false);
        when.TextTrimming = TextTrimming.CharacterEllipsis;
        when.VerticalAlignment = VerticalAlignment.Center;
        when.ToolTip = result.Command;
        return Ui.Columns(4, [1, 0], when, copy);
    }

    private FrameworkElement Problem(ScoutState state, string output, string command)
    {
        var title = state switch
        {
            ScoutState.PluginMissing => "Docker Scout is not installed",
            ScoutState.NotSignedIn => "Sign in to use Docker Scout",
            _ => "Docker Scout could not give recommendations"
        };
        var heading = Ui.Text(title, 13, FontWeights.SemiBold, state == ScoutState.Failed ? "Error" : "OnSurface");
        var explain = Caption(ScoutStatus.Explain(state));
        var copy = Ui.Button("Copy output", Icons.ContentCopy, ButtonKind.Text, () => PageKit.Copy("$ " + command + "\n" + output, "the command output"));
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        var stack = Ui.VStack(4, heading, explain);
        if (output.Trim().Length > 0)
        {
            var last = output.Trim().Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
            var line = Ui.Mono(last, 12, "OnSurfaceVariant", true);
            line.ToolTip = output.Trim();
            stack.Children.Add(line);
        }
        stack.Children.Add(copy);
        if (state == ScoutState.PluginMissing) stack.Children.Add(ScoutSeverityUi.InstallGuideButton());
        return stack;
    }

    private static TextBlock Caption(string text, bool translate = true)
    {
        var caption = Ui.Text(text, 12, null, "OnSurfaceVariant", true, translate);
        return caption;
    }

    private void SetLabel(string label)
    {
        if (run.Content is Panel { } panel && panel.Children.OfType<TextBlock>().LastOrDefault() is { } text) text.Text = Loc.T(label);
        System.Windows.Automation.AutomationProperties.SetName(run, Loc.T(label));
    }

    private static string Format(string template, params object[] values) => string.Format(CultureInfo.CurrentCulture, Loc.T(template), values);

    private async Task RecommendAsync()
    {
        if (!ctx.Page.EnsureCurrent()) return;
        var reference = ctx.Image.Reference;
        var command = Command;
        var availability = await ScoutRunner.CheckAsync(ctx.Context);
        if (!availability.Available)
        {
            Store(new BaseImageRecommendationResult(ScoutState.PluginMissing, null, availability.Output, availability.Command, DateTime.UtcNow));
            return;
        }
        var previous = body.Content;
        body.Content = Ui.HStack(8, new ProgressBar { IsIndeterminate = true, Height = 4, Width = 80, VerticalAlignment = VerticalAlignment.Center }, Ui.Caption("Asking Docker Scout…"));
        CommandResult? result;
        try { result = await Host.Execute("Docker Scout recommendations for " + reference, ["scout", "recommendations", reference], true, ctx.Context); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            Store(new BaseImageRecommendationResult(ScoutState.Failed, null, e.Message, command, DateTime.UtcNow));
            return;
        }
        if (result == null) { body.Content = previous; return; }
        if (result.Cancelled) { body.Content = previous; return; }
        var output = ScoutReport.StripAnsi((result.StandardError + "\n" + result.StandardOutput).Trim());
        if (!result.Success)
        {
            Store(new BaseImageRecommendationResult(ScoutStatus.ClassifyFailure(output), null, $"docker scout recommendations exited with code {result.ExitCode}.\n{output}".Trim(), command, DateTime.UtcNow));
            return;
        }
        var report = ScoutRecommendationParser.Parse(result.StandardOutput, result.StandardError);
        var recognised = report.NoBaseImage || report.BaseImage != null;
        Store(new BaseImageRecommendationResult(recognised ? ScoutState.Ready : ScoutStatus.ClassifyFailure(output), recognised ? report : null, output, command, DateTime.UtcNow));
    }

    private void Store(BaseImageRecommendationResult result)
    {
        BaseImageRecommendationCache.Put(ctx.Context, ctx.Image.Id, result);
        Show(result);
    }
}
