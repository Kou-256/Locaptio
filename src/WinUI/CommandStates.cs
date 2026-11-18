using Locaptio.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Locaptio.WinUI;

// Which commands can be used right now, bookmark colors, and captions while replaying a recording.
public sealed partial class MainWindow
{
    private bool exclusiveTask;
    private readonly DispatcherTimer commandTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private int playbackCaptionIndex = -2;

    private void InitializeCommandStates()
    {
        // Colored icons (record, bookmark) keep their color when a button is disabled, so a disabled
        // control would still look usable. Dim the whole control instead.
        void Dim(Control control)
        {
            control.Opacity = control.IsEnabled ? 1 : .4;
            control.IsEnabledChanged += (_, _) => control.Opacity = control.IsEnabled ? 1 : .4;
        }
        foreach (var panel in new Panel[] { MainToolbar, RecordingTools, VideoControls })
            foreach (var control in panel.Children.OfType<Control>()) Dim(control);
        commandTimer.Tick += (_, _) => UpdateCommandStates(); commandTimer.Start();
        Closed += (_, _) => commandTimer.Stop();
        UpdateCommandStates();
    }

    /// <summary>One place for the rules, so a command is never shown as available when it would do nothing.</summary>
    private void UpdateCommandStates()
    {
        if (closing) return;
        bool busy = exclusiveTask || dialogOpen || stoppingAll || modelPreparationStop is not null;
        bool text = history.Count > 0, recording = recordingStop is not null, live = captionStop is not null && !exclusiveTask;
        bool review = reviewingRecording && playbackDocument is not null && !recording && !live && File.Exists(playbackPath);
        void Set(bool enabled, params object[] items)
        {
            foreach (var item in items)
                if (item is Control control) { if (control.IsEnabled != enabled) control.IsEnabled = enabled; control.Opacity = enabled ? 1 : .4; }
                else if (item is MenuFlyoutItemBase menu && menu.IsEnabled != enabled) menu.IsEnabled = enabled;
        }
        Set(!busy && (recording || text), BookmarkButton, BookmarkMenuItem);
        // Clear also closes a recording open for review, even one without text.
        Set(!busy && (text || review), ClearButton, ClearMenuItem);
        Set(!busy && !recording && !live && (text || review || SummaryPane.Visibility == Visibility.Visible), HomeButton, HomeMenuItem);
        Set(!busy && text, SubtitlesMenuItem, MoreSubtitlesItem, SubtitlesToolButton, CopyMenuItem, MoreCopyItem, ChangesMenuItem, MoreChangesItem, EditMenuItem);
        // The summary reads a finished transcript; during listening or recording the text is still changing.
        Set(!busy && text && !live && !recording, SummaryMenuItem, MoreSummaryItem, SummaryToolButton);
        Set(!busy && review, RetranscribeButton, RetranscribeMenuItem, SyncButton, SyncMenuItem, VersionsButton, VersionsMenuItem);
        Set(!busy, LibraryButton, ImportButton, MoreButton);
        Set(!busy && (recording || playbackDocument is not null), SearchButton);
        Set(!exclusiveTask, CaptionButton);
        // Audio and video recording exclude each other; each button stops its own recording.
        Set(!busy && (recordingStop is null || VideoRecording), VideoToggle);
        if (VideoRecording) Set(false, RecordToggle);
        bool video = review && PlaybackClock.IsVideo(playbackPath);
        var burn = video ? Visibility.Visible : Visibility.Collapsed;
        // The custom toolbar panel lays out again only when told; otherwise a newly shown button gets no space.
        if (BurnSubtitlesButton.Visibility != burn) { BurnSubtitlesButton.Visibility = burn; RecordingTools.InvalidateMeasure(); RecordingTools.InvalidateArrange(); }
        Set(!busy && video, BurnSubtitlesButton, BurnSubtitlesMenuItem);
    }

    // ---- Bookmark colors ------------------------------------------------------------------

    private static readonly string[] BookmarkPalette = ["#F5B942", "#60A5FA", "#C084FC", "#34D399", "#F87171", "#FB923C", "#F472B6", "#A3E635"];
    private string BookmarkColor(string label)
    {
        if (settings.BookmarkColors?.TryGetValue(label, out string? color) == true && CaptionWindow.ParseColor(color, 0x1000000) <= 0xFFFFFF) return color;
        int index = Array.IndexOf(BookmarkLabels, label);
        return BookmarkPalette[(index < 0 ? Math.Abs(label.GetHashCode()) : index) % BookmarkPalette.Length];
    }
    private SolidColorBrush BookmarkBrush(string label)
    {
        uint rgb = CaptionWindow.ParseColor(BookmarkColor(label), 0xF5B942);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }
    private FontIcon BookmarkIcon(string label, double size = 14) => new() { Glyph = "", FontSize = size, Foreground = BookmarkBrush(label) };

