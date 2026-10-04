using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

// A bounded comparative probe, not a general-purpose benchmark framework. Each
// candidate passes an independent order/identity check before it is measured.
const int samples = 7;
bool indexedOnly = args.Skip(1).Contains("--indexed");
var rows = new List<Row>();
foreach (int count in new[] { 64, 1024, 16384 })
{
    Entry[] entries = Enumerable.Range(0, count).Select(i => new Entry(i, (i * 7919 % count) / 4)).ToArray();
    int[] order = Enumerable.Range(0, count).ToArray();
    var random = new Random(42);
    random.Shuffle(order);
    Entry[] withdrawn = order.Take(Math.Min(32, count / 2)).Select(i => entries[i]).ToArray();
    Entry[] expected = entries.Except(withdrawn, ReferenceEqualityComparer.Instance)
        .Cast<Entry>().OrderBy(e => e.Offset).ThenBy(e => e.Id).ToArray();
    foreach (string candidate in indexedOnly
                 ? new[] { "indexed-heap", "priority-queue-remove", "sorted-set" }
                 : new[] { "snapshot-rebuild", "priority-queue-remove", "sorted-set" })
    {
        IQueue verified = Create(candidate);
        foreach (Entry entry in entries) verified.Add(entry);
        foreach (Entry entry in withdrawn)
        {
            if (!verified.Remove(entry) || verified.Remove(entry)) throw new Exception("Removal identity failed");
        }
        // Equality is deliberately hostile: a distinct equal-valued reference must
        // never remove a reservation, including equal-deadline members.
        if (verified.Remove(new Entry(expected[0].Id, expected[0].Offset))) throw new Exception("Alien removal");
        if (indexedOnly)
        {
            IQueue other = Create(candidate);
            var foreign = new Entry(expected[0].Id, expected[0].Offset);
            other.Add(foreign);
            if (verified.Remove(foreign)) throw new Exception("Foreign indexed owner removal");
            other.Pop();
        }
        foreach (Entry entry in expected)
            if (!ReferenceEquals(verified.Pop(), entry)) throw new Exception("Remaining deadline order failed");
        if (verified.Count != 0) throw new Exception("Drain failed");

        Measure(candidate, "fill-drain", count, count * 2,
            () => Create(candidate), q =>
            {
                foreach (Entry entry in entries) q.Add(entry);
                while (q.Count != 0) q.Pop();
            });
        Measure(candidate, "reuse-fill-drain", count, count * 2,
            () =>
            {
                IQueue q = Create(candidate);
                foreach (Entry entry in entries) q.Add(entry);
                while (q.Count != 0) q.Pop();
                return q;
            }, q =>
            {
                foreach (Entry entry in entries) q.Add(entry);
                while (q.Count != 0) q.Pop();
            });
        Measure(candidate, "remove-hit-and-miss", count, withdrawn.Length * 2,
            () => { IQueue q = Create(candidate); foreach (Entry entry in entries) q.Add(entry); return q; }, q =>
            {
                foreach (Entry entry in withdrawn) q.Remove(entry);
                foreach (Entry entry in withdrawn) q.Remove(entry);
            });
    }
}
foreach (int count in indexedOnly ? Array.Empty<int>() : new[] { 64, 1024, 4096 })
{
    // Admission/CTS/delegate creation and stop/cleanup are outside this interval.
    // A null-returning constructor factory retains work without introducing worker
    // scheduling noise. This measures the real public cancellation ownership path.
    var values = new List<(double Ns, double Bytes)>();
    for (int round = -2; round < samples; ++round)
    {
        var pool = new UnorderedThreadPoolEventExecutor(1, new NoWorker());
        var owners = new CancellationTokenSource[count];
        var tasks = new Task[count];
        try
        {
            for (int i = 0; i < count; ++i)
            {
                owners[i] = new CancellationTokenSource();
                tasks[i] = pool.ScheduleAsync(static () => { }, TimeSpan.FromDays(1), owners[i].Token);
            }
            int cancelCount = Math.Min(32, count / 2);
            int[] indices = Enumerable.Range(0, count).ToArray();
            new Random(42).Shuffle(indices);
            long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int i = 0; i < cancelCount; ++i) owners[indices[i]].Cancel();
            long ticks = Stopwatch.GetTimestamp() - start;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            if (pool.PendingTaskCount != count - cancelCount || indices.Take(cancelCount).Any(i => !tasks[i].IsCanceled))
                throw new Exception("Public cancellation ownership failed");
            if (round >= 0) values.Add((ticks * 1e9 / Stopwatch.Frequency / cancelCount, (double)allocated / cancelCount));
        }
        finally
        {
            await pool.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            foreach (var owner in owners) owner?.Dispose();
        }
    }
    Record("public-executor", "scheduled-owner-cancel", count, values);
}
var result = new
{
    runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    processors = Environment.ProcessorCount,
    samples,
    warmups = 2,
    stopwatchFrequency = Stopwatch.Frequency,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
    mode = indexedOnly ? "indexed-queues" : "unordered-queues",
    allocationScope = "current-thread managed bytes; no GC/worker/cross-thread allocation claim",
    rows
};
File.WriteAllText(args[0], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
foreach (Row row in rows) Console.WriteLine($"{row.Candidate},{row.Phase},{row.Count},{row.MedianNs:F1},{row.MedianBytes:F1}");

void Measure(string candidate, string phase, int count, int operations, Func<IQueue> setup, Action<IQueue> action)
{
    var values = new List<(double Ns, double Bytes)>();
    for (int round = -2; round < samples; ++round)
    {
        IQueue queue = setup();
        long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        action(queue);
        long ticks = Stopwatch.GetTimestamp() - start;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        if (round >= 0) values.Add((ticks * 1e9 / Stopwatch.Frequency / operations, (double)allocated / operations));
        // Indexed nodes are reused in the next setup. Release memberships after
        // recording the interval; cleanup is outside measured work/allocation.
        while (queue.Count != 0) queue.Pop();
    }
    Record(candidate, phase, count, values);
}
void Record(string candidate, string phase, int count, List<(double Ns, double Bytes)> values)
{
    double[] times = values.Select(v => v.Ns).Order().ToArray();
    double[] allocations = values.Select(v => v.Bytes).Order().ToArray();
    rows.Add(new Row(candidate, phase, count, times[samples / 2], times[0], times[^1], allocations[samples / 2]));
}
static IQueue Create(string candidate) => candidate switch
{
    "snapshot-rebuild" => new Heap(true),
    "priority-queue-remove" => new Heap(false),
    "indexed-heap" => new IndexedHeap(),
    _ => new Tree()
};
record Row(string Candidate, string Phase, int Count, double MedianNs, double MinNs, double MaxNs, double MedianBytes);
sealed class Entry(int id, int offset) : IPriorityQueueNode<Entry>
{
    int index = -1;
    internal int Id => id;
    internal int Offset => offset;
    // Includes signed-clock wrap and equal deadlines, with a valid compact horizon.
    internal (long Deadline, long Sequence) Priority => (unchecked(long.MaxValue - 1000 + offset), id);
    public override bool Equals(object other) => other is Entry;
    public override int GetHashCode() => 0;
    public int PriorityQueueIndex(DefaultPriorityQueue<Entry> queue) => index;
    public void PriorityQueueIndex(DefaultPriorityQueue<Entry> queue, int value) => index = value;
}
interface IQueue
{
    int Count { get; }
    void Add(Entry entry);
    bool Remove(Entry entry);
    Entry Pop();
}
sealed class Heap(bool rebuild) : IQueue
{
    readonly PriorityQueue<Entry, (long Deadline, long Sequence)> queue = new(PriorityComparer.Instance);
    public int Count => queue.Count;
    public void Add(Entry entry) => queue.Enqueue(entry, entry.Priority);
    public Entry Pop() => queue.Dequeue();
    public bool Remove(Entry entry) => rebuild ? RemoveByRebuilding(entry) :
        queue.Remove(entry, out _, out _, ReferenceEqualityComparer.Instance);
    private bool RemoveByRebuilding(Entry entry)
    {
        var items = queue.UnorderedItems.ToArray();
        if (!items.Any(item => ReferenceEquals(item.Element, entry))) return false;
        queue.Clear();
        foreach (var item in items)
            if (!ReferenceEquals(item.Element, entry)) queue.Enqueue(item.Element, item.Priority);
        return true;
    }
}
sealed class IndexedHeap : IQueue
{
    readonly DefaultPriorityQueue<Entry> queue = new(
        Comparer<Entry>.Create((a, b) => PriorityComparer.Instance.Compare(a.Priority, b.Priority)), 0);
    public int Count => queue.Count;
    public void Add(Entry entry) => queue.Offer(entry);
    public Entry Pop() => queue.Poll();
    public bool Remove(Entry entry) => queue.Remove(entry);
}
sealed class Tree : IQueue
{
    readonly SortedSet<Entry> queue = new(Comparer<Entry>.Create((a, b) => PriorityComparer.Instance.Compare(a.Priority, b.Priority)));
    public int Count => queue.Count;
    public void Add(Entry entry) => queue.Add(entry);
    public bool Remove(Entry entry)
    {
        // Priorities are unique but are not themselves identity. The adapter still
        // has to reject a distinct reference with the same deadline/sequence key.
        return queue.TryGetValue(entry, out Entry found) && ReferenceEquals(found, entry) && queue.Remove(entry);
    }
    public Entry Pop() { Entry entry = queue.Min; queue.Remove(entry); return entry; }
}
sealed class PriorityComparer : IComparer<(long Deadline, long Sequence)>
{
    internal static readonly PriorityComparer Instance = new();
    public int Compare((long Deadline, long Sequence) a, (long Deadline, long Sequence) b)
    {
        long delta = unchecked(a.Deadline - b.Deadline);
        return delta == 0 ? a.Sequence.CompareTo(b.Sequence) : delta < 0 ? -1 : 1;
    }
}
sealed class NoWorker : IThreadFactory
{
    public Thread NewThread(Action task) => null;
}
