using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MaterialDockerImage.UI;

/// <summary>A Compose or Dockerfile port specification: [ip:][host:]container[/protocol].</summary>
public sealed record EditorPortSpec(string Ip, string Host, string Container, string Protocol)
{
    public string ToDockerfile() => Container + (Protocol is "" or "tcp" ? "" : "/" + Protocol);
    public string ToCompose()
    {
        var sb = new StringBuilder();
        if (Ip.Length > 0) sb.Append(Ip.Contains(':') && !Ip.StartsWith('[') ? "[" + Ip + "]" : Ip).Append(':');
        if (Host.Length > 0 || Ip.Length > 0) sb.Append(Host).Append(':');
        sb.Append(Container);
        if (Protocol is not ("" or "tcp")) sb.Append('/').Append(Protocol);
        return sb.ToString();
    }
}

/// <summary>Parsing and formatting for picker values (permissions, durations, ports, colours, paths, secrets).</summary>
public static class EditorValueFormats
{
    private static readonly Regex HexRx = new(@"#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})(?![0-9A-Za-z_])", RegexOptions.CultureInvariant);
    private static readonly Regex RgbRx = new(@"rgb\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex DurationRx = new(@"^(?:(\d+(?:\.\d+)?)(ms|h|m|s))+$", RegexOptions.CultureInvariant);
    private static readonly Regex DurationPartRx = new(@"(\d+(?:\.\d+)?)(ms|h|m|s)", RegexOptions.CultureInvariant);

    // ------------------------------------------------------------------ permissions

    /// <summary>Parses octal (755, 0644) or assignment-style symbolic permissions (u=rwx,g=rx,o=). Index = who*3 + r/w/x.</summary>
    public static bool[]? ParsePermissions(string text)
    {
        text = text.Trim();
        var bits = new bool[9];
        if (Regex.IsMatch(text, "^[0-7]{3,4}$"))
        {
            var digits = text[^3..];
            for (var who = 0; who < 3; who++)
            {
                var d = digits[who] - '0';
                bits[who * 3] = (d & 4) != 0; bits[who * 3 + 1] = (d & 2) != 0; bits[who * 3 + 2] = (d & 1) != 0;
            }
            return bits;
        }
        if (text.Length == 0) return null;
        foreach (var clause in text.Split(','))
        {
            var m = Regex.Match(clause, "^([ugoa]+)=([rwxX]*)$");
            if (!m.Success) return null;
            foreach (var w in m.Groups[1].Value)
            {
                var targets = w == 'a' ? new[] { 0, 1, 2 } : new[] { "ugo".IndexOf(w) };
                foreach (var t in targets)
                {
                    bits[t * 3] = m.Groups[2].Value.Contains('r');
                    bits[t * 3 + 1] = m.Groups[2].Value.Contains('w');
                    bits[t * 3 + 2] = m.Groups[2].Value.Contains('x') || m.Groups[2].Value.Contains('X');
                }
            }
        }
        return bits;
    }

    public static string ToOctal(bool[] bits, bool leadingZero)
    {
        var sb = new StringBuilder(leadingZero ? "0" : "");
        for (var who = 0; who < 3; who++) sb.Append((bits[who * 3] ? 4 : 0) + (bits[who * 3 + 1] ? 2 : 0) + (bits[who * 3 + 2] ? 1 : 0));
        return sb.ToString();
    }

    public static string ToSymbolic(bool[] bits)
    {
        var parts = new List<string>();
        for (var who = 0; who < 3; who++)
            parts.Add("ugo"[who] + "=" + (bits[who * 3] ? "r" : "") + (bits[who * 3 + 1] ? "w" : "") + (bits[who * 3 + 2] ? "x" : ""));
        return string.Join(",", parts);
    }

    // ------------------------------------------------------------------ durations

    /// <summary>Parses a Go-style duration as used by HEALTHCHECK and Compose (1m30s, 500ms, 2h).</summary>
    public static TimeSpan? ParseDuration(string text)
    {
        text = text.Trim().Trim('"', '\'');
        if (text.Length == 0 || !DurationRx.IsMatch(text)) return null;
        var total = 0.0;
        foreach (Match m in DurationPartRx.Matches(text))
        {
            var n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            total += m.Groups[2].Value switch { "ms" => n, "s" => n * 1000, "m" => n * 60_000, _ => n * 3_600_000 };
        }
        return TimeSpan.FromMilliseconds(total);
    }

    public static string FormatDuration(TimeSpan value)
    {
        if (value <= TimeSpan.Zero) return "0s";
        var sb = new StringBuilder();
        if (value.TotalHours >= 1) sb.Append((int)value.TotalHours).Append('h');
        if (value.Minutes > 0) sb.Append(value.Minutes).Append('m');
        if (value.Seconds > 0) sb.Append(value.Seconds).Append('s');
        if (value.Milliseconds > 0) sb.Append(value.Milliseconds).Append("ms");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ ports

    /// <summary>Parses EXPOSE (80/udp) or Compose short syntax ([ip:][host:]container[/protocol]). Returns null when malformed.</summary>
    public static EditorPortSpec? ParsePort(string text)
    {
        text = text.Trim().Trim('"', '\'');
        if (text.Length == 0) return new("", "", "", "tcp");
        var protocol = "tcp";
        var slash = text.LastIndexOf('/');
        if (slash >= 0) { protocol = text[(slash + 1)..].ToLowerInvariant(); text = text[..slash]; }
        if (protocol is not ("tcp" or "udp" or "sctp")) return null;
        string ip = "";
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close < 0) return null;
            ip = text[1..close];
            text = text[(close + 1)..].TrimStart(':');
        }
        var parts = text.Split(':');
        string host = "", container;
        if (ip.Length > 0) { if (parts.Length == 2) { host = parts[0]; container = parts[1]; } else container = parts[0]; }
        else if (parts.Length == 1) container = parts[0];
        else if (parts.Length == 2) { host = parts[0]; container = parts[1]; }
        else if (parts.Length == 3) { ip = parts[0]; host = parts[1]; container = parts[2]; }
        else return null;
        if (!IsPortOrRange(container) || (host.Length > 0 && !IsPortOrRange(host))) return null;
        return new(ip, host, container, protocol);
    }

