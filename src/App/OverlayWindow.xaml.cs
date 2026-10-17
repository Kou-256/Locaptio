using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Media;
using System.Windows.Threading;
using Locaptio.Core;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;
using Microsoft.Win32;
using System.Text;

namespace Locaptio.App;
public partial class OverlayWindow : Window
{
    private readonly Settings settings = Settings.Load();
    private readonly ObservableCollection<CaptionRow> rows = [];
    private readonly TranscriptBuffer transcript = new();
    private CancellationTokenSource? recordingStop;
    private Task? recordingTask;
    private string? recordingPath;
    private long recordingStarted;
    private Task? textSaveTask;
    private CancellationTokenSource? stop;
    private Task? running;
    private LocalBackend? speech, translation;
    private Pipeline? pipeline;
    private long clearedAt;
    private long lastId;
    private bool closeReady, closing;
    private bool lifecycleMinimized;
    private readonly DispatcherTimer diagnostics = new() { Interval = TimeSpan.FromSeconds(1) };
    private TimeSpan previousCpu;
    private long previousClock;
    private double sttMs, translationMs, totalMs;
    private int maximumRows;
    private int translatedUpdates;
    private long pendingRender;
    private readonly string root = FindRoot();

    public OverlayWindow()
    {
        InitializeComponent(); Captions.ItemsSource = rows;
        Width = Math.Clamp(settings.Width, 620, SystemParameters.VirtualScreenWidth);
        Height = Math.Clamp(settings.Height, 240, SystemParameters.VirtualScreenHeight);
        Left = Math.Clamp(settings.Left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
        Top = Math.Clamp(settings.Top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
        ApplySettings();
        Loaded += async (_, _) =>
        {
            if (!Environment.GetCommandLineArgs().Contains("--verify-lifecycle")) return;
            MinimizeClick(this, new RoutedEventArgs()); await Task.Delay(100);
            lifecycleMinimized = WindowState == WindowState.Minimized;
            WindowState = WindowState.Normal; await Task.Delay(100);
            lifecycleMinimized &= WindowState == WindowState.Normal;
            Directory.CreateDirectory(Path.Combine(root, "artifacts"));
            StartRecording(Path.Combine(root, "artifacts", "wpf-shutdown.wav")); await Task.Delay(1000);
            Close(); Close(); // Repeated close requests must share the existing cleanup.
        };
        diagnostics.Tick += (_, _) => UpdateDiagnostics(); diagnostics.Start();
        CompositionTarget.Rendering += OnRendered;
        Loaded += async (_, _) =>
        {
            var hardware = await Task.Run(HardwareDetector.Detect);
            HardwareText.Text = $"CPU: {hardware.Cpu} · GPU: {string.Join(", ", hardware.Gpus)} · NPU候補: {string.Join(", ", hardware.NpuCandidates)}（MVP推論未対応）";
            if (Environment.GetCommandLineArgs().Contains("--soak"))
            {
                await StartAsync();
                for (int i = 0; i < 240 && pipeline is null && stop is not null; i++) await Task.Delay(250);
                var clock = Stopwatch.StartNew();
                long peakMemory = 0;
                while (clock.Elapsed.TotalSeconds < 1800 && pipeline is not null && stop is not null)
                {
                    using (var reader = new NAudio.Wave.WaveFileReader(Path.Combine(root, "artifacts", "test.wav")))
                    using (var output = new NAudio.Wave.WaveOutEvent())
                    {
                        output.Init(reader); output.Volume = 0.3f; output.Play();
                        await Task.Delay(10000);
                    }
                    using (var current = Process.GetCurrentProcess()) peakMemory = Math.Max(peakMemory, current.WorkingSet64);
                    File.WriteAllText(Path.Combine(root, "artifacts", "soak-progress.txt"), $"elapsed={clock.Elapsed.TotalSeconds:F0}s; translations={translatedUpdates}; maxRows={maximumRows}; UI peakMemory={peakMemory}; backend={BackendText.Text}");
                    await Task.Delay(50000);
                }
                bool passed = clock.Elapsed.TotalSeconds >= 1800 && translatedUpdates >= 30 && maximumRows <= settings.Rows;
                await StopAsync();
                File.WriteAllText(Path.Combine(root, "artifacts", "soak-result.txt"), $"PASS={passed}; duration={clock.Elapsed.TotalSeconds:F1}s; translatedUpdates={translatedUpdates}; maxRows={maximumRows}; UI peakMemory={peakMemory}; dropped={pipeline?.DroppedAudio}");
                closeReady = true; Close(); Environment.ExitCode = passed ? 0 : 1;
            }
            if (Environment.GetCommandLineArgs().Contains("--verify"))
            {
                bool storage = Environment.GetCommandLineArgs().Contains("--verify-storage");
                string? recordingTest = null;
                if (storage)
                {
                    Directory.CreateDirectory(Path.Combine(root, "artifacts"));
                    recordingTest = Path.Combine(root, "artifacts", "ui-recording.wav");
                    StartRecording(recordingTest);
                }
                await StartAsync();
                for (int i = 0; i < 480 && pipeline is null && stop is not null; i++) await Task.Delay(250);
                if (pipeline is not null)
                {
                    using var reader = new NAudio.Wave.WaveFileReader(Path.Combine(root, "artifacts", "test.wav"));
                    using var output = new NAudio.Wave.WaveOutEvent(); output.Init(reader); output.Play();
                    await Task.Delay(12000);
                }
                Directory.CreateDirectory(Path.Combine(root, "artifacts"));
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(this);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(root, "artifacts", "overlay.png"))) encoder.Save(file);
                bool subtitles = rows.Any(r => r.Japanese.Contains("テスト"));
                bool topmost = OverlayInterop.IsTopmost(this);
                int hit = OverlayInterop.ResizeHitTest(this);
                bool passed = subtitles && topmost && hit == 11;
                if (storage)
                {
                    passed &= recordingStop is not null && RecordButton.Content?.ToString() == "録音停止" && SaveTextButton.IsEnabled;
                    await File.WriteAllTextAsync(Path.Combine(root, "artifacts", "ui-transcript.txt"), transcript.Export(true), new UTF8Encoding(true));
                    await StopAsync(); // Caption Stop must leave independent recording active.
                    passed &= recordingStop is not null;
                    await StopRecordingAsync();
                    using var wave = new NAudio.Wave.WaveFileReader(recordingTest!);
                    passed &= wave.TotalTime.TotalSeconds > 10 && recordingStop is null && File.ReadAllText(Path.Combine(root, "artifacts", "ui-transcript.txt")).Contains("テスト");
                    File.WriteAllText(Path.Combine(root, "artifacts", "ui-storage-verification.txt"), $"PASS={passed}; recordingDuration={wave.TotalTime.TotalSeconds:F2}s; transcript saved; caption Stop leaves recording active; recording Stop finalizes WAV");
                }
                File.WriteAllText(Path.Combine(root, "artifacts", "ui-verification.txt"), $"Japanese fixture displayed: {subtitles}; topmost: {topmost}; resize right hit={hit} (expected 11); rows: {rows.Count}; status: {StatusText.Text}; backend: {BackendText.Text}");
                await StopAsync();
                if (stop is not null || speech is not null || translation is not null) passed = false;
                ClearRows();
                if (rows.Count != 0) passed = false;
                closeReady = true; Close();
                Environment.ExitCode = passed ? 0 : 1;
            }
            if (Environment.GetCommandLineArgs().Contains("--smoke"))
            {
                await StartAsync();
                await Task.Delay(3000);
                await StopAsync();
                closeReady = true; Close();
            }
        };
    }
    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "inference", "worker.py"))) return folder.FullName;
        return Directory.GetCurrentDirectory();
    }
    private void SetStatus(string text) => Dispatcher.BeginInvoke(() => StatusText.Text = text);
    private async void StartClick(object sender, RoutedEventArgs e) => await StartAsync();
    private async Task StartAsync()
    {
        if (stop is not null) return;
        ClearRows();
        stop = new(); StartButton.IsEnabled = false; StopButton.IsEnabled = true;
        StatusText.Text = "ローカルモデルを読み込み中…";
        running = RunAsync(stop.Token);
        await Task.Yield();
    }
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            foreach (string kind in new[] { "speech", "translation" })
                if (!File.Exists(Path.Combine(root, "models", kind, "model.bin"))) throw new InvalidOperationException("必要なモデルがインストールされていません。scripts/setup.ps1を実行してください。");
            speech = new(); translation = new(); speech.Status += SetStatus; translation.Status += SetStatus;
            using var initialization = CancellationTokenSource.CreateLinkedTokenSource(token);
            initialization.CancelAfter(TimeSpan.FromMinutes(2));
            bool cpu = settings.CpuOnly || Environment.GetCommandLineArgs().Contains("--cpu");
            await Task.WhenAll(speech.StartAsync(root, "speech", cpu, initialization.Token), translation.StartAsync(root, "translation", cpu, initialization.Token));
            BackendText.Text = $"Speech: {speech.Backend}   Translate: {translation.Backend}   ·   NPU: 未対応";
            pipeline = new Pipeline(speech, translation);
            pipeline.Status += SetStatus;
            pipeline.Caption += caption => Dispatcher.BeginInvoke(() => UpdateCaption(caption));
            StatusText.Text = "英語音声を待っています…";
            await Task.Run(() => pipeline.RunAsync(token), token);
        }
        catch (OperationCanceledException) { StatusText.Text = token.IsCancellationRequested ? "停止しました。" : "モデル初期化がタイムアウトしました。"; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally
        {
            speech?.Dispose(); translation?.Dispose(); speech = null; translation = null;
            stop?.Dispose(); stop = null; StartButton.IsEnabled = true; StopButton.IsEnabled = false;
        }
    }
    private async void StopClick(object sender, RoutedEventArgs e) => await StopAsync();
    private async Task StopAsync()
    {
        StopButton.IsEnabled = false;
        var cancellation = stop; var task = running;
        if (cancellation is not null) await cancellation.CancelAsync();
        if (task is not null) await task;
        StatusText.Text = "停止しました。";
    }
    private void UpdateCaption(CaptionUpdate update)
    {
        if (update.Started < clearedAt || stop?.IsCancellationRequested != false) return;
        transcript.Update(update);
        SaveTextButton.IsEnabled = transcript.Count > 0 && textSaveTask?.IsCompleted != false;
        var row = rows.FirstOrDefault(r => r.Id == update.Id);
        if (row is null)
        {
            if (update.Id < lastId || string.IsNullOrWhiteSpace(update.English)) return;
            row = new(update.Id); rows.Add(row); lastId = update.Id;
        }
        if (update.Revision < row.Revision) return;
        if (string.IsNullOrWhiteSpace(update.English)) { rows.Remove(row); return; }
        row.Revision = update.Revision; row.English = update.English;
        if (update.Japanese is not null) row.Japanese = update.Japanese;
        FormatRow(row); row.Refresh();
        while (rows.Count > settings.Rows) rows.RemoveAt(0);
        maximumRows = Math.Max(maximumRows, rows.Count);
        sttMs = update.SttMs;
        if (update.Japanese is not null)
        {
            translatedUpdates++;
            translationMs = update.TranslationMs; pendingRender = update.Started;
        }
        Scroll.ScrollToEnd();
    }
    private void OnRendered(object? sender, EventArgs e)
    {
        if (pendingRender == 0) return;
        totalMs = Stopwatch.GetElapsedTime(pendingRender).TotalMilliseconds; pendingRender = 0;
        Debug.WriteLine($"STT latency: {sttMs:F0} ms; Translation latency: {translationMs:F0} ms; Total latency: {totalMs:F0} ms");
    }
    private void FormatRow(CaptionRow row) { row.FontSize = settings.FontSize; row.ShowEnglish = settings.English; row.ShowJapanese = settings.Japanese; }
    private void ClearRows() { clearedAt = Stopwatch.GetTimestamp(); lastId = 0; rows.Clear(); transcript.Clear(); SaveTextButton.IsEnabled = false; }
    private void ClearClick(object sender, RoutedEventArgs e) => ClearRows();
    private async void SaveTextClick(object sender, RoutedEventArgs e)
    {
        if (transcript.Count == 0 || textSaveTask?.IsCompleted == false) return;
        var dialog = new SaveFileDialog
        {
            Title = "文字起こしを保存", FileName = $"Locaptio-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            InitialDirectory = Directory.Exists(settings.TextSaveDirectory) ? settings.TextSaveDirectory : "",
            Filter = "英語と日本語 (*.txt)|*.txt|英語のみ (*.txt)|*.txt", DefaultExt = ".txt", AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        string text = transcript.Export(dialog.FilterIndex == 1);
        SaveTextButton.IsEnabled = false;
        try { textSaveTask = File.WriteAllTextAsync(dialog.FileName, text, new UTF8Encoding(true)); await textSaveTask; StatusText.Text = "テキストを保存しました: " + dialog.FileName; }
        catch (Exception ex) { StatusText.Text = "テキスト保存に失敗しました: " + ex.Message; }
        finally { SaveTextButton.IsEnabled = transcript.Count > 0; }
    }
    private async void RecordClick(object sender, RoutedEventArgs e)
    {
        if (recordingStop is not null) { await StopRecordingAsync(); return; }
        var dialog = new SaveFileDialog
        {
            Title = "PC内部音声の録音先", FileName = $"Locaptio-{DateTime.Now:yyyyMMdd-HHmmss}.wav",
            InitialDirectory = Directory.Exists(settings.RecordingSaveDirectory) ? settings.RecordingSaveDirectory : "",
            Filter = "WAV音声 (*.wav)|*.wav", DefaultExt = ".wav", AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        StartRecording(dialog.FileName);
    }
    private void StartRecording(string path)
    {
        if (recordingStop is not null) return;
        recordingStop = new(); recordingPath = path; recordingStarted = Stopwatch.GetTimestamp();
        RecordButton.Content = "録音停止"; RecordingText.Visibility = Visibility.Visible;
        RecordingText.Text = "● 録音中 00:00:00 · " + path;
        recordingTask = RunRecordingAsync(path, recordingStop.Token);
    }
    private async Task RunRecordingAsync(string path, CancellationToken token)
    {
        var recorder = new LoopbackRecorder();
        recorder.Status += message => Dispatcher.BeginInvoke(() => { if (recordingStop is not null) RecordingText.ToolTip = message; });
        try
        {
            await Task.Run(() => recorder.RunAsync(path, token));
            StatusText.Text = $"録音を保存しました: {path}" + (recorder.DroppedFrames > 0 ? $"（音声取りこぼし: {recorder.DroppedFrames}）" : "");
        }
        catch (Exception ex) { StatusText.Text = "録音に失敗しました: " + ex.Message; }
        finally
        {
            recordingStop?.Dispose(); recordingStop = null; recordingPath = null;
            RecordButton.Content = "録音開始"; RecordButton.IsEnabled = true; RecordingText.Visibility = Visibility.Collapsed;
        }
    }
    private async Task StopRecordingAsync()
    {
        RecordButton.IsEnabled = false;
        var cancellation = recordingStop; var task = recordingTask;
        if (cancellation is not null) await cancellation.CancelAsync();
        if (task is not null) await task;
        RecordButton.IsEnabled = true;
    }
    private void DragWindow(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is TextBlock or Ellipse or Grid or StackPanel) DragMove(); }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(settings, ApplySettings) { Owner = this }; dialog.ShowDialog();
    }
    private void ApplySettings()
    {
        settings.FontSize = Math.Clamp(settings.FontSize, 14, 40); settings.Rows = Math.Clamp(settings.Rows, 1, 30);
        settings.BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0.25, 1);
        if (!settings.English && !settings.Japanese) settings.Japanese = true;
        Panel.Background = new SolidColorBrush(Color.FromArgb((byte)(settings.BackgroundOpacity * 255), 20, 26, 36));
        EnglishHeaderColumn.Width = settings.English ? new(1, GridUnitType.Star) : new(0);
        JapaneseHeaderColumn.Width = settings.Japanese ? new(1, GridUnitType.Star) : new(0);
        EnglishHeader.Visibility = settings.English ? Visibility.Visible : Visibility.Collapsed;
        JapaneseHeader.Visibility = settings.Japanese ? Visibility.Visible : Visibility.Collapsed;
        MetricsText.Visibility = settings.Diagnostics ? Visibility.Visible : Visibility.Collapsed;
        HardwareText.Visibility = settings.Diagnostics ? Visibility.Visible : Visibility.Collapsed;
        foreach (var row in rows) { FormatRow(row); row.Refresh(); }
        while (rows.Count > settings.Rows) rows.RemoveAt(0);
        settings.Save();
    }
    private void UpdateDiagnostics()
    {
        if (recordingStop is not null) RecordingText.Text = $"● 録音中 {Stopwatch.GetElapsedTime(recordingStarted):hh\\:mm\\:ss} · {recordingPath}";
        double cpu = 0;
        using (var current = Process.GetCurrentProcess()) cpu = current.TotalProcessorTime.TotalMilliseconds;
        cpu += speech?.CpuMilliseconds ?? 0;
        cpu += translation?.CpuMilliseconds ?? 0;
        var currentCpu = TimeSpan.FromMilliseconds(cpu); long now = Stopwatch.GetTimestamp();
        double percent = previousClock == 0 ? 0 : Math.Max(0, (currentCpu - previousCpu).TotalMilliseconds / Stopwatch.GetElapsedTime(previousClock, now).TotalMilliseconds / Environment.ProcessorCount * 100);
        previousCpu = currentCpu; previousClock = now;
        MetricsText.Text = $"CPU: {percent:F1}%  ·  Audio buffer: 約{pipeline?.BufferedAudioMs ?? 0}ms / 2s  ·  dropped: {pipeline?.DroppedAudio ?? 0}  ·  STT: {sttMs:F0}ms  ·  Translate: {translationMs:F0}ms  ·  Total: {totalMs:F0}ms\nEP候補: {translation?.AvailableProviders ?? "—"}（列挙≠モデル対応）";
        if (speech is not null && translation is not null) BackendText.Text = $"Speech: {speech.Backend}   Translate: {translation.Backend}   ·   NPU: 未対応";
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendered;
        if (closeReady) return;
        e.Cancel = true;
        if (closing) return;
        closing = true; diagnostics.Stop(); Hide();
        // Leave the synchronous Closing event before calling Close again, including when idle.
        await Dispatcher.Yield(DispatcherPriority.Background);
        var shutdown = Stopwatch.StartNew();
        try
        {
            await Task.WhenAll(StopAsync(), StopRecordingAsync());
            if (textSaveTask is not null) { try { await textSaveTask; } catch { /* Save handler reports the error. */ } }
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            settings.Left = bounds.Left; settings.Top = bounds.Top; settings.Width = bounds.Width; settings.Height = bounds.Height; settings.Save();
            if (Environment.GetCommandLineArgs().Contains("--verify-lifecycle"))
            {
                using var wave = new NAudio.Wave.WaveFileReader(Path.Combine(root, "artifacts", "wpf-shutdown.wav"));
                bool passed = lifecycleMinimized && shutdown.Elapsed.TotalSeconds < 5 && wave.TotalTime.TotalSeconds > .5;
                File.WriteAllText(Path.Combine(root, "artifacts", "wpf-lifecycle-verification.txt"), $"PASS={passed}; minimized/restored={lifecycleMinimized}; cleanupMs={shutdown.Elapsed.TotalMilliseconds:F0}; WAV={wave.TotalTime.TotalSeconds:F2}s; repeated close");
                Environment.ExitCode = passed ? 0 : 1;
            }
        }
        finally { closeReady = true; Close(); }
    }
}
