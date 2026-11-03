using System.Text;
using System.Text.Json;
using System.Windows;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;
using Microsoft.Win32;

namespace MaterialDockerImage.Pages;

/// <summary>
/// Builds a sanitized diagnostics report (Markdown or JSON) from the last check results. It contains no
/// credentials and no environment variable values, and the user profile path is replaced by %USERPROFILE%.
/// </summary>
internal static class DiagnosticReport
{
    private static AppHost Host => AppHost.Current;

    public static string Markdown(IReadOnlyList<DiagnosticCheck> checks, string? context)
    {
        var (version, _, built) = PageParts.AppInfo();
        var sb = new StringBuilder();
        sb.AppendLine("# Material Docker Image diagnostics report").AppendLine();
        sb.AppendLine($"- Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"- App version: {version} (build {PageParts.BuildText(built)})");
        sb.AppendLine($"- Windows: {Environment.OSVersion.VersionString}, {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}");
        sb.AppendLine($"- Engine context for this window: {context ?? "CLI default"}");
        sb.AppendLine("- Sanitized: no credentials, no environment variable values; the user profile path is shown as %USERPROFILE%.").AppendLine();
        sb.AppendLine("| Check | Status | Value | Detail |").AppendLine("| --- | --- | --- | --- |");
        foreach (var c in checks) sb.AppendLine($"| {Cell(c.Label)} | {c.Status} | {Cell(c.Last?.Value ?? "")} | {Cell(PageParts.Sanitize(c.Last?.Detail ?? "not run"))} |");
        foreach (var c in checks.Where(c => c.Last != null))
        {
            sb.AppendLine().AppendLine($"## {c.Label}").AppendLine();
            sb.AppendLine($"Command: `{c.Last!.Command}` · exit code {(c.Last.ExitCode?.ToString() ?? "n/a")} · ran {c.RanAt?.ToLocalTime():HH:mm:ss}").AppendLine();
            sb.AppendLine("```").AppendLine(PageParts.Sanitize(PageParts.Shorten(c.Last.Output, 8000)).Replace("```", "'''")).AppendLine("```");
        }
        return sb.ToString();
    }

    public static string Json(IReadOnlyList<DiagnosticCheck> checks, string? context)
    {
        var (version, _, built) = PageParts.AppInfo();
        var data = new
        {
            schema = "material-docker-image/diagnostics-report",
            version = 1,
            generated = DateTimeOffset.Now,
            app = new { version, build = built },
            windows = Environment.OSVersion.VersionString,
            context = context ?? "CLI default",
            sanitized = "No credentials or environment variable values; the user profile path is replaced by %USERPROFILE%.",
            checks = checks.Select(c => new
            {
                id = c.Id, label = c.Label, status = c.Status.ToString(), ranAt = c.RanAt,
                value = c.Last?.Value, detail = c.Last == null ? null : PageParts.Sanitize(c.Last.Detail),
                command = c.Last?.Command, exitCode = c.Last?.ExitCode,
                output = c.Last == null ? null : PageParts.Sanitize(PageParts.Shorten(c.Last.Output, 8000))
            })
        };
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Cell(string s) => s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    /// <summary>Shows the preview overlay; saving happens only after the user reviews the exact content.</summary>
    public static void Preview(IReadOnlyList<DiagnosticCheck> checks, string? context)
    {
        if (checks.All(c => c.Last == null)) { Host.Notify.Info("Run the checks first so the report has real results."); return; }
        var format = "Markdown";
        var view = new ReadOnlySearchText("Search report", 300, 460);
        var current = "";
        void Render() => view.Text = current = format == "JSON" ? Json(checks, context) : Markdown(checks, context);
        Render();
        Action? close = null;
        var save = Ui.Button("Save…", Icons.Save, ButtonKind.Filled, () =>
        {
            var json = format == "JSON";
            var dialog = new SaveFileDialog { FileName = $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.{(json ? "json" : "md")}", Filter = json ? "JSON|*.json" : "Markdown|*.md" };
            if (dialog.ShowDialog() != true) return;
            File.WriteAllText(dialog.FileName, current, new UTF8Encoding(false));
            Host.History.Record("diagnostics", $"Exported sanitized diagnostics report ({format})");
            Host.Notify.Success("Saved the sanitized report to " + PageParts.Sanitize(dialog.FileName) + ".");
            close!();
        });
        var actions = Ui.HStack(8, Ui.Button("Cancel", null, ButtonKind.Text, () => close!()), save);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var body = Ui.VStack(14,
            Ui.Title("Export sanitized report"),
            Ui.Caption("Review exactly what will be saved. Credentials and environment variable values are never included, and your user profile path appears as %USERPROFILE%."),
            Ui.Segmented(["Markdown", "JSON"], format, v => { format = v; Render(); }),
            view, actions);
        close = Host.Overlay.Show(body, 860);
    }
}