    public static bool IsPortOrRange(string text)
    {
        var m = Regex.Match(text, @"^(\d{1,5})(?:-(\d{1,5}))?$");
        if (!m.Success) return false;
        var a = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var b = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : a;
        return a is >= 1 and <= 65535 && b is >= 1 and <= 65535 && b >= a;
    }

    /// <summary>Host ports published by running containers, from docker ps "Ports" text such as "0.0.0.0:8080->80/tcp, [::]:8080->80/tcp".</summary>
    public static IEnumerable<(int Port, string Protocol)> PublishedHostPorts(string portsColumn)
    {
        foreach (Match m in Regex.Matches(portsColumn ?? "", @":(\d{1,5})(?:-(\d{1,5}))?->\d{1,5}(?:-\d{1,5})?/(tcp|udp|sctp)"))
        {
            var a = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var b = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : a;
            for (var p = a; p <= b && p - a < 1000; p++) yield return (p, m.Groups[3].Value);
        }
    }

    // ------------------------------------------------------------------ colours

    /// <summary>Finds #RGB, #RRGGBB or rgb(r, g, b) on the caret's line touching the caret.</summary>
    public static bool TryFindColour(string text, int caret, out int start, out int length)
    {
        start = length = 0;
        var lineStart = SuggestionText.LineStart(text, caret);
        var lineEnd = SuggestionText.LineEnd(text, caret);
        if (lineEnd < lineStart) return false;
        var line = text[lineStart..lineEnd];
        var column = caret - lineStart;
        foreach (var rx in new[] { HexRx, RgbRx })
            foreach (Match m in rx.Matches(line))
            {
                if (m.Index > column || m.Index + m.Length < column) continue;
                if (rx == HexRx && m.Index > 0 && char.IsLetterOrDigit(line[m.Index - 1])) continue;
                if (rx == RgbRx && !m.Groups.Cast<Group>().Skip(1).All(g => int.Parse(g.Value, CultureInfo.InvariantCulture) <= 255)) continue;
                start = lineStart + m.Index; length = m.Length;
                return true;
            }
        return false;
    }

