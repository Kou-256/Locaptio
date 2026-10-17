using System.Diagnostics;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Media;
using System.Text;
using System.Runtime.InteropServices;
using Locaptio.App;
using Locaptio.Core;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.Graphics;

namespace Locaptio.WinUI;
public sealed partial class MainWindow : Window
{
    private readonly Settings settings = Settings.Load();
    private readonly TranscriptBuffer transcript = new();
    private readonly SortedDictionary<long, CaptionUpdate> captions = new();
    // History contains data only; ListView realizes just the viewport's rows.
    private readonly ObservableCollection<CaptionUpdate> history = new();
    private readonly List<long> historyIds = new();
    private ScrollViewer? historyScroll;
    private bool followLatest = true;
    private int followPasses;
        private readonly DispatcherTimer followTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? captionStop, recordingStop;
    private Task? captionTask, recordingTask, saveTask;
    private Task? llmReady;
    private PreparedInference? prepared;
    private string preparedKey = "";
    private LocalBackend? speech, translation;
    private LocalBackend? accurateSpeech;
    private LocalLlmBackend? llm;
    private readonly string originalMode;
    private readonly bool originalLlm;
    private readonly bool originalCorrection;
    private readonly string originalDevice;
    private readonly bool originalCpu;
    private Pipeline? pipeline;
    private string root = "", recordingPath = "";
    private string activeSpeechModel = "";
    private long clearedAt, recordingStarted;
    private bool closeReady, closing, saving, dialogOpen;
    private bool initialized, modeChanging;
    private bool inputChanging, refreshingAudio, updatingRecordToggle;
    private bool correctionChecked, windowsBridgeConnected;
    private readonly List<(CaptionUpdate Caption, long At)> translationVerificationUpdates = [];
    private readonly List<double> verifiedRenderLags = [];
    private double sttMs, translationMs, totalMs;
    private readonly nint hwnd;
    private Window? settingsWindow;
    private CaptionWindow? captionWindow;
    private Window? modelHelpWindow;
    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLongW(nint window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
    public MainWindow()
    {
        originalMode = settings.CaptionMode; originalLlm = settings.UseLocalLlm;
        originalCorrection = settings.CorrectTranscriptWithAi;
        originalDevice = settings.InferenceDevice; originalCpu = settings.CpuOnly;
        if (Environment.GetCommandLineArgs().Contains("--verify-japanese")) settings.CaptionMode = "japanese";
        if (Environment.GetCommandLineArgs().Contains("--verify-llm")) { settings.UseLocalLlm = true; settings.CaptionMode = "translate"; }
        if (Environment.GetCommandLineArgs().Contains("--verify-correction")) settings.CorrectTranscriptWithAi = true;
        if (Environment.GetCommandLineArgs().Contains("--verify-npu")) { settings.InferenceDevice = "npu"; settings.CpuOnly = false; }
        if (Environment.GetCommandLineArgs().Contains("--verify-cuda")) { settings.InferenceDevice = "cuda"; settings.CpuOnly = false; settings.CaptionMode = "translate"; settings.UseLocalLlm = false; }
        InitializeComponent(); CaptionList.ItemsSource = history; followTimer.Tick += FollowHistory; hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Locaptio.ico"));
        ExtendsContentIntoTitleBar = false; SystemBackdrop = new TransparentWindowBackdrop();
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(true, true); presenter.IsAlwaysOnTop = true; presenter.IsMaximizable = true; presenter.IsMinimizable = true;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        double scale = GetDpiForWindow(hwnd) / 96.0;
        var size = new SizeInt32(Math.Clamp(settings.WinUiWidth > 0 ? settings.WinUiWidth : (int)(settings.Width * scale), Math.Min((int)(760 * scale), area.Width), area.Width), Math.Clamp(settings.WinUiHeight > 0 ? settings.WinUiHeight : (int)(settings.Height * scale), Math.Min((int)(360 * scale), area.Height), area.Height));
        AppWindow.MoveAndResize(new RectInt32(Math.Clamp(settings.WinUiLeft, area.X, area.X + area.Width - size.Width), Math.Clamp(settings.WinUiTop, area.Y, area.Y + area.Height - size.Height), size.Width, size.Height));
        AppWindow.Changed += (_, args) =>
        {
            if (!closing && (args.DidPositionChange || args.DidSizeChange) && presenter.State == OverlappedPresenterState.Restored) RememberBounds();
        };
        AppWindow.Closing += async (_, args) =>
        {
            if (closeReady) return;
            args.Cancel = true;
            await ShutdownAsync();
        };
        ModeSelector.OwnerWindow = this; AudioSelector.OwnerWindow = this; ModeSelector.ItemsSource = new[] { "英語 → 日本語 翻訳", "日本語の文字起こし" };
        root = FindRoot(); ModeSelector.SelectedIndex = settings.CaptionMode == "japanese" ? 1 : 0; RefreshAudioInputs(); ApplySettings(); initialized = true; timer.Tick += (_, _) => UpdateDiagnostics(); timer.Start();
        Root.Loaded += async (_, _) =>
        {
            ApplyMainTextOpacity(Root);
            if (Environment.GetCommandLineArgs().Contains("--verify-winui")) await VerifyAsync();
            else if (!Environment.GetCommandLineArgs().Any(a => a.StartsWith("--verify")) && settings.PreloadCudaModels && settings.InferenceDevice == "cuda") await PreloadAsync();
        };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-lifecycle")) await VerifyLifecycleAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-caption-idle")) await VerifyCaptionIdleAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-windows-translation")) await VerifyWindowsTranslationAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-windows-live")) await VerifyWindowsLiveAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-caption-style")) await VerifyCaptionStyleAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-opacity")) await VerifyOpacityAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-mixed")) await VerifyMixedInputAsync(); };
        Root.Loaded += async (_, _) => { if (Environment.GetCommandLineArgs().Contains("--verify-history")) await VerifyHistoryAsync(); };
    }
    private async Task ShutdownAsync()
    {
        if (closing) return;
        closing = true; timer.Stop(); followTimer.Stop(); captionWindow?.Close(); modelHelpWindow?.Close(); settingsWindow?.Close();
        AppWindow.Hide();
        await Task.Yield(); // Complete the native Closing callback before destroying the window.
        try
        {
            // Signal both operations immediately; inference cleanup must not delay WAV finalization.
            var shutdown = Stopwatch.StartNew();
            prepared?.Dispose(); prepared = null;
            await Task.WhenAll(StopAsync(), StopRecordingAsync());
            if (saveTask is not null) { try { await saveTask; } catch { } }
            if (((OverlappedPresenter)AppWindow.Presenter).State == OverlappedPresenterState.Restored) RememberBounds();
            if (Environment.GetCommandLineArgs().Contains("--verify-lifecycle")) { settings.CaptionMode = originalMode; settings.UseLocalLlm = originalLlm; }
            settings.Save();
            if (Environment.GetCommandLineArgs().Contains("--verify-lifecycle"))
            {
                using var wave = new NAudio.Wave.WaveFileReader(Path.Combine(root, "artifacts", "winui-shutdown.wav"));
                bool passed = lifecycleMinimized && shutdown.Elapsed.TotalSeconds < 5 && wave.TotalTime.TotalSeconds > .5;
                File.WriteAllText(Path.Combine(root, "artifacts", "winui-lifecycle-verification.txt"), $"PASS={passed}; minimized/restored={lifecycleMinimized}; cleanupMs={shutdown.Elapsed.TotalMilliseconds:F0}; WAV={wave.TotalTime.TotalSeconds:F2}s; startupClose={Environment.GetCommandLineArgs().Contains("--verify-startup-close")}");
                Environment.ExitCode = passed ? 0 : 1;
            }
        }
        finally { closeReady = true; Close(); }
    }
    private string FindRoot()
    {
        if (IsRuntime(settings.RuntimeDirectory)) return settings.RuntimeDirectory;
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (IsRuntime(folder.FullName)) { settings.RuntimeDirectory = folder.FullName; settings.Save(); return folder.FullName; }
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Locaptio", "runtime");
        if (IsRuntime(local)) return local;
        var previous = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalCaptionTranslate", "runtime");
        return IsRuntime(previous) ? previous : "";
    }
    private static bool IsRuntime(string path) => !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "inference", "worker.py"));
    private void Status(string text) => DispatcherQueue.TryEnqueue(() => { if (!closing) { if (text.StartsWith("Windowsライブキャプション接続済み")) windowsBridgeConnected = true; StatusText.Text = text; if (text.StartsWith("AI確認済み") || text.StartsWith("AI文脈補正を反映")) correctionChecked = true; } });
    private async void StartClick(object sender, RoutedEventArgs e) => await StartAsync();
    private async Task StartAsync()
    {
        if (captionStop is not null || closing) return; root = FindRoot();
        if (root.Length == 0 && (!UseWindowsCaptionEngine || settings.CaptionMode != "japanese")) { StatusText.Text = "Settingsでモデル・推論環境のフォルダーを指定してください。"; ((OverlappedPresenter)AppWindow.Presenter).Restore(); Activate(); return; }
        ClearCaptions(); correctionChecked = false; captionStop = new(); StartButton.IsEnabled = false; StopButton.IsEnabled = true;
        StatusText.Text = "ローカルモデルを読み込み中…"; captionTask = RunCaptionsAsync(captionStop.Token); await Task.Yield();
    }
    private async Task RunCaptionsAsync(CancellationToken token)
    {
        try
        {
            bool japanese = settings.CaptionMode == "japanese";
            if (UseWindowsCaptionEngine)
            {
                if (settings.AudioInputKind != "system") throw new InvalidOperationException("Windows連携はWindows側が選んだ音声を使います。音声入力を「PC全体の音声」にしてください。マイクや個別アプリを指定する場合はWhisperに切り替えてください。");
                prepared?.Dispose(); prepared = null; preparedKey = ""; speech = translation = accurateSpeech = null; llm = null;
                windowsBridgeConnected = false;
                using var live = new WindowsCaptionSession(AppContext.BaseDirectory, japanese, root, settings.InferenceDevice, settings.CpuOnly, settings.UseLocalLlm, settings.LlmModelPath, settings.TranslationGlossary);
                live.Caption += update => DispatcherQueue.TryEnqueue(() => UpdateCaption(update)); live.Status += Status;
                sttMs = translationMs = totalMs = 0;
                BackendText.Text = "Windowsライブキャプション連携（Windows側の認識結果）" + (!japanese ? " · FuguMT速報翻訳" + (settings.UseLocalLlm ? " → Qwen文脈翻訳: ON" : " · 文脈LLM翻訳: OFF") : "");
                StatusText.Text = "Windowsの認識言語を本アプリのモードと合わせてください。接続前の履歴は保存しません。";
                await live.RunAsync(token); return;
            }
            bool cpu = settings.CpuOnly || settings.InferenceDevice == "cpu" || Environment.GetCommandLineArgs().Contains("--cpu");
            string model = settings.FastTranscription && !settings.RealtimeCaptions && !japanese ? (File.Exists(Path.Combine(root, "models", "speech-base", "model.bin")) ? "base" : "small") : settings.SpeechModel;
            if (!File.Exists(Path.Combine(root, "models", "speech-" + model, "model.bin")))
            {
                model = new[] { "small", "base", "tiny" }.FirstOrDefault(candidate => File.Exists(Path.Combine(root, "models", "speech-" + candidate, "model.bin"))) ?? "legacy";
                if (japanese && model == "legacy") throw new InvalidOperationException("日本語用モデルがありません。scripts/setup-speech.ps1を実行してください。");
                Status("選択したモデルが未準備のため、準備済みモデルを使用します: " + model);
            }
            var session = PrepareModels(japanese, model, cpu);
            await session.Ready.WaitAsync(token);
            speech = session.LiveSpeech; translation = session.Translation;
            accurateSpeech = session.Preview is not null ? session.Speech : null;
            speech.ResetSpeechContext();
            accurateSpeech?.ResetSpeechContext();
            activeSpeechModel = model;
            llm = session.Llm; llmReady = session.LlmReady;
            ReadyLocalLlm? readyLlm = llm is not null && llmReady is not null ? new(llm, llmReady) : null;
            Status(settings.CorrectTranscriptWithAi ? "文脈AI補正はONです。準備後、確定した発話を補正します。" : "文脈AI補正はOFFです。設定でONにできます。");
            pipeline = new(speech, translation, japanese, !japanese && settings.UseLocalLlm ? readyLlm : null, SelectedAudio(), settings.FastTranscription, settings.CorrectTranscriptWithAi ? readyLlm : null, accurateSpeech is null ? null : new ReadySpeechBackend(accurateSpeech, session.AccuracyReady), translationGlossary: settings.TranslationGlossary); pipeline.Status += Status; pipeline.Caption += update => DispatcherQueue.TryEnqueue(() => UpdateCaption(update));
            StatusText.Text = japanese ? "日本語音声を待っています…" : "英語音声を待っています…"; await Task.Run(() => pipeline.RunAsync(token), token);
        }
        catch (OperationCanceledException) { StatusText.Text = token.IsCancellationRequested ? "停止しました。" : "モデル初期化がタイムアウトしました。"; }
        catch (Exception ex) { StatusText.Text = ex.Message; if (!closing && captionWindow is not null) { ((OverlappedPresenter)AppWindow.Presenter).Restore(); Activate(); } }
        finally
        {
            speech = null; accurateSpeech = null; translation = null; pipeline = null; llm = null; llmReady = null;
            captionWindow?.Update([]);
            captionStop?.Dispose(); captionStop = null; StartButton.IsEnabled = true; StopButton.IsEnabled = recordingStop is not null;
        }
    }
    private PreparedInference PrepareModels(bool japanese, string model, bool cpu)
    {
        string key = System.Text.Json.JsonSerializer.Serialize(new { root, japanese, model, cpu, settings.InferenceDevice, settings.SpeechPrompt, settings.FastTranscription, settings.RealtimeCaptions, settings.RealtimePreviewOnGpu, settings.RealtimePreviewModel, settings.CorrectTranscriptWithAi, settings.UseLocalLlm, settings.LlmModelPath });
        if (prepared is null || preparedKey != key || !prepared.IsUsable)
        {
            prepared?.Dispose();
            prepared = new(root, japanese, model, settings.SpeechPrompt, settings.InferenceDevice, cpu, settings.FastTranscription, settings.CorrectTranscriptWithAi || !japanese && settings.UseLocalLlm, settings.LlmModelPath, Status, realtime: settings.RealtimeCaptions, previewCpu: !settings.RealtimePreviewOnGpu, previewModel: settings.RealtimePreviewModel);
            preparedKey = key;
        }
        return prepared;
    }
    private async Task PreloadAsync()
    {
        if (UseWindowsCaptionEngine) return;
        if (closing || captionStop is not null) return;
        root = FindRoot(); if (root.Length == 0) return;
        bool japanese = settings.CaptionMode == "japanese";
        string model = settings.FastTranscription && !settings.RealtimeCaptions && !japanese ? "base" : settings.SpeechModel;
        if (!File.Exists(Path.Combine(root, "models", "speech-" + model, "model.bin"))) { Status("選択モデルは未準備です。モデルのヘルプを確認してください。"); return; }
        try
        {
            Status("CUDAモデルを先読み中…（音声の取得は開始しません）");
            await PrepareModels(japanese, model, settings.CpuOnly || settings.InferenceDevice == "cpu").Ready;
            if (!closing && captionStop is null) Status("モデル準備完了。Start／字幕ですぐに聞き取りを開始できます。");
        }
        catch (Exception ex) { if (!closing) Status("モデル準備失敗: " + ex.Message); }
    }
    private async void StopClick(object sender, RoutedEventArgs e) => await StopAllAsync();
    private async Task StopAllAsync()
    {
        StopButton.IsEnabled = false;
        await Task.WhenAll(StopAsync(), StopRecordingAsync());
        StopButton.IsEnabled = false;
    }
    private async Task StopAsync()
    {
        StopButton.IsEnabled = false;
        var stop = captionStop; var task = captionTask;
        if (stop is not null) await stop.CancelAsync();
        if (task is not null) await task;
        captionWindow?.Update([]);
        StopButton.IsEnabled = recordingStop is not null;
    }
    private void UpdateCaption(CaptionUpdate update)
    {
        if (update.Started < clearedAt || captionStop?.IsCancellationRequested != false || closing) return;
        if (Environment.GetCommandLineArgs().Contains("--verify-windows-translation")) translationVerificationUpdates.Add((update, Stopwatch.GetTimestamp()));
        transcript.Update(update); SaveButton.IsEnabled = transcript.Count > 0 && !saving;
        if (captions.TryGetValue(update.Id, out var old) && update.Revision < old.Revision) return;
        UpdateHistory(update);
        if (string.IsNullOrWhiteSpace(update.English)) captions.Remove(update.Id); else captions[update.Id] = update;
        while (captions.Count > settings.CaptionRows) captions.Remove(captions.Keys.First()); sttMs = update.SttMs;
        if (update.Japanese is not null) translationMs = update.TranslationMs;
        if (!update.SourceCorrected && update.AudioThrough > 0) totalMs = Stopwatch.GetElapsedTime(update.AudioThrough).TotalMilliseconds;
        captionWindow?.Update(captions.Values);
        if (Environment.GetCommandLineArgs().Contains("--verify-realtime") && !update.SourceCorrected && update.AudioThrough > 0 && !string.IsNullOrWhiteSpace(update.English)) verifiedRenderLags.Add(Stopwatch.GetElapsedTime(update.AudioThrough).TotalMilliseconds);
    }
    private static T? Descendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (Descendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private void PauseHistoryFollowing() { followLatest = false; followTimer.Stop(); }
    private void CaptionListLoaded(object sender, RoutedEventArgs e)
    {
        if (historyScroll is not null) return;
        historyScroll = Descendant<ScrollViewer>(CaptionList);
        if (historyScroll is null) return;
        // Layout/virtualization may also change offsets. Only user input pauses
        // following; ViewChanged alone cannot distinguish those two causes.
        CaptionList.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) =>
        {
            if (args.GetCurrentPoint(CaptionList).Properties.MouseWheelDelta > 0) PauseHistoryFollowing();
        }), true);
        CaptionList.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, args) =>
        {
            if (args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.Home) PauseHistoryFollowing();
        }), true);
        void ConnectScrollBars(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar bar && bar.Orientation == Orientation.Vertical)
                    bar.Scroll += (_, args) => { if (historyScroll.ScrollableHeight - args.NewValue > 2) PauseHistoryFollowing(); };
                ConnectScrollBars(child);
            }
        }
        ConnectScrollBars(historyScroll);
        historyScroll.ViewChanged += (_, args) =>
        {
            if (historyScroll.ScrollableHeight - historyScroll.VerticalOffset <= 2) followLatest = true;
        };
    }
    private void FollowHistory(object? sender, object e)
    {
        if (!followLatest || historyScroll is null || closing || followPasses <= 0) { followTimer.Stop(); return; }
        // The first pass realizes the last row; subsequent passes use its actual
        // measured height instead of the virtualization panel's estimate.
        if (followPasses == 3 && history.Count > 0) CaptionList.ScrollIntoView(history[^1]);
        else historyScroll.ChangeView(null, historyScroll.ScrollableHeight, null, true);
        if (--followPasses == 0) followTimer.Stop();
    }
    private void UpdateHistory(CaptionUpdate update)
    {
        int index = historyIds.BinarySearch(update.Id);
        if (index >= 0)
        {
            if (history[index].Revision > update.Revision) return;
            if (string.IsNullOrWhiteSpace(update.English)) { history.RemoveAt(index); historyIds.RemoveAt(index); }
            else history[index] = update;
        }
        else if (!string.IsNullOrWhiteSpace(update.English))
        {
            index = ~index; historyIds.Insert(index, update.Id); history.Insert(index, update);
        }
        if (followLatest) { followPasses = 3; followTimer.Start(); }
    }
    private void CaptionContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is CaptionUpdate caption && args.ItemContainer.ContentTemplateRoot is Grid row)
            StyleHistoryRow(row, caption);
    }
    private void StyleHistoryRow(Grid row, CaptionUpdate caption)
    {
        bool japanese = settings.CaptionMode == "japanese";
        row.Margin = new Thickness(0, 0, 0, settings.CaptionLineGap);
        row.ColumnDefinitions[0].Width = japanese || settings.English ? new(1, GridUnitType.Star) : new(0);
        row.ColumnDefinitions[1].Width = !japanese && settings.Japanese ? new(1, GridUnitType.Star) : new(0);
        var en = (TextBlock)row.Children[0]; var ja = (TextBlock)row.Children[1];
        en.Opacity = ja.Opacity = settings.TextOpacity;
        en.FontSize = ja.FontSize = settings.FontSize;
        en.LineHeight = ja.LineHeight = settings.FontSize * settings.CaptionLineHeight;
        en.LineStackingStrategy = ja.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        en.Visibility = japanese || settings.English ? Visibility.Visible : Visibility.Collapsed;
        ja.Visibility = !japanese && settings.Japanese ? Visibility.Visible : Visibility.Collapsed;
        en.Foreground = !settings.CaptionCorrectedSameColor && caption.SourceCorrected ? CaptionWindow.CorrectedBrush(settings) : CaptionWindow.OrdinaryBrush(settings, japanese);
        ja.Foreground = !settings.CaptionCorrectedSameColor && (caption.SourceCorrected || caption.TranslationRefined) ? CaptionWindow.CorrectedBrush(settings) : CaptionWindow.OrdinaryBrush(settings, true);
        ToolTipService.SetToolTip(en, caption.SourceCorrected ? "高精度モデルまたはAIで更新した文字起こし" : null);
        ToolTipService.SetToolTip(ja, caption.TranslationRefined ? "ローカルLLMによる翻訳" : null);
    }
    private Grid[] RealizedCaptionRows => CaptionList.ItemsPanelRoot is Panel panel
        ? panel.Children.OfType<ListViewItem>().Select(c => c.ContentTemplateRoot).OfType<Grid>().ToArray() : [];
    private void RenderCaptions()
    {
        // Appearance changes touch only realized rows, not the full session history.
        foreach (var row in RealizedCaptionRows)
            if (row.DataContext is CaptionUpdate caption) StyleHistoryRow(row, caption);
        Header.Visibility = Visibility.Visible;
        captionWindow?.Update(captions.Values);
    }
    private void ClearCaptions()
    {
        clearedAt = Stopwatch.GetTimestamp(); captions.Clear(); transcript.Clear();
        followTimer.Stop(); history.Clear(); historyIds.Clear(); followLatest = true;
        RenderCaptions(); SaveButton.IsEnabled = false;
    }
    private void CaptionClick(object sender, RoutedEventArgs e)
    {
        if (closing) return;
        if (captionWindow is not null) { captionWindow.Close(); return; }
        captionWindow = new CaptionWindow(settings);
        captionWindow.Closed += (_, _) => { captionWindow = null; if (!closing) { CaptionButton.Content = "字幕"; RenderCaptions(); ((OverlappedPresenter)AppWindow.Presenter).Restore(); Activate(); } };
        CaptionButton.Content = "字幕を閉じる"; captionWindow.Activate(); RenderCaptions(); captionWindow.HideIfIdle();
        ((OverlappedPresenter)AppWindow.Presenter).Minimize();
        if (captionStop is null && !Environment.GetCommandLineArgs().Any(a => a.StartsWith("--verify"))) _ = StartAsync();
    }
    private void ClearClick(object sender, RoutedEventArgs e) => ClearCaptions();
    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (saving || transcript.Count == 0 || dialogOpen) return; dialogOpen = true;
        try
        {
            bool japanese = settings.CaptionMode == "japanese";
            ContentDialogResult result = ContentDialogResult.Secondary;
            if (!japanese)
            {
                var choice = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "保存するテキスト", PrimaryButtonText = "英語と日本語", SecondaryButtonText = "英語のみ", CloseButtonText = "キャンセル", RequestedTheme = ElementTheme.Dark };
                result = await choice.ShowAsync(); if (result == ContentDialogResult.None) return;
            }
            var file = await MakeSavePicker("文字起こしを保存", ".txt", settings.TextSaveDirectory).PickSaveFileAsync(); if (file is null || closing) return;
            string text = transcript.Export(result == ContentDialogResult.Primary); saving = true; SaveButton.IsEnabled = false;
            saveTask = File.WriteAllTextAsync(file.Path, text, new UTF8Encoding(true)); await saveTask; StatusText.Text = "テキストを保存しました: " + file.Path;
        }
        catch (Exception ex) { StatusText.Text = "保存に失敗しました: " + ex.Message; }
        finally { saving = false; dialogOpen = false; SaveButton.IsEnabled = transcript.Count > 0; }
    }
    private FileSavePicker MakeSavePicker(string title, string extension, string directory)
    {
        var picker = new FileSavePicker(AppWindow.Id) { Title = title, SuggestedFileName = $"Locaptio-{DateTime.Now:yyyyMMdd-HHmmss}{extension}" };
        picker.FileTypeChoices.Add(extension == ".txt" ? "テキスト" : extension.TrimStart('.').ToUpperInvariant() + "音声", new List<string> { extension });
        if (Directory.Exists(directory)) picker.SuggestedFolder = directory; return picker;
    }
    private void SetRecordingChecked(bool value)
    {
        updatingRecordToggle = true; RecordToggle.IsChecked = value; updatingRecordToggle = false;
    }
    private async void RecordChanged(object sender, RoutedEventArgs e)
    {
        if (updatingRecordToggle || closing) return;
        if (RecordToggle.IsChecked != true) { await StopRecordingAsync(); return; }
        if (dialogOpen) { SetRecordingChecked(recordingStop is not null); return; }
        dialogOpen = true; RecordToggle.IsEnabled = false;
        try
        {
            var file = await MakeSavePicker("選択した音声の録音先", "." + settings.RecordingFormat, settings.RecordingSaveDirectory).PickSaveFileAsync();
            if (file is not null && !closing) StartRecording(file.Path); else SetRecordingChecked(false);
        }
        catch (Exception ex) { SetRecordingChecked(false); StatusText.Text = "録音先を選択できません: " + ex.Message; }
        finally { dialogOpen = false; RecordToggle.IsEnabled = true; }
    }
    // Windows captions cannot accept this application's mixed PCM stream.
    private bool UseWindowsCaptionEngine => settings.SpeechEngine == "windows-live" && settings.AudioInputKind != "mixed";
    private AudioInputSelection SelectedAudio() => new(settings.AudioInputKind, settings.AudioDeviceId, settings.AudioProcessId, settings.AudioProcessStarted);
    private void RefreshAudioInputs()
    {
        refreshingAudio = true;
        try
        {
            var choices = AudioInputCatalog.List(); var selection = SelectedAudio();
            var selected = choices.FirstOrDefault(c => c.Selection == selection);
            if (selected is null) { selected = new("前回の入力（再選択が必要）", selection); choices.Add(selected); }
            AudioSelector.ItemsSource = choices; AudioSelector.SelectedItem = selected;
        }
        catch (Exception ex) { StatusText.Text = "音声入力を一覧できません: " + ex.Message; }
        finally { refreshingAudio = false; }
    }
    private void AudioMenuOpened(object? sender, EventArgs e) { if (!inputChanging) RefreshAudioInputs(); }
    private async void AudioChanged(object? sender, EventArgs e)
    {
        if (!initialized || refreshingAudio || inputChanging || closing || AudioSelector.SelectedItem is not AudioInputChoice choice || choice.Selection == SelectedAudio()) return;
        inputChanging = true; AudioSelector.IsEnabled = false; ModeSelector.IsEnabled = false; StartButton.IsEnabled = false; RecordToggle.IsEnabled = false;
        bool restart = captionStop is not null, recording = recordingStop is not null;
        string recordingFolder = Path.GetDirectoryName(recordingPath) ?? settings.RecordingSaveDirectory;
        try
        {
            prepared?.Dispose(); prepared = null;
            await Task.WhenAll(StopAsync(), StopRecordingAsync());
            settings.AudioInputKind = choice.Selection.Kind; settings.AudioDeviceId = choice.Selection.DeviceId;
            settings.AudioProcessId = choice.Selection.ProcessId; settings.AudioProcessStarted = choice.Selection.ProcessStarted;
            settings.Save(); ClearCaptions();
            if (recording) StartRecording(Path.Combine(recordingFolder, $"Locaptio-{DateTime.Now:yyyyMMdd-HHmmss-fff}.{settings.RecordingFormat}"));
            if (restart) await StartAsync(); else StatusText.Text = "音声入力を切り替えました。";
        }
        catch (Exception ex) { StatusText.Text = "音声入力を切り替えられません: " + ex.Message; }
        finally { inputChanging = false; AudioSelector.IsEnabled = true; ModeSelector.IsEnabled = true; RecordToggle.IsEnabled = true; StartButton.IsEnabled = captionStop is null; }
    }
    private void StartRecording(string path)
    {
        recordingStop = new(); recordingPath = path; recordingStarted = Stopwatch.GetTimestamp();
        SetRecordingChecked(true); StopButton.IsEnabled = true; RecordingText.Visibility = Visibility.Visible; UpdateDiagnostics(); recordingTask = RecordAsync(path, recordingStop.Token);
    }
    private async Task RecordAsync(string path, CancellationToken token)
    {
        var recorder = new AudioRecording(SelectedAudio()); recorder.Status += Status;
        try { await Task.Run(() => recorder.RunAsync(path, token)); StatusText.Text = "録音を保存しました: " + path; }
        catch (Exception ex) { StatusText.Text = "録音に失敗しました: " + ex.Message; }
        finally { recordingStop?.Dispose(); recordingStop = null; SetRecordingChecked(false); RecordToggle.IsEnabled = true; RecordingText.Visibility = Visibility.Collapsed; StopButton.IsEnabled = captionStop is not null; }
    }
    private async Task StopRecordingAsync()
    {
        RecordToggle.IsEnabled = false;
        var stop = recordingStop; var task = recordingTask;
        if (stop is not null) await stop.CancelAsync();
        if (task is not null) await task;
        RecordToggle.IsEnabled = true;
    }
    private void UpdateDiagnostics()
    {
        if (recordingStop is not null) RecordingText.Text = $"● 録音中 {Stopwatch.GetElapsedTime(recordingStarted):hh\\:mm\\:ss} · {recordingPath}";
        if (speech is not null) BackendText.Text = $"Speech: {speech.Backend} · Model: {(speech.Model == "—" ? activeSpeechModel : speech.Model)}" + (translation is not null ? $" · Translate: {translation.Backend}" : " · 日本語文字起こし");
        if (speech is not null)
        {
            if (accurateSpeech is not null) BackendText.Text += " · 高精度更新: " + accurateSpeech.Model + " / " + accurateSpeech.Backend;
            BackendText.Text += settings.CorrectTranscriptWithAi ? " · AI補正: ON" : " · AI補正: OFF";
            if (llm is not null) BackendText.Text += " · ローカルAI: " + (llmReady?.IsFaulted == true ? "準備失敗" : llm.Backend);
        }
        if (settings.AudioInputKind == "mixed" && speech is not null) BackendText.Text += " · PC＋マイク同時入力（Whisper）";
        if (UseWindowsCaptionEngine) { MetricsText.Text = "認識はWindows側で実行・連携間隔100ms（音声認識自体の遅延は測定不可）"; return; }
        MetricsText.Text = $"Audio: {pipeline?.BufferedAudioMs ?? 0}ms · dropped: {pipeline?.DroppedAudio ?? 0} · STT: {sttMs:F0}ms · Translate: {translationMs:F0}ms · 速報の遅延（最新音声から）: {totalMs:F0}ms\nEP: {translation?.AvailableProviders ?? "—"}";
    }
    private void ApplyMainTextOpacity(DependencyObject parent)
    {
        if (parent is TextBlock text) text.Opacity = settings.TextOpacity;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) ApplyMainTextOpacity(VisualTreeHelper.GetChild(parent, i));
    }
    private void ApplySettings()
    {
        settings.CaptionLineGap = Math.Clamp(settings.CaptionLineGap, 0, 30);
        settings.CaptionLineHeight = Math.Clamp(settings.CaptionLineHeight, 1.05, 1.8);
        if (settings.CaptionMode is not ("translate" or "japanese")) settings.CaptionMode = "translate";
        if (settings.SpeechModel is not ("tiny" or "base" or "small" or "medium" or "large-v3-turbo")) settings.SpeechModel = "large-v3-turbo";
        if (settings.InferenceDevice is not ("gpu" or "cuda" or "npu" or "cpu")) settings.InferenceDevice = "gpu";
        if (settings.CaptionLayout is not ("english-top" or "japanese-top" or "side-by-side")) settings.CaptionLayout = "english-top";
        if (settings.CaptionPalette is not ("white-yellow" or "white" or "cyan-white" or "custom")) settings.CaptionPalette = "white";
        if (settings.CaptionBackground is not ("dark" or "black" or "light" or "live")) settings.CaptionBackground = "live";
        if (settings.CaptionAlignment is not ("left" or "center" or "right")) settings.CaptionAlignment = "center";
        settings.CaptionEnglishSize = Math.Clamp(settings.CaptionEnglishSize, 14, 64); settings.CaptionJapaneseSize = Math.Clamp(settings.CaptionJapaneseSize, 14, 64);
        settings.CaptionHideSeconds = Math.Clamp(settings.CaptionHideSeconds, 1, 20); settings.CaptionOpacity = Math.Clamp(settings.CaptionOpacity, 0, 1); settings.CaptionTextOpacity = Math.Clamp(settings.CaptionTextOpacity, 0, 1); settings.CaptionRows = Math.Clamp(settings.CaptionRows, 1, 10);
        if (settings.RecordingFormat is not ("wav" or "mp3" or "m4a")) settings.RecordingFormat = "wav";
        if (settings.MenuBarTheme is not ("white" or "matching")) settings.MenuBarTheme = "white";
        Root.Background = CaptionWindow.BackgroundBrush(settings, settings.BackgroundOpacity);
        Root.RequestedTheme = settings.CaptionBackground == "light" ? ElementTheme.Light : ElementTheme.Dark;
        if (settingsWindow?.Content is Grid settingsRoot) { settingsRoot.Background = CaptionWindow.SurfaceBrush(settings); settingsRoot.RequestedTheme = Root.RequestedTheme; }
        bool matchingMenu = settings.MenuBarTheme == "matching";
        HeaderMenu.RequestedTheme = matchingMenu ? Root.RequestedTheme : ElementTheme.Light;
        HeaderMenu.Background = matchingMenu ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 247, 250));
        settings.FontSize = Math.Clamp(settings.FontSize, 14, 40); settings.BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0, 1); settings.TextOpacity = Math.Clamp(settings.TextOpacity, 0, 1);
        if (!settings.English && !settings.Japanese) settings.Japanese = true;
        WindowAppearance.SetOpacity(hwnd, 1); ApplyMainTextOpacity(Root); WindowAppearance.SetBorder(hwnd, settings.CaptionBorderColor);
        bool japanese = settings.CaptionMode == "japanese";
        EnglishHeader.Text = japanese ? "日本語" : "English";
        Header.ColumnDefinitions[0].Width = japanese || settings.English ? new(1, GridUnitType.Star) : new(0); Header.ColumnDefinitions[1].Width = !japanese && settings.Japanese ? new(1, GridUnitType.Star) : new(0);
        EnglishHeader.Visibility = japanese || settings.English ? Visibility.Visible : Visibility.Collapsed; JapaneseHeader.Visibility = !japanese && settings.Japanese ? Visibility.Visible : Visibility.Collapsed;
        MetricsText.Visibility = settings.Diagnostics ? Visibility.Visible : Visibility.Collapsed;
        while (captions.Count > settings.CaptionRows) captions.Remove(captions.Keys.First()); RenderCaptions(); settings.Save();
    }
    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (closing) return;
        if (settingsWindow is not null)
        {
            var presenter = (OverlappedPresenter)settingsWindow.AppWindow.Presenter;
            if (presenter.State == OverlappedPresenterState.Minimized) presenter.Restore();
            settingsWindow.Activate(); return;
        }
        try
        {
            var window = new Window { Title = "Locaptio — 設定" };
            settingsWindow = window;
            window.Closed += (_, _) => settingsWindow = null;
            // WinUI overlays its vertical scrollbar on the viewport; reserve a gutter inside the content.
            var content = new StackPanel { Spacing = 12, Padding = new Thickness(0, 0, 32, 0) };
            content.Children.Add(new TextBlock { Text = "メニューバーの色" });
            var menuTheme = new DownwardSelector
            {
                OwnerWindow = window, HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = new[] { "白", "字幕画面と同じ背景色" }, SelectedIndex = settings.MenuBarTheme == "matching" ? 1 : 0
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(menuTheme, "メニューバーの色");
            menuTheme.SelectionChanged += (_, _) => { settings.MenuBarTheme = menuTheme.SelectedIndex == 1 ? "matching" : "white"; ApplySettings(); };
            content.Children.Add(menuTheme);
            void Slider(string label, double min, double max, double value, Action<double> set)
            {
                var caption = new TextBlock { Text = $"{label}: {value:F0}" }; content.Children.Add(caption);
                var control = new Slider { Minimum = min, Maximum = max, Value = value, StepFrequency = 1 };
                control.ValueChanged += (_, _) => { set(control.Value); caption.Text = $"{label}: {control.Value:F0}"; ApplySettings(); }; content.Children.Add(control);
            }
            void Toggle(string label, bool value, Action<bool> set)
            {
                var control = new CheckBox { Content = label, IsChecked = value }; control.Click += (_, _) => { set(control.IsChecked == true); ApplySettings(); }; content.Children.Add(control);
            }
            Slider("フォントサイズ", 14, 40, settings.FontSize, v => settings.FontSize = v); Slider("本体：背景の透過率 (%)", 0, 100, (1 - settings.BackgroundOpacity) * 100, v => settings.BackgroundOpacity = 1 - v / 100); Slider("本体：文字の不透明度 (%)", 0, 100, settings.TextOpacity * 100, v => settings.TextOpacity = v / 100); content.Children.Add(new TextBlock { Text = "本体の履歴：全行を表示（行数制限なし）", TextWrapping = TextWrapping.Wrap });
            DownwardSelector Choice(string title, string[] labels, int index, Action<int> set)
            {
                content.Children.Add(new TextBlock { Text = title });
                var control = new DownwardSelector { OwnerWindow = window, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = labels, SelectedIndex = index };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, title);
                control.SelectionChanged += (_, _) => { set(control.SelectedIndex); ApplySettings(); }; content.Children.Add(control); return control;
            }
            content.Children.Add(new TextBlock { Text = "飛び出す字幕ウィンドウ", FontSize = 22 });
            Choice("字幕の配置", ["上に英語・下に日本語", "上に日本語・下に英語", "左に英語・右に日本語"], settings.CaptionLayout == "japanese-top" ? 1 : settings.CaptionLayout == "side-by-side" ? 2 : 0, i => settings.CaptionLayout = i == 1 ? "japanese-top" : i == 2 ? "side-by-side" : "english-top");
            Slider("字幕の英語サイズ", 14, 64, settings.CaptionEnglishSize, v => settings.CaptionEnglishSize = v);
            Slider("字幕の日本語サイズ", 14, 64, settings.CaptionJapaneseSize, v => settings.CaptionJapaneseSize = v);
            Slider("字幕の行と行の間隔", 0, 30, settings.CaptionLineGap, v => settings.CaptionLineGap = v);
            Slider("折り返し行の高さ (%)", 105, 180, settings.CaptionLineHeight * 100, v => settings.CaptionLineHeight = v / 100);
            Toggle("補正済みの字幕も通常と同じ文字色にする", settings.CaptionCorrectedSameColor, v => settings.CaptionCorrectedSameColor = v);
            DownwardSelector? textPaletteSelector = null, surfaceSelector = null;
            void ColorSetting(string label, string value, Action<string> set)
            {
                var input = new TextBox { Header = label + "（#RRGGBB）", Text = value };
                var picker = new ColorPicker { Color = Windows.UI.Color.FromArgb(255, (byte)(CaptionWindow.ParseColor(value, 0xFFFFFF) >> 16), (byte)(CaptionWindow.ParseColor(value, 0xFFFFFF) >> 8), (byte)CaptionWindow.ParseColor(value, 0xFFFFFF)), IsAlphaEnabled = false, IsMoreButtonVisible = false };
                picker.ColorChanged += (_, args) => { input.Text = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}"; };
                input.TextChanged += (_, _) => { string hex = input.Text.Trim().TrimStart('#'); if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb)) { set("#" + hex.ToUpperInvariant()); picker.Color = Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); ApplySettings(); } };
                content.Children.Add(input); content.Children.Add(new Expander { Header = label + "を選択", Content = picker, HorizontalAlignment = HorizontalAlignment.Stretch });
            }
            ColorSetting("通常の文字色", settings.CaptionTextColor, v => { settings.CaptionTextColor = v; settings.CaptionPalette = "custom"; if (textPaletteSelector is not null) textPaletteSelector.SelectedIndex = 3; });
            ColorSetting("アプリと字幕の背景色", settings.CaptionSurfaceColor, v => { settings.CaptionSurfaceColor = v; settings.CaptionBackground = "live"; if (surfaceSelector is not null) surfaceSelector.SelectedIndex = 3; });
            ColorSetting("字幕とウィンドウの枠色", settings.CaptionBorderColor, v => settings.CaptionBorderColor = v);
            var correctedColor = new TextBox { Header = "補正済みの文字色（#RRGGBB）", Text = settings.CaptionCorrectedColor, PlaceholderText = "#70E1C4" };
            var colorPicker = new ColorPicker { Color = CaptionWindow.CorrectedBrush(settings).Color, IsAlphaEnabled = false, IsMoreButtonVisible = false };
            colorPicker.ColorChanged += (_, args) => { settings.CaptionCorrectedColor = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}"; correctedColor.Text = settings.CaptionCorrectedColor; ApplySettings(); };
            correctedColor.TextChanged += (_, _) =>
            {
                string hex = correctedColor.Text.Trim().TrimStart('#');
                if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
                {
                    settings.CaptionCorrectedColor = "#" + hex.ToUpperInvariant(); colorPicker.Color = Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); ApplySettings();
                }
            };
            content.Children.Add(correctedColor); content.Children.Add(new Expander { Header = "色を選択", Content = colorPicker, HorizontalAlignment = HorizontalAlignment.Stretch });
            Slider("字幕：背景の透過率 (%)", 0, 100, (1 - settings.CaptionOpacity) * 100, v => settings.CaptionOpacity = 1 - v / 100);
            Slider("字幕：文字の不透明度 (%)", 0, 100, settings.CaptionTextOpacity * 100, v => settings.CaptionTextOpacity = v / 100);
            Slider("字幕に表示する発話数", 1, 10, settings.CaptionRows, v => settings.CaptionRows = (int)v);
            Slider("字幕を消すまでの秒数（認識更新が止まった後）", 1, 20, settings.CaptionHideSeconds, v => settings.CaptionHideSeconds = v);
            textPaletteSelector = Choice("字幕の文字色", ["英語: 白／日本語: 黄色", "両方とも白（既定）", "英語: 水色／日本語: 白", "指定した文字色"], settings.CaptionPalette == "custom" ? 3 : settings.CaptionPalette == "white" ? 1 : settings.CaptionPalette == "cyan-white" ? 2 : 0, i => settings.CaptionPalette = i == 3 ? "custom" : i == 1 ? "white" : i == 2 ? "cyan-white" : "white-yellow");
            surfaceSelector = Choice("字幕の背景", ["従来の暗い背景", "黒", "白（文字は濃色）", "ライブキャプション風／指定した背景色"], settings.CaptionBackground == "live" ? 3 : settings.CaptionBackground == "black" ? 1 : settings.CaptionBackground == "light" ? 2 : 0, i => settings.CaptionBackground = i == 3 ? "live" : i == 1 ? "black" : i == 2 ? "light" : "dark");
            Choice("字幕の文字揃え", ["中央", "左", "右"], settings.CaptionAlignment == "left" ? 1 : settings.CaptionAlignment == "right" ? 2 : 0, i => settings.CaptionAlignment = i == 1 ? "left" : i == 2 ? "right" : "center");
            Toggle("字幕の文字を太くする", settings.CaptionBold, v => settings.CaptionBold = v);
            var en = new CheckBox { Content = "英語を表示", IsChecked = settings.English }; var ja = new CheckBox { Content = "日本語を表示", IsChecked = settings.Japanese };
            en.Click += (_, _) => { settings.English = en.IsChecked == true; if (!settings.English && !settings.Japanese) { settings.Japanese = true; ja.IsChecked = true; } ApplySettings(); };
            ja.Click += (_, _) => { settings.Japanese = ja.IsChecked == true; if (!settings.English && !settings.Japanese) { settings.English = true; en.IsChecked = true; } ApplySettings(); };
            content.Children.Add(en); content.Children.Add(ja);
            Choice("音声認識エンジン（次回Startから）", ["Whisper（アプリ単独で認識）", "Windowsライブキャプション連携（Windowsも起動）"], settings.SpeechEngine == "windows-live" ? 1 : 0, i => settings.SpeechEngine = i == 1 ? "windows-live" : "whisper");
            content.Children.Add(new TextBlock { Text = "Windows連携はWindowsの字幕を読み取り、本体・独立字幕・保存に反映します。Windows側の認識言語を合わせてください。入力・マイク・認識デバイスはWindows側に従います。「PC内音声＋マイク」の同時入力を選ぶと、混合音声を認識するため自動的にWhisperを使います。連携前の履歴は取り込みません。認識結果のAI補正は行いません。英日翻訳はAI翻訳モデルで速報を出し、ローカルLLMがONなら文脈を使って後から訳を改善します。Windows更新で連携できなくなる場合があります。", TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = "音声認識に使うデバイス（Whisper／次回Startから）" });
            var device = new DownwardSelector { OwnerWindow = window, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "GPU自動（使えない場合はCPU）", "NVIDIA GPU（CUDA）", "AMD XDNA 2 NPU", "CPU" }, SelectedIndex = settings.CpuOnly || settings.InferenceDevice == "cpu" ? 3 : settings.InferenceDevice == "npu" ? 2 : settings.InferenceDevice == "cuda" ? 1 : 0 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(device, "音声認識デバイス");
            device.SelectionChanged += (_, _) => { settings.InferenceDevice = device.SelectedIndex == 1 ? "cuda" : device.SelectedIndex == 2 ? "npu" : device.SelectedIndex == 3 ? "cpu" : "gpu"; settings.CpuOnly = device.SelectedIndex == 3; settings.Save(); }; content.Children.Add(device);
            content.Children.Add(new TextBlock { Text = "AMD NPUは専用Whisper-smallを使います。翻訳・AI補正はGPU/CPUです。NPUを使えない場合は理由を表示します。", TextWrapping = TextWrapping.Wrap });
            Toggle("起動時にCUDAモデルを先読み（音声取得なし）", settings.PreloadCudaModels, v => settings.PreloadCudaModels = v);
            var preloadButton = new Button { Content = "現在のモデルを今すぐ準備" }; preloadButton.Click += async (_, _) => await PreloadAsync(); content.Children.Add(preloadButton);
            Toggle("高速文字起こし（次回Startから）", settings.FastTranscription, v => settings.FastTranscription = v);
            Toggle("リアルタイム速報（軽いモデルで先に表示し、選択モデルで後から精度を改善）", settings.RealtimeCaptions, v => settings.RealtimeCaptions = v);
            Choice("速報モデル（次回Startから）", ["base：速度と聞き取りのバランス", "tiny：最速優先（途中字幕の誤りが増えます）"], settings.RealtimePreviewModel == "tiny" ? 1 : 0, i => settings.RealtimePreviewModel = i == 1 ? "tiny" : "base");
            Toggle("速報もGPUで実行（通常はOFF：CPUで速報／GPUで高精度更新）", settings.RealtimePreviewOnGpu, v => settings.RealtimePreviewOnGpu = v);
            content.Children.Add(new TextBlock { Text = "高速ON・GPU/CUDA設定で使用します。標準はCPUのbaseです。速度優先なら速報モデルをtinyに変更できますが、途中字幕の誤りが増えます。GPUの選択モデルで後から精度を改善します。速報もGPUにすると高精度処理と競合する場合があります。音声取り込みを止めず、途中字幕は最新の音声へ更新します。NPU／CPU専用設定では従来の認識経路を使います。", TextWrapping = TextWrapping.Wrap });
            Toggle("文脈を見て後からAIで文章を補正（次回Startから）", settings.CorrectTranscriptWithAi, v => settings.CorrectTranscriptWithAi = v);
            content.Children.Add(new TextBlock { Text = "高速でも日本語は選択したモデルを使用します（CPUではsmall）。最初は約400ms分の音声から簡易認識し、確定時は精度を優先します。英語の高速モードはbaseを使います。AI補正は補正済みの直前最大8発話を参照。日本語の補正では文脈を推論するため、数秒〜十数秒後に反映される場合があります。先行字幕はその間も表示します。数字が変わる補正は拒否し、補正後も既定では通常と同じ文字色です。色を変える場合は「補正済みの字幕も通常と同じ文字色にする」をOFFにします。保存テキストに補正ラベルは付けません。", TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = "録音ファイル形式（次の録音から）" });
            var format = new DownwardSelector { OwnerWindow = window, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "WAV（無圧縮）", "MP3", "M4A（AAC）" }, SelectedIndex = settings.RecordingFormat == "mp3" ? 1 : settings.RecordingFormat == "m4a" ? 2 : 0 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(format, "録音ファイル形式");
            format.SelectionChanged += (_, _) => { settings.RecordingFormat = format.SelectedIndex == 1 ? "mp3" : format.SelectedIndex == 2 ? "m4a" : "wav"; settings.Save(); }; content.Children.Add(format);
            Toggle("診断情報を表示", settings.Diagnostics, v => settings.Diagnostics = v);
            content.Children.Add(new TextBlock { Text = "音声認識モデル（日本語／英語の高速OFF・GPU/CPU）" });
            var modelHelp = new Button { Content = "モデルのヘルプ" }; modelHelp.Click += ModelHelpClick; content.Children.Add(modelHelp);
            string[] modelIds = ["tiny", "base", "small", "medium", "large-v3-turbo"];
            var models = new DownwardSelector { OwnerWindow = window, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "tiny（最軽量）", "base（速度優先）", "small（速度と精度のバランス）", "medium（精度優先・負荷大）", "large-v3-turbo（GPU向け高速モデル）" }, SelectedIndex = Array.IndexOf(modelIds, settings.SpeechModel) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(models, "音声認識モデル");
            models.SelectionChanged += (_, _) => { if (models.SelectedIndex >= 0) { settings.SpeechModel = modelIds[models.SelectedIndex]; settings.Save(); } }; content.Children.Add(models);
            content.Children.Add(new TextBlock { Text = "専門用語・固有名詞（認識のヒント）" });
            var prompt = new TextBox { Text = settings.SpeechPrompt, MaxLength = 500, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, PlaceholderText = "例: WASAPI, DirectML, ニューラルネットワーク" };
            prompt.TextChanged += (_, _) => { settings.SpeechPrompt = prompt.Text; settings.Save(); }; content.Children.Add(prompt);
            content.Children.Add(new TextBlock { Text = "large-v3-turboはGPU向け、smallはCPUでも使える精度優先モデル、base/tinyは速度優先です。未準備のモデルはscripts/setup-speech.ps1 -Model モデル名で追加できます。", TextWrapping = TextWrapping.Wrap });
            Toggle("ローカルLLMで文脈を踏まえて翻訳（次回Startから）", settings.UseLocalLlm, v => settings.UseLocalLlm = v);
            content.Children.Add(new TextBlock { Text = "英文表示と独立してAI翻訳します。Windows連携では、途中の英文にも文脈LLMを使い、速報訳・文脈訳の準備ができたものから表示します。確定文は全文を再確認して更新します。文章のAI補正は上の設定で独立してON/OFFできます。", TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = "翻訳の用語メモ（次回Startから）" });
            var terminology = new TextBox { Text = settings.TranslationGlossary, MaxLength = 1000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, PlaceholderText = "例: 32-bit float = 32ビット浮動小数点、subject = 被写体（撮影の話題）" };
            terminology.TextChanged += (_, _) => { settings.TranslationGlossary = terminology.Text; settings.Save(); }; content.Children.Add(terminology);
            var llmModel = new TextBox { Text = settings.LlmModelPath, PlaceholderText = "空欄: セットアップしたQwen3-4B GGUF", Header = "ローカルLLMのGGUFファイル" };
            llmModel.TextChanged += (_, _) => { settings.LlmModelPath = llmModel.Text; settings.Save(); }; content.Children.Add(llmModel);
            var llmBrowse = new Button { Content = "GGUFファイルを選択…" };
            llmBrowse.Click += async (_, _) =>
            {
                try { var picker = new FileOpenPicker(window.AppWindow.Id); picker.FileTypeFilter.Add(".gguf"); var file = await picker.PickSingleFileAsync(); if (file is not null) llmModel.Text = file.Path; }
                catch (Exception ex) { StatusText.Text = ex.Message; }
            }; content.Children.Add(llmBrowse);
            void Folder(string label, string value, Action<string> set, bool runtime = false)
            {
                content.Children.Add(new TextBlock { Text = label }); var path = new TextBox { Text = value, IsReadOnly = true }; content.Children.Add(path);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; var browse = new Button { Content = "フォルダーを選択…" };
                browse.Click += async (_, _) =>
                {
                    try
                    {
                        var picker = new FolderPicker(window.AppWindow.Id); if (Directory.Exists(path.Text)) picker.SuggestedFolder = path.Text;
                        var folder = await picker.PickSingleFolderAsync(); if (folder is null) return;
                        if (runtime && !IsRuntime(folder.Path)) { StatusText.Text = "inference/worker.pyがあるセットアップ済みフォルダーを選んでください。"; return; }
                        path.Text = folder.Path; set(folder.Path); settings.Save();
                    }
                    catch (Exception ex) { StatusText.Text = ex.Message; }
                };
                var reset = new Button { Content = "指定を解除" }; reset.Click += (_, _) => { path.Text = ""; set(""); settings.Save(); };
                buttons.Children.Add(browse); buttons.Children.Add(reset); content.Children.Add(buttons);
            }
            Folder("テキストの保存先", settings.TextSaveDirectory, v => settings.TextSaveDirectory = v); Folder("録音の保存先", settings.RecordingSaveDirectory, v => settings.RecordingSaveDirectory = v); Folder("モデル・推論環境", settings.RuntimeDirectory, v => settings.RuntimeDirectory = v, true);
            content.Children.Add(new TextBlock { Text = "設定は自動で記憶します。保存・録音は操作したときだけ行います。", TextWrapping = TextWrapping.Wrap });
            var panel = new Grid { RequestedTheme = Root.RequestedTheme, Background = CaptionWindow.SurfaceBrush(settings), Padding = new Thickness(28), RowSpacing = 16 };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition());
            panel.Children.Add(new TextBlock { Text = "設定", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 1); panel.Children.Add(scroll); window.Content = panel;
            window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Locaptio.ico"));
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)) / 96.0;
            int width = Math.Min((int)(820 * scale), area.Width - 32), height = Math.Min((int)(900 * scale), area.Height - 32);
            window.AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
            ((OverlappedPresenter)window.AppWindow.Presenter).IsAlwaysOnTop = true;
            window.Activate();
        }
        catch (Exception ex) { settingsWindow?.Close(); settingsWindow = null; StatusText.Text = ex.Message; }
    }
    private async void CloseClick(object sender, RoutedEventArgs e) => await ShutdownAsync();
    private void ModelHelpClick(object sender, RoutedEventArgs e)
    {
        if (closing) return;
        if (modelHelpWindow is not null) { ((OverlappedPresenter)modelHelpWindow.AppWindow.Presenter).Restore(); modelHelpWindow.Activate(); return; }
        var window = new Window { Title = "Locaptio — モデルのヘルプ" };
        modelHelpWindow = window; window.Closed += (_, _) => modelHelpWindow = null;
        var content = new StackPanel { Spacing = 16, Padding = new Thickness(28) };
        void Explain(string title, string body)
        {
            content.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, FontSize = 16 });
        }
        Explain("モデルの選び方", "音声認識モデルは音声を文字へ変換します。翻訳や文章のAI補正は別のモデルが担当します。精度や速度は話者、雑音、PC、設定によって変わります。");
        Explain("Whisper：音声 → 英語／日本語の文字", "tiny：最軽量。速度や少ないメモリを優先する場合。\nbase：軽量。高速文字起こしの標準モデル。\nsmall：速度と精度のバランス。CPU利用時にも候補。\nmedium：より大きなモデル。処理時間とメモリが増えるため余裕のあるPC向け。\nlarge-v3-turbo：large-v3を高速化したモデル。CUDA GPUで精度と速度を両立したい場合の候補。日本語訳はこのモデル自身ではなく下の翻訳モデルが生成します。");
        Explain("Windowsライブキャプション連携", "設定の「音声認識エンジン」で選ぶと、Windowsのライブキャプションが認識した文字を取り込みます。Windowsが公開している字幕の文字情報を100ms間隔で読み取ります。モデル単体の再配布ではありません。本体・独立字幕・テキスト保存に反映し、連携前の履歴は取り込みません。\n入力と認識言語はWindows側に従います。日本語文字起こしではWindowsの認識言語を日本語、英日翻訳では英語にしてください。Windows側の字幕も開いておく必要があり、ウィンドウを閉じると再接続待ちになります。認識文のAI補正は行いません。英日翻訳はFuguMTで速報を出し、ローカルLLMがONなら直前4発話と用語メモを使い、後から自然な日本語へ更新します。\n認識GPU/NPUを本アプリから指定する方式ではありません。マイクやアプリ別音声を本アプリで選択したい場合はWhisperを使ってください。「PC内音声＋マイク」の同時入力では、自動的にWhisperを使って混合音声を認識します。英日翻訳は従来のローカル翻訳モデルを使うため、その翻訳処理の時間は別途かかります。");
        Explain("Windowsのライブキャプションとの違い", "Windowsは端末内に組み込まれたコンパクトなAzure AI Speechモデルで字幕を生成します。このアプリはWhisperで蓄積音声を繰り返し認識するため、モデル計算と音声の蓄積に待ち時間が生じます。速報では軽いtiny/baseを別プロセスで使い、高精度認識やAI補正を待たずに表示します。同じ速度を保証するものではありません。Windowsの既定色はOSテーマでも変わるため、本アプリの既定色は添付例に近い白と濃いグレーです。参考: https://support.microsoft.com/en-us/accessibility/windows/use-live-captions-to-better-understand-audio");
        Explain("速報と高精度更新", "高速ON＋リアルタイム速報ON：CPUのbase（標準）またはtinyで先に表示し、GPU/CUDAの選択モデルで後から更新します。tinyは速い一方、途中字幕の誤りが増えます。確定処理と速報を別プロセスで並行実行します。速報もGPUへ切り替えられますが、GPUの混雑で遅くなる場合があります。\n速報OFF：日本語は選択モデル、英語の高速ONはbaseを使用します。\nAMD NPU：専用Whisper-small ONNXを使用します。\nCPU専用：large-v3-turboは準備済みsmallへ切り替わる場合があります。\n実際のモデルとデバイスは本体に表示します。");
        Explain("開始までの待ち時間", "CUDAの先読みをONにすると、起動直後にダウンロード済みのモデルをGPUへ読み込みます。音声取得はStart／字幕を押すまで始まりません。準備完了後はモデルを再利用します。停止時に処理を中断した場合や設定変更時は再準備が必要です。初回のGPU初期化時間は残ります。");
        Explain("FuguMT：英語の文字 → 日本語訳", "ニューラル機械翻訳のAIモデルです。通常の英日翻訳と途中の速報訳を担当します。GPU自動ではDirectML、NVIDIA GPU（CUDA）ではCTranslate2のCUDA経路、CPUではCPU推論を使います。");
        Explain("Qwen3 GGUF：後からAIで補正・翻訳", "ローカルLLMです。文脈を使う文章補正と確定文の翻訳に使います。Windows連携の英日翻訳でも使用し、直前の4発話と用語メモから専門用語・多義語の意味を判断します。音声を直接認識するモデルではありません。準備には追加のメモリが必要で、補正や翻訳が遅れて更新されることがあります。GPU経路はVulkanで、WhisperのCUDA選択とは独立しています。");
        Explain("NVIDIA GPU（CUDA）", "対応するNVIDIA GPU・ドライバーとCUDA 12／cuDNN 9の推論ライブラリが必要です。本アプリの準備用スクリプトはscripts/setup-cuda.ps1です。CUDAを明示選択した場合、利用できなければ理由を表示します。CPUへ自動で切り替えたい場合はGPU自動を選んでください。");
        content.Children.Add(new HyperlinkButton { Content = "Whisperの公式モデル説明", NavigateUri = new Uri("https://github.com/openai/whisper") });
        content.Children.Add(new HyperlinkButton { Content = "faster-whisperの公式GPU要件", NavigateUri = new Uri("https://github.com/SYSTRAN/faster-whisper") });
        window.Content = new ScrollViewer { Content = content, RequestedTheme = Root.RequestedTheme, Background = CaptionWindow.SurfaceBrush(settings), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        double scale = GetDpiForWindow(hwnd) / 96.0;
        int width = Math.Min((int)(760 * scale), work.Width - 32), height = Math.Min((int)(850 * scale), work.Height - 32);
        window.AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Locaptio.ico"));
        ((OverlappedPresenter)window.AppWindow.Presenter).IsAlwaysOnTop = true; window.Activate();
    }
    private async void AboutClick(object sender, RoutedEventArgs e)
    {
        if (dialogOpen || closing) return;
        dialogOpen = true;
        try
        {
            await new ContentDialog
            {
                Title = "Locaptio", XamlRoot = Root.XamlRoot, RequestedTheme = ElementTheme.Dark,
                Content = "完全ローカルの音声文字起こし・英日翻訳アプリです。\n\n上部のタイトルバーをドラッグして移動、ダブルクリックで最大化・元に戻せます。設定メニューから音声認識デバイス、AI補正、保存先などを変更できます。",
                CloseButtonText = "閉じる"
            }.ShowAsync();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { dialogOpen = false; }
    }
    private void RememberBounds()
    {
        settings.WinUiLeft = AppWindow.Position.X; settings.WinUiTop = AppWindow.Position.Y;
        settings.WinUiWidth = AppWindow.Size.Width; settings.WinUiHeight = AppWindow.Size.Height;
    }
    private async Task VerifyWindowsTranslationAsync()
    {
        string engine = settings.SpeechEngine, kind = settings.AudioInputKind, mode = settings.CaptionMode;
        bool useLlm = settings.UseLocalLlm, passed = false;
        try
        {
            settings.SpeechEngine = "windows-live"; settings.AudioInputKind = "system"; settings.CaptionMode = "translate"; settings.UseLocalLlm = true; ApplySettings();
            CaptionClick(this, new RoutedEventArgs()); ((OverlappedPresenter)AppWindow.Presenter).Restore(); await StartAsync();
            // Initialization messages can replace the connection status; wait for the bridge process instead.
            for (int i=0;i<200 && !windowsBridgeConnected && captionStop is not null;i++) await Task.Delay(100);
            if (!windowsBridgeConnected || captionStop is null) throw new Exception(StatusText.Text);
            using var reader = new NAudio.Wave.WaveFileReader(Path.Combine(root, "artifacts", "test-translation.wav"));
            using var output = new NAudio.Wave.WaveOutEvent(); output.Init(reader); long playback = Stopwatch.GetTimestamp(); output.Play();
            await Task.Delay(reader.TotalTime + TimeSpan.FromSeconds(10));
            string text = transcript.Export(true); var rendered = translationVerificationUpdates.ToArray();
            double First(Func<CaptionUpdate,bool> predicate) => rendered.Where(c=>predicate(c.Caption)).Select(c=>(c.At-playback)*1000.0/Stopwatch.Frequency).DefaultIfEmpty(-1).Min();
            bool Fixture(CaptionUpdate c) => c.English.StartsWith("If", StringComparison.OrdinalIgnoreCase) || c.English.Contains("videographer", StringComparison.OrdinalIgnoreCase);
            double firstSource = First(c=>Fixture(c)), firstTranslation = First(c=>Fixture(c) && !string.IsNullOrWhiteSpace(c.Japanese)), firstQuality = First(c=>Fixture(c) && c.TranslationRefined);
            bool terms = !text.Contains("必要") && captions.Values.Where(c=>c.English.Contains("subject",StringComparison.OrdinalIgnoreCase)).All(c=>c.Japanese is not null && System.Text.RegularExpressions.Regex.IsMatch(c.Japanese,"声|音")) && text.Contains("latitude", StringComparison.OrdinalIgnoreCase) && !text.Contains("緯度") && !text.Contains("被験者") && !text.Contains("浮き輪") && !System.Text.RegularExpressions.Regex.IsMatch(text, @"[这们吗为说让从过时]|的(?:时候|時候)");
            passed = terms && firstSource >= 0 && firstSource < 3000 && firstTranslation > 0 && firstTranslation < 5000 && firstQuality > 0 && CaptionList.Visibility == Visibility.Visible && captions.Values.Any(c=>c.TranslationRefined);
            await File.WriteAllTextAsync(Path.Combine(root,"artifacts","windows-translation-transcript.txt"),text,new UTF8Encoding(true));
            await File.WriteAllTextAsync(Path.Combine(root,"artifacts","windows-translation-events.json"),System.Text.Json.JsonSerializer.Serialize(rendered.Select(c=>new { c.Caption.Id,c.Caption.Revision,c.Caption.English,c.Caption.Japanese,c.Caption.Final,c.Caption.TranslationRefined, playbackMs=(c.At-playback)*1000.0/Stopwatch.Frequency })));
            var stopping = Stopwatch.StartNew(); await StopAsync();
            File.WriteAllText(Path.Combine(root,"artifacts","windows-translation-verification.txt"),$"PASS={passed}; firstEnglish={firstSource:F0}ms; firstJapanese={firstTranslation:F0}ms; firstContextTranslation={firstQuality:F0}ms; sourceRevisions={rendered.Select(c=>(c.Caption.Id,c.Caption.Revision)).Distinct().Count()}; contextualUpdates={rendered.Count(c=>c.Caption.TranslationRefined)}; terms={terms}; stop={stopping.Elapsed.TotalMilliseconds:F0}ms; status={StatusText.Text}");
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(root,"artifacts","windows-translation-verification.txt"),"PASS=False; "+ex); }
        finally { settings.SpeechEngine = engine; settings.AudioInputKind = kind; settings.CaptionMode = mode; settings.UseLocalLlm = useLlm; settings.Save(); Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync(); }
    }
    private async Task VerifyWindowsLiveAsync()
    {
        string engine = settings.SpeechEngine, kind = settings.AudioInputKind, mode = settings.CaptionMode;
        bool passed = false;
        try
        {
            settings.SpeechEngine = "windows-live"; settings.AudioInputKind = "system"; settings.CaptionMode = "japanese"; ApplySettings();
            CaptionClick(this, new RoutedEventArgs()); ((OverlappedPresenter)AppWindow.Presenter).Restore();
            await StartAsync();
            for (int i = 0; i < 100 && !StatusText.Text.Contains("接続済み") && captionStop is not null; i++) await Task.Delay(100);
            if (!StatusText.Text.Contains("接続済み")) throw new Exception("Bridge not connected: " + StatusText.Text);
            if (transcript.Count != 0) throw new Exception("Imported Windows caption history before connection");
            using var reader = new NAudio.Wave.WaveFileReader(Path.Combine(root, "artifacts", "test-ja-continuous.wav"));
            using var output = new NAudio.Wave.WaveOutEvent(); output.Init(reader); var playback = Stopwatch.StartNew(); output.Play();
            double first = -1;
            while (playback.Elapsed < reader.TotalTime + TimeSpan.FromSeconds(2))
            {
                if (first < 0 && transcript.Count > 0) first = playback.Elapsed.TotalMilliseconds;
                await Task.Delay(50);
            }
            var text = transcript.Export(true);
            passed = text.Contains("音声認識") && history.Count > 0 && CaptionList.Visibility == Visibility.Visible && captionWindow?.RenderedPositions().Length > 0 && captionWindow.HasNoNativeFrame();
            await File.WriteAllTextAsync(Path.Combine(root, "artifacts", "windows-live-transcript.txt"), text, new UTF8Encoding(true));
            var stop = Stopwatch.StartNew(); await StopAsync();
            File.WriteAllText(Path.Combine(root, "artifacts", "windows-live-verification.txt"), $"PASS={passed}; first={first:F0}ms; stop={stop.Elapsed.TotalMilliseconds:F0}ms; rows={transcript.Count}; mainAndPopout=True; previousHistoryExcluded=True; bridgeWindowsRecognition=True");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(root, "artifacts", "windows-live-verification.txt"), "PASS=False; " + ex); }
        finally { settings.SpeechEngine = engine; settings.AudioInputKind = kind; settings.CaptionMode = mode; settings.Save(); Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync(); }
    }
    private async Task VerifyCaptionStyleAsync()
    {
        string mode = settings.CaptionMode, color = settings.CaptionCorrectedColor, layout = settings.CaptionLayout;
        string originalText = settings.CaptionTextColor, originalSurface = settings.CaptionSurfaceColor, originalPalette = settings.CaptionPalette, originalBackground = settings.CaptionBackground;
        double gap = settings.CaptionLineGap; bool english = settings.English, japanese = settings.Japanese, sameColor = settings.CaptionCorrectedSameColor;
        bool passed = false;
        try
        {
            settings.CaptionMode = "translate"; settings.CaptionLayout = "english-top"; settings.English = settings.Japanese = true;
            settings.CaptionLineGap = 2;
            CaptionClick(CaptionButton, new RoutedEventArgs());
            var sample = new CaptionUpdate(1, 1, "Captions appear around the text.", null, false, Stopwatch.GetTimestamp(), 0, 0);
            captionWindow!.Update([sample]);
            captionWindow.Update([sample with { Japanese = "字幕は文字の周囲だけを囲みます。" }]);
            await Task.Delay(150);
            var compact = captionWindow.RenderedPositions(); double compactGap = compact[1].Y - compact[0].Y;
            bool clipped = captionWindow.HasTextOnlyShape();
            settings.CaptionLineGap = 20; captionWindow.ApplyAppearance(); await Task.Delay(100);
            var expanded = captionWindow.RenderedPositions(); bool spacing = expanded[1].Y - expanded[0].Y > compactGap + 15;
            settings.CaptionLineGap = 2; settings.CaptionCorrectedColor = "#C990FF"; settings.CaptionCorrectedSameColor = false;
            captionWindow.Update([sample with { Revision = 2, SourceCorrected = true, English = "Context corrected this caption.", Japanese = "文脈を見て補正した字幕です。" }]);
            await Task.Delay(150);
            bool colored = captionWindow.UsesCorrectionColor();
            settings.CaptionCorrectedSameColor = true; captionWindow.ApplyAppearance();
            bool ordinary = captionWindow.UsesOrdinaryColors();
            bool defaultSame = new Settings().CaptionCorrectedSameColor;
            captions[sample.Id] = sample with { SourceCorrected = true, Japanese = "本体にも表示します。" }; UpdateHistory(captions[sample.Id]); RenderCaptions();
            ((OverlappedPresenter)AppWindow.Presenter).Restore(); await Task.Delay(100);
            var mainTexts = RealizedCaptionRows.SelectMany(g => g.Children.OfType<TextBlock>()).Where(t => t.Visibility == Visibility.Visible).ToArray();
            bool mirrored = captionWindow.RenderedPositions().All(p => mainTexts.Any(t => t.Text == p.Text)) && mainTexts.Length == 2 && CaptionList.Visibility == Visibility.Visible && Header.Visibility == Visibility.Visible
                && mainTexts.All(t => ((Microsoft.UI.Xaml.Media.SolidColorBrush)t.Foreground).Color == CaptionWindow.OrdinaryBrush(settings, false).Color);
            bool liveDefaults = CaptionWindow.SurfaceBrush(new Settings()).Color == Windows.UI.Color.FromArgb(255, 54, 54, 54) && CaptionWindow.OrdinaryBrush(new Settings(), true).Color == Windows.UI.Color.FromArgb(255, 255, 255, 255);
            SettingsClick(this, new RoutedEventArgs()); await Task.Delay(200);
            var settingsPanel = (Grid)settingsWindow!.Content;
            var settingItems = (StackPanel)settingsPanel.Children.OfType<ScrollViewer>().Single().Content;
            var normalInput = settingItems.Children.OfType<TextBox>().Single(t => (string)t.Header == "通常の文字色（#RRGGBB）");
            var surfaceInput = settingItems.Children.OfType<TextBox>().Single(t => (string)t.Header == "アプリと字幕の背景色（#RRGGBB）");
            normalInput.ApplyTemplate(); surfaceInput.ApplyTemplate();
            normalInput.Text = "#AADDEE"; surfaceInput.Text = "#444444"; await Task.Delay(150);
            string colorDebug = $"palette={settings.CaptionPalette}, text={settings.CaptionTextColor}, surface={settings.CaptionSurfaceColor}, bg={((Microsoft.UI.Xaml.Media.SolidColorBrush)Root.Background).Color}, ordinary={captionWindow.UsesOrdinaryColors()}";
            bool customColors = settings.CaptionPalette == "custom" && settings.CaptionTextColor == "#AADDEE" && settings.CaptionSurfaceColor == "#444444" && captionWindow.UsesOrdinaryColors()
                && ((Microsoft.UI.Xaml.Media.SolidColorBrush)Root.Background).Color == Windows.UI.Color.FromArgb(255, 68, 68, 68);
            var paletteSelector = settingItems.Children.OfType<DownwardSelector>().Single(c => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(c) == "字幕の文字色");
            colorDebug += $"; selector={paletteSelector.SelectedIndex}"; customColors &= paletteSelector.SelectedIndex == 3;
            paletteSelector.SelectedIndex = 1; await Task.Delay(100); customColors &= settings.CaptionPalette == "white" && captionWindow.UsesOrdinaryColors(); colorDebug += $"; preset={settings.CaptionPalette}, ordinary={captionWindow.UsesOrdinaryColors()}";
            settings.CaptionTextColor = originalText; settings.CaptionSurfaceColor = originalSurface; settings.CaptionPalette = originalPalette; settings.CaptionBackground = originalBackground; ApplySettings(); settingsWindow.Close();
            bool noMarker = captionWindow.RenderedPositions().All(t => !t.Text.Contains("AI補正"));
            await captionWindow.CaptureAsync(Path.Combine(root, "artifacts", "caption-text-bubbles.png"));
            bool noNativeFrame = captionWindow.HasNoNativeFrame();
            var captionSize = captionWindow.AppWindow.Size;
            captionWindow.AppWindow.Resize(new SizeInt32(Math.Max(360, captionSize.Width / 2), captionSize.Height));
            var longText = sample with { Revision = 3, English = string.Join(" ", Enumerable.Repeat("Thank you very much for coming today.", 12)), Japanese = string.Concat(Enumerable.Repeat("本日はお越しいただき、ありがとうございます。", 12)) };
            captionWindow.Update([longText]); await Task.Delay(150);
            bool wrappedClipped = captionWindow.HasTextOnlyShape() && captionWindow.HasNoNativeFrame();
            captionWindow.AppWindow.Hide();
            captionWindow.Update([sample with { Id = 2, Revision = 1 }]); await Task.Delay(100);
            bool shownWithoutFrame = captionWindow.AppWindow.IsVisible && captionWindow.HasNoNativeFrame() && captionWindow.HasTextOnlyShape();
            captionWindow.AppWindow.Resize(captionSize);
            passed = clipped && spacing && colored && ordinary && defaultSame && mirrored && liveDefaults && customColors && noMarker && noNativeFrame && wrappedClipped && shownWithoutFrame;
            File.WriteAllText(Path.Combine(root, "artifacts", "caption-style-verification.txt"), $"PASS={passed}; textOnlyRegion={clipped}; adjustableGap={spacing}; customCorrectionColor={colored}; sameColor={ordinary}; defaultSame={defaultSame}; mainAndPopout={mirrored}; liveDefaults={liveDefaults}; colorSettings={customColors} ({colorDebug}); noAIMarker={noMarker}; noNativeFrame={noNativeFrame}; opaqueComposition={WindowAppearance.IsOpaque(hwnd)}; neutralBorder={settings.CaptionBorderColor}; longTextClipped={wrappedClipped}; redisplayWithoutFrame={shownWithoutFrame}");
            if (Environment.GetCommandLineArgs().Contains("--preview-caption-frame"))
            {
                for (int i = 0; i < 45; i++)
                {
                    captionWindow.Update([sample with { Revision = 10 + i, English = "Thank you very much for coming today." }]);
                    captionWindow.Update([sample with { Revision = 10 + i, English = "Thank you very much for coming today.", Japanese = "本日はお越しいただき、ありがとうございます。" }]);
                    await Task.Delay(1000);
                }
            }
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(root, "artifacts", "caption-style-verification.txt"), ex.ToString()); }
        finally { settings.CaptionTextColor = originalText; settings.CaptionSurfaceColor = originalSurface; settings.CaptionPalette = originalPalette; settings.CaptionBackground = originalBackground; settings.CaptionCorrectedSameColor = sameColor; settings.CaptionMode = mode; settings.CaptionCorrectedColor = color; settings.CaptionLineGap = gap; settings.CaptionLayout = layout; settings.English = english; settings.Japanese = japanese; settings.Save(); Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync(); }
    }
    private async Task VerifyCaptionIdleAsync()
    {
        double original = settings.CaptionHideSeconds;
        bool passed = false;
        try
        {
            settings.CaptionHideSeconds = 1;
            CaptionClick(CaptionButton, new RoutedEventArgs()); await Task.Delay(100);
            bool idleHidden = !captionWindow!.AppWindow.IsVisible;
            bool minimized = ((OverlappedPresenter)AppWindow.Presenter).State == OverlappedPresenterState.Minimized;
            var first = new CaptionUpdate(1, 1, "Test speech", null, false, Stopwatch.GetTimestamp(), 0, 0);
            captionWindow.Update([first]); await Task.Delay(100); bool speechShown = captionWindow.AppWindow.IsVisible;
            await Task.Delay(1400); bool silenceHidden = !captionWindow.AppWindow.IsVisible;
            captionWindow.Update([first with { Japanese = "テスト" }]); bool lateTranslationHidden = !captionWindow.AppWindow.IsVisible;
            captionWindow.Update([first with { Revision = 2, English = "Corrected speech", SourceCorrected = true }]); bool lateCorrectionHidden = !captionWindow.AppWindow.IsVisible;
            captionWindow.Update([first with { Id = 2, English = "Next speech" }]); await Task.Delay(100); bool resumed = captionWindow.AppWindow.IsVisible;
            bool translatedSpeechVisible = true;
            for (int i=2;i<8;i++)
            {
                captionWindow.Update([first with { Id=2, Revision=i, English="Next speech " + new string('x',i), Japanese="次の発話" }]);
                await Task.Delay(350); translatedSpeechVisible &= captionWindow.AppWindow.IsVisible;
            }
            passed = idleHidden && minimized && speechShown && silenceHidden && lateTranslationHidden && lateCorrectionHidden && resumed && translatedSpeechVisible;
            File.WriteAllText(Path.Combine(root, "artifacts", "caption-idle-verification.txt"), $"PASS={passed}; idleHidden={idleHidden}; minimized={minimized}; speechShown={speechShown}; silenceHidden={silenceHidden}; lateTranslationHidden={lateTranslationHidden}; lateCorrectionHidden={lateCorrectionHidden}; resumed={resumed}; translatedSpeechVisible={translatedSpeechVisible}");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(root, "artifacts", "caption-idle-verification.txt"), ex.ToString()); }
        finally { settings.CaptionHideSeconds = original; settings.Save(); Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync(); }
    }
    private void MinimizeClick(object sender, RoutedEventArgs e)
    {
        if (((OverlappedPresenter)AppWindow.Presenter).State == OverlappedPresenterState.Restored) RememberBounds();
        ((OverlappedPresenter)AppWindow.Presenter).Minimize();
    }
    private bool lifecycleMinimized;
    private async Task VerifyLifecycleAsync()
    {
        MinimizeClick(this, new RoutedEventArgs()); await Task.Delay(100);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        lifecycleMinimized = presenter.State == OverlappedPresenterState.Minimized;
        presenter.Restore(); await Task.Delay(100);
        lifecycleMinimized &= presenter.State == OverlappedPresenterState.Restored;
        var restoredSize = AppWindow.Size;
        presenter.Maximize(); await Task.Delay(100);
        lifecycleMinimized &= presenter.State == OverlappedPresenterState.Maximized;
        presenter.Restore(); await Task.Delay(100);
        lifecycleMinimized &= presenter.State == OverlappedPresenterState.Restored && AppWindow.Size.Width == restoredSize.Width && AppWindow.Size.Height == restoredSize.Height;
        Directory.CreateDirectory(Path.Combine(root, "artifacts"));
        StartRecording(Path.Combine(root, "artifacts", "winui-shutdown.wav")); await StartAsync();
        if (Environment.GetCommandLineArgs().Contains("--verify-startup-close")) await Task.Delay(1000);
        else
        {
            for (int i = 0; i < 720 && pipeline is null && captionStop is not null; i++) await Task.Delay(250);
            if (pipeline is null) lifecycleMinimized = false;
            using var reader = new NAudio.Wave.WaveFileReader(Path.Combine(root, "artifacts", settings.CaptionMode == "japanese" ? "test-ja.wav" : "test.wav"));
            using var output = new NAudio.Wave.WaveOutEvent(); output.Init(reader); output.Play(); await Task.Delay(2200);
        }
        if (Environment.GetCommandLineArgs().Contains("--verify-minimized-close")) { MinimizeClick(this, new RoutedEventArgs()); await Task.Delay(100); }
        if (Environment.GetCommandLineArgs().Contains("--verify-system-close")) PostMessageW(hwnd, 0x0010, 0, 0);
        else await ShutdownAsync();
    }
    private async void ModeChanged(object? sender, EventArgs e)
    {
        if (!initialized || modeChanging || closing) return;
        modeChanging = true; ModeSelector.IsEnabled = false; StartButton.IsEnabled = false;
        try
        {
            bool restart = captionStop is not null; await StopAsync();
            settings.CaptionMode = ModeSelector.SelectedIndex == 1 ? "japanese" : "translate";
            ClearCaptions(); ApplySettings(); BackendText.Text = "";
            if (restart) await StartAsync(); else StatusText.Text = "モードを切り替えました。Startで開始します。";
        }
        finally { modeChanging = false; ModeSelector.IsEnabled = true; StartButton.IsEnabled = captionStop is null; }
    }
    private async Task VerifyAsync()
    {
        bool passed = false; string artifacts = Path.Combine(root, "artifacts"); Directory.CreateDirectory(artifacts);
        string originalLayout = settings.CaptionLayout; bool originalEnglish = settings.English, originalJapanese = settings.Japanese;
        var originalInput = SelectedAudio();
        try
        {
            if (Environment.GetCommandLineArgs().Contains("--verify-realtime"))
            {
                settings.AudioInputKind = "application"; settings.AudioDeviceId = ""; settings.AudioProcessId = Environment.ProcessId; settings.AudioProcessStarted = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
                CaptionClick(CaptionButton, new RoutedEventArgs());
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-popout")) { settings.English = true; settings.Japanese = true; CaptionClick(CaptionButton, new RoutedEventArgs()); }
            StartRecording(Path.Combine(artifacts, "winui-recording.wav")); await StartAsync();
            for (int i = 0; i < 720 && pipeline is null && captionStop is not null; i++) await Task.Delay(250);
            bool immediate = false;
            if (pipeline is not null)
            {
                using var reader = new NAudio.Wave.WaveFileReader(Path.Combine(artifacts, Environment.GetCommandLineArgs().Contains("--verify-realtime") ? "test-ja-continuous.wav" : settings.CaptionMode == "japanese" ? "test-ja.wav" : "test.wav")); using var output = new NAudio.Wave.WaveOutEvent(); output.Init(reader); output.Play();
                for (int i = 0; i < 150 && transcript.Count == 0; i++) await Task.Delay(100);
                immediate = transcript.Count > 0;
                if (Environment.GetCommandLineArgs().Contains("--verify-realtime")) await Task.Delay(reader.TotalTime + TimeSpan.FromSeconds(6));
                else if (Environment.GetCommandLineArgs().Contains("--verify-correction"))
                    for (int i = 0; i < 900 && !correctionChecked; i++) await Task.Delay(100);
                else await Task.Delay(settings.UseLocalLlm ? 30000 : 18000);
            }
            string backend = BackendText.Text; string status = StatusText.Text; await File.WriteAllTextAsync(Path.Combine(artifacts, "winui-transcript.txt"), transcript.Export(true), new UTF8Encoding(true));
            passed = transcript.Export(true).Contains("テスト") && ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop && history.Count == transcript.Count;
            if (settings.CaptionMode == "japanese") passed &= translation is null && captions.Values.All(c => c.SourceLanguage == "ja") && Header.ColumnDefinitions[1].Width.Value == 0 && !transcript.Export(true).Contains("English:");
            if (Environment.GetCommandLineArgs().Contains("--verify-llm")) passed &= captions.Values.Any(c => c.TranslationRefined);
            if (Environment.GetCommandLineArgs().Contains("--verify-correction")) passed &= immediate && correctionChecked && !transcript.Export(true).Contains("［AI補正］");
            if (Environment.GetCommandLineArgs().Contains("--verify-npu")) passed &= speech?.Backend.Contains("NPU") == true;
            if (Environment.GetCommandLineArgs().Contains("--verify-cuda")) passed &= (accurateSpeech ?? speech)?.Backend == "CUDA GPU" && translation?.Backend == "CUDA GPU";
            if (Environment.GetCommandLineArgs().Contains("--verify-realtime"))
            {
                var sorted = verifiedRenderLags.Order().ToArray();
                double p95 = sorted.Length == 0 ? double.PositiveInfinity : sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * .95))];
                passed &= immediate && sorted.Length >= 10 && p95 < 2500 && accurateSpeech?.Backend == "CUDA GPU" && speech?.Model == settings.RealtimePreviewModel && captionWindow?.HasNoNativeFrame() == true;
                await captionWindow!.CaptureAsync(Path.Combine(artifacts, "realtime-captions.png"));
                File.WriteAllText(Path.Combine(artifacts, "winui-realtime-verification.txt"), $"PASS={passed}; renderedUpdates={sorted.Length}; renderedLagP95={p95:F0}ms; live={speech?.Backend}/{speech?.Model}; accuracy={accurateSpeech?.Backend}/{accurateSpeech?.Model}; noNativeFrame={captionWindow.HasNoNativeFrame()}; mainAndPopout={CaptionList.Visibility == Visibility.Visible && history.Count > 0}");
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-popout"))
            {
                var last = captions.Values.Last();
                settings.CaptionLayout = "english-top"; ApplySettings(); await Task.Delay(100);
                var positions = captionWindow!.RenderedPositions();
                var en = positions.Last(p => p.Text.StartsWith(last.English)); var ja = positions.Last(p => p.Text == last.Japanese);
                passed &= en.Y < ja.Y && Header.Visibility == Visibility.Visible && history.Count > 0;
                await captionWindow.CaptureAsync(Path.Combine(artifacts, "caption-popout.png"));
                settings.CaptionLayout = "japanese-top"; ApplySettings(); await Task.Delay(100);
                positions = captionWindow.RenderedPositions();
                passed &= positions.Last(p => p.Text.StartsWith(last.English)).Y > positions.Last(p => p.Text == last.Japanese).Y;
                settings.CaptionLayout = "side-by-side"; ApplySettings(); await Task.Delay(100);
                positions = captionWindow.RenderedPositions(); en = positions.Last(p => p.Text.StartsWith(last.English)); ja = positions.Last(p => p.Text == last.Japanese);
                passed &= en.X < ja.X && Math.Abs(en.Y - ja.Y) < 2;
                CaptionClick(CaptionButton, new RoutedEventArgs()); passed &= captionWindow is null && Header.Visibility == Visibility.Visible;
                CaptionClick(CaptionButton, new RoutedEventArgs());
                File.WriteAllText(Path.Combine(artifacts, "caption-popout-verification.txt"), $"PASS={passed}; English above Japanese / reverse / side-by-side; detach/reattach; speech={speech?.Backend}; translation={translation?.Backend}");
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-device-switch"))
            {
                await File.WriteAllTextAsync(Path.Combine(artifacts, "winui-npu-transcript.txt"), transcript.Export(true));
                var recordingBefore = recordingStop; await StopAsync(); settings.InferenceDevice = "gpu"; settings.CpuOnly = false; await StartAsync();
                for (int i = 0; i < 720 && pipeline is null && captionStop is not null; i++) await Task.Delay(250);
                if (pipeline is not null)
                {
                    using var reader = new NAudio.Wave.WaveFileReader(Path.Combine(artifacts, "test-ja.wav")); using var output = new NAudio.Wave.WaveOutEvent(); output.Init(reader); output.Play(); await Task.Delay(12000);
                }
                passed &= (accurateSpeech ?? speech)?.Backend.Contains("CUDA") == true && transcript.Export(true).Contains("テスト") && ReferenceEquals(recordingBefore, recordingStop);
                await File.WriteAllTextAsync(Path.Combine(artifacts, "winui-gpu-transcript.txt"), transcript.Export(true));
                backend = "NPU → " + BackendText.Text;
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-switch"))
            {
                var recordingBefore = recordingStop;
                ModeSelector.SelectedIndex = settings.CaptionMode == "japanese" ? 0 : 1;
                for (int i = 0; i < 720 && (modeChanging || pipeline is null); i++) await Task.Delay(250);
                passed &= !modeChanging && pipeline is not null && settings.CaptionMode == "japanese" && translation is null && llm is null && transcript.Count == 0 && ReferenceEquals(recordingBefore, recordingStop);
                if (pipeline is not null)
                {
                    using var reader = new NAudio.Wave.WaveFileReader(Path.Combine(artifacts, "test-ja.wav")); using var output = new NAudio.Wave.WaveOutEvent(); output.Init(reader); output.Play(); await Task.Delay(18000);
                    passed &= transcript.Export(true).Contains("テスト") && !transcript.Export(true).Contains("English:");
                    await File.WriteAllTextAsync(Path.Combine(artifacts, "winui-switched-transcript.txt"), transcript.Export(true), new UTF8Encoding(true));
                }
                backend = BackendText.Text; status = StatusText.Text;
            }
            await StopAsync(); passed &= recordingStop is not null; await StopRecordingAsync();
            using var wave = new NAudio.Wave.WaveFileReader(Path.Combine(artifacts, "winui-recording.wav")); passed &= wave.TotalTime.TotalSeconds > 10;
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(Root); var pixels = await bitmap.GetPixelsAsync();
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream(); var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(pixels));
            await encoder.FlushAsync(); stream.Seek(0); using var file = File.Create(Path.Combine(artifacts, "winui-overlay.png")); await System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream).CopyToAsync(file);
            File.WriteAllText(Path.Combine(artifacts, "winui-verification.txt"), $"PASS={passed}; backend={backend}; status={status}; topmost=true; WAV={wave.TotalTime.TotalSeconds:F2}s; independent recording stop; TXT UTF-8; rendered PNG");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(artifacts, "winui-verification.txt"), "PASS=False; " + ex); }
        finally { settings.AudioInputKind = originalInput.Kind; settings.AudioDeviceId = originalInput.DeviceId; settings.AudioProcessId = originalInput.ProcessId; settings.AudioProcessStarted = originalInput.ProcessStarted; settings.CaptionLayout = originalLayout; settings.English = originalEnglish; settings.Japanese = originalJapanese; settings.CaptionMode = originalMode; settings.UseLocalLlm = originalLlm; settings.CorrectTranscriptWithAi = originalCorrection; settings.InferenceDevice = originalDevice; settings.CpuOnly = originalCpu; Environment.ExitCode = passed ? 0 : 1; await ShutdownAsync(); }
    }
}
