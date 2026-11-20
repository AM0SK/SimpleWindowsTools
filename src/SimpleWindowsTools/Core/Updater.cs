using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleWindowsTools.Core;

/// <summary>A newer release on GitHub.</summary>
public sealed record UpdateInfo(Version Version, string DownloadUrl, string PageUrl, long Size);

/// <summary>
/// Updates come from GitHub Releases of <see cref="Owner"/>/<see cref="Repo"/>: the latest published release
/// (not drafts or pre-releases) whose tag is "v1.2.3" and which carries <see cref="AssetName"/>.
/// The GitHub workflow (.github/workflows/release.yml) publishes one whenever the csproj version changes.
/// Installing = download the setup exe to %TEMP% and run it; it finds the installed copy and updates it.
/// </summary>
public static class Updater
{
    public const string Owner = "AM0SK";
    public const string Repo = "SimpleWindowsTools";
    public const string AssetName = "SimpleWindowsTools-Setup.exe";

    public static string RepoUrl => $"https://github.com/{Owner}/{Repo}";
    public static string ReleasesUrl => RepoUrl + "/releases";
    private static string LatestApi => $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    public static Version CurrentVersion { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub requires a User-Agent; a plain product name, nothing about the user.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"SimpleWindowsTools/{CurrentVersion}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>"v0.5.0" / "0.5" → 0.5.0; anything else → null.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(t, out var v) ? Normalize(v) : null;
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>Reads the GitHub "latest release" JSON. Returns the update if it is newer than <paramref name="current"/>.</summary>
    public static UpdateInfo? ParseLatest(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
        var version = ParseTag(root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null);
        if (version is null || version <= current) return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        var asset = assets.EnumerateArray().FirstOrDefault(a =>
            string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind != JsonValueKind.Object) return null;
        var url = asset.GetProperty("browser_download_url").GetString();
        // Only ever download from GitHub itself.
        if (url is null || !url.StartsWith($"https://github.com/{Owner}/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase)) return null;
        long size = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
        var page = root.TryGetProperty("html_url", out var html) ? html.GetString() ?? ReleasesUrl : ReleasesUrl;
        return new UpdateInfo(version, url, page, size);
    }

    /// <summary>Asks GitHub for the latest release. Null = up to date. Throws on network/API errors.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(LatestApi, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases yet
        response.EnsureSuccessStatusCode();
        return ParseLatest(await response.Content.ReadAsStringAsync(ct), CurrentVersion);
    }

    /// <summary>Downloads the setup exe to %TEMP%\SimpleWindowsTools-Update and returns its path.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "SimpleWindowsTools-Update");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"SimpleWindowsTools-Setup-{update.Version}.exe");
        var part = file + ".part";

        using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? update.Size;
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(part);
            await CopyAsync(src, dst, total, StallTimeout, progress, ct);
        }

        if (!LooksLikeExe(part)) { File.Delete(part); throw new InvalidDataException("Downloaded file is not a Windows program."); }
        File.Move(part, file, overwrite: true);
        return file;
    }

    /// <summary>Download fails if no data arrives for this long (HttpClient.Timeout stops at the headers).</summary>
    internal static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Copies the download. Throws <see cref="TimeoutException"/> when the connection goes quiet for
    /// <paramref name="stall"/>, and <see cref="InvalidDataException"/> when it ends before
    /// <paramref name="expected"/> bytes (a cut-off file must never be started).
    /// </summary>
    internal static async Task<long> CopyAsync(Stream src, Stream dst, long expected, TimeSpan stall,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var buffer = new byte[81920];
        long done = 0;
        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (true)
        {
            quiet.CancelAfter(stall);
            int read;
            try
            {
                read = await src.ReadAsync(buffer, quiet.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"No data for {stall.TotalSeconds:0} s, download stopped at {done} bytes.");
            }
            if (read == 0) break;
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (expected > 0) progress?.Report((double)done / expected);
        }
        if (expected > 0 && done != expected)
            throw new InvalidDataException($"Download cut off: got {done} of {expected} bytes.");
        return done;
    }

    /// <summary>At least 1 MB and starts with "MZ" — catches HTML error pages and cut-off downloads.</summary>
    public static bool LooksLikeExe(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 1024 * 1024) return false;
        using var f = File.OpenRead(path);
        return f.ReadByte() == 'M' && f.ReadByte() == 'Z';
    }
}
