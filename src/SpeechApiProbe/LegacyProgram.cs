#if LEGACY_SPEECH_API
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.AI.Speech;
using Windows.ApplicationModel;

var report = new Dictionary<string, object?> { ["sdk"] = "2.4.1-experimental", ["os"] = RuntimeInformation.OSDescription, ["startedUtc"] = DateTimeOffset.UtcNow, ["cloudFallback"] = false, ["audioCaptured"] = false };
int i = Array.IndexOf(args, "--output");
string output = Path.GetFullPath(i >= 0 && i + 1 < args.Length ? args[i + 1] : "speech-api-legacy.json");
void Save() => File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
try
{
    int nativeDirIndex = Array.IndexOf(args, "--model-library-directory");
    if (nativeDirIndex >= 0 && nativeDirIndex + 1 < args.Length)
    {
        string nativeDir = Path.GetFullPath(args[nativeDirIndex + 1]);
        NativeLibrary.Load(Path.Combine(nativeDir, "asrmodelapi.dll"));
        NativeLibrary.Load(Path.Combine(nativeDir, "cacheapi.dll"));
        NativeLibrary.Load(Path.Combine(nativeDir, "asrapi.dll"));
        NativeLibrary.Load(Path.Combine(nativeDir, "vadapi.dll"));
        report["modelLibrariesPreloaded"] = nativeDir;
    }
    if (args.Contains("--preload-runtime"))
    {
        // Diagnostic only: test whether the module error is DLL search related.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "onnxruntime.dll"));
        report["runtimePreloaded"] = true;
    }
    report["stage"] = "package-identity"; Save();
    report["package"] = Package.Current.Id.FullName;
    if (args.Contains("--initialize-winml"))
    {
        report["stage"] = "initialize-winml"; Save();
        var catalog = Microsoft.Windows.AI.MachineLearning.ExecutionProviderCatalog.GetDefault();
        report["executionProviders"] = catalog.FindAllProviders().Select(p => p.Name).ToArray();
        await catalog.RegisterCertifiedAsync();
        report["winmlInitialized"] = true;
    }
    if (args.Contains("--diagnostic-delay")) await Task.Delay(10000);
    report["stage"] = "ready-check"; Save();
    report["readyState"] = SpeechRecognitionModel.GetReadyState().ToString(); Save();
    if (args.Contains("--prepare"))
    {
        report["stage"] = "prepare-model"; Save();
        var result = await SpeechRecognitionModel.EnsureReadyAsync();
        report["preparationStatus"] = result.Status.ToString(); report["preparationError"] = result.ExtendedError?.ToString();
        report["readyState"] = SpeechRecognitionModel.GetReadyState().ToString();
    }
    if (!args.Contains("--status-only") && SpeechRecognitionModel.GetReadyState() == Microsoft.Windows.AI.AIFeatureReadyState.Ready)
    {
        report["stage"] = "create-model"; Save();
        var clock = Stopwatch.StartNew();
        var created = await SpeechRecognitionModel.TryCreateAsync();
        report["creationError"] = created.ExtendedError?.ToString();
        report["creationHResult"] = created.ExtendedError is { } createError ? $"0x{createError.HResult:X8}" : null;
        using var model = created.SpeechModel ?? throw new InvalidOperationException("Speech model creation failed", created.ExtendedError);
        report["modelCreated"] = true; report["modelLoadMs"] = clock.Elapsed.TotalMilliseconds; Save();
        int waveIndex = Array.IndexOf(args, "--wave");
        if (waveIndex >= 0 && waveIndex + 1 < args.Length)
        {
            report["stage"] = "recognize-file"; Save();
            using var recognition = new BatchRecognition(model); clock.Restart();
            report["transcript"] = await recognition.RecognizeFromFile(Path.GetFullPath(args[waveIndex + 1]));
            report["recognitionMs"] = clock.Elapsed.TotalMilliseconds;
        }
    }
}
catch (Exception ex) { report["error"] = ex.Message; report["hresult"] = $"0x{ex.HResult:X8}"; report["innerError"] = ex.InnerException?.ToString(); }
report["finishedUtc"] = DateTimeOffset.UtcNow; Save();
#endif
