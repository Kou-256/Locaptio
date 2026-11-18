using System.Diagnostics;
using Locaptio.Core;
using Locaptio.Core.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Locaptio.WinUI;

/// <summary>
/// Live summary (setting "LiveSummary"): while transcribing, every few minutes of speech is summarized
/// by the local AI as one block (key points, decisions, TODOs) and added to the summary pane, so the
/// conversation can be followed in larger pieces than single utterances. Nothing is downloaded for it.
/// </summary>
public sealed partial class MainWindow
{
    private sealed record LiveSummaryBlock(long FirstId, long LastId, SummaryItem[] Items);
    private readonly List<LiveSummaryBlock> liveBlocks = new();
    private readonly DispatcherTimer liveSummaryTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private long liveSummaryNextId;
    private bool liveSummaryRunning, liveSummaryNoModelShown;
    /// <summary>Self-checks summarize with a stand-in instead of the local LLM.</summary>
    private ISummaryBackend? liveSummaryBackendForVerification;
    /// <summary>A block is summarized once it has this much text, or after this long with at least the minimum.</summary>
    private const int LiveBlockChars = 1500, LiveMinimumChars = 300;
    private static readonly TimeSpan LiveBlockTime = TimeSpan.FromMinutes(2.5);

    private void InitializeLiveSummary()
    {
        liveSummaryTimer.Tick += async (_, _) => await LiveSummaryTickAsync();
        liveSummaryTimer.Start();
        Closed += (_, _) => liveSummaryTimer.Stop();
    }

    private void ResetLiveSummary() { liveBlocks.Clear(); liveSummaryNextId = 0; }
    private void LiveSummaryClick(object sender, RoutedEventArgs e) => SetLiveSummary(LiveSummaryMenuItem.IsChecked);
    private void SetLiveSummary(bool on)
    {
        settings.LiveSummary = on; settings.Save(); LiveSummaryMenuItem.IsChecked = on; liveSummaryNoModelShown = false;
        if (!on) { Status("ライブ要約をOFFにしました。"); return; }
        // Speech already on screen counts from now on; earlier text is covered by the full summary.
        if (captionStop is not null && liveBlocks.Count == 0) liveSummaryNextId = (history.LastOrDefault()?.Id ?? 0) + 1;
        if (summaryItems.Length == 0) { ShowSummaryPane(true); RenderLiveSummary(); }
        Status("ライブ要約をONにしました。文字起こし中、数分ごとの発話のまとまりを要約して右の欄に追加します。");
    }

    /// <param name="flush">Summarize what is left even if the block is short (when listening stops).</param>
    private bool liveWasListening;
    private async Task LiveSummaryTickAsync(bool flush = false)
    {
        // When listening stops, the last, shorter block is summarized too.
        if (captionStop is not null) liveWasListening = true;
        else if (liveWasListening) { liveWasListening = false; flush = true; }
        if (!settings.LiveSummary || liveSummaryRunning || closing || exclusiveTask || (captionStop is null && !flush)) return;
        var rows = history.Where(r => r.Id >= liveSummaryNextId && r.Caption.Final && r.English.Trim().Length > 0).ToList();
        int chars = rows.Sum(r => r.English.Length);
        if (chars < (flush ? 80 : LiveMinimumChars)) return;
        bool old = rows.Count > 0 && Stopwatch.GetElapsedTime(rows[0].Caption.Started) >= LiveBlockTime;
        if (!flush && chars < LiveBlockChars && !old) return;
        // One block stays within what the model reads at once.
        int taken = 0, total = 0;
        while (taken < rows.Count && (taken == 0 || total + rows[taken].English.Length <= 2400)) total += rows[taken++].English.Length;
        rows = rows.Take(taken).ToList();
        root = FindRoot();
        if (liveSummaryBackendForVerification is null && (root.Length == 0 || LocalLlmPreparation.Required(root, LlmModelFile)))
        {
            if (!liveSummaryNoModelShown) Status("ライブ要約にはローカルAIのモデルが必要です。設定の「翻訳・ローカルLLM」でダウンロードしてください。");
            liveSummaryNoModelShown = true; return;
        }
        liveSummaryRunning = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var input = rows.Select(r => (r.Id, r.English)).ToArray();
            ISummaryBackend backend = liveSummaryBackendForVerification ?? await SummaryLlmAsync(timeout.Token);
            var items = await Task.Run(() => TranscriptSummary.RunAsync(backend, input, null, timeout.Token), timeout.Token);
            toolLlmUsed = Stopwatch.GetTimestamp();
            liveSummaryNextId = rows[^1].Id + 1;
            if (items.Length > 0) liveBlocks.Add(new(rows[0].Id, rows[^1].Id, items));
            if (closing) return;
            if (SummaryPane.Visibility != Visibility.Visible) ShowSummaryPane(true);
            RenderLiveSummary();
        }
        catch (Exception ex) when (!closing) { Status("ライブ要約を作成できませんでした（次のまとまりで再試行します）: " + ex.Message); }
        finally { liveSummaryRunning = false; }
    }

    private void RenderLiveSummary()
    {
        if (summaryItems.Length > 0) return; // a full summary made afterwards takes the pane
        SummaryContent.Children.Clear();
        SummaryContent.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8,
            Text = $"ライブ要約：約{LiveBlockTime.TotalMinutes:0.#}分（または約{LiveBlockChars}文字）ごとの発話のまとまりを、ローカルAIが自動で要約します。新しいまとまりが上です。" + (captionStop is null ? " 全体の要約は「作成」を押してください。" : "") });
        if (liveBlocks.Count == 0) { SummaryContent.Children.Add(new TextBlock { Text = "最初のまとまりを待っています…", FontSize = 12, Opacity = .65 }); return; }
        foreach (var block in Enumerable.Reverse(liveBlocks))
        {
            var card = new StackPanel { Spacing = 4, Padding = new Thickness(8, 6, 8, 8), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(28, 128, 128, 128)) };
            var range = new HyperlinkButton { Content = $"▶ {RowTime(block.FirstId)} – {RowTime(block.LastId)}", FontSize = 12, Padding = new Thickness(0), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            long first = block.FirstId; range.Click += (_, _) => JumpToRow(first, true);
            card.Children.Add(range);
            foreach (var item in block.Items.OrderBy(i => Array.IndexOf(TranscriptSummary.Sections, i.Section)))
            {
                string mark = item.Section switch { "決定事項" => "【決定】", "TODO" => "【TODO】", _ => "・" };
                var text = new TextBlock { Text = mark + item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13, IsTextSelectionEnabled = true };
                if (item.RowIds.FirstOrDefault(id => FindCaption(id) is not null) is long evidence and > 0)
                    ToolTipService.SetToolTip(text, RowTime(evidence) + "　" + FindCaption(evidence)!.English);
                card.Children.Add(text);
            }
            SummaryContent.Children.Add(card);
        }
    }
}
