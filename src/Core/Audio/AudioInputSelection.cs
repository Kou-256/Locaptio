using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace Locaptio.Core.Audio;
public sealed record AudioInputSelection(string Kind = "system", string DeviceId = "", int ProcessId = 0, long ProcessStarted = 0);
public sealed record AudioInputChoice(string Label, AudioInputSelection Selection)
{
    public override string ToString() => Label;
}
public static class AudioInputCatalog
{
    public static List<AudioInputChoice> List()
    {
        List<AudioInputChoice> result = [new("PC全体の音声（既定の出力）", new()), new("マイク（既定の入力）", new("microphone")), new("PC内音声＋マイク（既定の機器・同時入力）", new("mixed"))];
        using var devices = new MMDeviceEnumerator();
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        foreach (var device in devices.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device) result.Add(new((flow == DataFlow.Render ? "出力: " : "マイク: ") + device.FriendlyName, new(flow == DataFlow.Render ? "system" : "microphone", device.ID)));
        }
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            try
            {
                if (process.Id == Environment.ProcessId || string.IsNullOrWhiteSpace(process.MainWindowTitle)) continue;
                result.Add(new($"アプリ: {process.ProcessName} · {process.MainWindowTitle}", new("application", ProcessId: process.Id, ProcessStarted: process.StartTime.ToUniversalTime().Ticks)));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        return result;
    }
}
