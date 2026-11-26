using System.Buffers.Binary;

namespace Locaptio.Core;

/// <summary>
/// Makes a fragmented MP4 that was cut off (Locaptio closed while recording) playable again. Video is recorded as
/// fragments that are written as they are made, so everything up to the last complete fragment is in the file; only
/// the end is missing. The repair drops the incomplete fragment, writes the total length into the movie header
/// (it stays 0 until recording stops) and adds the closing index box. Windows' media components refuse the file
/// without them.
/// </summary>
public static class Mp4Repair
{
    private readonly record struct Box(string Type, long Start, int Header, long Size, bool Complete)
    {
        public long End => Start + Size;
        public long Body => Start + Header;
    }

    /// <summary>
    /// True when the file is a playable MP4 afterwards (also when it needed nothing). False when it cannot be used:
    /// not an MP4, no movie header, or no complete fragment (a recording interrupted in its first moments, or one
    /// written by an older version, which kept the header until the end).
    /// </summary>
    public static bool Repair(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var top = Boxes(data, 0, data.Length).ToList();
        if (top.Count == 0 || top[0].Type != "ftyp" || top.FirstOrDefault(b => b.Type == "moov") is not { Complete: true } moov) return false;
        if (!top.Any(b => b.Type == "moof")) return top.All(b => b.Complete);
        if (top[^1] is { Type: "mfra", Complete: true } && Duration(data, moov) > 0) return true;

        // Keep whole fragments (moof followed by its mdat) and add up each track's time.
        var tracks = Tracks(data, moov);
        var defaults = TrackDefaults(data, moov);
        long end = 0; bool any = false;
        for (int i = 0; i < top.Count; i++)
        {
            var box = top[i];
            if (!box.Complete || box.Type == "mfra") break;
            if (box.Type != "moof") { end = box.End; continue; }
            if (i + 1 >= top.Count || top[i + 1] is not { Type: "mdat", Complete: true } mdat) break;
            foreach (var traf in Boxes(data, box.Body, box.End).Where(b => b.Type == "traf" && b.Complete)) AddFragment(data, traf, tracks, defaults);
            end = mdat.End; any = true; i++;
        }
        if (!any) return false;
        var header = Boxes(data, moov.Body, moov.End).FirstOrDefault(b => b.Type == "mvhd");
        if (header.Type != "mvhd") return false;
        int version = data[header.Body];
        uint movieScale = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)header.Body + (version == 1 ? 20 : 12)));
        ulong duration = tracks.Values.Where(t => t.Scale > 0).Select(t => (ulong)(t.End * (double)movieScale / t.Scale)).DefaultIfEmpty(0UL).Max();
        if (duration == 0) return false;
        if (version == 1) BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan((int)header.Body + 24), duration);
        else BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan((int)header.Body + 16), (uint)Math.Min(uint.MaxValue, duration));
        // An empty random-access index: its last box (mfro) gives the size of the whole mfra box.
        var index = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(index, 24); "mfra"u8.CopyTo(index.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(index.AsSpan(8), 16); "mfro"u8.CopyTo(index.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(index.AsSpan(20), 24);
        string temporary = path + ".repair";
        using (var output = File.Create(temporary)) { output.Write(data, 0, (int)end); output.Write(index); }
        File.Move(temporary, path, true);
        return true;
    }

    private static ulong Duration(byte[] data, Box moov)
    {
        var header = Boxes(data, moov.Body, moov.End).FirstOrDefault(b => b.Type == "mvhd");
        if (header.Type != "mvhd") return 0;
        int version = data[header.Body];
        return version == 1 ? BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan((int)header.Body + 24)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)header.Body + 16));
    }

    private sealed class Track { public uint Scale; public ulong End; }

    /// <summary>Track ID → its time scale (from tkhd and mdhd).</summary>
    private static Dictionary<uint, Track> Tracks(byte[] data, Box moov)
    {
        var tracks = new Dictionary<uint, Track>();
        foreach (var trak in Boxes(data, moov.Body, moov.End).Where(b => b.Type == "trak"))
        {
            uint id = 0, scale = 0;
            foreach (var box in Boxes(data, trak.Body, trak.End))
            {
                if (box.Type == "tkhd") id = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)box.Body + (data[box.Body] == 1 ? 20 : 12)));
                if (box.Type == "mdia")
                    foreach (var media in Boxes(data, box.Body, box.End).Where(b => b.Type == "mdhd"))
                        scale = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)media.Body + (data[media.Body] == 1 ? 20 : 12)));
            }
            if (id != 0) tracks[id] = new Track { Scale = scale };
        }
        return tracks;
    }

    /// <summary>Track ID → default sample duration (trex in mvex).</summary>
    private static Dictionary<uint, uint> TrackDefaults(byte[] data, Box moov)
    {
        var defaults = new Dictionary<uint, uint>();
        foreach (var mvex in Boxes(data, moov.Body, moov.End).Where(b => b.Type == "mvex"))
            foreach (var trex in Boxes(data, mvex.Body, mvex.End).Where(b => b.Type == "trex"))
                defaults[BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)trex.Body + 4))] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)trex.Body + 12));
        return defaults;
    }

    /// <summary>Adds one track fragment's samples to its track's end time (tfdt when present, else after the previous fragment).</summary>
    private static void AddFragment(byte[] data, Box traf, Dictionary<uint, Track> tracks, Dictionary<uint, uint> defaults)
    {
        uint id = 0, defaultDuration = 0; ulong? start = null; ulong total = 0; bool hasDefault = false;
        foreach (var box in Boxes(data, traf.Body, traf.End))
        {
            int at = (int)box.Body;
            if (box.Type == "tfhd")
            {
                uint flags = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at)) & 0xFFFFFF;
                id = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 4)); int q = at + 8;
                if ((flags & 0x01) != 0) q += 8;
                if ((flags & 0x02) != 0) q += 4;
                if ((flags & 0x08) != 0) { defaultDuration = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(q)); hasDefault = true; }
            }
            else if (box.Type == "tfdt")
                start = data[at] == 1 ? BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(at + 4)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 4));
            else if (box.Type == "trun")
            {
                uint flags = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at)) & 0xFFFFFF;
                uint count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 4)); int q = at + 8;
                if ((flags & 0x001) != 0) q += 4;
                if ((flags & 0x004) != 0) q += 4;
                int each = new[] { 0x100u, 0x200u, 0x400u, 0x800u }.Count(f => (flags & f) != 0) * 4;
                uint fallback = hasDefault ? defaultDuration : defaults.GetValueOrDefault(id);
                for (uint i = 0; i < count && q + each <= box.End; i++, q += each)
                    total += (flags & 0x100) != 0 ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(q)) : fallback;
            }
        }
        if (tracks.TryGetValue(id, out var track)) track.End = (start ?? track.End) + total;
    }

    private static IEnumerable<Box> Boxes(byte[] data, long from, long to)
    {
        for (long at = from; at + 8 <= to;)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)at)); int header = 8;
            string type = System.Text.Encoding.Latin1.GetString(data, (int)at + 4, 4);
            if (size == 1) { if (at + 16 > to) yield break; size = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan((int)at + 8)); header = 16; }
            else if (size == 0) size = to - at;
            if (size < header || at + size > to) { yield return new(type, at, header, size, false); yield break; }
            yield return new(type, at, header, size, true);
            at += size;
        }
    }
}
