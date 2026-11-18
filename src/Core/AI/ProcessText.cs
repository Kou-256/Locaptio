using System.Globalization;
using System.Text;

namespace Locaptio.Core.AI;

/// <summary>
/// Reads the output of setup scripts line by line. Python and uv write UTF-8, but Windows PowerShell
/// writes its own error messages in the console code page (Shift-JIS on Japanese Windows), so a line
/// that is not valid UTF-8 is decoded with that code page instead of turning into replacement characters.
/// </summary>
public static class ProcessText
{
    private static readonly UTF8Encoding Strict = new(false, true);
    private static readonly Lazy<Encoding> Console = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
        catch (Exception) { return Encoding.Latin1; }
    });

    public static string Decode(ReadOnlySpan<byte> line)
    {
        if (line.Length > 0 && line[^1] == '\r') line = line[..^1];
        if (line.Length >= 3 && line[0] == 0xEF && line[1] == 0xBB && line[2] == 0xBF) line = line[3..];
        try { return Strict.GetString(line); }
        catch (DecoderFallbackException) { return Console.Value.GetString(line); }
    }

    public static async Task ReadLinesAsync(Stream stream, Action<string> line, CancellationToken token)
    {
        var buffer = new byte[8192]; var pending = new List<byte>(256); int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
            for (int i = 0; i < read; i++)
            {
                // Progress bars rewrite one line with carriage returns; treat them as line ends too.
                if (buffer[i] is (byte)'\n' or (byte)'\r') { if (pending.Count > 0) line(Decode(pending.ToArray())); pending.Clear(); }
                else pending.Add(buffer[i]);
            }
        if (pending.Count > 0) line(Decode(pending.ToArray()));
    }
}
