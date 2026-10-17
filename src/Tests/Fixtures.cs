using Locaptio.Core.AI;

sealed class FixtureSpeech(string text) : ISpeechRecognitionBackend
{
    public Task<InferenceResult> RecognizeAsync(float[] samples, CancellationToken token, bool final = true) => Task.FromResult(new InferenceResult(text, 0));
}
sealed class FixtureTranslation : ITranslationBackend
{
    public int Calls;
    public Task<InferenceResult> TranslateAsync(string text, CancellationToken token, bool final = true) { Interlocked.Increment(ref Calls); return Task.FromResult(new InferenceResult("標準の訳", 0)); }
}
sealed class FailingRefiner : ITranslationRefinementBackend
{
    public int Calls;
    public Task<InferenceResult> RefineAsync(string text, CancellationToken token) { Interlocked.Increment(ref Calls); throw new InvalidOperationException("Fixture LLM failure"); }
}
