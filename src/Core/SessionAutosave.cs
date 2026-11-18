using System.Text.Json;

namespace Locaptio.Core;

public sealed record AutosaveRow(long Id, double Seconds, string Text, string? Translation, string Language, int SpeakerId = 0, string? Original = null, bool Edited = false);
public sealed record AutosaveSnapshot(int Version, DateTimeOffset SavedAt, string Mode, string? RecordingPath, AutosaveRow[] Rows, RecordingBookmark[]? Bookmarks = null, AutosaveSummary? Summary = null);
/// <summary>One live-summary block: the rows it covers and its lines (row IDs are the snapshot's).</summary>
public sealed record AutosaveSummaryBlock(long FirstId, long LastId, SummaryItem[] Items);
/// <summary>The summary pane: a full summary, live blocks, folded blocks, the "so far" digest and where live summary continues.</summary>
public sealed record AutosaveSummary(SummaryItem[] Full, AutosaveSummaryBlock[] Live, AutosaveSummaryBlock[] Archived, AutosaveSummaryBlock? Digest, long NextId);

/// <summary>
/// Crash recovery for the transcript on screen. The snapshot is replaced atomically while a
/// session has unsaved text and deleted on a normal exit, so finding one at startup means the
/// previous run did not end normally. Only text and file paths are stored, never audio.
/// </summary>
public static class SessionAutosave
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Locaptio", "autosave", "session.json");

    public static async Task SaveAsync(string path, AutosaveSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot));
        // Keep the previous snapshot: a crash during this write must not lose both copies.
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path);
    }
    public static AutosaveSnapshot? TryLoad(string path)
    {
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            try
            {
                var info = new FileInfo(candidate);
                if (!info.Exists || info.Length > 64 * 1024 * 1024) continue;
                var snapshot = JsonSerializer.Deserialize<AutosaveSnapshot>(File.ReadAllText(candidate));
                if (snapshot is not { Version: 1, Rows: not null }) continue;
                var rows = snapshot.Rows.Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Text) && double.IsFinite(r.Seconds) && r.Seconds >= 0).OrderBy(r => r.Id).ToArray();
                if (rows.Length == 0 || rows.Select(r => r.Id).Distinct().Count() != rows.Length) continue;
                return snapshot with { Rows = rows, Bookmarks = snapshot.Bookmarks?.Where(b => b is not null && double.IsFinite(b.Seconds) && b.Seconds >= 0 && !string.IsNullOrWhiteSpace(b.Label)).ToArray() };
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return null;
    }
    public static void Discard(string path)
    {
        foreach (string file in new[] { path, path + ".bak", path + ".tmp" })
            try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Makes the audio of an interrupted recording playable and returns its path, or null when
    /// nothing usable remains. MP3/M4A recordings are captured as WAV first, so an interrupted
    /// one is recovered as that WAV with its search data moved beside it.
    /// </summary>
    public static string? RecoverRecording(string? recordingPath)
    {
        if (string.IsNullOrWhiteSpace(recordingPath)) return null;
        try
        {
            string checkpoint = recordingPath + ".recording.wav";
            string recovered = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(recordingPath))!, Path.GetFileNameWithoutExtension(recordingPath) + ".recovered.wav");
            if (File.Exists(checkpoint) && !File.Exists(recovered))
            {
                File.Move(checkpoint, recovered);
                string sidecar = RecordingTranscript.Sidecar(recordingPath);
                if (File.Exists(sidecar)) File.Move(sidecar, RecordingTranscript.Sidecar(recovered), true);
            }
            // Also reached when recovery is offered again after an earlier attempt already moved the checkpoint.
            if (File.Exists(recovered)) return RepairWave(recovered) ? recovered : null;
            if (!File.Exists(recordingPath)) return null;
            return !Path.GetExtension(recordingPath).Equals(".wav", StringComparison.OrdinalIgnoreCase) || RepairWave(recordingPath) ? recordingPath : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// A WAV header records its own length. After a crash it still describes the last flush,
    /// so players stop early or reject the file; rewrite both sizes from the real file length.
    /// </summary>
    public static bool RepairWave(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var header = new byte[12];
        if (stream.Length < 44 || stream.Read(header, 0, 12) != 12 || header[0] != 'R' || header[1] != 'I' || header[2] != 'F' || header[3] != 'F' || header[8] != 'W' || header[9] != 'A' || header[10] != 'V' || header[11] != 'E') return false;
        var chunk = new byte[8]; int blockAlign = 2;
        while (stream.Position + 8 <= stream.Length)
        {
            long at = stream.Position;
            if (stream.Read(chunk, 0, 8) != 8) return false;
            string id = System.Text.Encoding.ASCII.GetString(chunk, 0, 4); uint size = BitConverter.ToUInt32(chunk, 4);
            if (id == "fmt " && size >= 16)
            {
                var format = new byte[16]; if (stream.Read(format, 0, 16) != 16) return false;
                blockAlign = Math.Max(1, (int)BitConverter.ToUInt16(format, 12)); stream.Position = at + 8 + size + (size & 1); continue;
            }
            if (id != "data") { stream.Position = at + 8 + size + (size & 1); continue; }
            long data = stream.Length - (at + 8); data -= data % blockAlign;
            if (data <= 0 || data > uint.MaxValue) return false;
            if (size != data)
            {
                stream.SetLength(at + 8 + data);
                stream.Position = at + 4; stream.Write(BitConverter.GetBytes((uint)data));
                stream.Position = 4; stream.Write(BitConverter.GetBytes((uint)(stream.Length - 8)));
            }
            return true;
        }
        return false;
    }
}
