using System.IO.Compression;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;
using UhOptimizer.Core;

static class OptimizationTests
{
    private static void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Throws(Action action) { bool failed = false; try { action(); } catch { failed = true; } Assert(failed, "Expected failure"); }
    public const string Settings = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Settings><version value=\"27\"/><graphics><ShadowQuality value=\"3\"/><TextureQuality value=\"2\"/><MSAA value=\"4\"/><PostFX value=\"3\"/><LodScale value=\"1.0\"/><GrassQuality value=\"2\"/><TXAA_Enabled value=\"true\"/><UnknownFutureSetting value=\"hello\"/></graphics><video><ScreenWidth value=\"1920\"/><ScreenHeight value=\"1080\"/><AdapterIndex value=\"1\"/><RefreshRate value=\"144\"/><VSync value=\"1\"/></video><VideoCardDescription>Fixture GPU Ω</VideoCardDescription></Settings>";
    public static void Run(Action<string, Action> test, string root)
    {
        string Base() { string dir = Path.Combine(root, "optimizations", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); return dir; }
        (string dir, string path) Graphics()
        {
            string dir = Base(), path = Path.Combine(dir, "CitizenFX", "gta5_settings.xml"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, Settings); return (dir, path);
        }
        FileTuningService Service(string dir, Func<bool>? game = null) => new(Path.Combine(dir, "backup"), p => LocalFiles.Inside(p, dir), game);
        string Zip(string dir, params (string path, string text)[] entries)
        {
            string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".zip");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var entry in entries) { using var writer = new StreamWriter(archive.CreateEntry(entry.path).Open()); writer.Write(entry.text); }
            return path;
        }
        string AppFolder(string dir)
        {
            string app = Path.Combine(dir, "FiveM.app"); Directory.CreateDirectory(Path.Combine(app, "citizen")); File.WriteAllText(Path.Combine(app, "CitizenFX.ini"), "fixture"); return app;
        }
        string Old(string dir, string name, string content = "old temp")
        {
            Directory.CreateDirectory(dir); string path = Path.Combine(dir, name); File.WriteAllText(path, content);
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-10)); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-10)); return path;
        }
        void LinkTest(string name, Action setup, Action body, Action cleanup)
        {
            try { setup(); }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 5 or 1314)
            {
                if (Environment.GetEnvironmentVariable("CI") == "true") test(name, () => throw new Exception("Required CI link fixture could not be created.", ex));
                else Console.WriteLine("SKIP " + name + ": local sandbox denied link creation; required in CI.");
                return;
            }
            try { test(name, body); } finally { cleanup(); }
        }
        test("Low-end preview preserves adapter, refresh rate, VSync, resolution and unknown settings", () =>
        {
            var (dir, path) = Graphics(); byte[] before = File.ReadAllBytes(path); var plan = LowEndGraphics.Preview(path, false);
            Assert(File.ReadAllBytes(path).SequenceEqual(before) && plan.Files.Count == 1);
            var doc = LocalFiles.Xml(plan.Files[0].Replacement);
            Assert(doc.Root!.Element("video")!.ToString() == LocalFiles.Xml(before).Root!.Element("video")!.ToString());
            Assert(doc.Root.Element("graphics")!.Element("ShadowQuality")!.Attribute("value")!.Value == "0");
            Assert(doc.Root.Element("graphics")!.Element("UnknownFutureSetting")!.Attribute("value")!.Value == "hello");
            Assert(doc.Root.Element("VideoCardDescription")!.Value == "Fixture GPU Ω");
        });
        test("720p is explicit and never increases an existing smaller dimension", () =>
        {
            var (_, path) = Graphics(); var plan = LowEndGraphics.Preview(path, true); var doc = LocalFiles.Xml(plan.Files[0].Replacement);
            Assert(doc.Root!.Element("video")!.Element("ScreenWidth")!.Attribute("value")!.Value == "1280");
            File.WriteAllText(path, Settings.Replace("1920", "1024").Replace("1080", "600"));
            doc = LocalFiles.Xml(LowEndGraphics.Preview(path, true).Files[0].Replacement);
            Assert(doc.Root!.Element("video")!.Element("ScreenWidth")!.Attribute("value")!.Value == "1024" && doc.Root.Element("video")!.Element("ScreenHeight")!.Attribute("value")!.Value == "600");
        });
        test("Invalid, duplicate, foreign and external-entity settings fail before writing", () =>
        {
            var (_, path) = Graphics();
            foreach (string xml in new[] { "<bad", Settings.Replace("<MSAA value=\"4\"/>", ""), Settings.Replace("<MSAA value=\"4\"/>", "<MSAA value=\"4\"/><MSAA value=\"2\"/>"), "<!DOCTYPE Settings [<!ENTITY x SYSTEM 'file:///C:/secret'>]><Settings>&x;</Settings>", Settings.Replace("<LodScale value=\"1.0\"/>", "<LodScale value=\"NaN\"/>") })
            { File.WriteAllText(path, xml); Throws(() => LowEndGraphics.Preview(path, false)); Assert(File.ReadAllText(path) == xml); }
            Throws(() => LowEndGraphics.Preview(Path.Combine(Path.GetDirectoryName(path)!, "settings.xml"), false));
        });
        test("Graphics apply verifies changes and restart recovery restores byte-exact original", () =>
        {
            var (dir, path) = Graphics(); byte[] original = File.ReadAllBytes(path); var service = Service(dir); var plan = LowEndGraphics.Preview(path, true); service.Apply(plan);
            Assert(service.Load()!.Status == "Applied" && File.ReadAllBytes(path).SequenceEqual(plan.Files[0].Replacement));
            Throws(() => service.Apply(plan)); Service(dir).Restore(); Assert(File.ReadAllBytes(path).SequenceEqual(original) && service.Load()!.Status == "Restored");
        });
        test("Unknown graphics formats are refused instead of applying Legacy values", () =>
        {
            var (_, path) = Graphics(); File.WriteAllText(path, Settings.Replace("value=\"27\"", "value=\"28\"")); Throws(() => LowEndGraphics.Preview(path, false));
            File.WriteAllText(path, Settings.Replace("<version value=\"27\"/>", "")); Throws(() => LowEndGraphics.Preview(path, false));
        });
        test("Already-low graphics create no redundant backup", () =>
        {
            var (dir, path) = Graphics(); Service(dir).Apply(LowEndGraphics.Preview(path, false)); Assert(LowEndGraphics.Preview(path, false).Files.Count == 0);
        });
        test("Changed file after preview blocks apply before the backup or target write", () =>
        {
            var (dir, path) = Graphics(); var plan = LowEndGraphics.Preview(path, false); File.WriteAllText(path, Settings + " ");
            var service = Service(dir); Throws(() => service.Apply(plan)); Assert(!File.Exists(service.JournalPath) && File.ReadAllText(path) == Settings + " ");
        });
        test("Backup write failure leaves original settings untouched", () =>
        {
            var (dir, path) = Graphics(); var service = Service(dir); Directory.CreateDirectory(service.JournalPath);
            Throws(() => service.Apply(LowEndGraphics.Preview(path, false))); Assert(File.ReadAllText(path) == Settings);
        });
        test("Running game blocks both apply and restore", () =>
        {
            var (dir, path) = Graphics(); bool running = true; var service = Service(dir, () => running);
            Throws(() => service.Apply(LowEndGraphics.Preview(path, false))); Assert(File.ReadAllText(path) == Settings);
            running = false; service.Apply(LowEndGraphics.Preview(path, false)); running = true; Throws(() => service.Restore()); Assert(service.Load()!.Status == "Applied");
        });
        test("Interrupted multi-file apply leaves a Prepared backup recoverable after restart", () =>
        {
            string dir = Base(), a = Path.Combine(dir, "a.txt"), b = Path.Combine(dir, "b.txt"); File.WriteAllText(a, "original");
            int guards = 0; var service = Service(dir, () => ++guards >= 3);
            var plan = new TuningPlan("fixture", [new(a, Encoding.UTF8.GetBytes("original"), Encoding.UTF8.GetBytes("changed")), new(b, null, Encoding.UTF8.GetBytes("introduced"))], []);
            Throws(() => service.Apply(plan)); Assert(service.Load()!.Status == "Prepared" && File.ReadAllText(a) == "changed" && !File.Exists(b));
            Service(dir).Restore(); Assert(File.ReadAllText(a) == "original" && !File.Exists(b));
        });
        test("Restore reviews later edits and preserves them before replacing originals", () =>
        {
            var (dir, path) = Graphics(); var service = Service(dir); service.Apply(LowEndGraphics.Preview(path, false)); File.WriteAllText(path, "newer user edit");
            Assert(service.Conflicts().SequenceEqual(new[] { path })); Throws(() => service.Restore()); Assert(File.ReadAllText(path) == "newer user edit");
            service.Restore(true); Assert(File.ReadAllText(path) == Settings);
            string copy = Directory.GetFiles(Path.GetDirectoryName(service.JournalPath)!, "before-restore-*.json").Single();
            using var json = JsonDocument.Parse(File.ReadAllText(copy)); Assert(Encoding.UTF8.GetString(json.RootElement[0].GetProperty("Content").GetBytesFromBase64()) == "newer user edit");
        });
        test("Corrupt backup and mismatched original checksum block new writes", () =>
        {
            var (dir, path) = Graphics(); var service = Service(dir); service.Apply(LowEndGraphics.Preview(path, false));
            var json = JsonNode.Parse(File.ReadAllText(service.JournalPath))!; json["Files"]![0]!["OriginalHash"] = new string('0', 64); File.WriteAllText(service.JournalPath, json.ToJsonString());
            byte[] before = File.ReadAllBytes(path); Throws(() => service.Restore()); Throws(() => service.Apply(LowEndGraphics.Preview(path, false))); Assert(File.ReadAllBytes(path).SequenceEqual(before));
        });
        test("ZIP import preserves existing visual files and removes introduced files on restore", () =>
        {
            string dir = Base(), app = AppFolder(dir); string visual = Path.Combine(app, "citizen", "common", "data", "visualsettings.dat"); Directory.CreateDirectory(Path.GetDirectoryName(visual)!); File.WriteAllText(visual, "# original visual");
            string zip = Zip(dir, ("citizen/common/data/visualsettings.dat", "# new visual"), ("citizen/common/data/timecycle/w_clear.xml", "<timecycle/>"));
            var plan = LegacyVisualPack.Preview(zip, app); var service = new FileTuningService(Path.Combine(dir, "backup"), LegacyVisualPack.IsTarget);
            service.Apply(plan); string added = plan.Files.Single(f => f.Original is null).Path; Assert(File.Exists(added));
            service.Restore(); Assert(File.ReadAllText(visual) == "# original visual" && !File.Exists(added));
        });
        test("ZIP traversal, scripts, executable, malformed XML and duplicate targets are rejected", () =>
        {
            string dir = Base(), app = AppFolder(dir);
            foreach (var entry in new[] { ("../outside.txt", "bad"), ("citizen/common/data/timecycle/../../evil.xml", "<x/>"), ("citizen/common/data/timecycle/evil.lua", "script"), ("payload.exe", "MZ"), ("citizen/common/data/timecycle/w_clear.xml", "<bad"), ("citizen\\common\\data\\visualsettings.dat", "# bad") })
                Throws(() => LegacyVisualPack.Preview(Zip(dir, entry), app));
            Throws(() => LegacyVisualPack.Preview(Zip(dir, ("citizen/common/data/visualsettings.dat", "# first"), ("citizen/common/data/VISUALSETTINGS.DAT", "# duplicate")), app));
            Assert(!File.Exists(Path.Combine(dir, "outside.txt")) && Directory.GetFiles(app, "*", SearchOption.AllDirectories).Length == 1);
            Assert(!LegacyVisualPack.IsTarget(Path.Combine(dir, "common", "data", "visualsettings.dat")));
        });
        test("ZIP expansion limits reject oversized XML before extraction", () =>
        {
            string dir = Base(), app = AppFolder(dir); string zip = Zip(dir, ("citizen/common/data/timecycle/w_clear.xml", "<x>" + new string('a', 4 * 1024 * 1024) + "</x>"));
            Throws(() => LegacyVisualPack.Preview(zip, app)); Assert(Directory.GetFiles(app, "*", SearchOption.AllDirectories).Length == 1);
        });
        test("Cleanup preview is read-only, excludes recent files and cannot target arbitrary folders", () =>
        {
            string dir = Base(), temp = Path.Combine(dir, "Temp"), old = Old(temp, "old.tmp"), fresh = Path.Combine(temp, "fresh.tmp"); File.WriteAllText(fresh, "keep");
            string protectedFile = Old(temp, "readonly.tmp"); File.SetAttributes(protectedFile, FileAttributes.ReadOnly);
            try
            {
                var service = new TempCleanupService([new("fixture", temp)]); var preview = service.Preview();
                Assert(preview.Files.Count == 1 && preview.Files[0].Path == old && File.Exists(old) && File.Exists(fresh));
                Throws(() => new TempCleanupService([new("bad", dir)]));
            }
            finally { File.SetAttributes(protectedFile, FileAttributes.Normal); }
        });
        test("Cleanup deletes only previewed old unchanged files and skips locked or replaced files", () =>
        {
            string temp = Path.Combine(Base(), "Temp"); string old = Old(temp, "old.tmp"), changed = Old(temp, "changed.tmp"), locked = Old(temp, "locked.tmp");
            var service = new TempCleanupService([new("fixture", temp)]); var preview = service.Preview(); Assert(preview.Files.Count == 3);
            File.WriteAllText(changed, "new content after preview"); string unseen = Old(temp, "not-in-preview.tmp");
            using (var handle = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var result = service.Clean(preview); Assert(result.Deleted == 1 && result.Skipped == 2 && !File.Exists(old) && File.Exists(locked) && File.Exists(changed) && File.Exists(unseen));
            }
        });
        {
            string dir = Base(), temp = Path.Combine(dir, "Temp"), outside = Path.Combine(dir, "outside"); Directory.CreateDirectory(temp); string keep = Old(outside, "keep.tmp"), link = Path.Combine(temp, "junction");
            LinkTest("Cleanup excludes linked folders and file tuning refuses linked ancestors", () => Junction(link, outside), () =>
        {
                var preview = new TempCleanupService([new("fixture", temp)]).Preview(); Assert(preview.Files.Count == 0 && preview.Skipped > 0 && File.Exists(keep));
                Throws(() => LocalFiles.Read(Path.Combine(link, "keep.tmp"))); Throws(() => new TempCleanupService([new("bad", link)]));
        }, () => Directory.Delete(link));
        }
        {
            string dir = Base(), temp = Path.Combine(dir, "Temp"), original = Old(temp, "linked.tmp"), other = Path.Combine(dir, "other.tmp");
            LinkTest("Cleanup checks the final handle and never deletes hard-linked files", () => { if (!CreateHardLinkW(other, original, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error()); }, () =>
        {
            var service = new TempCleanupService([new("fixture", temp)]);
            var result = service.Clean(service.Preview()); Assert(result.Deleted == 0 && result.Skipped == 1 && File.Exists(original) && File.Exists(other));
        }, () => File.Delete(other));
        }
        test("Cleanup cancellation and game detection preserve unprocessed files", () =>
        {
            string temp = Path.Combine(Base(), "Temp"), old = Old(temp, "keep.tmp"); bool running = true;
            var service = new TempCleanupService([new("fixture", temp)], () => running); var preview = service.Preview();
            Throws(() => service.Clean(preview)); running = false; var result = service.Clean(preview, new CancellationToken(true));
            Assert(result.Interrupted && result.Deleted == 0 && File.Exists(old)); Throws(() => service.Preview(new CancellationToken(true)));
        });
        test("Cleanup refuses a different file even when its size and timestamps match the preview", () =>
        {
            string dir = Base(), temp = Path.Combine(dir, "Temp"), path = Old(temp, "replaced.tmp");
            var service = new TempCleanupService([new("fixture", temp)]); var preview = service.Preview(); var snapshot = preview.Files.Single();
            File.Move(path, Path.Combine(dir, "original.tmp")); File.WriteAllText(path, "old temp");
            File.SetCreationTimeUtc(path, snapshot.CreatedUtc); File.SetLastWriteTimeUtc(path, snapshot.WrittenUtc);
            var result = service.Clean(preview); Assert(result.Deleted == 0 && result.Skipped == 1 && File.Exists(path));
        });
        test("Expired or injected cleanup candidates cannot delete outside selected roots", () =>
        {
            string dir = Base(), temp = Path.Combine(dir, "Temp"), old = Old(temp, "old.tmp"), outside = Old(Path.Combine(dir, "outside"), "keep.tmp");
            var service = new TempCleanupService([new("fixture", temp)]); var preview = service.Preview();
            Throws(() => service.Clean(preview with { CreatedUtc = DateTimeOffset.UtcNow.AddHours(-1) }));
            var bad = preview.Files[0] with { Path = outside }; var result = service.Clean(preview with { Files = new[] { bad } });
            Assert(result.Deleted == 0 && File.Exists(old) && File.Exists(outside));
        });
        test("Prefetch is explicit, old .pf only, and never descends into ReadyBoot", () =>
        {
            string dir = Base(), temp = Path.Combine(dir, "Temp"), prefetch = Path.Combine(dir, "Prefetch"); Directory.CreateDirectory(temp);
            string pf = Old(prefetch, "old.pf"), layout = Old(prefetch, "Layout.ini"), boot = Old(Path.Combine(prefetch, "ReadyBoot"), "keep.pf");
            Assert(new TempCleanupService([new("temp", temp)]).Preview().Files.Count == 0);
            var service = new TempCleanupService([new("prefetch", prefetch, true)]); var preview = service.Preview(); Assert(preview.Files.Count == 1 && preview.Files[0].Path == pf);
            Assert(service.Clean(preview).Deleted == 1 && File.Exists(layout) && File.Exists(boot) && Directory.Exists(prefetch));
        });
    }
    private static void Junction(string path, string target)
    {
        Directory.CreateDirectory(path);
        using var handle = CreateFileW(path, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + Path.GetFullPath(target)); byte[] print = Encoding.Unicode.GetBytes(Path.GetFullPath(target));
        byte[] buffer = new byte[16 + substitute.Length + 2 + print.Length + 2];
        BitConverter.GetBytes(0xA0000003u).CopyTo(buffer, 0); BitConverter.GetBytes((ushort)(buffer.Length - 8)).CopyTo(buffer, 4);
        BitConverter.GetBytes((ushort)substitute.Length).CopyTo(buffer, 10); BitConverter.GetBytes((ushort)(substitute.Length + 2)).CopyTo(buffer, 12); BitConverter.GetBytes((ushort)print.Length).CopyTo(buffer, 14);
        substitute.CopyTo(buffer, 16); print.CopyTo(buffer, 18 + substitute.Length);
        if (!DeviceIoControl(handle, 0x900A4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newName, string existingName, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string file, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint control, byte[] buffer, int length, IntPtr output, int outputLength, out uint returned, IntPtr overlapped);
}
