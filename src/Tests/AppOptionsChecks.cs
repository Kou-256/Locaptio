using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Locaptio.Core;

internal static class AppOptionsChecks
{
    public static async Task RunAsync()
    {
        var date = new DateTimeOffset(2026, 11, 11, 17, 23, 45, TimeSpan.FromHours(9));
        const string body = "first\r\n\r\n日本語の翻訳\r\nsecond\r\n";
        string text = TranscriptExport.Format(body, 2, date, TimeSpan.FromHours(25) + TimeSpan.FromSeconds(62), true, true, true);
        Assert(text.Contains("2026-11-11 17:23:45 +09:00") && text.Contains("25時間01分02秒") && text.Contains("3行 / 発話数: 2"), "Export metadata incorrect");
        Assert(TranscriptExport.Format(body, 2, date, TimeSpan.Zero, false, false, false) == body, "Disabled metadata changed the transcript");
        Assert(!TranscriptExport.Format(body, 2, date, TimeSpan.Zero, false, true, false).Contains("保存日時"), "Independent export switches failed");
        Assert(TranscriptExport.FileName("{yyyy}{MM}{dd}_", date) == "20261111_.txt", "Default filename incorrect");
        Assert(TranscriptExport.FileName("会議_{yyyy}-{MM}-{dd}_{HH}{mm}{ss}", date) == "会議_2026-11-11_172345.txt", "Custom filename tokens failed");
        Assert(TranscriptExport.FileName("CON", date) == "_CON.txt" && !TranscriptExport.FileName("../x:y.txt", date).Contains('/'), "Filename sanitization failed");
        var clock = new TranscriptSessionClock(); clock.Start(); await Task.Delay(20); clock.Stop(); var elapsed = clock.Elapsed;
        await Task.Delay(20); Assert(clock.Elapsed == elapsed && elapsed > TimeSpan.Zero, "Stopped duration kept growing");
        clock.Reset(false); Assert(clock.Elapsed == TimeSpan.Zero, "Clear did not reset duration");

        byte[] bytes = Encoding.UTF8.GetBytes("fixture release zip bytes");
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string Payload(string tag = "v1.2.0", string? digest = null, string? url = null, bool prerelease = false) => JsonSerializer.Serialize(new
        {
            tag_name = tag, draft = false, prerelease,
            assets = new[] { new { name = "Locaptio-win-x64.zip", browser_download_url = url ?? $"https://github.com/Kou-256/Locaptio/releases/download/{tag}/Locaptio-win-x64.zip", size = bytes.Length, digest = digest ?? "sha256:" + hash } }
        });
        var release = AppUpdates.Parse(Payload(), new Version(1, 1, 9))!;
        var setupRelease = AppUpdates.Parse(Payload().Replace("Locaptio-win-x64.zip", "Locaptio-Setup-x64.exe"), new Version(1, 1, 9))!;
        Assert(setupRelease.Download.AbsolutePath.EndsWith("Locaptio-Setup-x64.exe"), "Setup update not detected");
        Assert(release.Version == new Version(1, 2, 0), "New version not detected");
        Assert(AppUpdates.Parse(Payload(), new Version(1, 2, 0, 0)) is null && AppUpdates.Parse(Payload(), new Version(2, 0, 0)) is null, "Same version/downgrade offered");
        Assert(AppUpdates.Parse(Payload(prerelease: true), new Version(1, 0, 0)) is null, "Preview release offered");
        foreach (string payload in new[] { Payload(digest: ""), Payload(url: "https://example.com/evil.zip") })
            try { AppUpdates.Parse(payload, new Version(1, 0, 0)); throw new Exception("Invalid release accepted"); } catch (InvalidDataException) { }
        string folder = Path.Combine(Path.GetTempPath(), "Locaptio-update-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new HttpClient(new FixtureHandler(bytes)); var service = new AppUpdates(client);
            string path = await service.DownloadAsync(release, folder, null, CancellationToken.None);
            Assert(File.ReadAllBytes(path).SequenceEqual(bytes) && !Directory.EnumerateFiles(folder, "*.partial").Any(), "Verified download failed");
            File.Delete(path);
            var incorrect = release with { Sha256 = new string('0', 64) };
            try { await service.DownloadAsync(incorrect, folder, null, CancellationToken.None); throw new Exception("Hash mismatch accepted"); } catch (InvalidDataException) { }
            Assert(!File.Exists(path) && !Directory.EnumerateFiles(folder).Any(), "Failed download was left as a completed update");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { await service.DownloadAsync(release, folder, null, canceled.Token); throw new Exception("Download ignored cancellation"); } catch (OperationCanceledException) { }
            Assert(!Directory.EnumerateFiles(folder).Any(), "Canceled download left temporary files");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, false); }
        Console.WriteLine("PASS: export flags/date/duration/line count; default/custom/safe filenames; session clock; update versions, trusted URL, SHA-256, verified download, failure cleanup and cancellation");
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class FixtureHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
    }
}