    /// <summary>Settings rows: a color swatch, the label and a delete button for each bookmark type.</summary>
    private void AddBookmarkTypeSettings(StackPanel content)
    {
        content.Children.Add(new TextBlock { Text = "ブックマークの種類と色（いちばん上がワンクリックとCtrl+Bで付く種類）" });
        var rows = new StackPanel { Spacing = 6 };
        void Store()
        {
            var entries = rows.Children.OfType<Grid>().Select(r => (Label: ((TextBox)r.Children[1]).Text.Trim().Replace("、", "").Replace(",", ""), Color: (string)((Button)r.Children[0]).Tag))
                .Where(e => e.Label.Length is > 0 and <= 20).GroupBy(e => e.Label).Select(g => g.First()).Take(8).ToArray();
            if (entries.Length == 0) return;
            settings.BookmarkLabels = string.Join("、", entries.Select(e => e.Label));
            settings.BookmarkColors = entries.ToDictionary(e => e.Label, e => e.Color);
            settings.Save(); RefreshBookmarkLabels(); RefreshBookmarks();
        }
        void AddRow(string label, string color)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var swatch = new Button { Tag = color, Width = 40, Height = 32, Padding = new Thickness(0), Content = new FontIcon { Glyph = "", FontSize = 16 } };
            void Paint() { uint rgb = CaptionWindow.ParseColor((string)swatch.Tag, 0xF5B942); ((FontIcon)swatch.Content).Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)); }
            Paint();
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, "ブックマークの色");
            var palette = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, MaxWidth = 200 };
            foreach (string preset in BookmarkPalette)
            {
                uint rgb = CaptionWindow.ParseColor(preset, 0);
                var cell = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(6), Tag = preset, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cell, preset);
                palette.Items.Add(cell);
            }
            var flyout = new Flyout { Content = palette };
            palette.ItemClick += (_, e) => { swatch.Tag = (string)((Border)e.ClickedItem).Tag; Paint(); flyout.Hide(); Store(); };
            swatch.Flyout = flyout;
            var name = new TextBox { Text = label, MaxLength = 20, PlaceholderText = "例: 重要" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(name, "ブックマークの種類");
            name.TextChanged += (_, _) => Store();
            var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 13 }, Padding = new Thickness(8, 6, 8, 6) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, "この種類を削除");
            remove.Click += (_, _) => { if (rows.Children.Count > 1) { rows.Children.Remove(row); Store(); } };
            Grid.SetColumn(name, 1); Grid.SetColumn(remove, 2);
            row.Children.Add(swatch); row.Children.Add(name); row.Children.Add(remove); rows.Children.Add(row);
        }
        foreach (string label in BookmarkLabels) AddRow(label, BookmarkColor(label));
        var add = new Button { Content = "＋ 種類を追加" };
        add.Click += (_, _) => { if (rows.Children.Count < 8) AddRow("", BookmarkPalette[rows.Children.Count % BookmarkPalette.Length]); };
        content.Children.Add(rows); content.Children.Add(add);
        content.Children.Add(new TextBlock { Text = "★を押すと色を選べます。色は本体のブックマーク一覧と、印を付けた行の左端の線に使います。最大8種類です。", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
    }

    // ---- Captions while replaying a recording -------------------------------------------

    private bool PlaybackCaptions => captionWindow is { Playback: true } && reviewingRecording && playbackDocument is not null && captionStop is null;
    /// <summary>Shows the utterance at the playback position (and the ones before it) in the caption window.</summary>
    private void UpdatePlaybackCaptions(bool force = false)
    {
        if (!PlaybackCaptions) return;
        var document = playbackDocument!;
        var phrase = RecordingTranscript.At(document, playbackSeconds);
        int index = phrase is null ? -1 : historyIds.BinarySearch(phrase.Id);
        // After an utterance with a known end, clear the window once the hide delay has passed.
        if (phrase?.EndSeconds is double end && playbackSeconds > end + settings.CaptionHideSeconds) index = -1;
        if (!force && index == playbackCaptionIndex) return;
        playbackCaptionIndex = index;
        captionWindow!.Update(index < 0 ? [] : history.Take(index + 1).TakeLast(Math.Max(1, settings.CaptionRows)).Select(row => row.Caption).ToArray());
    }
}
