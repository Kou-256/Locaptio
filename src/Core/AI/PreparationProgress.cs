namespace Locaptio.Core.AI;

// Counts completed stages, never estimates time or a compiler's internal progress.
public sealed record PreparationProgress(string Component, int Completed, int Total, string Stage, bool Failed = false)
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Completed / Total, 0, 1) : 0;
}

public sealed class PreparationTracker
{
    private readonly Dictionary<string, PreparationProgress> components = new();
    public void Clear() => components.Clear();
    public void Update(PreparationProgress progress) => components[progress.Component] = progress;
    public double Percent => components.Count == 0 ? 0 : components.Values.Average(p => p.Fraction) * 100;
    public bool Complete => components.Count > 0 && components.Values.All(p => p.Fraction >= 1 && !p.Failed);
    public bool Failed => components.Values.Any(p => p.Failed);
    public string Description => string.Join(" / ", components.Values.Where(p => p.Fraction < 1 || p.Failed).Select(p => $"{p.Component}: {p.Stage} ({Math.Clamp(p.Completed, 0, Math.Max(1, p.Total))}/{Math.Max(1, p.Total)}工程完了)"));
}
