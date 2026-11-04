using System.Text;
using MaterialDockerImage.UI;

/// <summary>
/// Checks for the folder search's file attribute filters (hidden, read-only, system) and the extra encodings (UTF-32 with a byte order mark and a chosen encoding
/// for files without one). Every check works on temporary folders and compares raw bytes. Framework-free logic only.
/// </summary>
public static class RegexGrepAttributeEncodingChecks
{
    static int count;
    static readonly RegexBuilderFlags Cs = new() { IgnoreCase = false };

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    public static int Run()
    {
        count = 0;
        Attributes();
        ReadOnlyEdits();
        Encodings();
        UndoBytes();
        ActionFiles();
        return count;
    }

    static string TempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "regex-attr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void Cleanup(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static HashSet<string> Names(RegexGrepResult r) => r.Matches.Select(m => Path.GetFileName(m.File)).ToHashSet();

    static void Attributes()
    {
        var dir = TempFolder();
        try
        {
            File.WriteAllText(Path.Combine(dir, "plain.txt"), "NEEDLE plain");
            File.WriteAllText(Path.Combine(dir, "hidden.txt"), "NEEDLE hidden");
            File.WriteAllText(Path.Combine(dir, "readonly.txt"), "NEEDLE readonly");
            File.WriteAllText(Path.Combine(dir, "system.txt"), "NEEDLE system");
            Directory.CreateDirectory(Path.Combine(dir, "hiddendir"));
            File.WriteAllText(Path.Combine(dir, "hiddendir", "inside.txt"), "NEEDLE inside");
            File.SetAttributes(Path.Combine(dir, "hidden.txt"), FileAttributes.Hidden);
            File.SetAttributes(Path.Combine(dir, "readonly.txt"), FileAttributes.ReadOnly);
            File.SetAttributes(Path.Combine(dir, "system.txt"), FileAttributes.System);
            File.SetAttributes(Path.Combine(dir, "hiddendir"), FileAttributes.Directory | FileAttributes.Hidden);
            var o = new RegexGrepOptions { Folder = dir };

            var all = RegexGrep.Search("NEEDLE", Cs, o);
            Check(all.Matches.Count == 5, "by default hidden, read-only and system files are searched, as folder searches always did");
            var noHidden = RegexGrep.Search("NEEDLE", Cs, o with { IncludeHidden = false });
            Check(!Names(noHidden).Contains("hidden.txt") && !Names(noHidden).Contains("inside.txt") && Names(noHidden).Contains("plain.txt") && noHidden.Matches.Count == 3, "turning hidden files off leaves out hidden files and the files inside hidden folders");
            Check(noHidden.Skipped.Any(s => s.File.EndsWith("hidden.txt") && s.Reason.Contains("hidden")), "a hidden file that is left out is listed with the reason");
            var noRo = RegexGrep.Search("NEEDLE", Cs, o with { IncludeReadOnly = false });
            Check(!Names(noRo).Contains("readonly.txt") && noRo.Matches.Count == 4 && noRo.FilesSearched == 4, "turning read-only files off leaves out only the read-only file");
            var noSys = RegexGrep.Search("NEEDLE", Cs, o with { IncludeSystem = false });
            Check(!Names(noSys).Contains("system.txt") && noSys.Matches.Count == 4, "turning system files off leaves out only the system file");
            var none = RegexGrep.Search("NEEDLE", Cs, o with { IncludeHidden = false, IncludeReadOnly = false, IncludeSystem = false });
            Check(Names(none).SetEquals(["plain.txt"]), "all three filters off leave only the ordinary file");
            var plan = RegexGrep.PlanReplace("NEEDLE", Cs, "FOUND", o with { IncludeHidden = false, IncludeSystem = false });
            Check(plan.Valid && plan.Files.Select(f => Path.GetFileName(f.File)).ToHashSet().SetEquals(["plain.txt"]), "a replacement plan honours the same attribute filters, so a filtered-out file is never changed");
        }
        finally { Cleanup(dir); }
    }

