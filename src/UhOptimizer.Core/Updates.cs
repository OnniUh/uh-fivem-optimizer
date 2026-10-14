using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UhOptimizer.Core;

public record UpdateCandidate(string Version, string Tag, string SourceCommit, string ReleaseUrl, Uri DownloadUrl, long Size, string Sha256);
public record UpdateCheck(string LatestVersion, UpdateCandidate? Available);
public record UpdateProgress(long Received, long Total, bool UsingCachedFile = false);

/// <summary>User-initiated, public GitHub release checks. No account token, analytics, or startup polling.</summary>
public sealed class UpdateClient(HttpClient http)
{
    public const string Repository = "OnniUh/uh-fivem-optimizer";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    private const string Api = "https://api.github.com/repos/" + Repository;
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public static Version ParseVersion(string value)
    {
        if (!Regex.IsMatch(value, @"^\d{1,5}\.\d{1,5}\.\d{1,5}$") || !Version.TryParse(value, out var version))
            throw new InvalidDataException("The release has an unsupported version format.");
        return version;
    }
    public static string ParseHash(string value)
    {
        if (!Regex.IsMatch(value, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("The release has an invalid SHA-256 checksum.");
        return value.ToLowerInvariant();
    }
    private static Uri AssetUri(JsonElement asset, string tag, string name)
    {
        string expected = ReleasesUrl + "/download/" + Uri.EscapeDataString(tag) + "/" + name;
        if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var uri) || uri.AbsoluteUri != expected)
            throw new InvalidDataException("This release points outside the expected repository download path.");
        return uri;
    }
    private static string Digest(JsonElement asset)
    {
        string? value = asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null;
        if (value is null || !value.StartsWith("sha256:", StringComparison.Ordinal)) throw new InvalidDataException("The release asset has no GitHub SHA-256 digest.");
        return ParseHash(value[7..]);
    }

