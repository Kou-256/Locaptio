#if !LEGACY_SPEECH_API
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Speech;
using Windows.ApplicationModel;

// No cloud SDK, API key, microphone or loopback capture. Only an explicitly
// supplied local WAV may be transcribed. A model request requires --prepare.
var report = new Dictionary<string, object?> { ["startedUtc"] = DateTimeOffset.UtcNow, ["os"] = RuntimeInformation.OSDescription, ["sdk"] = "2.5.4-experimental", ["audioCaptured"] = false, ["cloudFallback"] = false };
string Option(string name, string fallback = "") { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
string output = Path.GetFullPath(Option("--output", "speech-api-probe.json"));
void Save() { Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); }
try
{
    try { report["package"] = Package.Current.Id.FullName; } catch (Exception ex) { report["package"] = null; report["packageError"] = $"0x{ex.HResult:X8}"; }
    Save();
    var clock = Stopwatch.StartNew();
    report["computeDeviceOptions"] = Enum.GetNames<AIComputeDevice>();
    report["defaultLocale"] = SpeechRecognitionModelFactoryOptions.DefaultLocale;
    string locale = Option("--locale"); string device = Option("--device");
    var policies = device.Length > 0 ? new List<AIComputeDevice> { Enum.Parse<AIComputeDevice>(device, true) } : new List<AIComputeDevice>();
    report["requestedLocale"] = locale.Length > 0 ? locale : "default";
    report["requestedDevicePolicies"] = policies.Select(p => p.ToString()).ToArray();
    var factory = locale.Length > 0 || policies.Count > 0 ? new SpeechRecognitionModelFactory(new SpeechRecognitionModelFactoryOptions(locale.Length > 0 ? locale : SpeechRecognitionModelFactoryOptions.DefaultLocale, policies)) : SpeechRecognitionModelFactory.Default;
    report["stage"] = "ready-check"; Save();
    var state = factory.GetReadyState(); report["readyState"] = state.ToString(); Save();
    if (state != AIFeatureReadyState.Ready && args.Contains("--prepare") && state != AIFeatureReadyState.NotSupportedOnCurrentSystem)
    {
        report["modelPreparationRequested"] = true; report["stage"] = "prepare-model"; Save();
        var ready = await factory.EnsureReadyAsync();
        report["preparationStatus"] = ready.Status.ToString(); report["preparationError"] = ready.ExtendedError?.ToString();
        report["preparationHResult"] = ready.ExtendedError is { } preparationError ? $"0x{preparationError.HResult:X8}" : null;
        state = factory.GetReadyState(); report["readyState"] = state.ToString(); Save();
    }
    if (!args.Contains("--status-only") && (state == AIFeatureReadyState.Ready || args.Contains("--force-create")))
    {
        report["stage"] = "create-model"; Save();
        using var model = await factory.CreateAsync();
        report["modelCreated"] = true; report["modelLoadMs"] = clock.Elapsed.TotalMilliseconds; Save();
        string wave = Option("--wave");
        if (wave.Length > 0)
        {
            using var recognizer = new BatchRecognition(model); clock.Restart();
            report["stage"] = "recognize-file"; Save();
            report["transcript"] = await recognizer.RecognizeFromFile(Path.GetFullPath(wave));
            report["recognitionMs"] = clock.Elapsed.TotalMilliseconds;
        }
    }
    report["finishedUtc"] = DateTimeOffset.UtcNow; Save();
    Console.WriteLine(JsonSerializer.Serialize(report));
    return state == AIFeatureReadyState.Ready ? 0 : 2;
}
catch (Exception ex)
{
    report["error"] = ex.Message; report["hresult"] = $"0x{ex.HResult:X8}"; report["errorType"] = ex.GetType().FullName;
    report["finishedUtc"] = DateTimeOffset.UtcNow;
    Save(); Console.WriteLine(JsonSerializer.Serialize(report)); return 1;
}
#endif
