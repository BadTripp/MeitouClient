using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Meitou.Launcher;

/// <summary>A news post: <c>news/&lt;yyyy-mm-dd&gt;-&lt;slug&gt;.md</c> with an optional front matter block (docs/launcher.md, "News").</summary>
public sealed record NewsPost(string File, string Title, DateOnly? Date, string? Image, string Body, string BaseUrl);

/// <summary>
/// The Home page's posts, from the repository's <c>news/</c> folder: listed through the contents API (one call, cached with its
/// ETag), each post fetched from raw.githubusercontent.com and kept on disk by its blob hash, so only new or changed posts download.
/// Images are fetched the same way and cached by URL. Works offline from the cache.
/// </summary>
public sealed class NewsFeed(HttpClient http)
{
    public const string Branch = "master";
    const string Folder = "news";
    const int MaxPosts = 20;

    static string CacheDirectory => Path.Combine(LauncherConfig.DataDirectory, "news-cache");

    /// <summary>The newest posts first (by file name, which starts with the date).</summary>
    public async Task<(List<NewsPost> Posts, string? Problem)> FetchAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(CacheDirectory);
        var listFile = Path.Combine(CacheDirectory, "list.json");
        var etagFile = Path.Combine(CacheDirectory, "list.etag");
        string? listJson = File.Exists(listFile) ? await File.ReadAllTextAsync(listFile, ct) : null;
        string? problem = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Releases.Repository}/contents/{Folder}?ref={Branch}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            if (listJson is not null && File.Exists(etagFile)) request.Headers.TryAddWithoutValidation("If-None-Match", await File.ReadAllTextAsync(etagFile, ct));
            using var response = await http.SendAsync(request, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotModified) { }
            else if (response.IsSuccessStatusCode)
            {
                listJson = await response.Content.ReadAsStringAsync(timeout.Token);
                await File.WriteAllTextAsync(listFile, listJson, ct);
                await File.WriteAllTextAsync(etagFile, response.Headers.ETag?.ToString() ?? "", ct);
            }
            else if (response.StatusCode == HttpStatusCode.NotFound) listJson = "[]";
            else problem = $"News: GitHub answered {(int)response.StatusCode}";
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            problem = "News: GitHub not reachable" + (listJson is null ? "" : "; showing what was read before");
        }
        if (listJson is null) return ([], problem);

        var posts = new List<NewsPost>();
        using var doc = JsonDocument.Parse(listJson);
        var files = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray()
                .Where(f => f.GetProperty("type").GetString() == "file" && (f.GetProperty("name").GetString() ?? "").EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(f.GetProperty("name").GetString(), "README.md", StringComparison.OrdinalIgnoreCase))
                .Select(f => (Name: f.GetProperty("name").GetString()!, Sha: f.GetProperty("sha").GetString()!, Url: f.GetProperty("download_url").GetString()!))
                .OrderByDescending(f => f.Name, StringComparer.Ordinal).Take(MaxPosts).ToList()
            : [];
        foreach (var (name, sha, url) in files)
        {
            var cached = Path.Combine(CacheDirectory, sha + ".md");
            try
            {
                if (!File.Exists(cached)) await File.WriteAllTextAsync(cached, await http.GetStringAsync(url, ct), ct);
                posts.Add(Parse(name, await File.ReadAllTextAsync(cached, ct), url[..(url.LastIndexOf('/') + 1)]));
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
            {
                problem ??= $"News: {name} could not be read";
            }
        }
        return (posts, problem);
    }

    /// <summary>Front matter (<c>---</c> lines around <c>key: value</c> lines: title, date, image), then Markdown.</summary>
    static NewsPost Parse(string file, string text, string baseUrl)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int bodyStart = 0;
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            int end = Array.FindIndex(lines, 1, l => l.Trim() == "---");
            if (end > 0)
            {
                foreach (var line in lines[1..end])
                    if (line.IndexOf(':') is var colon and > 0)
                        meta[line[..colon].Trim()] = line[(colon + 1)..].Trim().Trim('"');
                bodyStart = end + 1;
            }
        }
        DateOnly? date = meta.TryGetValue("date", out var d) && DateOnly.TryParse(d, out var parsed) ? parsed
            : file.Length >= 10 && DateOnly.TryParse(file[..10], out var fromName) ? fromName : null;
        var title = meta.GetValueOrDefault("title") ?? Path.GetFileNameWithoutExtension(file);
        return new NewsPost(file, title, date, meta.GetValueOrDefault("image"), string.Join("\n", lines[bodyStart..]).Trim(), baseUrl);
    }

    /// <summary>An image's bytes, from the disk cache or downloaded once; null when it cannot be had.</summary>
    public async Task<byte[]?> ImageAsync(string url, CancellationToken ct = default)
    {
        var cached = Path.Combine(CacheDirectory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32] + ".img");
        try
        {
            if (File.Exists(cached)) return await File.ReadAllBytesAsync(cached, ct);
            var bytes = await http.GetByteArrayAsync(url, ct);
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllBytesAsync(cached, bytes, ct);
            return bytes;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            return null;
        }
    }

    /// <summary>A link or image path in a post, made absolute against the post's folder.</summary>
    public static string Resolve(NewsPost post, string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var absolute) ? absolute.ToString() : new Uri(new Uri(post.BaseUrl), url).ToString();
}
