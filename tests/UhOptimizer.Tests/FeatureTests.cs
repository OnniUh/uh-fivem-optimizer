using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UhOptimizer.Core;

static class FeatureTests
{
    private static void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Throws(Action action) { bool failed = false; try { action(); } catch { failed = true; } Assert(failed, "Expected failure"); }
    public static void Run(Action<string, Action> test, string root)
    {
        string folder = Path.Combine(root, "features"); Directory.CreateDirectory(folder);
        test("Custom colors normalize and reject malformed input", () =>
        {
            Assert(UiColor.Parse(" 12ab34 ").Hex == "#12AB34");
            foreach (string value in new[] { "red", "#123", "#FFFFFFFF", "#12ZZFF", "url(file)", "" }) Assert(!UiColor.TryParse(value, out _));
        });
        test("Custom colors retain readable accent and all primary-button stops", () =>
        {
            for (int r = 0; r <= 255; r += 51) for (int g = 0; g <= 255; g += 51) for (int b = 0; b <= 255; b += 51)
            {
                var p = ThemePalette.Create(new UiColor((byte)r, (byte)g, (byte)b).Hex);
                Assert(p.TextAccent.Contrast(p.Panel) >= 4.5, "Unreadable accent " + p.Accent.Hex);
                Assert(p.TextAccent.Contrast(p.AccentWash) >= 4.5, "Unreadable selected navigation " + p.Accent.Hex);
                foreach (var stop in new[] { p.ButtonTop, p.ButtonBase, p.ButtonBottom }) Assert(stop.Contrast(p.ButtonInk) >= 4.5, "Unreadable button " + p.Accent.Hex);
            }
        });
        test("Appearance persists color and motion without touching recovery", () =>
        {
            var store = new AppearanceStore(folder); string recovery = Path.Combine(folder, "power-backup.json"); File.WriteAllText(recovery, "keep-recovery");
            store.Save(new() { AccentHex = "#80c7ff", ReduceMotion = true, PauseWhileGaming = false });
            var loaded = new AppearanceStore(folder).Load(); Assert(loaded.AccentHex == "#80C7FF" && loaded.ReduceMotion && !loaded.PauseWhileGaming);
            Assert(File.ReadAllText(recovery) == "keep-recovery" && Directory.GetFiles(folder, "*.tmp").Length == 0);
        });
        test("Invalid appearance remains available for inspection", () =>
        {
            var store = new AppearanceStore(folder); File.WriteAllText(store.SettingsPath, "{broken"); Throws(() => store.Load()); Assert(File.ReadAllText(store.SettingsPath) == "{broken");
            Throws(() => store.Save(new() { AccentHex = "not-a-color" })); Assert(File.ReadAllText(store.SettingsPath) == "{broken");
        });
        test("Missing appearance starts with green and game-aware motion", () =>
        {
            var value = new AppearanceStore(Path.Combine(folder, "fresh")).Load(); Assert(value.AccentHex == "#B5F36B" && value.PauseWhileGaming && !value.ReduceMotion);
        });
        test("Unsupported appearance schema is rejected", () =>
        {
            var store = new AppearanceStore(folder); File.WriteAllText(store.SettingsPath, "{\"SchemaVersion\":2,\"AccentHex\":\"#FFFFFF\"}"); Throws(() => store.Load());
        });
        test("Version comparisons use numeric components", () => { Assert(UpdateClient.ParseVersion("0.10.0") > UpdateClient.ParseVersion("0.9.0")); Throws(() => UpdateClient.ParseVersion("latest")); });
        test("Up-to-date checks do not download EXEs or checksums", () =>
        {
            using var fixture = new ReleaseFixture("0.2.0"); var check = fixture.Client.CheckAsync("0.2.0").GetAwaiter().GetResult();
            Assert(check.Available is null && fixture.Handler.Requests.Count == 2 && !fixture.Handler.Requests.Contains(fixture.ExeUrl));
        });
        test("Newer release validates metadata and checksums without downloading EXE", () =>
        {
            using var fixture = new ReleaseFixture("0.3.0"); var check = fixture.Client.CheckAsync("0.2.0").GetAwaiter().GetResult();
            Assert(check.Available?.Version == "0.3.0" && check.Available.Sha256 == fixture.ExeHash && !fixture.Handler.Requests.Contains(fixture.ExeUrl));
        });
        test("Older release never offers a downgrade", () =>
        {
            using var fixture = new ReleaseFixture("0.1.0"); Assert(fixture.Client.CheckAsync("0.2.0").GetAwaiter().GetResult().Available is null);
        });
        const string listingUrl = "https://api.github.com/repos/" + UpdateClient.Repository + "/releases?per_page=20";
        void Merge(ReleaseFixture destination, params ReleaseFixture[] ordered)
        {
            var listing = new JsonArray();
            foreach (var fixture in ordered)
            {
                listing.Add(JsonNode.Parse(fixture.Handler.Data[listingUrl])![0]!.DeepClone());
                foreach (var pair in fixture.Handler.Data.Where(p => p.Key != listingUrl)) destination.Handler.Data[pair.Key] = pair.Value;
            }
            destination.Handler.Data[listingUrl] = JsonSerializer.SerializeToUtf8Bytes(listing);
        }
        test("Oldest-first release listings offer the highest numeric version", () =>
        {
            using var old = new ReleaseFixture("0.1.0", tag: "preview-111111111111");
            using var current = new ReleaseFixture("0.2.0", tag: "preview-222222222222");
            using var newest = new ReleaseFixture("0.10.0", tag: "preview-333333333333");
            Merge(old, old, current, newest);
            var check = old.Client.CheckAsync("0.2.0").GetAwaiter().GetResult();
            Assert(check.LatestVersion == "0.10.0" && check.Available?.DownloadUrl.AbsoluteUri == newest.ExeUrl);
            Assert(!old.Handler.Requests.Any(u => u.EndsWith(".exe")) && old.Handler.Requests.Count(u => u.EndsWith("SHA256SUMS.txt")) == 1);
        });
        test("Unsorted releases choose the largest version even when a newer publication is a rollback", () =>
        {
            using var first = new ReleaseFixture("0.3.0", tag: "preview-111111111111");
            using var max = new ReleaseFixture("0.10.0", tag: "preview-222222222222");
            using var last = new ReleaseFixture("0.4.0", tag: "preview-333333333333");
            Merge(first, first, max, last); Assert(first.Client.CheckAsync("0.2.0").GetAwaiter().GetResult().Available?.Version == "0.10.0");
        });
        test("Up-to-date checks scan older entries without downloading checksums or offering downgrade", () =>
        {
            using var old = new ReleaseFixture("0.1.0", tag: "preview-111111111111");
            using var current = new ReleaseFixture("0.3.1", tag: "preview-222222222222"); Merge(old, old, current);
            var check = old.Client.CheckAsync("0.3.1").GetAwaiter().GetResult();
            Assert(check.Available is null && check.LatestVersion == "0.3.1" && old.Handler.Requests.Count == 3);
        });
        test("Tampered build metadata is rejected", () =>
        {
            using var fixture = new ReleaseFixture("0.3.0"); fixture.Handler.Data[fixture.InfoUrl] = Encoding.UTF8.GetBytes("{tampered}");
            Throws(() => fixture.Client.CheckAsync("0.2.0").GetAwaiter().GetResult()); Assert(!fixture.Handler.Requests.Contains(fixture.ExeUrl));
        });
        test("Mismatched source commit is rejected", () =>
        {
            using var fixture = new ReleaseFixture("0.3.0", release => release["target_commitish"] = new string('b', 40));
            Throws(() => fixture.Client.CheckAsync("0.2.0").GetAwaiter().GetResult());
        });
        test("Foreign asset URLs are rejected before a network request", () =>
        {
            using var fixture = new ReleaseFixture("0.3.0", release => release["assets"]![1]!["browser_download_url"] = "https://example.com/build-info.json");
            Throws(() => fixture.Client.CheckAsync("0.2.0").GetAwaiter().GetResult()); Assert(fixture.Handler.Requests.Count == 1);
        });
        test("Oversized release metadata is bounded", () =>
        {
            using var fixture = new ReleaseFixture("0.3.0"); fixture.Handler.Data[fixture.InfoUrl] = new byte[9000];
            Throws(() => fixture.Client.CheckAsync("0.2.0").GetAwaiter().GetResult());
        });
        test("Verified update is reused instead of downloading again", () =>
        {
            using var fixture = new ReleaseFixture("0.3.0"); var candidate = fixture.Client.CheckAsync("0.2.0").GetAwaiter().GetResult().Available!;
            string cache = Path.Combine(folder, "reuse"); string first = fixture.Client.DownloadAsync(candidate, cache, null).GetAwaiter().GetResult();
            string second = fixture.Client.DownloadAsync(candidate, cache, null).GetAwaiter().GetResult();
            Assert(first == second && fixture.Handler.Requests.Count(x => x == fixture.ExeUrl) == 1 && File.ReadAllBytes(first).SequenceEqual(fixture.ExeBytes));
        });
        test("Corrupted downloads are discarded without changing installed app", () =>
        {
            using var fixture = new ReleaseFixture("0.3.0"); var candidate = fixture.Client.CheckAsync("0.2.0").GetAwaiter().GetResult().Available!;
            fixture.Handler.Data[fixture.ExeUrl] = new byte[fixture.ExeBytes.Length]; string cache = Path.Combine(folder, "bad-download");
            string installed = Path.Combine(folder, "installed.exe"); File.WriteAllText(installed, "original");
            Throws(() => fixture.Client.DownloadAsync(candidate, cache, null).GetAwaiter().GetResult());
            Assert(File.ReadAllText(installed) == "original" && Directory.GetFiles(cache, "*.part", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(cache, "*.exe", SearchOption.AllDirectories).Length == 0);
        });
        test("Canceled partial download leaves installed files untouched", () =>
        {
            using var fixture = new ReleaseFixture("0.3.0"); var candidate = fixture.Client.CheckAsync("0.2.0").GetAwaiter().GetResult().Available!;
            using var cancel = new CancellationTokenSource(); string cache = Path.Combine(folder, "cancel-download");
            var progress = new InlineProgress(value => { if (value.Received > 0) cancel.Cancel(); });
            Throws(() => fixture.Client.DownloadAsync(candidate, cache, progress, cancel.Token).GetAwaiter().GetResult());
            Assert(Directory.GetFiles(cache, "*.part", SearchOption.AllDirectories).Length == 0 && Directory.GetFiles(cache, "*.exe", SearchOption.AllDirectories).Length == 0);
        });
        test("Foreign download redirects are refused", () =>
        {
            using var fixture = new ReleaseFixture("0.3.0"); var candidate = fixture.Client.CheckAsync("0.2.0").GetAwaiter().GetResult().Available!;
            fixture.Handler.Redirects[fixture.ExeUrl] = "https://example.com/evil.exe";
            Throws(() => fixture.Client.DownloadAsync(candidate, Path.Combine(folder, "redirect-bad"), null).GetAwaiter().GetResult());
            Assert(!fixture.Handler.Requests.Any(x => x.Contains("example.com")));
        });
        test("Approved GitHub CDN redirect retains checksum verification", () =>
        {
            using var fixture = new ReleaseFixture("0.3.0"); var candidate = fixture.Client.CheckAsync("0.2.0").GetAwaiter().GetResult().Available!;
            const string cdn = "https://release-assets.githubusercontent.com/github-production-release-asset/test";
            fixture.Handler.Redirects[fixture.ExeUrl] = cdn; fixture.Handler.Data[cdn] = fixture.ExeBytes;
            string file = fixture.Client.DownloadAsync(candidate, Path.Combine(folder, "redirect-good"), null).GetAwaiter().GetResult();
            Assert(File.ReadAllBytes(file).SequenceEqual(fixture.ExeBytes) && fixture.Handler.Requests.Contains(cdn));
        });
        test("Atomic update keeps original EXE as a recovery copy", () =>
        {
            string dir = Path.Combine(folder, "install-good"); Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "app.exe"), staged = Path.Combine(dir, "new.tmp"), backup = target + ".previous";
            File.WriteAllText(target, "original"); File.WriteAllText(staged, "new-version");
            string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("new-version"))); bool launched = false;
            UpdateFileTransaction.ReplaceAndLaunch(staged, target, backup, hash, path => { launched = true; Assert(File.ReadAllText(path) == "new-version"); });
            Assert(launched && File.ReadAllText(backup) == "original");
        });
        test("Failed update relaunch restores the original EXE", () =>
        {
            string dir = Path.Combine(folder, "install-fail"); Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "app.exe"), staged = Path.Combine(dir, "new.tmp"), backup = target + ".previous";
            File.WriteAllText(target, "original"); File.WriteAllText(staged, "new-version");
            string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("new-version")));
            Throws(() => UpdateFileTransaction.ReplaceAndLaunch(staged, target, backup, hash, _ => throw new IOException("Simulated launch failure")));
            Assert(File.ReadAllText(target) == "original");
        });
        test("Staged checksum failure never replaces the current EXE", () =>
        {
            string dir = Path.Combine(folder, "stage-fail"); Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, "app.exe"), staged = Path.Combine(dir, "new.tmp");
            File.WriteAllText(target, "original"); File.WriteAllText(staged, "bad-download"); bool launched = false;
            Throws(() => UpdateFileTransaction.ReplaceAndLaunch(staged, target, target + ".previous", new string('a', 64), _ => launched = true));
            Assert(!launched && File.ReadAllText(target) == "original");
        });
    }

    private sealed class InlineProgress(Action<UpdateProgress> report) : IProgress<UpdateProgress> { public void Report(UpdateProgress value) => report(value); }
    private sealed class ReleaseFixture : IDisposable
    {
        public readonly FakeHandler Handler = new();
        private readonly HttpClient _http;
        public UpdateClient Client { get; }
        public readonly byte[] ExeBytes = new byte[200_000];
        public string ExeHash { get; }
        public string Tag { get; }
        public string ExeUrl => UpdateClient.ReleasesUrl + "/download/" + Tag + "/UhFiveMOptimizer.exe";
        public string InfoUrl => UpdateClient.ReleasesUrl + "/download/" + Tag + "/build-info.json";
        public ReleaseFixture(string version, Action<JsonObject>? change = null, string tag = "preview-aaaaaaaaaaaa")
        {
            Tag = tag;
            ExeBytes[0] = (byte)'M'; ExeBytes[1] = (byte)'Z'; ExeHash = Hash(ExeBytes);
            byte[] info = JsonSerializer.SerializeToUtf8Bytes(new { app = "Uh FiveM Optimizer", version, sourceCommit = new string('a', 40), target = "win-x64", selfContained = true });
            byte[] sums = Encoding.UTF8.GetBytes(ExeHash + "  UhFiveMOptimizer.exe\n" + Hash(info) + "  build-info.json\n");
            object Asset(string name, byte[] bytes) => new { name, size = bytes.Length, digest = "sha256:" + Hash(bytes), browser_download_url = UpdateClient.ReleasesUrl + "/download/" + Tag + "/" + name };
            var release = JsonSerializer.SerializeToNode(new { draft = false, tag_name = Tag, target_commitish = new string('a', 40), assets = new[] { Asset("UhFiveMOptimizer.exe", ExeBytes), Asset("build-info.json", info), Asset("SHA256SUMS.txt", sums) } })!.AsObject();
            change?.Invoke(release);
            Handler.Data["https://api.github.com/repos/" + UpdateClient.Repository + "/releases?per_page=20"] = JsonSerializer.SerializeToUtf8Bytes(new JsonArray(release));
            Handler.Data[InfoUrl] = info; Handler.Data[ExeUrl] = ExeBytes;
            Handler.Data[UpdateClient.ReleasesUrl + "/download/" + Tag + "/SHA256SUMS.txt"] = sums;
            _http = new(Handler); Client = new(_http);
        }
        public void Dispose() => _http.Dispose();
        private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly Dictionary<string, byte[]> Data = [];
        public readonly Dictionary<string, string> Redirects = [];
        public readonly List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); string uri = request.RequestUri!.AbsoluteUri; Requests.Add(uri);
            Assert(request.Headers.Authorization is null, "Update request unexpectedly included credentials");
            if (Redirects.TryGetValue(uri, out var redirect))
            { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri(redirect); return Task.FromResult(response); }
            if (!Data.TryGetValue(uri, out var bytes)) throw new HttpRequestException("Unexpected test URL " + uri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
