using System.Diagnostics;
using System.Text.Json;
using MaterialDockerImage.UI;

/// <summary>
/// Checks the version remarks of the flavor comparison against real engines, when they are installed. Every Python rule is probed on Python 3.10, 3.11, 3.13
/// and 3.14 through the Windows <c>py</c> launcher, and every ECMAScript rule's "2018 and later" side on the installed Node. The probe and its subject go to the
/// engine on standard input as JSON, so nothing is quoted on a command line. An engine that is not installed prints SKIP and counts nothing, so a missing
/// interpreter is never mistaken for a pass. The ECMAScript 2017 side has no engine on a current machine and stays backed by the specification only.
/// </summary>
public static class RegexFlavorVersionToolChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    // Prints Y when the pattern compiles and matches the subject, X when it compiles without a match, N when the engine rejects it.
    const string PythonProbe = "import re,sys,json\nd=json.load(sys.stdin)\ntry:\n  print('Y' if re.search(d['p'], d['s']) else 'X')\nexcept re.error:\n  print('N')\n";
    const string NodeProbe = "let t='';process.stdin.on('data',c=>t+=c).on('end',()=>{const d=JSON.parse(t);try{console.log(new RegExp(d.p,d.f).test(d.s)?'Y':'X')}catch(e){console.log('N')}})";

    // The version each rank of the table stands for, and the interpreter that represents it here.
    static readonly (string VersionId, string Launcher)[] PythonVersions = [("py39", "-3.10"), ("py311", "-3.11"), ("py311", "-3.13"), ("py314", "-3.14")];

    public static int Run()
    {
        count = 0;
        Python();
        Node();
        return count;
    }

    static string? Execute(string file, IEnumerable<string> args, string input)
    {
        try
        {
            var info = new ProcessStartInfo(file) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var process = Process.Start(info)!;
            process.StandardInput.Write(input);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(20_000) && process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    static string Expected(FlavorSupport support) => support == FlavorSupport.No ? "N" : "Y";

    static void Python()
    {
        var constructs = new[] { "possessive", "atomic", "anchor-z" };
        foreach (var (versionId, launcher) in PythonVersions)
        {
            if (Execute("py", [launcher, "-c", "print('ok')"], "") != "ok") { Console.WriteLine($"SKIP Python {launcher[1..]} is not installed, so its version remarks were not probed"); continue; }
            foreach (var id in constructs)
            {
                var construct = RegexFlavors.Construct(id);
                var expected = Expected(RegexFlavorVersions.Cell(id, "python", versionId).Support);
                var actual = Execute("py", [launcher, "-c", PythonProbe], JsonSerializer.Serialize(new { p = construct.Probe, s = construct.ProbeSubject }));
                Check(actual == expected, $"Python {launcher[1..]} agrees with the version table on {construct.Label} (table {expected}, engine {actual})");
            }
        }
    }

    static void Node()
    {
        var node = Environment.GetEnvironmentVariable("REGEX_NODE") is { Length: > 0 } custom ? custom : "node";
        if (Execute(node, ["-e", "console.log('ok')"], "") != "ok") { Console.WriteLine("SKIP Node is not installed, so the ECMAScript 2018 remarks were not probed"); return; }
        // Inline (?s) and the u flag are how the table phrases these two in JavaScript: the s flag replaces (?s), and \p needs u.
        var cases = new (string Id, string Pattern, string Flags)[]
        {
            ("lookbehind", RegexFlavors.Construct("lookbehind").Probe, ""), ("lookbehind-variable", RegexFlavors.Construct("lookbehind-variable").Probe, ""),
            ("named-angle", RegexFlavors.Construct("named-angle").Probe, ""), ("named-backref", RegexFlavors.Construct("named-backref").Probe, ""),
            ("unicode-category", RegexFlavors.Construct("unicode-category").Probe, "u"), ("flag-s", "a.b", "s"),
        };
        foreach (var (id, pattern, flags) in cases)
        {
            var construct = RegexFlavors.Construct(id);
            var expected = Expected(RegexFlavorVersions.Cell(id, "js", "es2018").Support);
            var actual = Execute(node, ["-e", NodeProbe], JsonSerializer.Serialize(new { p = pattern, f = flags, s = construct.ProbeSubject }));
            Check(actual == expected, $"Node agrees with the ECMAScript 2018 row on {construct.Label} (table {expected}, engine {actual})");
        }
        // The inline form is what the "as the s flag, not inline" remark warns about: current JavaScript still rejects (?s).
        Check(Execute(node, ["-e", NodeProbe], JsonSerializer.Serialize(new { p = "(?s)a.b", f = "", s = "a\nb" })) == "N", "Node still rejects the inline (?s) form, as the remark says");

        // The "ECMAScript 2017 and earlier" side needs an engine of that age, such as Node 8.0.0 (V8 5.8). It is probed only when
        // REGEX_NODE_ES2017 names one, because no current machine ships it; otherwise this side is reported as skipped.
        var old = Environment.GetEnvironmentVariable("REGEX_NODE_ES2017");
        if (old is not { Length: > 0 } || Execute(old, ["-e", "console.log('ok')"], "") != "ok") { Console.WriteLine("SKIP no ECMAScript 2017 engine given in REGEX_NODE_ES2017, so the 2017 rejections were not probed"); return; }
        foreach (var (id, pattern, flags) in cases)
        {
            var construct = RegexFlavors.Construct(id);
            var expected = Expected(RegexFlavorVersions.Cell(id, "js", "es2017").Support);
            var actual = Execute(old, ["-e", NodeProbe], JsonSerializer.Serialize(new { p = pattern, f = flags, s = construct.ProbeSubject }));
            Check(actual == expected, $"an ECMAScript 2017 engine agrees with the 2017 row on {construct.Label} (table {expected}, engine {actual})");
        }
    }
}
