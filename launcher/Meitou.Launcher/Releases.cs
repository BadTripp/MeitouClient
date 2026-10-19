using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Meitou.Launcher;

/// <summary>What a GitHub release of this repository is: the game (tags <c>v&lt;version&gt;</c>) or the launcher (<c>launcher-v&lt;version&gt;</c>).</summary>
public enum ReleaseKind { Game, Launcher }

public sealed record ReleaseAsset(string Name, string Url, long Size, string? Digest);

public sealed record Release(ReleaseKind Kind, string Tag, string Version, string Title, bool Prerelease, DateTimeOffset Published,
    string Notes, string PageUrl, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>The zip for this machine: <c>Meitou-&lt;version&gt;-&lt;rid&gt;.zip</c> or <c>MeitouLauncher-&lt;version&gt;-&lt;rid&gt;.zip</c>.</summary>
    public ReleaseAsset? Asset => Assets.FirstOrDefault(a => a.Name == $"{(Kind == ReleaseKind.Game ? "Meitou" : "MeitouLauncher")}-{Version}-{Releases.Rid}.zip");
}

/// <summary>
/// The repository's GitHub releases (docs/launcher.md): listed through the REST API without a token, with the last answer and its
/// ETag cached so an unchanged list costs no rate limit; installed game versions in <c>versions/&lt;version&gt;</c>.
/// </summary>
public sealed class Releases(HttpClient http)
{
    public const string Repository = "VlcekM/MeitouClient";

    /// <summary>The platform part of asset names, e.g. <c>win-x64</c>, <c>linux-x64</c>, <c>osx-arm64</c>.</summary>
    public static string Rid => RuntimeInformation.RuntimeIdentifier;

    /// <summary>The game's executable names, newest first: <c>MeitouClient</c> since 0.15, <c>meitou</c> before (<c>.exe</c> on Windows).</summary>
    static readonly string[] GameExecutables = OperatingSystem.IsWindows() ? ["MeitouClient.exe", "meitou.exe"] : ["MeitouClient", "meitou"];

    /// <summary>The game's executable in <paramref name="directory"/>, or null.</summary>
    public static string? FindGame(string directory) =>
        GameExecutables.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);

    static string VersionsDirectory => Path.Combine(LauncherConfig.DataDirectory, "versions");
    static string CacheFile => Path.Combine(LauncherConfig.DataDirectory, "releases-cache.json");

    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // a big download may take minutes; callers cancel instead
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("meitou-launcher", typeof(Releases).Assembly.GetName().Version?.ToString(3) ?? "0"));
        return client;
    }

    /// <summary>Every published release, newest first. Falls back to the cached list when GitHub cannot be reached or rate-limits.</summary>
    public async Task<(List<Release> Releases, string? Problem)> FetchAsync(CancellationToken ct = default)
    {
        var cached = ReadCache();
        string? problem = null;
        string? body = cached?.Body;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases?per_page=100");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            if (cached?.ETag is { } etag) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using var response = await http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotModified) { }
            else if (response.IsSuccessStatusCode)
            {
                body = await response.Content.ReadAsStringAsync(timeout.Token);
                WriteCache(new Cache(response.Headers.ETag?.ToString(), body));
            }
            else problem = $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}" + (body is null ? "" : "; showing the last list");
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            problem = $"GitHub not reachable ({e.Message})" + (body is null ? "" : "; showing the last list");
        }
        return (body is null ? [] : Parse(body), problem);
    }

    static List<Release> Parse(string json)
    {
        var list = new List<Release>();
        using var doc = JsonDocument.Parse(json);
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.GetProperty("draft").GetBoolean()) continue;
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            ReleaseKind kind;
            string version;
            if (tag.StartsWith("launcher-v", StringComparison.Ordinal)) { kind = ReleaseKind.Launcher; version = tag["launcher-v".Length..]; }
            else if (tag.Length > 1 && tag[0] == 'v' && char.IsAsciiDigit(tag[1])) { kind = ReleaseKind.Game; version = tag[1..]; }
            else continue;
            var assets = r.GetProperty("assets").EnumerateArray().Select(a => new ReleaseAsset(
                a.GetProperty("name").GetString() ?? "",
                a.GetProperty("browser_download_url").GetString() ?? "",
                a.GetProperty("size").GetInt64(),
                a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null)).ToList();
            list.Add(new Release(kind, tag, version, r.GetProperty("name").GetString() ?? tag, r.GetProperty("prerelease").GetBoolean(),
                r.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String ? p.GetDateTimeOffset() : default,
                r.GetProperty("body").GetString() ?? "", r.GetProperty("html_url").GetString() ?? "", assets));
        }
        return [.. list.OrderByDescending(r => r.Published)];
    }

    /// <summary>Installed game versions (folders of <c>versions/</c> holding the game), newest version first.</summary>
    public static List<string> Installed()
    {
        if (!Directory.Exists(VersionsDirectory)) return [];
        return [.. Directory.EnumerateDirectories(VersionsDirectory)
            .Where(d => FindGame(d) is not null)
            .Select(Path.GetFileName).OfType<string>()
            .OrderByDescending(v => System.Version.TryParse(v.Split('-')[0], out var n) ? n : new System.Version())];
    }

    public static string VersionDirectory(string version) => Path.Combine(VersionsDirectory, version);

    /// <summary>
    /// Downloads the release's zip, checks its SHA-256 against the digest GitHub lists (when it lists one) and unpacks it into
    /// <c>versions/&lt;version&gt;</c>, taking the zip's single top folder as the version's folder.
    /// </summary>
    public async Task InstallAsync(Release release, IProgress<double> progress, CancellationToken ct = default)
    {
        var asset = release.Asset ?? throw new InvalidOperationException($"{release.Title} has no download for {Rid}.");
        var downloads = Path.Combine(LauncherConfig.DataDirectory, "downloads");
        Directory.CreateDirectory(downloads);
        var zip = Path.Combine(downloads, asset.Name);
        var target = VersionDirectory(release.Version);
        var staging = target + ".partial";
        try
        {
            using (var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? asset.Size;
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var file = File.Create(zip);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1 << 16];
                long done = 0;
                int n;
                while ((n = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                    sha.AppendData(buffer, 0, n);
                    done += n;
                    if (total > 0) progress.Report((double)done / total);
                }
                var hash = Convert.ToHexStringLower(sha.GetHashAndReset());
                if (asset.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.Ordinal) && digest[7..] != hash)
                    throw new InvalidDataException($"{asset.Name} is damaged: SHA-256 {hash}, GitHub lists {digest[7..]}.");
            }

            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            await ZipFile.ExtractToDirectoryAsync(zip, staging, ct);
            var root = staging;
            if (FindGame(root) is null && Directory.GetFiles(root).Length == 0 && Directory.GetDirectories(root) is [var only])
                root = only;
            var exe = FindGame(root) ?? throw new InvalidDataException($"{asset.Name} has no {GameExecutables[0]}.");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(root, target);
        }
        finally
        {
            // Also on failure or cancel: no half download or half unpacked version is left behind.
            File.Delete(zip);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    internal sealed record Cache(string? ETag, string Body);

    static Cache? ReadCache()
    {
        try { return File.Exists(CacheFile) ? JsonSerializer.Deserialize(File.ReadAllText(CacheFile), LauncherJson.Default.Cache) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    static void WriteCache(Cache cache)
    {
        Directory.CreateDirectory(LauncherConfig.DataDirectory);
        File.WriteAllText(CacheFile, JsonSerializer.Serialize(cache, LauncherJson.Default.Cache));
    }
}
