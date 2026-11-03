using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MaterialDockerImage.Services;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Pages;

/// <summary>Registry names found in the Docker CLI configuration file. Credential values are never read or kept.</summary>
public sealed record RegistryAuthEntry(string Registry, string Source);
public sealed record RegistryAuthFile(string Path, bool FromDockerConfigVariable, bool Exists, IReadOnlyList<RegistryAuthEntry> Entries, string? CredentialStore, string? Error);

/// <summary>Small helpers shared by the Registry, Diagnostics, Tools, Activity, Settings and Setup pages.</summary>
internal static class PageParts
{
    /// <summary>A titled card section with optional trailing actions that wrap at narrow widths.</summary>
    public static Border Section(string title, string? subtitle, UIElement body, params UIElement[] actions)
    {
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Ui.VStack(2, Ui.Text(title, 16, FontWeights.SemiBold), subtitle == null ? null : Ui.Caption(subtitle)));
        if (actions.Length > 0)
        {
            var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            foreach (var a in actions) { if (a is FrameworkElement fe) fe.Margin = new Thickness(8, 2, 0, 2); wrap.Children.Add(a); }
            head.Children.Add(wrap.At(0, 1));
        }
        return Ui.Card(Ui.VStack(14, head, body), 20, "SurfaceLow", 16);
    }

    /// <summary>Two columns side by side when wide enough, stacked otherwise.</summary>
    public static Grid Responsive(UIElement first, UIElement second, double breakpoint = 1100, double firstWeight = 1, double secondWeight = 1, double gap = 16)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(firstWeight, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(secondWeight, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(first); grid.Children.Add(second);
        var wide = (bool?)null;
        void Arrange()
        {
            var now = grid.ActualWidth <= 0 || grid.ActualWidth >= breakpoint;
            if (wide == now) return;
            wide = now;
            first.At(0, 0, 1, now ? 1 : 3);
            second.At(now ? 0 : 1, now ? 2 : 0, 1, now ? 1 : 3);
            if (second is FrameworkElement fe) fe.Margin = new Thickness(0, now ? 0 : gap, 0, 0);
        }
        grid.SizeChanged += (_, _) => Arrange();
        Arrange();
        return grid;
    }

    /// <summary>Opens a folder, file or https address with the shell, reporting failures instead of throwing.</summary>
    public static void OpenExternal(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException or PlatformNotSupportedException)
        { AppHost.Current.Notify.Error("Could not open " + target + ": " + e.Message); }
    }

    public static void OpenFolder(string path)
    {
        try { Directory.CreateDirectory(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppHost.Current.Notify.Error("The folder could not be created: " + e.Message); return; }
        OpenExternal(path);
    }

    /// <summary>Version and build timestamp from assembly metadata. Null timestamp means it was not recorded at build time.</summary>
    public static (string Version, string? Informational, DateTimeOffset? Built) AppInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString(3) ?? "unavailable";
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var stamp = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(x => x.Key == "BuildTimestamp")?.Value;
        return (version, info, DateTimeOffset.TryParse(stamp, out var t) ? t : null);
    }

    public static string BuildText(DateTimeOffset? built) => built is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") : "not recorded in this build";

    /// <summary>Reads only the registry names (keys of auths and credHelpers) and the credsStore helper name.</summary>
    public static RegistryAuthFile ReadAuthFile()
    {
        var configured = Environment.GetEnvironmentVariable("DOCKER_CONFIG");
        var fromVariable = !string.IsNullOrWhiteSpace(configured);
        var folder = fromVariable ? configured! : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker");
        var path = Path.Combine(folder, "config.json");
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new(path, fromVariable, false, [], null, null);
            if (info.Length > 4 * 1024 * 1024) return new(path, fromVariable, true, [], null, "The file is larger than 4 MiB and was not read.");
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var entries = new List<RegistryAuthEntry>();
            string? store = null;
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("auths", out var auths) && auths.ValueKind == JsonValueKind.Object)
                    foreach (var p in auths.EnumerateObject()) entries.Add(new(p.Name, "auths"));
                if (document.RootElement.TryGetProperty("credHelpers", out var helpers) && helpers.ValueKind == JsonValueKind.Object)
                    foreach (var p in helpers.EnumerateObject()) entries.Add(new(p.Name, p.Value.ValueKind == JsonValueKind.String ? "credential helper " + p.Value.GetString() : "credential helper"));
                if (document.RootElement.TryGetProperty("credsStore", out var s) && s.ValueKind == JsonValueKind.String) store = s.GetString();
            }
            return new(path, fromVariable, true, entries, store, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(path, fromVariable, true, [], null, e is JsonException ? "The file is not valid JSON." : e.Message);
        }
    }

    /// <summary>Display form of the configuration path that does not reveal the user profile or environment values.</summary>
    public static string DisplayPath(RegistryAuthFile file) => file.FromDockerConfigVariable ? "%DOCKER_CONFIG%\\config.json" : Sanitize(file.Path);

    private static readonly Regex UrlCredentials = new(@"(?<scheme>[a-z][a-z0-9+.-]*://)[^/\s@]+@", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SecretPairs = new(@"(?<key>""?(auth|password|passwd|secret|token|identitytoken|registrytoken)""?\s*[:=]\s*)(""[^""]*""|\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Replaces the user profile path with %USERPROFILE% and masks URL user information and secret-looking pairs.</summary>
    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3)
        {
            // Plain, forward-slash and JSON-escaped spellings of the profile path.
            foreach (var form in new[] { profile.Replace("\\", "\\\\"), profile, profile.Replace('\\', '/') }.Distinct())
                text = text.Replace(form, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }
        text = UrlCredentials.Replace(text, m => m.Groups["scheme"].Value + "***@");
        text = SecretPairs.Replace(text, m => m.Groups["key"].Value + "\"(redacted)\"");
        return text;
    }

    /// <summary>Copies every saved property from one settings object to another (used by restore and import).</summary>
    public static void CopySettings(UserSettings from, UserSettings to)
    {
        foreach (var property in typeof(UserSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.SetMethod?.IsPublic != true) continue;
            if (property.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null) continue;
            property.SetValue(to, property.GetValue(from));
        }
        to.Normalize();
    }

    public static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    /// <summary>A read-only monospace text block for command output, selectable and copyable.</summary>
    public static TextBox OutputBox(string text, double maxHeight = 260)
    {
        var box = Ui.CodeBox(text, 0);
        box.IsReadOnly = true; box.MaxHeight = maxHeight; box.MinHeight = 40;
        return box;
    }
}
