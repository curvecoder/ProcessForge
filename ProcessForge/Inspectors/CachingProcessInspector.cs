using System.Collections.Concurrent;
using ProcessForge.Contracts;
using ProcessForge.Models;

namespace ProcessForge.Inspectors;

/// <summary>
/// Decorates another <see cref="IProcessInspector"/> and keeps the metadata it resolved, keyed by the process
/// identifier and the process start time, so that repeated scans only resolve new processes.
/// </summary>
/// <remarks>
/// <para>
/// Before each call the decorator reads the start time of the process (a cheap system call on Windows, one read of
/// <c>/proc/&lt;pid&gt;/stat</c> on Linux). A PID reused by a new process has a different start time, so it is
/// resolved again. A process whose start time cannot be read (exited, protected, hidden by <c>/proc</c>, or an
/// operating system other than Windows and Linux) is resolved by the inner inspector on every call and never cached.
/// </para>
/// <para>
/// Metadata is kept as first resolved, including fields the inner inspector left empty: an account renamed after the
/// process started keeps its old <see cref="ProcessMetadata.UserName"/>, and an owner or command line that could not
/// be read stays empty until the PID is reused. When the cache reaches its capacity it is cleared, which bounds memory
/// in long-running monitors; choose a capacity above the number of live processes, otherwise every scan refills it.
/// Instances are thread-safe; keep one for the lifetime of the scanner.
/// </para>
/// </remarks>
public sealed class CachingProcessInspector : IProcessInspector
{
    private readonly IProcessInspector inner;
    private readonly int capacity;
    private readonly IProcessStartTimeReader startTimeReader;
    private readonly ConcurrentDictionary<int, CacheEntry> entries = new();

    /// <summary>
    /// Creates a caching decorator around <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">Inspector that resolves the processes missing from the cache.</param>
    /// <param name="capacity">Number of processes kept before the cache is cleared.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is zero or negative.</exception>
    public CachingProcessInspector(IProcessInspector inner, int capacity = 4096)
        : this(inner, capacity, new ProcessStartTimeReader())
    {
    }

    internal CachingProcessInspector(IProcessInspector inner, int capacity, IProcessStartTimeReader startTimeReader)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentNullException.ThrowIfNull(startTimeReader);
        this.inner = inner;
        this.capacity = capacity;
        this.startTimeReader = startTimeReader;
    }

    /// <inheritdoc />
    public async Task<ProcessMetadata> ResolveProcessAsync(int processId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startTime = startTimeReader.Read(processId);
        if (startTime is not { } knownStartTime)
        {
            return await inner.ResolveProcessAsync(processId, cancellationToken).ConfigureAwait(false);
        }

        if (entries.TryGetValue(processId, out var cached) && cached.StartTime == knownStartTime)
        {
            return cached.Metadata;
        }

        var metadata = await inner.ResolveProcessAsync(processId, cancellationToken).ConfigureAwait(false);

        // The process may have exited and its PID been reused while the inner inspector was reading it: the metadata
        // could then mix both processes, so it is only cached when the start time is still the same.
        if (startTimeReader.Read(processId) == knownStartTime)
        {
            Store(processId, new CacheEntry(knownStartTime, metadata));
        }

        return metadata;
    }

    private void Store(int processId, CacheEntry entry)
    {
        if (!entries.ContainsKey(processId) && entries.Count >= capacity)
        {
            entries.Clear();
        }

        entries[processId] = entry;
    }

    private readonly record struct CacheEntry(long StartTime, ProcessMetadata Metadata);
}
