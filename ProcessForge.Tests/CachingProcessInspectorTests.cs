using System.Diagnostics;
using AwesomeAssertions;
using ProcessForge.Contracts;
using ProcessForge.Inspectors;
using ProcessForge.Models;
using Xunit;

namespace ProcessForge.Tests;

public sealed class CachingProcessInspectorTests
{
    [Fact]
    public async Task ResolveProcessAsync_WhenStartTimeIsUnchanged_ResolvesOnceAndReturnsSameInstance()
    {
        // Arrange
        var inner = new CountingInspector();
        var startTimes = new FakeStartTimeReader();
        startTimes.Set(42, 1000);
        var sut = new CachingProcessInspector(inner, 16, startTimes);

        // Act
        var first = await sut.ResolveProcessAsync(42);
        var second = await sut.ResolveProcessAsync(42);

        // Assert
        inner.Calls.Should().Be(1);
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenPidIsReusedByNewProcess_ResolvesAgain()
    {
        // Arrange
        var inner = new CountingInspector();
        var startTimes = new FakeStartTimeReader();
        startTimes.Set(42, 1000);
        var sut = new CachingProcessInspector(inner, 16, startTimes);
        var first = await sut.ResolveProcessAsync(42);
        startTimes.Set(42, 2000);

        // Act
        var second = await sut.ResolveProcessAsync(42);

        // Assert
        inner.Calls.Should().Be(2);
        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenProcessHasExited_ResolvesOnEveryCall()
    {
        // Arrange: an exited process has no readable start time, so the inner inspector reports it as terminated.
        var inner = new CountingInspector();
        var startTimes = new FakeStartTimeReader();
        startTimes.Set(42, 1000);
        var sut = new CachingProcessInspector(inner, 16, startTimes);
        await sut.ResolveProcessAsync(42);
        startTimes.Remove(42);

        // Act
        await sut.ResolveProcessAsync(42);
        await sut.ResolveProcessAsync(42);

        // Assert
        inner.Calls.Should().Be(3);
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenStartTimeChangesDuringResolution_DoesNotCache()
    {
        // Arrange
        var startTimes = new FakeStartTimeReader();
        startTimes.Set(42, 1000);
        var inner = new CountingInspector(onResolve: () => startTimes.Set(42, 2000));
        var sut = new CachingProcessInspector(inner, 16, startTimes);
        await sut.ResolveProcessAsync(42);

        // Act
        await sut.ResolveProcessAsync(42);

        // Assert
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenCapacityIsReached_ClearsCache()
    {
        // Arrange
        var inner = new CountingInspector();
        var startTimes = new FakeStartTimeReader();
        startTimes.Set(1, 10);
        startTimes.Set(2, 20);
        var sut = new CachingProcessInspector(inner, 1, startTimes);

        // Act
        await sut.ResolveProcessAsync(1);
        await sut.ResolveProcessAsync(2);
        await sut.ResolveProcessAsync(1);

        // Assert
        inner.Calls.Should().Be(3);
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenTokenIsCanceled_ThrowsOperationCanceledException()
    {
        // Arrange
        var startTimes = new FakeStartTimeReader();
        startTimes.Set(42, 1000);
        var sut = new CachingProcessInspector(new CountingInspector(), 16, startTimes);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => sut.ResolveProcessAsync(42, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Constructor_WhenInnerIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new CachingProcessInspector(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("inner");
    }

    [Fact]
    public void Constructor_WhenCapacityIsZero_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => new CachingProcessInspector(new CountingInspector(), 0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("capacity");
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenDecoratingRealInspectorOnCurrentProcess_ReturnsCachedInnerResult()
    {
        // Arrange
        using var current = Process.GetCurrentProcess();
        var inner = new CrossPlatformProcessInspector();
        var sut = new CachingProcessInspector(inner);
        var expected = await inner.ResolveProcessAsync(current.Id);

        // Act
        var first = await sut.ResolveProcessAsync(current.Id);
        var second = await sut.ResolveProcessAsync(current.Id);

        // Assert
        first.Should().Be(expected);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            second.Should().BeSameAs(first);
        }
    }

    private sealed class CountingInspector(Action? onResolve = null) : IProcessInspector
    {
        public int Calls { get; private set; }

        public Task<ProcessMetadata> ResolveProcessAsync(int processId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            onResolve?.Invoke();
            return Task.FromResult(new ProcessMetadata(processId, $"p{Calls}", string.Empty, string.Empty, string.Empty, string.Empty));
        }
    }

    private sealed class FakeStartTimeReader : IProcessStartTimeReader
    {
        private readonly Dictionary<int, long> startTimes = [];

        public void Set(int processId, long startTime) => startTimes[processId] = startTime;

        public void Remove(int processId) => startTimes.Remove(processId);

        public long? Read(int processId) => startTimes.TryGetValue(processId, out var value) ? value : null;
    }
}
