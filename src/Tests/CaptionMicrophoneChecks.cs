using Locaptio.Bridge;
using Locaptio.Core;
using Locaptio.Core.Audio;

internal static class CaptionMicrophoneChecks
{
    private sealed class Ui(bool? enabled, bool apply = true) : ICaptionMicrophoneUi
    {
        public bool? Enabled { get; private set; } = enabled;
        public int Toggles, Closed;
        public void Open() { }
        public void Toggle() { Toggles++; if (apply) Enabled = !Enabled; }
        public bool WaitFor(bool value) => Enabled == value;
        public void Close() => Closed++;
    }
    public static void Run()
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var ui = new Ui(false);
        CaptionMicrophonePolicy.Synchronize(ui, true); CaptionMicrophonePolicy.Synchronize(ui, true);
        Check(ui.Enabled == true && ui.Toggles == 1, "Mixed input toggled a configured mic back off");
        CaptionMicrophonePolicy.Synchronize(ui, false); CaptionMicrophonePolicy.Synchronize(ui, false);
        Check(ui.Enabled == false && ui.Toggles == 2 && ui.Closed == 4, "PC-only did not disable mic idempotently");
        var unknown = new Ui(null);
        try { CaptionMicrophonePolicy.Synchronize(unknown, true); throw new Exception("Unknown state accepted"); } catch (InvalidOperationException) { }
        Check(unknown.Toggles == 0 && unknown.Closed == 1, "Unknown mic state changed blindly");
        var failed = new Ui(false, false);
        try { CaptionMicrophonePolicy.Synchronize(failed, true); throw new Exception("Unverified mic change accepted"); } catch (InvalidOperationException) { }
        Check(failed.Closed == 1, "Failed mic change left menus open");
        Check(WindowsCaptionInputPolicy.Choices().Count == 2 && WindowsCaptionInputPolicy.Choices().All(c => WindowsCaptionInputPolicy.Supported(c.Selection)), "Windows offered unsupported inputs");
        Check(WindowsCaptionInputPolicy.Normalize(new("microphone", "old-device")).Kind == "mixed" && WindowsCaptionInputPolicy.Normalize(new("application", ProcessId: 123)).Kind == "system", "Old selections did not migrate");
        Console.WriteLine("PASS: PC/mixed Windows routes; legacy input normalization; mic ON/OFF state verification, idempotence, unknown-state safety, failure cleanup");
    }
}
