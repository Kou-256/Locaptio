using Locaptio.Core;
using Locaptio.Core.AI;
using Locaptio.Core.Audio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace Locaptio.WinUI;

public sealed partial class MainWindow
{
    private async void ImportAudioClick(object sender, RoutedEventArgs e) => await ImportAudioPathAsync(null);
    private void AudioDragOver(object sender, DragEventArgs e)
    {
        if (!dialogOpen && !closing && e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "音声・動画ファイルを文字起こし（MP3 / M4A / WAV / MP4 / MOV など）";
        }
        e.Handled = true;
    }
    private async void AudioDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (dialogOpen || closing) return;
        try
        {
            var files = (await e.DataView.GetStorageItemsAsync()).OfType<Windows.Storage.StorageFile>().ToArray();
            if (files.Length != 1 || !PlaybackClock.AudioExtensions.Concat(PlaybackClock.VideoExtensions).Contains(Path.GetExtension(files[0].Path).ToLowerInvariant()))
            { Status("音声（MP3・M4A・WAV）か動画（MP4・MOV・M4V・WMV・AVI・MKV）のファイルを1つずつドロップしてください。"); ShowStatusDetails(pin: true); return; }
            await ImportAudioPathAsync(files[0].Path);
        }
        catch (Exception ex) { Status("ドロップしたファイルを読み込めません: " + ex.Message); ShowStatusDetails(pin: true); }
    }
    private async Task ImportAudioPathAsync(string? source)
    {
        if (dialogOpen || closing) return;
        if (modelPreparationStop is not null) { Status("モデル準備が完了するか、中止してから読み込んでください。"); return; }
        dialogOpen = true; StartButton.IsEnabled = RecordToggle.IsEnabled = ModeSelector.IsEnabled = AudioSelector.IsEnabled = false;
        try
        {
            if (source is null)
            {
                var picker = new FileOpenPicker(AppWindow.Id) { Title = "音声・動画ファイルから文字起こし（原本は変更しません）" };
                foreach (string extension in PlaybackClock.AudioExtensions.Concat(PlaybackClock.VideoExtensions)) picker.FileTypeFilter.Add(extension);
                var selected = await picker.PickSingleFileAsync(); if (selected is null) return;
                source = selected.Path;
            }
            using (var check = new NAudio.Wave.AudioFileReader(source)) { if (check.TotalTime <= TimeSpan.Zero) throw new InvalidDataException("音声が空です。"); }
            if (!await ConfirmTranscriptReplacementAsync("音声ファイルから文字起こしを開始します。")) return;
            await StopAllAsync();
            prepared?.Dispose(); prepared = null;
            root = FindRoot(); if (root.Length == 0) throw new InvalidOperationException("設定で推論環境のフォルダーを選択してください。");
            ClosePlayback();
            captionStop = new(); StopButton.IsEnabled = true;
            captionTask = ImportAudioAsync(source, captionStop.Token);
            await captionTask;
        }
        catch (Exception ex) { Status("音声ファイルを読み込めません: " + ex.Message); ShowStatusDetails(pin: true); }
        finally
        {
            dialogOpen = false; StartButton.IsEnabled = !closing && captionStop is null;
            RecordToggle.IsEnabled = ModeSelector.IsEnabled = AudioSelector.IsEnabled = !closing;
        }
    }
    private async Task ImportAudioAsync(string source, CancellationToken token)
    {
        await Task.Yield();
        string? audio = null; bool copied = false; List<RecordingPhrase> phrases = new();
        var recordedAt = DateTimeOffset.Now; bool japanese = SpeechJapanese; var route = Route is { } chosen ? TranslationPacks.Route(chosen.Source, chosen.Target, root) : null;
        RecordingDocument Document() => new(1, Path.GetFileName(audio!), recordedAt, true, phrases.ToArray(), Model: settings.SpeechModel);
        try
        {
            SessionTitle.Text = "ファイル文字起こし：" + Path.GetFileName(source) + "（Stopで中止）";
            // Validate the codec before downloading models or replacing the visible transcript.
            using (var check = new NAudio.Wave.AudioFileReader(source)) { if (check.TotalTime <= TimeSpan.Zero) throw new InvalidDataException("音声が空です。"); }
            bool cpu = settings.CpuOnly || settings.InferenceDevice == "cpu";
            if (MissingTranslationPack(root) is { } missing) throw new InvalidOperationException(missing);
            await SpeechPreparation.EnsureAsync(root, settings.SpeechModel, route is { BuiltIn: true }, cpu ? "cpu" : settings.InferenceDevice, BeginPreparation(), token);
            using var worker = new PreparedInference(root, japanese, settings.SpeechModel, Terms.SpeechPrompt(settings.SpeechPrompt), settings.InferenceDevice, cpu, false,
                settings.CorrectTranscriptWithAi, LlmModelFile, Status, progress: BeginPreparation(), route: route);
            worker.Speech.RequestTimeout = TimeSpan.FromMinutes(5);
            if (worker.Translation is not null) worker.Translation.RequestTimeout = TimeSpan.FromMinutes(5);
            await worker.Ready.WaitAsync(token);
            ISpeechCorrectionBackend? correction = worker.Llm is not null && worker.LlmReady is not null ? new ReadyLocalLlm(worker.Llm, worker.LlmReady) : null;
            if (worker.Llm is not null) worker.Llm.CorrectionGlossary = Terms.CorrectionHint();
            var dictionary = Terms;
            audio = RecordingLibrary.CreateSessionPath(Path.Combine(RecordingRoot, Path.GetFileName(source)));
            await using (var input = File.OpenRead(source))
            await using (var output = new FileStream(audio, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output, token);
            copied = true;
            await RecordingTranscript.SaveAsync(audio, Document());
            ClearCaptions(); reviewingRecording = false; playbackDocument = null;
            PlaybackPanel.Visibility = Visibility.Collapsed;
            DateTime nextSave = DateTime.UtcNow;
            var progress = new Progress<double>(percent => { if (!closing) StatusText.Text = $"ファイル文字起こし：{percent:F0}%（Stopで中止・認識済み部分は保存）"; });
            await Task.Run(() => FileTranscription.RunAsync(audio, worker.Speech, japanese ? "ja" : "en", async phrase =>
            {
                phrases.Add(phrase);
                var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        if (!closing)
                        {
                            var caption = new CaptionUpdate(phrase.Id, 1, phrase.Text, phrase.Translation, true, phrase.Started, 0, 0, phrase.Language);
                            edits.Restore(phrase.Id, phrase.Original, false);
                            UpdateHistory(caption); transcript.Update(caption); RenderCaptions(); SaveButton.IsEnabled = true;
                        }
                        shown.SetResult();
                    }
                    catch (Exception ex) { shown.SetException(ex); }
                })) throw new OperationCanceledException(token);
                await shown.Task;
                if (DateTime.UtcNow >= nextSave) { await RecordingTranscript.SaveAsync(audio, Document()); nextSave = DateTime.UtcNow.AddSeconds(5); }
            }, value => ((IProgress<double>)progress).Report(value), token, worker.Translation, correction, dictionary.Apply), token);
            StatusText.Text = "ファイルの文字起こしが完了しました。録音一覧から検索・再生できます。";
        }
        catch (OperationCanceledException) { StatusText.Text = "ファイル文字起こしを中止しました。認識済みの文章は保存します。"; }
        catch (Exception ex) { StatusText.Text = "ファイル文字起こしに失敗しました: " + ex.Message; string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Locaptio", "logs", "file-import-error.txt");
            try { Directory.CreateDirectory(Path.GetDirectoryName(log)!); File.WriteAllText(log, DateTimeOffset.Now + "\n" + ex); } catch (IOException) { }
            if (Environment.GetCommandLineArgs().Contains("--verify-file-import")) File.WriteAllText(Path.Combine(root, "artifacts", "file-import-failure.txt"), ex.ToString()); ShowStatusDetails(pin: true); }
        finally
        {
            if (audio is not null)
            {
                try
                {
                    if (copied)
                    {
                        await RecordingTranscript.SaveAsync(audio, Document()); await RecordingLibrary.SaveTextAsync(audio, Document());
                        if (!closing) await LoadRecordingAsync(audio);
                    }
                    else if (File.Exists(audio)) File.Delete(audio);
                }
                catch (Exception ex) { Status("読み込みデータを保存できません: " + ex.Message); }
            }
            preparationEpoch++; preparationVisible = false; PreparationPanel.Visibility = Visibility.Collapsed;
            captionStop?.Dispose(); captionStop = null;
            StartButton.IsEnabled = !closing; StopButton.IsEnabled = false;
        }
    }
}