    /// <summary>Parses a colour found by <see cref="TryFindColour"/>.</summary>
    public static bool TryParseColour(string text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var m = RgbRx.Match(text);
        if (m.Success && m.Index == 0 && m.Length == text.Length)
        {
            var v = m.Groups.Cast<Group>().Skip(1).Select(x => int.Parse(x.Value, CultureInfo.InvariantCulture)).ToArray();
            if (v.Any(x => x > 255)) return false;
            r = (byte)v[0]; g = (byte)v[1]; b = (byte)v[2];
            return true;
        }
        if (!HexRx.IsMatch(text) || text.Length is not (4 or 7)) return false;
        var hex = text[1..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        r = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        g = byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        b = byte.Parse(hex[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Formats a colour in the same notation (and letter case) as the original text.</summary>
    public static string FormatColour(string original, byte r, byte g, byte b)
    {
        if (original.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)) return $"{original[..3]}({r}, {g}, {b})";
        var hex = $"#{r:X2}{g:X2}{b:X2}";
        return original.Skip(1).Any(char.IsLower) ? hex.ToLowerInvariant() : hex;
    }

    // ------------------------------------------------------------------ paths

    /// <summary>
    /// Converts a chosen file or folder to a path relative to <paramref name="root"/> with forward slashes. Returns
    /// false with a reason when the path is outside the root and <paramref name="allowOutside"/> is false.
    /// </summary>
    public static bool TryRelativePath(string root, string chosen, bool allowOutside, out string relative, out string error)
    {
        relative = ""; error = "";
        if (string.IsNullOrWhiteSpace(root)) { error = "No build context folder is set. Choose one first."; return false; }
        var fullRoot = Path.GetFullPath(root);
        var fullChosen = Path.GetFullPath(chosen);
        var rel = Path.GetRelativePath(fullRoot, fullChosen);
        var outside = Path.IsPathRooted(rel) || rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || rel.StartsWith("../", StringComparison.Ordinal);
        if (outside && !allowOutside)
        {
            error = $"{fullChosen} is outside the build context {fullRoot}. COPY and ADD can only read files inside the context; move the file into it or choose another context folder.";
            return false;
        }
        relative = rel.Replace('\\', '/');
        if (relative.Length == 0) relative = ".";
        return true;
    }

    // ------------------------------------------------------------------ secrets

    /// <summary>
    /// Rewrites an ARG or ENV that carries a secret-looking variable into a RUN --mount=type=secret starting point.
    /// The secret's value is dropped (never copied). Other variables on the same instruction are kept.
    /// </summary>
    public static SuggestionEdit SecretRewrite(string text, EditorValueSpan span)
    {
        var scan = DockerfileCaretScan.Run(text, span.Start);
        var instruction = scan.Current ?? throw new InvalidOperationException("No ARG or ENV instruction at the caret.");
        var keyword = instruction.EffectiveKeyword;
        var args = instruction.Args;
        var name = span.SecretName;
        var id = name.ToLowerInvariant().Replace('_', '-');
        var start = instruction.Tokens[0].Start;
        var end = instruction.Tokens[^1].End;
        var legacyEnv = keyword == "ENV" && args.Count > 0 && !args[0].Text.Contains('=');
        var keep = legacyEnv ? new List<string>() : args.Where(a => !a.Text.Split('=')[0].Equals(name, StringComparison.Ordinal)).Select(a => a.Text).ToList();
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var sb = new StringBuilder();
        var prefix = instruction.Keyword.Equals("ONBUILD", StringComparison.OrdinalIgnoreCase) ? instruction.Keyword + " " + instruction.Tokens[1].Text : instruction.Keyword;
        if (keep.Count > 0) sb.Append(prefix).Append(' ').Append(string.Join(" ", keep)).Append(newline);
        sb.Append("# ").Append(name).Append(" is a build secret: build with --secret id=").Append(id).Append(",env=").Append(name).Append(newline);
        sb.Append("RUN --mount=type=secret,id=").Append(id).Append(",env=").Append(name).Append(' ');
        return new SuggestionEdit(start, end - start, sb.ToString(), start + sb.Length);
    }
}