    public async Task<UpdateCheck> CheckAsync(string currentVersion, CancellationToken cancellation = default)
    {
        var current = ParseVersion(currentVersion);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(35));
        var token = timeout.Token;
        byte[] listing = await ReadSmallAsync(new Uri(Api + "/releases?per_page=20"), 2_000_000, token);
        using var doc = JsonDocument.Parse(listing);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("GitHub returned an invalid release list.");
        Version latest = new(0, 0, 0); bool found = false;
        (JsonElement Exe, JsonElement Info, JsonElement Sums, string Tag, BuildManifest Manifest)? selected = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var assets = release.GetProperty("assets").EnumerateArray().ToArray();
            JsonElement Find(string name) => assets.FirstOrDefault(a => a.GetProperty("name").GetString() == name);
            var exe = Find("UhFiveMOptimizer.exe"); var info = Find("build-info.json"); var sums = Find("SHA256SUMS.txt");
            if (exe.ValueKind == JsonValueKind.Undefined || info.ValueKind == JsonValueKind.Undefined || sums.ValueKind == JsonValueKind.Undefined) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!Regex.IsMatch(tag, @"^(preview-[a-f0-9]{12}|v\d+\.\d+\.\d+)$")) continue;
            var infoUri = AssetUri(info, tag, "build-info.json");
            byte[] metadata = await ReadSmallAsync(infoUri, 8192, token);
            if (Convert.ToHexStringLower(SHA256.HashData(metadata)) != Digest(info)) throw new InvalidDataException("Build metadata did not match GitHub's digest.");
            var manifest = JsonSerializer.Deserialize<BuildManifest>(metadata, JsonOptions) ?? throw new InvalidDataException("Missing build metadata.");
            if (manifest.App != "Uh FiveM Optimizer" || manifest.Target != "win-x64" || !manifest.SelfContained) continue;
            var version = ParseVersion(manifest.Version);
            string source = manifest.SourceCommit;
            if (!Regex.IsMatch(source, "^[a-f0-9]{40}$") || release.GetProperty("target_commitish").GetString() != source)
                throw new InvalidDataException("The release and its recorded source commit do not match.");
            found = true;
            // GitHub release ordering is not a version ordering. Inspect the entire bounded page.
            if (version > latest)
            {
                latest = version;
                if (version > current) selected = (exe, info, sums, tag, manifest);
            }
        }
        if (!found) throw new InvalidDataException("No compatible published Windows release was found. Try again after a release is published.");
        if (selected is not { } choice) return new(latest.ToString(3), null);
        {
            var (exe, info, sums, tag, manifest) = choice;
            long size = exe.GetProperty("size").GetInt64();
            if (size < 10_000 || size > 512L * 1024 * 1024) throw new InvalidDataException("The update size is outside the supported range.");
            var sumsUri = AssetUri(sums, tag, "SHA256SUMS.txt");
            byte[] checksumBytes = await ReadSmallAsync(sumsUri, 8192, token);
            if (Convert.ToHexStringLower(SHA256.HashData(checksumBytes)) != Digest(sums)) throw new InvalidDataException("The checksum file did not match GitHub's digest.");
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in Encoding.UTF8.GetString(checksumBytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Regex.Match(line.Trim(), @"^([a-fA-F0-9]{64})\s+\*?([a-zA-Z0-9_.-]+)$");
                if (match.Success) hashes[match.Groups[2].Value] = ParseHash(match.Groups[1].Value);
            }
            string exeHash = Digest(exe);
            if (!hashes.TryGetValue("UhFiveMOptimizer.exe", out var expectedExe) || expectedExe != exeHash ||
                !hashes.TryGetValue("build-info.json", out var expectedInfo) || expectedInfo != Digest(info))
                throw new InvalidDataException("The release checksums are incomplete or inconsistent.");
            string releaseUrl = ReleasesUrl + "/tag/" + Uri.EscapeDataString(tag);
            var best = new UpdateCandidate(manifest.Version, tag, manifest.SourceCommit, releaseUrl, AssetUri(exe, tag, "UhFiveMOptimizer.exe"), size, exeHash);
            return new(latest.ToString(3), best);
        }
    }

    public async Task<string> DownloadAsync(UpdateCandidate candidate, string cacheRoot, IProgress<UpdateProgress>? progress, CancellationToken cancellation = default)
    {
        string hash = ParseHash(candidate.Sha256);
        if (candidate.Size < 10_000 || candidate.Size > 512L * 1024 * 1024 ||
            candidate.DownloadUrl.AbsoluteUri != ReleasesUrl + "/download/" + Uri.EscapeDataString(candidate.Tag) + "/UhFiveMOptimizer.exe")
            throw new InvalidDataException("The update download is invalid.");
        string folder = Path.Combine(cacheRoot, hash); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "UhFiveMOptimizer.exe");
        if (File.Exists(file) && new FileInfo(file).Length == candidate.Size && await FileHashAsync(file, cancellation) == hash)
        { progress?.Report(new(candidate.Size, candidate.Size, true)); return file; }
        string partial = Path.Combine(folder, "download.part");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        try
        {
            using var response = await SendAsync(candidate.DownloadUrl, token);
            if (response.Content.Headers.ContentLength is long length && length != candidate.Size) throw new InvalidDataException("The update size does not match the release.");
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[65536]; long received = 0; var lastReport = DateTime.UtcNow.AddSeconds(-1);
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    received += count; if (received > candidate.Size) throw new InvalidDataException("The download exceeded the expected size.");
                    digest.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), token);
                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 120) { progress?.Report(new(received, candidate.Size)); lastReport = DateTime.UtcNow; }
                }
                if (received != candidate.Size) throw new InvalidDataException("The download ended before the complete update arrived.");
                output.Flush(flushToDisk: true);
            }
            if (Convert.ToHexStringLower(digest.GetHashAndReset()) != hash) throw new InvalidDataException("The downloaded EXE failed SHA-256 verification. It will not be installed.");
            using (var check = File.OpenRead(partial)) if (check.ReadByte() != 'M' || check.ReadByte() != 'Z') throw new InvalidDataException("The download is not a Windows executable.");
            File.Move(partial, file, overwrite: true); progress?.Report(new(candidate.Size, candidate.Size)); return file;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    public static async Task<string> FileHashAsync(string path, CancellationToken cancellation = default)
    { await using var file = File.OpenRead(path); return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellation)); }

    private async Task<byte[]> ReadSmallAsync(Uri uri, int maximum, CancellationToken token)
    {
        using var response = await SendAsync(uri, token);
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        { if (output.Length + read > maximum) throw new InvalidDataException("Update metadata exceeded its size limit."); output.Write(buffer, 0, read); }
        return output.ToArray();
    }
    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken token)
    {
        for (int redirects = 0; redirects < 5; redirects++)
        {
            if (uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort ||
                uri.Host is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("An update request was redirected outside GitHub's approved HTTPS hosts.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("UhFiveMOptimizer/0.3.1");
            if (uri.Host == "api.github.com") { request.Headers.Accept.ParseAdd("application/vnd.github+json"); request.Headers.Add("X-GitHub-Api-Version", "2022-11-28"); }
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var next = response.Headers.Location; response.Dispose();
                if (next is null) throw new InvalidDataException("GitHub returned a redirect without a location.");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next); continue;
            }
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            { response.Dispose(); throw new HttpRequestException("GitHub temporarily limited this update check. Please try again later."); }
            try { response.EnsureSuccessStatusCode(); return response; } catch { response.Dispose(); throw; }
        }
        throw new HttpRequestException("The update request exceeded its redirect limit.");
    }
    private sealed record BuildManifest(string App, string Version, string SourceCommit, string Target, bool SelfContained);
}

public static class UpdateFileTransaction
{
    /// <summary>Caller waits for the old app to exit first. Failed relaunch restores the previous executable.</summary>
    public static void ReplaceAndLaunch(string staged, string target, string backup, string expectedHash, Action<string> launch)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(target))!;
        if (Path.GetDirectoryName(Path.GetFullPath(staged)) != directory || Path.GetDirectoryName(Path.GetFullPath(backup)) != directory ||
            Path.GetFullPath(staged) == Path.GetFullPath(target) || Path.GetFullPath(backup) == Path.GetFullPath(target))
            throw new InvalidDataException("Update staging and backup must be separate files in the installation folder.");
        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(staged) & FileAttributes.ReparsePoint) != 0 ||
            (File.Exists(backup) && (File.GetAttributes(backup) & FileAttributes.ReparsePoint) != 0))
            throw new InvalidDataException("Updates cannot replace linked files.");
        using (var input = File.OpenRead(staged))
            if (Convert.ToHexStringLower(SHA256.HashData(input)) != UpdateClient.ParseHash(expectedHash)) throw new InvalidDataException("The staged update failed verification.");
        File.Replace(staged, target, backup);
        try { launch(target); }
        catch
        {
            string failed = staged + ".failed";
            File.Replace(backup, target, failed);
            throw new IOException("The new app could not start. The previous executable has been restored; reopen it to continue.");
        }
    }
}
