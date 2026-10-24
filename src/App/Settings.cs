using System.IO;
using System.Text.Json;

namespace Locaptio.App;
public sealed class Settings
{
    public double FontSize { get; set; } = 22;
    public double BackgroundOpacity { get; set; } = .3;
    public double TextOpacity { get; set; } = 1;
    public string MenuBarTheme { get; set; } = "white";
    public string CaptionLayout { get; set; } = "english-top";
    public double CaptionEnglishSize { get; set; } = 28;
    public double CaptionJapaneseSize { get; set; } = 32;
    public double CaptionOpacity { get; set; } = .3;
    public double CaptionTextOpacity { get; set; } = 1;
    public string CaptionPalette { get; set; } = "white";
    public string CaptionBackground { get; set; } = "live";
    public string CaptionAlignment { get; set; } = "center";
    public bool CaptionBold { get; set; } = true;
    public int CaptionRows { get; set; } = 2;
    public double CaptionLineGap { get; set; } = 2;
    public double CaptionLineHeight { get; set; } = 1.15;
    public int AppearanceVersion { get; set; }
    public bool CaptionCorrectedSameColor { get; set; } = true;
    public string CaptionTextColor { get; set; } = "#FFFFFF";
    public string CaptionSurfaceColor { get; set; } = "#363636";
    public string CaptionBorderColor { get; set; } = "#858585";
    public string SpeechEngine { get; set; } = "whisper";
    public bool AutoRestartWindowsCaptions { get; set; } = true;
    public string RealtimePreviewModel { get; set; } = "base";
    public string CaptionCorrectedColor { get; set; } = "#70E1C4";
    public double CaptionHideSeconds { get; set; } = 5;
    public bool PreloadCudaModels { get; set; } = true;
    public int CaptionWindowWidth { get; set; }
    public int CaptionWindowHeight { get; set; }
    public int CaptionWindowLeft { get; set; } = -1;
    public int CaptionWindowTop { get; set; } = -1;
    public int Rows { get; set; } = 6;
    public bool English { get; set; } = true;
    public bool Japanese { get; set; } = true;
    public bool CpuOnly { get; set; }
    public bool Diagnostics { get; set; }
    public string TextSaveDirectory { get; set; } = "";
    public string RecordingSaveDirectory { get; set; } = "";
    public string RuntimeDirectory { get; set; } = "";
    public bool PreferNpu { get; set; } = true;
    public string InferenceDevice { get; set; } = "gpu";
    public string AudioInputKind { get; set; } = "system";
    public string AudioDeviceId { get; set; } = "";
    public int AudioProcessId { get; set; }
    public long AudioProcessStarted { get; set; }
    public string RecordingFormat { get; set; } = "wav";
    public bool FastTranscription { get; set; } = true;
    public bool RealtimeCaptions { get; set; } = true;
    public bool RealtimePreviewOnGpu { get; set; }
    public bool CorrectTranscriptWithAi { get; set; }
    public string CaptionMode { get; set; } = "translate";
    public string SpeechModel { get; set; } = "large-v3-turbo";
    public string SpeechPrompt { get; set; } = "";
    public string TranslationGlossary { get; set; } = "";
    public bool UseLocalLlm { get; set; } = true;
    public string LlmModelPath { get; set; } = "";
    public int WinUiWidth { get; set; }
    public int WinUiHeight { get; set; }
    public int WinUiLeft { get; set; } = 80;
    public int WinUiTop { get; set; } = 80;
    public double Left { get; set; } = 80;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 1060;
    public double Height { get; set; } = 360;
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Locaptio", "settings.json");
    private static string LegacyFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalCaptionTranslate", "settings.json");
    public static Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(File.Exists(FilePath) ? FilePath : LegacyFilePath)) ?? new();
            if (settings.AppearanceVersion < 1)
            {
                if (settings.CaptionPalette == "white-yellow") settings.CaptionPalette = "white";
                if (settings.CaptionBackground == "dark") settings.CaptionBackground = "live";
                if (settings.CaptionOpacity == .9) settings.CaptionOpacity = 1;
                if (settings.BackgroundOpacity == .88) settings.BackgroundOpacity = 1;
                settings.AppearanceVersion = 1;
            }
            if (settings.AppearanceVersion < 2)
            {
                if (settings.CaptionSurfaceColor == "#333333") settings.CaptionSurfaceColor = "#363636";
                settings.AppearanceVersion = 2;
            }
            if (settings.AppearanceVersion < 3)
            {
                // Replace the old fully opaque defaults; preserve customized backgrounds.
                if (settings.BackgroundOpacity == 1) settings.BackgroundOpacity = .3;
                if (settings.CaptionOpacity == 1) settings.CaptionOpacity = .3;
                settings.AppearanceVersion = 3;
            }
            return settings;
        }
        catch { return new() { AppearanceVersion = 3 }; }
    }
    public void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); } catch { /* Read-only profile: keep settings in RAM. */ }
    }
}
