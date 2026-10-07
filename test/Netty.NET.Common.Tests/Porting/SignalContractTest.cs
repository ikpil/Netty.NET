using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Netty.NET.Common.Tests.Porting;

public class SignalContractTest
{
    [Fact]
    public async Task ConcurrentRegistryLookupsPublishOneSignalIdentity()
    {
        string name = "signal-race-" + Guid.NewGuid();
        var lookups = new Task<Signal>[32];
        for (int i = 0; i < lookups.Length; i++)
            lookups[i] = Task.Run(() => Signal.ValueOf(name));
        Signal[] values = await Task.WhenAll(lookups);
        foreach (Signal value in values)
        {
            Assert.Same(values[0], value);
            Assert.Equal(name, value.Name);
            Assert.Equal(values[0].Id, value.Id);
        }
        Assert.Same(values[0], Signal.ValueOf(name));
    }

    [Fact]
    public void TypedRegistryNamesUseTheNativeTypeNameAndOrdinalIdentity()
    {
        string component = "scope-" + Guid.NewGuid();
        Signal value = Signal.ValueOf(typeof(SignalContractTest), component);
        string name = typeof(SignalContractTest).FullName + '#' + component;
        Assert.Equal(name, value.Name);
        Assert.Equal(name, value.ToString());
        Assert.Same(value, Signal.ValueOf(name));
        Assert.NotSame(value, Signal.ValueOf(name.ToUpperInvariant()));
    }

    [Fact]
    public void OnlyTheRegistryCanConstructSignalInstances()
    {
        Assert.Empty(typeof(Signal).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.True(typeof(Signal).IsSealed);
    }

    [Fact]
    public void ReplayCatchBoundaryPreservesIdentityAndEmptyDiagnosticsOnRepeatedThrows()
    {
        Signal replay = Signal.ValueOf("replay-" + Guid.NewGuid());
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                // ReplayingDecoder catches the marker, validates its identity and
                // returns to the checkpoint rather than treating it as a failure.
                throw replay;
            }
            catch (Signal caught)
            {
                Assert.Same(replay, caught);
                caught.Expect(replay);
                Assert.Null(caught.InnerException);
                Assert.Equal(string.Empty, caught.StackTrace);
                Assert.Equal(replay.Name, caught.ToString());
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnexpectedMarkersKeepThePinnedFailureText(bool useNull)
    {
        Signal expected = Signal.ValueOf("expected-" + Guid.NewGuid());
        Signal actual = useNull ? null : Signal.ValueOf("actual-" + Guid.NewGuid());
        var failure = Assert.Throws<InvalidOperationException>(() => expected.Expect(actual));
        Assert.Equal("unexpected signal: " + (actual?.Name ?? "null"), failure.Message);
        expected.Expect(expected);
    }

    [Fact]
    public void IdentityHashingAndNativeOrderingWorkAsCollectionKeys()
    {
        string prefix = "ordered-signal-" + Guid.NewGuid();
        var ordered = new SortedSet<Signal>();
        var keys = new Dictionary<Signal, int>();
        for (int i = 0; i < 32; i++)
        {
            Signal value = Signal.ValueOf(prefix + i);
            Assert.True(value.Equals(value));
            Assert.False(value.Equals(null));
            Assert.Equal(RuntimeHelpers.GetHashCode(value), value.GetHashCode());
            Assert.Equal(0, value.CompareTo(value));
            keys.Add(value, i);
            Assert.True(ordered.Add(value));
            Assert.False(ordered.Add(Signal.ValueOf(prefix + i)));
        }
        Signal previous = null;
        foreach (Signal value in ordered)
        {
            Assert.Equal(keys[value], keys[Signal.ValueOf(value.Name)]);
            if (previous != null)
            {
                Assert.False(previous.Equals(value));
                Assert.True(previous.CompareTo(value) < 0);
                Assert.True(value.CompareTo(previous) > 0);
            }
            previous = value;
        }
        Assert.Equal(32, ordered.Count);
    }

    [Fact]
    public void NullComparisonUsesTheNativeArgumentBoundary()
    {
        var signal = Signal.ValueOf("compare-null-" + Guid.NewGuid());
        Assert.Throws<ArgumentNullException>(() => signal.CompareTo(null));
    }

    [Fact]
    public void FactoriesValidateNamesBeforeRegistryPublication()
    {
        Assert.Throws<ArgumentNullException>(() => Signal.ValueOf((string)null));
        Assert.Throws<ArgumentException>(() => Signal.ValueOf(string.Empty));
        Assert.Throws<ArgumentNullException>(() => Signal.ValueOf(null, "name"));
        Assert.Throws<ArgumentNullException>(() => Signal.ValueOf(typeof(SignalContractTest), null));
        // The typed overload validates components for null, then validates the
        // combined name: an empty second component still produces a nonempty key.
        var typed = Signal.ValueOf(typeof(SignalContractTest), string.Empty);
        Assert.Same(typed, Signal.ValueOf(typeof(SignalContractTest).FullName + '#'));
    }
}
