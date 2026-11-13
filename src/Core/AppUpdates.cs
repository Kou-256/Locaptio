using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Locaptio.Core;

public sealed record AppRelease(Version Version, Uri Download, long Size, string Sha256);

/// <summary>Public release metadata only; never sends audio, transcripts or settings.</summary>
public sealed class AppUpdates(HttpClient http)
{
    public const string LatestApi = "https://api.github.com/repos/Kou-256/Locaptio/releases/latest";
    private const long MaximumSize = 1_073_741_824;
    public static string InstallerName(Version version) => $"Locaptio-Setup-{new Version(version.Major, version.Minor, Math.Max(0, version.Build)).ToString(3)}-x64.exe";
    public static AppRelease? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json); var data = doc.RootElement;
        if (data.GetProperty("draft").GetBoolean() || data.GetProperty("prerelease").GetBoolean()) return null;
        string tag = data.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v'), out var version) || Normalize(version) <= Normalize(current)) return null;
        foreach (var asset in data.GetProperty("assets").EnumerateArray().OrderBy(a => a.GetProperty("name").GetString() == InstallerName(version) ? 0 : a.GetProperty("name").GetString() == "Locaptio-Setup-x64.exe" ? 1 : 2))
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (name is not ("Locaptio-win-x64.zip" or "Locaptio-Setup-x64.exe") && name != InstallerName(version)) continue;
            string expected = $"https://github.com/Kou-256/Locaptio/releases/download/{tag}/{name}";
            if (asset.GetProperty("browser_download_url").GetString() != expected) throw new InvalidDataException("更新ファイルの配布元が一致しません。");
            long size = asset.GetProperty("size").GetInt64();
            string hash = asset.TryGetProperty("digest", out var digest) ? digest.GetString() ?? "" : "";
            if (size <= 0 || size > MaximumSize || !hash.StartsWith("sha256:") || hash.Length != 71 || !hash[7..].All(Uri.IsHexDigit))
                throw new InvalidDataException("更新ファイルのサイズ／SHA-256を確認できません。");
            return new(version, new Uri(expected), size, hash[7..]);
        }
        return null; // A source-only release is not a portable app update.
    }
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
    public async Task<AppRelease?> CheckAsync(Version current, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        request.Headers.UserAgent.ParseAdd("Locaptio/" + current);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(token), current);
    }
    public async Task<string> DownloadAsync(AppRelease release, string directory, Action<int>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        string name = Path.GetFileName(release.Download.LocalPath);
        if (name != InstallerName(release.Version) && name is not ("Locaptio-Setup-x64.exe" or "Locaptio-win-x64.zip")) throw new InvalidDataException("更新ファイル名が正しくありません。");
        string target = Path.Combine(directory, name);
        if (File.Exists(target) && await MatchesAsync(target, release, token)) { progress?.Invoke(100); return target; }
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, release.Download);
            request.Headers.UserAgent.ParseAdd("Locaptio-Updater");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size) throw new InvalidDataException("更新ファイルのサイズが一致しません。");
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920]; long total = 0; int lastPercent = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    total += read;
                    if (total > release.Size) throw new InvalidDataException("更新ファイルが予定サイズを超えました。");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    int percent = (int)(total * 100 / release.Size);
                    if (percent != lastPercent) { lastPercent = percent; progress?.Invoke(Math.Min(99, percent)); }
                }
            }
            if (!await MatchesAsync(temporary, release, token)) throw new InvalidDataException("更新ファイルのSHA-256が一致しません。再度ダウンロードしてください。");
            File.Move(temporary, target, true); progress?.Invoke(100); return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task<bool> MatchesAsync(string path, AppRelease release, CancellationToken token)
    {
        if (new FileInfo(path).Length != release.Size) return false;
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
