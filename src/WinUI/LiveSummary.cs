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
    /// <summary>"So far": older blocks condensed again, so a long session stays readable at a glance.</summary>
    private LiveSummaryBlock? liveDigest;
    private readonly List<LiveSummaryBlock> liveArchived = new();
    /// <summary>The newest blocks stay as they are; older ones are folded into <see cref="liveDigest"/>.</summary>
    private const int LiveRecentBlocks = 3;
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

    private void ResetLiveSummary() { liveBlocks.Clear(); liveArchived.Clear(); liveDigest = null; liveSummaryNextId = 0; }
    private void LiveSummaryClick(object sender, RoutedEventArgs e) => SetLiveSummary(LiveSummaryMenuItem.IsChecked);
    private void LiveSummaryToggleClick(object sender, RoutedEventArgs e) => SetLiveSummary(LiveSummaryToggle.IsChecked == true);
    private void SetLiveSummary(bool on)
    {
        settings.LiveSummary = on; settings.Save(); LiveSummaryMenuItem.IsChecked = on; LiveSummaryToggle.IsChecked = on; liveSummaryNoModelShown = false;
        if (!on) { Status("ライブ要約をOFFにしました。作成済みの要約は残ります。"); return; }
        // Turned on mid-session (also while recording): what was said before is caught up, block by block.
        if (liveBlocks.Count == 0 && liveDigest is null) liveSummaryNextId = history.FirstOrDefault()?.Id ?? 0;
        if (summaryItems.Length == 0) { ShowSummaryPane(true); RenderLiveSummary(); }
        Status("ライブ要約をONにしました。録音・文字起こし中、数分ごとの発話を要約して右の欄に追加します（これまでの発話もさかのぼって要約します）。");
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
            if (items.Length > 0) { liveBlocks.Add(new(rows[0].Id, rows[^1].Id, items)); sessionDirty = true; }
            if (liveBlocks.Count > LiveRecentBlocks && !closing) await CondenseLiveSummaryAsync(backend, timeout.Token);
            if (closing) return;
            if (SummaryPane.Visibility != Visibility.Visible) ShowSummaryPane(true);
            RenderLiveSummary();
        }
        catch (Exception ex) when (!closing) { Status("ライブ要約を作成できませんでした（次のまとまりで再試行します）: " + ex.Message); }
        finally { liveSummaryRunning = false; }
    }

    /// <summary>Folds the blocks older than the newest few, together with the previous digest, into one "so far" summary.</summary>
    private async Task CondenseLiveSummaryAsync(ISummaryBackend backend, CancellationToken token)
    {
        var older = liveBlocks.Take(liveBlocks.Count - LiveRecentBlocks).ToList();
        // Each earlier summary line is one numbered input; its first evidence row keeps the link to what was said.
        var input = (liveDigest?.Items ?? []).Concat(older.SelectMany(b => b.Items))
            .Select(i => (Id: i.RowIds.FirstOrDefault(), Text: (i.Section == "要点" ? "" : "【" + i.Section + "】") + i.Text)).ToArray();
        var items = await Task.Run(() => TranscriptSummary.RunAsync(backend, input, null, token), token);
        if (items.Length == 0) return; // keep the blocks rather than lose them
        liveDigest = new(liveDigest?.FirstId ?? older[0].FirstId, older[^1].LastId, items);
        liveArchived.AddRange(older);
        liveBlocks.RemoveRange(0, older.Count);
    }

    private StackPanel LiveSummaryCard(LiveSummaryBlock block, string? heading = null)
    {
        var card = new StackPanel { Spacing = 4, Padding = new Thickness(8, 6, 8, 8), CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(28, 128, 128, 128)) };
        if (heading is not null) card.Children.Add(new TextBlock { Text = heading, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = .85 });
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
        return card;
    }

    /// <summary>
    /// 「今までを要約」 while transcribing or recording: turns live summary on if needed and summarizes every
    /// utterance not yet covered, block by block, without stopping. Older blocks are condensed as usual.
    /// </summary>
    private async Task SummarizeSoFarAsync()
    {
        if (!settings.LiveSummary) SetLiveSummary(true);
        ShowSummaryPane(true);
        SummaryRunButton.IsEnabled = false;
        try
        {
            for (int round = 0; round < 50 && !closing; round++)
            {
                while (liveSummaryRunning && !closing) await Task.Delay(200);
                long before = liveSummaryNextId; int blocks = liveBlocks.Count + liveArchived.Count;
                bool pending = history.Any(r => r.Id >= liveSummaryNextId && r.Caption.Final && r.English.Trim().Length > 0);
                if (!pending) break;
                Status("ライブ要約：ここまでの発話を要約しています…");
                await LiveSummaryTickAsync(flush: true);
                if (liveSummaryNextId == before && liveBlocks.Count + liveArchived.Count == blocks) break; // nothing more could be summarized (too short, no model or an error)
            }
            RenderLiveSummary();
            if (liveBlocks.Count + liveArchived.Count > 0) Status("ここまでの発話を要約しました。文字起こしは続いています。以降も数分ごとに自動で追加します。");
        }
        finally { SummaryRunButton.IsEnabled = true; }
    }

    /// <summary>The summary pane for the crash-recovery snapshot; null when there is nothing to keep.</summary>
    private AutosaveSummary? SummarySnapshot()
    {
        if (summaryItems.Length == 0 && liveBlocks.Count == 0 && liveDigest is null) return null;
        static AutosaveSummaryBlock Block(LiveSummaryBlock b) => new(b.FirstId, b.LastId, b.Items);
        return new(summaryItems, liveBlocks.Select(Block).ToArray(), liveArchived.Select(Block).ToArray(), liveDigest is null ? null : Block(liveDigest), liveSummaryNextId);
    }
    /// <summary>Puts a saved summary pane back after 「前回の作業を復元」; <paramref name="ids"/> maps saved row IDs to the restored ones.</summary>
    private void RestoreSummarySnapshot(AutosaveSummary saved, IReadOnlyDictionary<long, long> ids)
    {
        long Map(long id) => ids.TryGetValue(id, out long mapped) ? mapped : 0;
        SummaryItem Item(SummaryItem i) => i with { RowIds = (i.RowIds ?? []).Select(Map).Where(id => id != 0).ToArray() };
        LiveSummaryBlock? Block(AutosaveSummaryBlock? b) => b is null ? null : new(Map(b.FirstId), Map(b.LastId), (b.Items ?? []).Select(Item).ToArray());
        summaryItems = (saved.Full ?? []).Select(Item).ToArray();
        liveBlocks.Clear(); liveBlocks.AddRange((saved.Live ?? []).Select(b => Block(b)!));
        liveArchived.Clear(); liveArchived.AddRange((saved.Archived ?? []).Select(b => Block(b)!));
        liveDigest = Block(saved.Digest);
        // Live summary continues with the first row it had not covered (or after the restored rows).
        long next = ids.Where(p => p.Key >= saved.NextId).OrderBy(p => p.Key).Select(p => p.Value).FirstOrDefault();
        liveSummaryNextId = next != 0 ? next : (history.LastOrDefault()?.Id ?? 0) + 1;
        if (summaryItems.Length > 0 || liveBlocks.Count > 0 || liveDigest is not null) { ShowSummaryPane(true); RenderSummary(); }
    }

    private void RenderLiveSummary()
    {
        if (summaryItems.Length > 0) { RenderSummary(); return; } // a full summary shows the live summary below it
        bool listening = captionStop is not null || recordingStop is not null;
        SummaryRunButton.Content = listening ? "今までを要約" : "作成";
        SummaryContent.Children.Clear();
        SummaryContent.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .8,
            Text = $"ライブ要約：約{LiveBlockTime.TotalMinutes:0.#}分（または約{LiveBlockChars}文字）ごとの発話のまとまりを、ローカルAIが自動で要約します。新しいまとまりが上です。長くなると古いまとまりは「ここまでの流れ」にまとめ直します。" + (listening ? " 「今までを要約」を押すと、次のまとまりを待たずにここまでの発話を要約します。" : " 全体の要約は「作成」を押してください。") });
        if (liveBlocks.Count == 0 && liveDigest is null) { SummaryContent.Children.Add(new TextBlock { Text = "最初のまとまりを待っています…", FontSize = 12, Opacity = .65 }); return; }
        AddLiveSummaryCards();
    }

    /// <summary>Live blocks (newest first), the "so far" digest and the folded originals.</summary>
    private void AddLiveSummaryCards()
    {
        foreach (var block in Enumerable.Reverse(liveBlocks)) SummaryContent.Children.Add(LiveSummaryCard(block));
        if (liveDigest is null) return;
        SummaryContent.Children.Add(LiveSummaryCard(liveDigest, $"ここまでの流れ（古い{liveArchived.Count}まとまりをまとめ直し）"));
        var details = new StackPanel { Spacing = 6 };
        foreach (var block in Enumerable.Reverse(liveArchived)) details.Children.Add(LiveSummaryCard(block));
        SummaryContent.Children.Add(new Expander { Header = "まとめ直す前の要約を表示", Content = details, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
    }
}
