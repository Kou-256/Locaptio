namespace Locaptio.Core.Audio;

/// <summary>Two bounded mono/16kHz queues, consumed on a common clock.</summary>
public sealed class AudioMixBuffer(int capacity = 3200)
{
    private readonly float[][] buffers = [new float[capacity], new float[capacity]];
    private readonly int[] heads = new int[2], counts = new int[2];
    private readonly object gate = new();
    private long dropped;
    public long DroppedSamples { get { lock (gate) return dropped; } }
    public void Clear(int input) { lock (gate) heads[input] = counts[input] = 0; }
    public void Add(int input, ReadOnlySpan<float> samples)
    {
        lock (gate)
        {
            foreach (float sample in samples)
            {
                if (counts[input] == capacity) { heads[input] = (heads[input] + 1) % capacity; counts[input]--; dropped++; }
                buffers[input][(heads[input] + counts[input]) % capacity] = sample;
                counts[input]++;
            }
        }
    }
    public float[] Read(int size)
    {
        var mixed = new float[size];
        lock (gate)
        {
            for (int source = 0; source < 2; source++)
            {
                int count = Math.Min(size, counts[source]);
                for (int i = 0; i < count; i++) mixed[i] += buffers[source][(heads[source] + i) % capacity] * .5f;
                heads[source] = (heads[source] + count) % capacity; counts[source] -= count;
            }
        }
        return mixed;
    }
}