    static void ReadOnlyEdits()
    {
        var dir = TempFolder();
        try
        {
            var ro = Path.Combine(dir, "ro.txt");
            File.WriteAllText(ro, "NEEDLE");
            File.SetAttributes(ro, FileAttributes.ReadOnly);
            var plan = RegexGrep.PlanReplace("NEEDLE", Cs, "FOUND", new RegexGrepOptions { Folder = dir });
            Check(plan.Valid && plan.Files.Count == 0 && plan.Skipped.Any(s => s.Reason.Contains("read-only")), "a read-only file included in the search is still never edited, and the reason is listed");
            Check(File.ReadAllText(ro) == "NEEDLE", "the read-only file keeps its content");
        }
        finally { Cleanup(dir); }
    }

    static byte[] Replaced(string folder, string name, RegexGrepEncoding enc, string snapshots, out RegexGrepApplyResult apply)
    {
        var plan = RegexGrep.PlanReplace("NEEDLE", Cs, "FOUND", new RegexGrepOptions { Folder = folder, BomlessEncoding = enc });
        apply = RegexGrep.Apply(plan, snapshots);
        return File.ReadAllBytes(Path.Combine(folder, name));
    }

    static void Encodings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1252 = Encoding.GetEncoding(1252);
        var dir = TempFolder();
        var snaps = TempFolder();
        try
        {
            // Windows-1252: e-acute is 0xE9 (invalid UTF-8) and the euro sign is 0x80.
            var ansi = cp1252.GetBytes("caf\u00e9 \u20ac NEEDLE\r\nend\r\n");
            File.WriteAllBytes(Path.Combine(dir, "ansi.txt"), ansi);
            var auto = RegexGrep.Search("NEEDLE", Cs, new RegexGrepOptions { Folder = dir });
            Check(auto.Matches.Count == 1, "without a chosen encoding the single-byte file is still searchable as lossy UTF-8");
            var autoPlan = RegexGrep.PlanReplace("NEEDLE", Cs, "FOUND", new RegexGrepOptions { Folder = dir });
            Check(autoPlan.Files.Count == 0 && autoPlan.Skipped.Any(s => s.Reason.Contains("encoding")), "by default (UTF-8) the single-byte file is never edited, because rewriting it would corrupt it");
            var found = RegexGrep.Search("caf\u00e9 \u20ac", Cs, new RegexGrepOptions { Folder = dir, BomlessEncoding = RegexGrepEncoding.Windows1252 });
            Check(found.Matches.Count == 1 && found.Matches[0].Column == 1, "with Windows-1252 chosen the accented letter and the euro sign are found as text");
            var after = Replaced(dir, "ansi.txt", RegexGrepEncoding.Windows1252, snaps, out var applied);
            Check(applied.Success && after.SequenceEqual(cp1252.GetBytes("caf\u00e9 \u20ac FOUND\r\nend\r\n")), "a Windows-1252 file is written back in Windows-1252, byte-exact apart from the replaced text (line endings kept)");

            // Latin-1 round trip with a byte that Windows-1252 would read differently.
            var latin = new byte[] { 0x61, 0xE9, 0x20, 0x4E, 0x45, 0x45, 0x44, 0x4C, 0x45, 0x20, 0xA4 };
            File.WriteAllBytes(Path.Combine(dir, "latin.txt"), latin);
            File.Delete(Path.Combine(dir, "ansi.txt"));
            var latinAfter = Replaced(dir, "latin.txt", RegexGrepEncoding.Latin1, snaps, out var latinApplied);
            Check(latinApplied.Success && latinAfter.SequenceEqual(new byte[] { 0x61, 0xE9, 0x20, 0x46, 0x4F, 0x55, 0x4E, 0x44, 0x20, 0xA4 }), "a Latin-1 file is written back in Latin-1, byte-exact apart from the replaced text");
            File.Delete(Path.Combine(dir, "latin.txt"));

            // BOM-less UTF-16, both byte orders.
            foreach (var (bigEndian, enc, name) in new[] { (false, RegexGrepEncoding.Utf16Le, "le.txt"), (true, RegexGrepEncoding.Utf16Be, "be.txt") })
            {
                var codec = new UnicodeEncoding(bigEndian, false);
                File.WriteAllBytes(Path.Combine(dir, name), codec.GetBytes("h\u00e9llo NEEDLE\n\u6f22\u5b57\n"));
                var plain = RegexGrep.Search("NEEDLE", Cs, new RegexGrepOptions { Folder = dir });
                Check(plain.Matches.Count == 0 && plain.Skipped.Any(s => s.File.EndsWith(name) && s.Reason.Contains("binary")), $"a BOM-less {(bigEndian ? "UTF-16 BE" : "UTF-16 LE")} file is binary under the default encoding");
                var hit = RegexGrep.Search("NEEDLE", Cs, new RegexGrepOptions { Folder = dir, BomlessEncoding = enc });
                Check(hit.Matches.Count == 1 && hit.Matches[0].Line == 1 && hit.Matches[0].Column == 7, $"with {(bigEndian ? "UTF-16 big endian" : "UTF-16 little endian")} chosen the file is searched as text");
                var bytes = Replaced(dir, name, enc, snaps, out var ok);
                Check(ok.Success && bytes.SequenceEqual(codec.GetBytes("h\u00e9llo FOUND\n\u6f22\u5b57\n")) && !(bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF))), $"a BOM-less {(bigEndian ? "UTF-16 BE" : "UTF-16 LE")} file is written back without gaining a byte order mark, byte-exact apart from the replaced text");
                File.Delete(Path.Combine(dir, name));
            }

            // UTF-32 with a byte order mark is detected without any option.
            foreach (var (bigEndian, name) in new[] { (false, "u32le.txt"), (true, "u32be.txt") })
            {
                var codec = new UTF32Encoding(bigEndian, true);
                var original = codec.GetPreamble().Concat(codec.GetBytes("x NEEDLE \U0001F600\n")).ToArray();
                File.WriteAllBytes(Path.Combine(dir, name), original);
                var hit = RegexGrep.Search("NEEDLE", Cs, new RegexGrepOptions { Folder = dir });
                Check(hit.Matches.Count == 1 && hit.Matches[0].Column == 3, $"a UTF-32 {(bigEndian ? "BE" : "LE")} file with a byte order mark is detected and searched with the default encoding");
                var bytes = Replaced(dir, name, RegexGrepEncoding.Auto, snaps, out var ok);
                Check(ok.Success && bytes.SequenceEqual(codec.GetPreamble().Concat(codec.GetBytes("x FOUND \U0001F600\n")).ToArray()), $"a UTF-32 {(bigEndian ? "BE" : "LE")} file keeps its byte order mark and encoding after a replacement");
                File.Delete(Path.Combine(dir, name));
            }

            // A UTF-16 LE file with a byte order mark must not be mistaken for UTF-32 LE (FF FE then a letter and 00).
            var u16 = new UnicodeEncoding(false, true);
            File.WriteAllBytes(Path.Combine(dir, "u16.txt"), u16.GetPreamble().Concat(u16.GetBytes("NEEDLE")).ToArray());
            var u16Bytes = Replaced(dir, "u16.txt", RegexGrepEncoding.Auto, snaps, out var u16Ok);
            Check(u16Ok.Success && u16Bytes.SequenceEqual(u16.GetPreamble().Concat(u16.GetBytes("FOUND")).ToArray()), "a UTF-16 file with a byte order mark is still read as UTF-16 and not as UTF-32");
            File.Delete(Path.Combine(dir, "u16.txt"));

            // A byte order mark beats the chosen encoding.
            var u8 = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("caf\u00e9 NEEDLE")).ToArray();
            File.WriteAllBytes(Path.Combine(dir, "u8.txt"), u8);
            var u8Bytes = Replaced(dir, "u8.txt", RegexGrepEncoding.Latin1, snaps, out var u8Ok);
            Check(u8Ok.Success && u8Bytes.SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("caf\u00e9 FOUND")).ToArray()), "a UTF-8 byte order mark wins over a chosen single-byte encoding");
            File.Delete(Path.Combine(dir, "u8.txt"));

            // Plain UTF-8 stays the default and is unchanged.
            File.WriteAllBytes(Path.Combine(dir, "u8plain.txt"), Encoding.UTF8.GetBytes("caf\u00e9 NEEDLE"));
            var plainBytes = Replaced(dir, "u8plain.txt", RegexGrepEncoding.Auto, snaps, out var plainOk);
            Check(plainOk.Success && plainBytes.SequenceEqual(Encoding.UTF8.GetBytes("caf\u00e9 FOUND")), "a UTF-8 file without a byte order mark is written back as UTF-8 by default");

            // An out-of-range encoding value is not trusted.
            var bad = RegexGrep.Search("NEEDLE", Cs, new RegexGrepOptions { Folder = dir, BomlessEncoding = (RegexGrepEncoding)99 });
            Check(bad.Valid, "an unknown encoding value falls back to the default instead of failing");
        }
        finally { Cleanup(dir); Cleanup(snaps); }
    }

    static void UndoBytes()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var dir = TempFolder();
        var snaps = TempFolder();
        try
        {
            var original = Encoding.GetEncoding(1252).GetBytes("caf\u00e9 NEEDLE \u20ac\r\n");
            var path = Path.Combine(dir, "a.txt");
            File.WriteAllBytes(path, original);
            var plan = RegexGrep.PlanReplace("NEEDLE", Cs, "FOUND", new RegexGrepOptions { Folder = dir, BomlessEncoding = RegexGrepEncoding.Windows1252 });
            var apply = RegexGrep.Apply(plan, snaps);
            Check(apply.Success && !File.ReadAllBytes(path).SequenceEqual(original), "the Windows-1252 replacement is applied");
            var undo = RegexGrep.Undo(snaps, apply.SnapshotId!);
            Check(undo.Success && undo.FilesRestored == 1 && File.ReadAllBytes(path).SequenceEqual(original), "undoing a Windows-1252 replacement restores the original bytes exactly");
        }
        finally { Cleanup(dir); Cleanup(snaps); }
    }

    static void ActionFiles()
    {
        var action = new RegexSearchAction { Pattern = "a", Folder = "C:\\x", IncludeHidden = false, IncludeReadOnly = false, IncludeSystem = false, BomlessEncoding = RegexGrepEncoding.Utf16Be };
        var back = RegexSearchAction.Parse(action.Serialize());
        Check(!back.IncludeHidden && !back.IncludeReadOnly && !back.IncludeSystem && back.BomlessEncoding == RegexGrepEncoding.Utf16Be, "an action file keeps the attribute filters and the chosen encoding");
        var options = back.ToGrepOptions();
        Check(!options.IncludeHidden && options.BomlessEncoding == RegexGrepEncoding.Utf16Be, "the folder search options built from an action carry the new fields");
        var defaults = RegexSearchAction.Parse(new RegexSearchAction { Pattern = "a" }.Serialize());
        Check(defaults.IncludeHidden && defaults.IncludeReadOnly && defaults.IncludeSystem && defaults.BomlessEncoding == RegexGrepEncoding.Auto && !new RegexSearchAction { Pattern = "a" }.Serialize().Contains("bomlessEncoding"), "an action with the defaults writes none of the new fields and reads back as the defaults");
        string Bad(string text) { try { RegexSearchAction.Parse(text); return ""; } catch (InvalidDataException e) { return e.Message; } }
        var json = new RegexSearchAction { Pattern = "a" }.Serialize().Replace("\"globs\"", "\"bomlessEncoding\": \"ebcdic\",\n  \"globs\"");
        Check(Bad(json).Contains("encoding"), "an action file naming an unknown encoding is refused");
        Check(new RegexSearchAction { Pattern = "a", BomlessEncoding = (RegexGrepEncoding)42 }.Problem() != null, "an action with an unknown encoding value reports a problem");
    }
}
