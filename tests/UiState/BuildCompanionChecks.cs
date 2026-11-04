using BuildCompanion;

/// <summary>Build companion checks: validation, argument building, state mapping, real process streaming and cancellation.</summary>
public static class BuildCompanionChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    static void Reject(Action action, string name)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { count++; Console.WriteLine("PASS " + name); return; }
        throw new Exception("Expected refusal: " + name);
    }

    public static int Run()
    {
        count = 0;
        Check(ArgumentValidation.IsValidVersion("1.2.3") && ArgumentValidation.IsValidVersion("0.140.1"), "companion accepts three numeric version parts");
        foreach (var badVersion in new[] { "1.2", "1.2.3.4", "v1.2.3", "1.2.x", "1.2.3\n", "1.2.3 -Tests", "1.2.3&calc", "", "\u0661.2.3", " 1.2.3", "1.2.3-beta" })
            Check(!ArgumentValidation.IsValidVersion(badVersion), "companion rejects version '" + badVersion.Replace("\n", "\\n") + "'");
        Check(ArgumentValidation.NormalizeVersion("  ") == null && ArgumentValidation.NormalizeVersion(" 2.0.1 ") == "2.0.1", "companion treats a blank version as absent and trims a valid one");
        Reject(() => ArgumentValidation.NormalizeVersion("1.2.3;x"), "companion refuses an injected version");

        var root = Path.Combine(Path.GetTempPath(), "companion repo " + Guid.NewGuid().ToString("N"));
        var plain = BuildPlan.Create(BuildAction.Build, root, null);
        Check(plain.Steps.Count == 1 && plain.Steps[0].FileName == "cmd.exe" && plain.Steps[0].Arguments == "/d /c \"\"" + Path.Combine(root, "build.bat") + "\"\"", "build without a version runs the exact quoted build.bat through cmd /c");
        var versioned = BuildPlan.Create(BuildAction.Build, root, "1.2.3");
        Check(versioned.Steps[0].Arguments.EndsWith("build.bat\" -Version 1.2.3\"") && versioned.Steps[0].WorkingDirectory == root, "build with a version appends -Version and keeps the repository as working directory");
        var run = BuildPlan.Create(BuildAction.BuildAndRun, root, "1.2.3");
        Check(run.Steps.Count == 2 && !run.Steps[0].IsLaunch && run.Steps[1].IsLaunch && run.Steps[1].FileName == Path.Combine(root, "out", "publish", "MaterialDockerImage.exe"), "build and run builds first and then launches out/publish/MaterialDockerImage.exe");
        var installer = BuildPlan.Create(BuildAction.BuildInstaller, root, "1.2.3");
        Check(installer.Steps.Count == 1 && installer.Steps[0].Arguments.Contains("build-installer.bat\" -Version 1.2.3"), "installer action runs build-installer.bat with the version");
        Reject(() => BuildPlan.Create(BuildAction.BuildInstaller, root, null), "installer without a version is refused");
        Reject(() => BuildPlan.Create(BuildAction.Build, root, "1.2.3 && del *"), "injected version never reaches a command line");
        foreach (var unsafeRoot in new[] { "relative\\path", "C:\\a\"b", "C:\\100%\\x", "C:\\a^b", "C:\\a|b", "C:\\a!b", "" })
            Reject(() => BuildPlan.Create(BuildAction.Build, unsafeRoot, null), "unsafe repository path refused: " + unsafeRoot);

        Check(RunStates.FromExit(0, false, false) == RunState.Succeeded, "exit code 0 is the only success");
        Check(RunStates.FromExit(1, false, false) == RunState.Failed && RunStates.FromExit(-1, false, false) == RunState.Failed, "non-zero exit codes are failures");
        Check(RunStates.FromExit(null, false, false) == RunState.Failed, "a missing exit code is never success");
        Check(RunStates.FromExit(0, true, false) == RunState.Cancelled, "cancellation beats exit code 0");
        Check(RunStates.FromExit(0, false, true) == RunState.LaunchFailed, "a launch failure is never success");

        Check(PhaseTracker.Classify("  Determining projects to restore...") == PhaseTracker.Phase.Compiling && PhaseTracker.Classify("Ready: .NET SDK 10") == PhaseTracker.Phase.Dependencies && PhaseTracker.Classify("nuget pack x") == PhaseTracker.Phase.Packaging, "phase classifier recognises dependency, compile and package output");
        var tracker = new PhaseTracker();
        tracker.Observe("nuget pack");
        tracker.Observe("Ready: .NET");
        Check(tracker.Current == PhaseTracker.Phase.Packaging, "phase only moves forward");

        Check(CompanionStrings.Keys.All(CompanionStrings.IsComplete), "every companion string has English and Cantonese text");
        Check(CompanionStrings.Get("build", CompanionLanguage.Both).Contains(" · ") && CompanionStrings.Get("missing-key", CompanionLanguage.English) == "missing-key", "bilingual mode joins both languages and unknown keys stay visible");
        Check(CompanionStrings.Next(CompanionLanguage.Both) == CompanionLanguage.English, "language cycle returns to English");

        Check(RepoLocator.ReadRepoArgument(new[] { "--repo", "C:\\x" }) == "C:\\x" && RepoLocator.ReadRepoArgument(new[] { "--repo=C:\\y" }) == "C:\\y" && RepoLocator.ReadRepoArgument(new[] { "-x" }) == null, "repository argument parsing");
        Check(RepoLocator.Find(new[] { "--repo", "relative" }, "", _ => true).Error is not null, "relative --repo path refused");
        Check(RepoLocator.Find(new[] { "--repo", root }, "", _ => false).Path == null, "--repo without build.bat is refused");
        Check(RepoLocator.Find(new[] { "--repo", root }, "", d => d == root).Path == root, "valid --repo is used");
        var walkStart = Path.Combine(root, "a", "b");
        Check(RepoLocator.Find(Array.Empty<string>(), walkStart, d => d == root).Path == root, "repository is found by walking up from the executable directory");
        Check(RepoLocator.Find(Array.Empty<string>(), walkStart, _ => false).Source == "none", "no repository found asks the user");

        RunProcessChecks();
        return count;
    }

    // Real process runs against a throwaway repository whose build.bat is a stand-in. The companion's cmd /c route is exercised for real.
    static void RunProcessChecks()
    {
        var fake = Path.Combine(Path.GetTempPath(), "companion fake " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fake);
        try
        {
            File.WriteAllText(Path.Combine(fake, "build.bat"), "@echo off\r\necho Determining projects to restore...\r\necho args=%*\r\necho problem 1>&2\r\nexit /b %FAKE_EXIT%\r\n");
            File.WriteAllText(Path.Combine(fake, "build-installer.bat"), "@echo off\r\necho args=%*\r\nexit /b 0\r\n");
            Check(!SetupCheck.CanBuild(SetupCheck.Inspect(fake)), "setup check reports missing download-dependencies.bat");
            File.WriteAllText(Path.Combine(fake, "download-dependencies.bat"), "@echo off\r\n");
            var setup = SetupCheck.Inspect(fake);
            Check(SetupCheck.CanBuild(setup) && !setup.First(x => x.Key == "dotnet").Present && !setup.First(x => x.Key == "node").Present, "setup check allows a build while .tools files are still to be created");
            Directory.CreateDirectory(Path.Combine(fake, ".tools"));
            File.WriteAllText(Path.Combine(fake, ".tools", "dotnet-path.txt"), "x");
            Check(SetupCheck.Inspect(fake).First(x => x.Key == "dotnet").Present, "setup check sees .tools/dotnet-path.txt");

            Environment.SetEnvironmentVariable("FAKE_EXIT", "0");
            var lines = new System.Collections.Concurrent.ConcurrentQueue<LogLine>();
            var launched = new List<string>();
            var runner = new BuildRunner(step => { launched.Add(step.FileName); return null; });
            var ok = runner.RunAsync(BuildPlan.Create(BuildAction.Build, fake, "4.5.6"), lines.Enqueue, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(ok.State == RunState.Succeeded && ok.ExitCode == 0, "real build.bat run with exit 0 succeeds");
            Check(lines.Any(l => !l.IsError && l.Text.Contains("args=-Version 4.5.6")), "stdout streams and carries the validated -Version argument");
            Check(lines.Any(l => l.IsError && l.Text.Contains("problem")), "stderr streams separately");

            Environment.SetEnvironmentVariable("FAKE_EXIT", "7");
            var bad = runner.RunAsync(BuildPlan.Create(BuildAction.BuildAndRun, fake, null), _ => { }, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(bad.State == RunState.Failed && bad.ExitCode == 7 && launched.Count == 0, "exit 7 is a failure, reports the real code and never launches the application");

            Environment.SetEnvironmentVariable("FAKE_EXIT", "0");
            var noApp = runner.RunAsync(BuildPlan.Create(BuildAction.BuildAndRun, fake, null), _ => { }, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(noApp.State == RunState.LaunchFailed && launched.Count == 0, "a missing published application is a launch failure, not success");

            Directory.CreateDirectory(Path.Combine(fake, "out", "publish"));
            File.WriteAllText(Path.Combine(fake, "out", "publish", "MaterialDockerImage.exe"), "stand-in");
            var withApp = runner.RunAsync(BuildPlan.Create(BuildAction.BuildAndRun, fake, null), _ => { }, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(withApp.State == RunState.Succeeded && launched.Count == 1, "build and run launches the application only after exit 0 (launcher injected, no window opened)");

            File.WriteAllText(Path.Combine(fake, "build.bat"), "@echo off\r\necho started\r\nping -n 60 127.0.0.1 >nul\r\nexit /b 0\r\n");
            using var cts = new CancellationTokenSource();
            var cancelLines = new System.Collections.Concurrent.ConcurrentQueue<LogLine>();
            var task = runner.RunAsync(BuildPlan.Create(BuildAction.Build, fake, null), cancelLines.Enqueue, null, cts.Token);
            var waited = 0;
            while (!cancelLines.Any(l => l.Text.Contains("started")) && waited++ < 100) Thread.Sleep(100);
            var before = DateTime.UtcNow;
            cts.Cancel();
            var cancelled = task.GetAwaiter().GetResult();
            Check(cancelled.State == RunState.Cancelled && (DateTime.UtcNow - before).TotalSeconds < 20, "cancel kills the process tree promptly and reports Cancelled, never success");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_EXIT", null);
            try { Directory.Delete(fake, true); } catch (IOException) { }
        }
    }
}
