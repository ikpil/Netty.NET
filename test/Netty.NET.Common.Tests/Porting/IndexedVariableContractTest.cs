using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Thread-local globals")]
public class IndexedVariableContractTest : IDisposable
{
    private static readonly FieldInfo IndexField = typeof(InternalThreadLocalMap)
        .GetField("nextIndex", BindingFlags.NonPublic | BindingFlags.Static);

    public IndexedVariableContractTest() => FastThreadLocal.RemoveAll();
    public void Dispose() => FastThreadLocal.RemoveAll();

    private static int ReadIndex() => (int)IndexField.GetValue(null);
    private static void WriteIndex(int value) => IndexField.SetValue(null, value);

    [Fact]
    public void AllocationStopsAtTheNativeArrayLimit()
    {
        int previous = ReadIndex();
        try
        {
            WriteIndex(Array.MaxLength - 1);
            Assert.Equal(Array.MaxLength - 1, InternalThreadLocalMap.NextVariableIndex());
            Assert.Throws<InvalidOperationException>(() => InternalThreadLocalMap.NextVariableIndex());
            Assert.Equal(Array.MaxLength, ReadIndex());
            Assert.Equal(Array.MaxLength - 1, InternalThreadLocalMap.LastVariableIndex());
        }
        finally { WriteIndex(previous); }
    }

    [Fact]
    public void ConcurrentClaimsAreUniqueAndStopAtTheNativeLimit()
    {
        int previous = ReadIndex();
        try
        {
            WriteIndex(Array.MaxLength - 4);
            var claimed = new ConcurrentDictionary<int, byte>();
            int failures = 0;
            Parallel.For(0, 64, _ =>
            {
                int index;
                try { index = InternalThreadLocalMap.NextVariableIndex(); }
                catch (InvalidOperationException) { Interlocked.Increment(ref failures); return; }
                Assert.True(claimed.TryAdd(index, 0), "An indexed variable ID must never be issued twice.");
            });
            Assert.Equal(4, claimed.Count);
            Assert.Equal(60, failures);
            for (int index = Array.MaxLength - 4; index < Array.MaxLength; index++)
                Assert.True(claimed.ContainsKey(index));
            Assert.Equal(Array.MaxLength, ReadIndex());
        }
        finally { WriteIndex(previous); }
    }

    [Theory]
    [MemberData(nameof(GrowthCases))]
    public void GrowthCapacityStaysPositiveWithinTheRuntimeLimit(int index, int expected)
    {
        var capacity = typeof(InternalThreadLocalMap).GetMethod("IndexedVariableTableCapacity",
            BindingFlags.NonPublic | BindingFlags.Static);
        int actual = (int)capacity.Invoke(null, [index]);
        Assert.Equal(expected, actual);
        Assert.True(actual > index);
        Assert.True(actual <= Array.MaxLength);
    }

    [Theory]
    [MemberData(nameof(InvalidWriteIndices))]
    public void RejectedWritesPreserveExistingStorage(int index)
    {
        var map = InternalThreadLocalMap.Get();
        object value = new();
        map.SetIndexedVariable(1, value);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetAndSetIndexedVariable(index, new object()));
        Assert.Same(value, map.IndexedVariable(1));
        Assert.Same(InternalThreadLocalMap.UNSET, map.IndexedVariable(32));
    }

    public static TheoryData<int, int> GrowthCases => new()
    {
        { 31, 32 }, { 32, 64 }, { 1023, 1024 },
        { 1 << 30, Array.MaxLength }, { Array.MaxLength - 1, Array.MaxLength }
    };

    public static TheoryData<int> InvalidWriteIndices => new() { -1, Array.MaxLength, int.MaxValue };

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 1)]
    [InlineData(int.MinValue, 1)]
    [InlineData(-1, 2)]
    [InlineData(int.MinValue, 2)]
    public void NegativeReadsRemovalsAndPresenceChecksRejectWithoutChangingBindings(int index, int operation)
    {
        var map = InternalThreadLocalMap.Get();
        object value = new();
        map.SetIndexedVariable(1, value);
        map.SetIndexedVariable(33, null);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            if (operation == 0) map.IndexedVariable(index);
            else if (operation == 1) map.RemoveIndexedVariable(index);
            else map.IsIndexedVariableSet(index);
        });
        Assert.Equal("index", error.ParamName);
        Assert.Same(value, map.IndexedVariable(1));
        Assert.True(map.IsIndexedVariableSet(33));
        Assert.Null(map.IndexedVariable(33));
        Assert.Same(InternalThreadLocalMap.UNSET, map.IndexedVariable(32));
    }

    [Fact]
    public void NativeSlotsDistinguishUnsetNullAndReferencesAcrossGrowthAndRemoval()
    {
        var map = InternalThreadLocalMap.Get();
        foreach (int index in new[] { 32, Array.MaxLength, int.MaxValue })
        {
            Assert.Same(InternalThreadLocalMap.UNSET, map.IndexedVariable(index));
            Assert.False(map.IsIndexedVariableSet(index));
            Assert.Same(InternalThreadLocalMap.UNSET, map.RemoveIndexedVariable(index));
        }
        Assert.True(map.SetIndexedVariable(1, null));
        Assert.True(map.IsIndexedVariableSet(1));
        Assert.Null(map.IndexedVariable(1));
        object value = new();
        Assert.Null(map.GetAndSetIndexedVariable(1, value));
        Assert.False(map.SetIndexedVariable(1, value));
        Assert.Same(InternalThreadLocalMap.UNSET, map.GetAndSetIndexedVariable(33, null));
        Assert.Same(value, map.IndexedVariable(1));
        Assert.Same(InternalThreadLocalMap.UNSET, map.IndexedVariable(32));
        Assert.True(map.IsIndexedVariableSet(33));
        Assert.Null(map.RemoveIndexedVariable(33));
        Assert.False(map.IsIndexedVariableSet(33));
        Assert.Same(value, map.GetAndSetIndexedVariable(1, InternalThreadLocalMap.UNSET));
        Assert.False(map.IsIndexedVariableSet(1));
        Assert.Same(InternalThreadLocalMap.UNSET, map.RemoveIndexedVariable(1));
    }
}
