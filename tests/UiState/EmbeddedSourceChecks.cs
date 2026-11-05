using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildCompanion;

/// <summary>
/// The build companion's packed source: unpacking into a commit-named folder, reuse of an intact earlier unpack, repair of a damaged one,
/// refusal of unsafe paths, unlisted files and wrong hashes, and the order in which the companion finds a source folder. Framework-free
/// logic with temporary folders; the published executable's packed copy is proved separately by build/publish-companion.ps1.
/// </summary>
public static class EmbeddedSourceChecks
{
    static int count;
    const string Commit = "0123456789abcdef0123456789abcdef01234567";

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // Builds a zip from (path, content) pairs. The manifest lists manifestFiles (defaults to the same files) with hashes overridable per path.
    static MemoryStream Zip((string Path, string Text)[] files, (string Path, string Text)[]? manifestFiles = null, string commit = Commit, string? badHashFor = null, bool manifest = true)
    {
        var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
            if (manifest)
            {
                var rows = (manifestFiles ?? files).Select(f => new { path = f.Path, sha256 = f.Path == badHashFor ? new string('0', 64) : Sha(new UTF8Encoding(false).GetBytes(f.Text)) });
                using var writer = new StreamWriter(archive.CreateEntry(EmbeddedSource.ManifestName).Open(), new UTF8Encoding(false));
                writer.Write(JsonSerializer.Serialize(new { source = commit, files = rows }));
            }
        }
        memory.Position = 0;
        return memory;
    }

    public static int Run()
    {
        count = 0;
        var root = Path.Combine(Path.GetTempPath(), "mdi-embedded-" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = new[] { ("build.bat", "@echo build\r\n"), ("src/App/Program.cs", "class P {}\n"), ("build/common.ps1", "# helpers\n") };
            var first = EmbeddedSource.Extract(Zip(files), root);
            var target = Path.Combine(root, Commit);
            Check(first.Path == target && first.Source == Commit && first.Error is null, "packed source unpacks into a folder named after its commit");
            Check(File.ReadAllText(Path.Combine(target, "src", "App", "Program.cs")) == "class P {}\n" && File.Exists(Path.Combine(target, EmbeddedSource.ManifestName)), "unpacked files keep their bytes and the manifest travels with them");
            Check(Directory.GetDirectories(root).Length == 1, "no temporary folder is left behind after unpacking");

            var stamp = File.GetLastWriteTimeUtc(Path.Combine(target, "build.bat"));
            Thread.Sleep(20);
            Check(EmbeddedSource.Extract(Zip(files), root).Path == target && File.GetLastWriteTimeUtc(Path.Combine(target, "build.bat")) == stamp, "an intact earlier unpack of the same commit is reused, not rewritten");

            File.WriteAllText(Path.Combine(target, "build.bat"), "@echo tampered\r\n");
            Check(EmbeddedSource.Extract(Zip(files), root).Path == target && File.ReadAllText(Path.Combine(target, "build.bat")) == "@echo build\r\n", "a damaged earlier unpack is replaced by a fresh one");
            File.WriteAllText(Path.Combine(target, "out.log"), "build output");
            Check(EmbeddedSource.IsIntact(target, EmbeddedSource.ReadManifest(new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(target, EmbeddedSource.ManifestName)))))), "files the build adds (out, .tools) do not make an unpack look damaged");

            var other = Path.Combine(Path.GetTempPath(), "mdi-embedded-" + Guid.NewGuid().ToString("N"));
            try
            {
                Check(EmbeddedSource.Extract(Zip(files, badHashFor: "src/App/Program.cs"), other).Error is { } e1 && e1.Contains("hash") && !Directory.Exists(Path.Combine(other, Commit)), "a file whose bytes differ from the manifest is refused and nothing is kept");
                Check(EmbeddedSource.Extract(Zip(files, manifestFiles: files.Take(2).ToArray()), other).Error is { } e2 && e2.Contains("manifest"), "a file the manifest does not list is refused");
                Check(EmbeddedSource.Extract(Zip(files, manifest: false), other).Error is { } e3 && e3.Contains("manifest"), "a zip without a manifest is refused");
                Check(EmbeddedSource.Extract(Zip(files, commit: "not-a-commit"), other).Error is not null, "a manifest without a valid commit is refused");
                Check(EmbeddedSource.Extract(Zip([("../evil.bat", "x"), ("build.bat", "b")]), other).Error is { } e4 && e4.Contains("Unsafe"), "a path that climbs out of the target is refused");
                Check(!Directory.Exists(Path.Combine(Path.GetDirectoryName(other)!, "evil.bat")) && !File.Exists(Path.Combine(Path.GetDirectoryName(other)!, "evil.bat")), "nothing is written outside the target by a climbing path");
            }
            finally { if (Directory.Exists(other)) Directory.Delete(other, recursive: true); }

            foreach (var bad in new[] { "/abs.bat", "a\\b.bat", "C:x.bat", "a/../b", "a/./b", "a//b", "file.txt:stream", "a /b", "trailing./x", "" })
                Check(!EmbeddedSource.IsSafeEntry(bad), "unsafe packed path refused: '" + bad + "'");
            Check(EmbeddedSource.IsSafeEntry("src/MaterialDockerImage/App.xaml.cs") && EmbeddedSource.IsSafeEntry(".github/workflows/release.yml"), "ordinary source paths, including dot folders, are accepted");

            // Order: --repo, then a walk up from the executable, then the packed source; the folder picker only when all three give nothing.
            var packedCalls = 0;
            EmbeddedSourceResult? Packed() { packedCalls++; return new EmbeddedSourceResult(target, Commit, null); }
            var walk = RepoLocator.Find([], @"C:\Repo\out\companion", dir => dir == @"C:\Repo", Packed);
            Check(walk.Source == "walk" && walk.Path == @"C:\Repo" && packedCalls == 0, "a checkout above the executable wins over the packed source, which is not unpacked");
            var packed = RepoLocator.Find([], @"C:\Downloads", dir => dir == target, Packed);
            Check(packed.Source == "embedded" && packed.Path == target && packedCalls == 1, "with no checkout nearby the companion uses its packed source instead of asking for a folder");
            var argument = RepoLocator.Find(["--repo", @"C:\Other"], @"C:\Downloads", dir => dir == @"C:\Other", Packed);
            Check(argument.Source == "argument" && packedCalls == 1, "--repo still wins over the packed source");
            var failed = RepoLocator.Find([], @"C:\Downloads", _ => false, () => new EmbeddedSourceResult(null, null, "The embedded source could not be unpacked: disk full"));
            Check(failed.Path is null && failed.Error!.Contains("disk full"), "a failed unpack is reported, so the folder picker becomes the fallback with a reason");
            Check(RepoLocator.Find([], @"C:\Downloads", _ => false).Source == "none", "a companion built without packed source falls back to asking, as before");
            Check(EmbeddedSource.OpenPacked() is null, "the test build carries no packed source, so nothing here depends on it");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        return count;
    }
}
