using System.Diagnostics;
using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>Finds and starts Visual Studio Code (or the editor configured in settings) without a shell.</summary>
public static class ExternalEditor
{
    private static string? cachedCode;
    private static bool searched;

    /// <summary>Locates the VS Code command on PATH using where.exe. Returns null when it is not installed.</summary>
    public static async Task<string?> FindVsCodeAsync()
    {
        if (searched) return cachedCode;
        cachedCode = await Task.Run(() =>
        {
            try
            {
                var where = Path.Combine(Environment.SystemDirectory, "where.exe");
                if (!File.Exists(where)) return null;
                var info = new ProcessStartInfo(where) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                info.ArgumentList.Add("code");
                using var process = Process.Start(info);
                if (process == null) return null;
                var output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(5000)) { try { process.Kill(); } catch (InvalidOperationException) { } return null; }
                if (process.ExitCode != 0) return null;
                var candidates = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(File.Exists).ToList();
                // code.cmd lives in <install>\bin; prefer the Code.exe beside that folder so no batch interpreter is involved.
                foreach (var candidate in candidates)
                {
                    var exe = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(candidate) ?? "", "..", "Code.exe"));
                    if (File.Exists(exe)) return exe;
                }
                return candidates.FirstOrDefault(c => c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ?? candidates.FirstOrDefault();
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException) { return null; }
        });
        searched = true;
        return cachedCode;
    }

    /// <summary>Opens <paramref name="path"/> in the configured editor or VS Code. Returns a user-facing problem, or null on success.</summary>
    public static async Task<string?> OpenAsync(string path, string configuredEditor)
    {
        string? executable = null;
        if (!string.IsNullOrWhiteSpace(configuredEditor))
        {
            if (!File.Exists(configuredEditor)) return $"The external editor in Settings ({configuredEditor}) was not found. Update it in Settings or clear it to use VS Code.";
            executable = configuredEditor;
        }
        executable ??= await FindVsCodeAsync();
        if (executable == null) return "Visual Studio Code was not found on PATH (where code found nothing). Install VS Code with the \"Add to PATH\" option, or set an external editor in Settings.";
        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && path.IndexOfAny(['&', '|', '<', '>', '^', '%', '"', '!']) >= 0)
            return "This path contains characters that the VS Code command script cannot receive safely. Rename the folder or set Code.exe as the external editor in Settings.";
        try
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) };
            info.ArgumentList.Add(path);
            using var _ = Process.Start(info);
            return null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return Loc.T("The editor could not be started: ") + e.Message;
        }
    }
}
