using System.Diagnostics;

namespace Locaptio.Core;

public static class TranscriptExport
{
    public static string FileName(string pattern, DateTimeOffset date)
    {
        string name = pattern ?? "{yyyy}{MM}{dd}_";
        foreach (string token in new[] { "yyyy", "MM", "dd", "HH", "mm", "ss" })
            name = name.Replace("{" + token + "}", date.ToString(token, System.Globalization.CultureInfo.InvariantCulture));
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (string.IsNullOrWhiteSpace(name)) name = date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "_";
        if (name.Length > 120) name = name[..(char.IsHighSurrogate(name[119]) ? 119 : 120)];
        // Windows reserves these device names even when an extension is appended.
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem)) name = "_" + name;
        return name + ".txt";
    }
    public static string Format(string body, int utterances, DateTimeOffset savedAt, TimeSpan duration,
        bool includeDate, bool includeDuration, bool includeLines)
    {
        if (!includeDate && !includeDuration && !includeLines) return body;
        var header = new List<string>();
        if (includeDate) header.Add($"保存日時: {savedAt:yyyy-MM-dd HH:mm:ss zzz}");
        if (includeDuration) header.Add($"セッション時間（準備・無音を含む）: {(long)duration.TotalHours}時間{duration.Minutes:D2}分{duration.Seconds:D2}秒");
        if (includeLines)
        {
            int lines = body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Count(line => !string.IsNullOrWhiteSpace(line));
            header.Add($"本文行数（空行を除く）: {lines}行 / 発話数: {utterances}");
        }
        return string.Join(Environment.NewLine, header) + Environment.NewLine + Environment.NewLine + body;
    }
}

public sealed class TranscriptSessionClock
{
    private readonly Stopwatch clock = new();
    public TimeSpan Elapsed => clock.Elapsed;
    public void Start() => clock.Start();
    public void Stop() => clock.Stop();
    public void Reset(bool running) { clock.Reset(); if (running) clock.Start(); }
}
