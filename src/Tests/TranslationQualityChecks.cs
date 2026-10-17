using Locaptio.Core.AI;
internal static class TranslationQualityChecks
{
    internal static async Task RunAsync()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(4)); using var llm = new LocalLlmBackend();
        await llm.StartAsync(Directory.GetCurrentDirectory(), "", false, stop.Token);
        var samples = new[] {
            ("You get more latitude to work with in post.", new[]{"We are discussing cameras, RAW shooting and 32-bit float audio recording."}),
            ("This is great when your subject suddenly decides to get loud.", new[]{"We are filming an interview with a camera and recording audio."}),
            ("32-bit float gives you enormous headroom and protects the recording from clipping.", new[]{"We are discussing microphones and digital audio recording."}),
            ("This is great when your subject suddenly gets loud.", new[]{"from clipping.", "If you are a videographer, think of thirty two bit float, like shooting in raw.", "You are not necessarily getting a more detailed image.", "You are getting more latitude to work within post."}),
            ("You are not necessarily getting a more detailed image.", new[]{"If you are a videographer, think of thirty two bit float like shooting in RAW."}) };
        var reports = new List<string>();
        for(int i=0;i<samples.Length;i++)
        {
            var result=await llm.RefineAsync(samples[i].Item1,samples[i].Item2,"",stop.Token); reports.Add($"{samples[i].Item1}\n{result.Text} ({result.Milliseconds:F0}ms)");
            if(i==0&&result.Text.Contains("緯度") || (i==1||i==3)&&result.Text.Contains("被験者") || i==2&&(!result.Text.Contains("32")||result.Text.Contains("浮き輪"))) throw new Exception("Contextual term mistranslation: "+result.Text);
            if ((i==1||i==3) && !System.Text.RegularExpressions.Regex.IsMatch(result.Text,"声|音")) throw new Exception("Loud was confused with physical size: "+result.Text);
            if (i==4 && (result.Text.Contains("必要") || !result.Text.Contains("必ずしも"))) throw new Exception("Not necessarily was confused with necessity: "+result.Text);
            Console.WriteLine($"PASS: contextual translation {i+1}: {result.Text} ({result.Milliseconds:F0}ms)");
        }
        File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(),"artifacts","contextual-translation.txt"),string.Join("\n\n",reports));
    }
}
