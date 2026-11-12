using System.Text;

namespace Locaptio.Core;
public sealed record SavedRecording(string AudioPath, string Title, DateTime Modified);
public static class RecordingLibrary
{
    public static string CreateSessionPath(string requested)
    {
        string parent = Path.GetDirectoryName(Path.GetFullPath(requested))!;
        string name = Path.GetFileNameWithoutExtension(requested);
        string folder = Path.Combine(parent, name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, Path.GetFileName(requested));
    }
    public static SavedRecording[] List(string root)
    {
        if (!Directory.Exists(root)) return [];
        var folders = new[] { root }.Concat(Directory.EnumerateDirectories(root).Take(1000));
        List<SavedRecording> result = [];
        foreach (var folder in folders)
        {
            try
            {
                foreach (string sidecar in Directory.EnumerateFiles(folder, "*.locaptio.json").Take(1000))
                {
                    string audio = sidecar[..^".locaptio.json".Length];
                    if (!File.Exists(audio)) continue;
                    DateTime modified = File.GetLastWriteTime(audio);
                    result.Add(new(audio, Path.GetFileNameWithoutExtension(audio) + "  ·  " + modified.ToString("yyyy/MM/dd HH:mm:ss"), modified));
                }
            }
            catch (UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(r => r.Modified).ToArray();
    }
    public static Task SaveTextAsync(string audio, RecordingDocument document)
    {
        var text = new StringBuilder();
        text.AppendLine("録音日時: " + document.RecordedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        foreach (var phrase in document.Phrases)
        {
            text.Append('[').Append(TimeSpan.FromSeconds(phrase.Seconds).ToString(@"hh\:mm\:ss")).Append("] ").AppendLine(phrase.Text);
            if (!string.IsNullOrWhiteSpace(phrase.Translation)) text.AppendLine(phrase.Translation);
            text.AppendLine();
        }
        return File.WriteAllTextAsync(audio + ".txt", text.ToString(), new UTF8Encoding(false));
    }
}
