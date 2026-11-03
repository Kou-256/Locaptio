using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Locaptio.Core;

internal static class CaptionHistoryChecks
{
    public static void Run()
    {
        var first = new CaptionUpdate(1, 1, "Partial", null, false, 1, 0, 0);
        var row = new CaptionHistoryEntry(first);
        var rows = new ObservableCollection<CaptionHistoryEntry> { row };
        int collectionChanges = 0; var properties = new List<string?>();
        rows.CollectionChanged += (_, _) => collectionChanges++;
        row.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        for (int i = 2; i <= 1000; i++) row.Update(first with { Revision = i, English = "Partial " + i });
        if (!ReferenceEquals(rows[0], row) || collectionChanges != 0 || properties.Count != 999 || properties.Any(p => p != "English")) throw new Exception("Partial text replaced its item or refreshed unrelated bindings");
        properties.Clear();
        row.Update(row.Caption with { Japanese = "翻訳", TranslationRefined = true });
        if (!properties.SequenceEqual(new[] { "Japanese" })) throw new Exception("Translation refreshed source text");
        properties.Clear();
        row.Update(row.Caption); row.Update(first);
        if (properties.Count != 0 || row.Caption.Revision != 1000) throw new Exception("Duplicate/stale updates redrew history");
        try { row.Update(row.Caption with { Id = 2 }); throw new Exception("Identity was not enforced"); } catch (ArgumentException) { }
        var selectionRows = new[]
        {
            new CaptionHistoryEntry(first with { English = "先頭行です", Japanese = "first line" }),
            new CaptionHistoryEntry(first with { Id = 5, English = "中間行です", Japanese = "middle line" }),
            new CaptionHistoryEntry(first with { Id = 9, English = "最後の行です", Japanese = "last line" })
        };
        var selection = new TranscriptSelection(new(1, 2), new(9, 3), 0);
        string expected = "行です" + Environment.NewLine + "中間行です" + Environment.NewLine + "最後の";
        var reversed = selection with { Anchor = selection.Focus, Focus = selection.Anchor };
        if (selection.Text(selectionRows) != expected || reversed.Text(selectionRows) != expected)
            throw new Exception("Multi-row selection or reverse drag copied the wrong text");
        if ((selection with { Column = 1 }).Text(selectionRows) != "rst line" + Environment.NewLine + "middle line" + Environment.NewLine + "las")
            throw new Exception("Selected language columns were mixed");
        if ((selection with { Focus = selection.Anchor }).Text(selectionRows) != "" || selection.Range(20, 4).Length != 0)
            throw new Exception("Empty/outside selections copied content");
        Console.WriteLine("PASS: multi-row partial selection, reverse drag, language columns, empty and non-contiguous row IDs");
        Console.WriteLine("PASS: 1,000 partial updates retain one history item, targeted bindings, stale/duplicate rejection and stable identity");
    }
}
