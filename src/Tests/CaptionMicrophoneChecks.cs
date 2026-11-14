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
        var boundary = new CaptionBaselineBoundary();
        var tracker = new WindowsCaptionTracker(true);
        Check(boundary.Begin("process-1:start-1"), "Initial connection did not establish a boundary");
        tracker.ResetBaseline("過去の文章。");
        Check(!boundary.Begin("process-1:start-1"), "Menu/UIA replacement discarded early speech");
        var early = tracker.Observe("過去の文章。録音開始直後の発話。", System.Diagnostics.Stopwatch.GetTimestamp()).Single();
        Check(early.English == "録音開始直後の発話。", "Setup-time speech was lost or old text leaked");
        tracker.ResetBaseline("過去の文章。録音開始直後の発話。");
        var newRecording = tracker.Observe("過去の文章。録音開始直後の発話。新規録音の最初。", System.Diagnostics.Stopwatch.GetTimestamp()).Single();
        Check(newRecording.English == "新規録音の最初。" && newRecording.Id > early.Id, "Recording reset lost the first new phrase or reused an ID");
        Check(boundary.Begin("process-1:start-2"), "Native process restart retained an obsolete boundary");
        Console.WriteLine("PASS: PC/mixed Windows routes; legacy input normalization; mic ON/OFF state verification, idempotence, unknown-state safety, failure cleanup");
    }
}
