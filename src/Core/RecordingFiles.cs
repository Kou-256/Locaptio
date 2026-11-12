using System.IO.Compression;

namespace Locaptio.Core;

public static class RecordingFiles
{
    public static string[] Bundle(string audio) => new[] { Path.GetFullPath(audio), RecordingTranscript.Sidecar(Path.GetFullPath(audio)), Path.GetFullPath(audio) + ".txt" }.Where(File.Exists).ToArray();
    public static async Task ShareAsync(string audio, string zip, CancellationToken token = default)
    {
        if (!File.Exists(audio)) throw new FileNotFoundException("音声ファイルが見つかりません。", audio);
        // CreateNew never overwrites either user data or an existing export.
        await using var target = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        try
        {
            using var archive = new ZipArchive(target, ZipArchiveMode.Create, true);
            foreach (var file in Bundle(audio))
            {
                var entry = archive.CreateEntry(Path.GetFileName(file), CompressionLevel.Fastest);
                await using var source = File.OpenRead(file); await using var output = entry.Open();
                await source.CopyToAsync(output, token);
            }
        }
        catch { await target.DisposeAsync(); File.Delete(zip); throw; }
    }
    public static async Task<string> MoveAsync(string audio, string destination, CancellationToken token = default)
    {
        if (!File.Exists(audio)) throw new FileNotFoundException("音声ファイルが見つかりません。", audio);
        string moved = RecordingLibrary.CreateSessionPath(Path.Combine(destination, Path.GetFileName(audio)));
        var copied = new List<string>();
        try
        {
            foreach (string source in Bundle(audio))
            {
                token.ThrowIfCancellationRequested();
                string target = moved + source[Path.GetFullPath(audio).Length..];
                copied.Add(target);
                await using var input = File.OpenRead(source);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, token);
            }
        }
        catch
        {
            foreach (string file in copied) if (File.Exists(file)) File.Delete(file);
            if (!Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(moved)!).Any()) Directory.Delete(Path.GetDirectoryName(moved)!);
            throw;
        }
        // Copies are durable before removing originals; unrelated folder contents are untouched.
        foreach (string file in Bundle(audio)) File.Delete(file);
        return moved;
    }
}
