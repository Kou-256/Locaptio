using System.IO;
using System.Text.Json;

namespace Locaptio.App;
public sealed class Settings
{
    public double FontSize { get; set; } = 22;
    public double BackgroundOpacity { get; set; } = .9;
    public double TextOpacity { get; set; } = 1;
    public string MenuBarTheme { get; set; } = "matching";
    public string CaptionLayout { get; set; } = "english-top";
    public double CaptionEnglishSize { get; set; } = 28;
    public double CaptionJapaneseSize { get; set; } = 32;
    public double CaptionOpacity { get; set; } = .8;
    public double CaptionWrapWidth { get; set; } = 960;
    public double CaptionTextOpacity { get; set; } = 1;
    /// <summary>Colors while translating: the original (spoken) text and its translation. "white-cyan" (default), "yellow-white", "white-yellow", "white", "cyan-white" or "custom".</summary>
    public string CaptionPalette { get; set; } = "white-cyan";
    /// <summary>Text color when only transcribing (no translation): "white", "cyan" or "yellow" ("custom" palette uses CaptionTextColor).</summary>
    public string CaptionTranscriptColor { get; set; } = "white";
    public string CaptionBackground { get; set; } = "live";
    public string CaptionAlignment { get; set; } = "center";
    public bool CaptionBold { get; set; } = true;
    public int CaptionRows { get; set; } = 2;
    public int CaptionMaxLines { get; set; } = 4;
    public bool CaptionSentenceMode { get; set; }
    public double CaptionLineGap { get; set; } = 2;
    public double CaptionLineHeight { get; set; } = 1.15;
    public int AppearanceVersion { get; set; }
    public bool CaptionCorrectedSameColor { get; set; } = true;
    public string CaptionTextColor { get; set; } = "#FFFFFF";
    public string CaptionSurfaceColor { get; set; } = "#363636";
    public string CaptionBorderColor { get; set; } = "#858585";
    public string SpeechEngine { get; set; } = "windows-live";
    public bool AutoRestartWindowsCaptions { get; set; } = true;
    public int WindowsCaptionRestartSeconds { get; set; } = 180;
    public bool AutoSwitchWindowsCaptionLanguage { get; set; } = true;
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
    public bool ExportSavedAt { get; set; } = true;
    public bool ExportDuration { get; set; } = true;
    public bool ExportLineCount { get; set; } = true;
    public string TextFileNamePattern { get; set; } = "{yyyy}{MM}{dd}_";
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public bool AutoDownloadUpdates { get; set; } = false; // Retained for old settings; downloads are always explicit.
    public string RecordingSaveDirectory { get; set; } = "";
    public string RuntimeDirectory { get; set; } = "";
    public bool PreferNpu { get; set; } = true;
    public string InferenceDevice { get; set; } = "gpu";
    public string AudioInputKind { get; set; } = "system";
    public string AudioDeviceId { get; set; } = "";
    public int AudioProcessId { get; set; }
    public long AudioProcessStarted { get; set; }
    public string PlaybackHighlightMode { get; set; } = "word";
    public string RecordingFormat { get; set; } = "wav";
    /// <summary>Text on screen when a new recording starts: "ask", "keep" (continue after it) or "clear".</summary>
    public string ExistingTextOnRecording { get; set; } = "ask";
    /// <summary>Double-click a sentence to edit it in place; Delete removes selected characters.</summary>
    public bool DirectTextEditing { get; set; } = true;
    /// <summary>Tint characters changed by AI correction and show an "AI" badge with the text from before it.</summary>
    public bool ShowAiChanges { get; set; } = true;
    /// <summary>While transcribing, summarize each few minutes of speech with the local AI.</summary>
    public bool LiveSummary { get; set; }
    /// <summary>Check recorded text against the audio with Whisper: "during" the recording, "after" it stops, or "off".</summary>
    public string ContentCheck { get; set; } = "during";
    public bool FastTranscription { get; set; } = true;
    public bool RealtimeCaptions { get; set; } = true;
    public bool RealtimePreviewOnGpu { get; set; }
    /// <summary>Correct each utterance with the local AI. Off by default (live summary is the lighter way to use the AI).</summary>
    public bool CorrectTranscriptWithAi { get; set; }
    public bool SpeakerDiarization { get; set; }
    public double SpeakerThreshold { get; set; } = .55;
    public string[] SpeakerColors { get; set; } = ["#7DD3FC", "#FCA5A5", "#86EFAC", "#FDE68A", "#C4B5FD", "#FDBA74", "#F9A8D4", "#5EEAD4"];
    /// <summary>Spoken language and optional translation target, e.g. "ja" or "en>ja" (CaptionModes). Older values still read.</summary>
    public string CaptionMode { get; set; } = "ja";
    /// <summary>Main toolbar items in display order (control names); missing names keep their default place.</summary>
    public string[] ToolbarOrder { get; set; } = [];
    /// <summary>Main toolbar items the user has hidden (control names).</summary>
    public string[] ToolbarHidden { get; set; } = [];
    /// <summary>Live summary: summarize after this many minutes of speech…</summary>
    public double LiveSummaryMinutes { get; set; } = 2.5;
    /// <summary>…or once this many characters have been said.</summary>
    public int LiveSummaryChars { get; set; } = 1500;
    /// <summary>Lines kept per block: "short", "normal" or "detailed".</summary>
    public string LiveSummaryDetail { get; set; } = "normal";
    /// <summary>Keep the main window in front of other windows.</summary>
    public bool KeepOnTop { get; set; } = true;
    /// <summary>Width of the summary pane beside the transcript (pixels at 100%).</summary>
    public double SummaryPaneWidth { get; set; } = 340;
    /// <summary>System-wide shortcut that toggles <see cref="KeepOnTop"/>: "ctrl-alt-t", "ctrl-shift-t", "ctrl-alt-f12" or "none".</summary>
    public string KeepOnTopHotkey { get; set; } = "ctrl-alt-t";
    /// <summary>Language English speech is translated into (M2M100 code).</summary>
    public string EnglishTranslationTarget { get; set; } = "ja";
    /// <summary>Language Japanese speech is translated into in "ja-translate" mode.</summary>
    public string JapaneseTranslationTarget { get; set; } = "en";
    public string SpeechModel { get; set; } = "large-v3-turbo";
    public string SpeechPrompt { get; set; } = "";
    public string TranslationGlossary { get; set; } = "";
    // One entry per line: "表記 | 誤表記, 誤表記 | 訳語". Parsed by Locaptio.Core.TermDictionary.
    public string TermDictionary { get; set; } = "";
    public string BookmarkLabels { get; set; } = "重要、後で確認、質問、TODO";
    public Dictionary<string, string> BookmarkColors { get; set; } = new() { ["重要"] = "#F5B942", ["後で確認"] = "#60A5FA", ["質問"] = "#C084FC", ["TODO"] = "#34D399" };
    public bool AutosaveSession { get; set; } = true;
    public bool SummarizeAfterRecording { get; set; }
    // Screen/window video recording.
    public int VideoFramerate { get; set; } = 60;
    /// <summary>"best", "high", "standard" or "small".</summary>
    public string VideoQuality { get; set; } = "best";
    /// <summary>Seconds counted down before recording starts; 0 = none.</summary>
    public int VideoCountdownSeconds { get; set; } = 3;
    /// <summary>The display or window recorded last ("display:" + device, "window:" + title).</summary>
    public string VideoLastTarget { get; set; } = "";
    public bool VideoCursor { get; set; } = true;
    public bool VideoClicks { get; set; } = true;
    public bool VideoSystemAudio { get; set; } = true;
    public bool VideoMicrophone { get; set; } = true;
    public bool VideoCountdown { get; set; } = true;
    public bool VideoTranscribe { get; set; } = true;
    public bool VideoHideLocaptio { get; set; } = true;
    public string SubtitleBurnContent { get; set; } = "source";
    public string SubtitleBurnSize { get; set; } = "medium";
    public string RetranscribeModel { get; set; } = "";
    public bool UseLocalLlm { get; set; } = true;
    /// <summary>Local LLM from the list in Settings ("qwen3-4b", …), or "custom" for LlmModelPath.</summary>
    public string LlmModel { get; set; } = "qwen3-4b";
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
            settings.AutoDownloadUpdates = false;
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
            if (settings.AppearanceVersion < 4)
            {
                if (settings.BackgroundOpacity == .3) settings.BackgroundOpacity = .9;
                if (settings.CaptionOpacity == .3) settings.CaptionOpacity = .8;
                if (settings.MenuBarTheme == "white") settings.MenuBarTheme = "matching";
                if (settings.CaptionPalette == "white") settings.CaptionPalette = "cyan-white";
                settings.AppearanceVersion = 4;
            }
            if (settings.AppearanceVersion < 5)
            {
                // 1.0.50–1.0.51 recorded video without the microphone by default; it is now on by default.
                settings.VideoMicrophone = true;
                settings.AppearanceVersion = 5;
            }
            if (settings.AppearanceVersion < 6)
            {
                // A GGUF file chosen before the model list existed stays in use.
                if (!string.IsNullOrWhiteSpace(settings.LlmModelPath)) settings.LlmModel = "custom";
                settings.AppearanceVersion = 6;
            }
            if (settings.AppearanceVersion < 7)
            {
                // 60 fps and the best quality are the defaults now; settings still at the old defaults move to them.
                if (settings.VideoFramerate == 30) settings.VideoFramerate = 60;
                if (settings.VideoQuality == "standard") settings.VideoQuality = "best";
                settings.VideoCountdownSeconds = settings.VideoCountdown ? 3 : 0;
                settings.AppearanceVersion = 7;
            }
            if (settings.AppearanceVersion < 8)
            {
                // AI correction is off by default from 1.0.52; it can be turned on again from the toolbar.
                settings.CorrectTranscriptWithAi = false;
                settings.AppearanceVersion = 8;
            }
            if (settings.AppearanceVersion < 9)
            {
                // From 1.0.60 the default is original white / translation cyan; move users still on the old default.
                if (settings.CaptionPalette == "cyan-white") settings.CaptionPalette = "white-cyan";
                settings.AppearanceVersion = 9;
            }
            return settings;
        }
        catch { return new() { AppearanceVersion = 9 }; }
    }
    public void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); } catch { /* Read-only profile: keep settings in RAM. */ }
    }
}
