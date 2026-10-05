using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class DefaultChooserContractTest
{
    // Identity-only children: selection must not execute work or start a worker.
    private sealed class Child : AbstractEventExecutor
    {
        public override void Execute(Action command) => throw new NotSupportedException();
        public override bool InEventLoop(Thread thread) => false;
        public override bool IsShuttingDown() => throw new NotSupportedException();
        public override bool IsShutdown() => throw new NotSupportedException();
        public override bool IsTerminated() => throw new NotSupportedException();
        [Obsolete]
        public override void Shutdown() => throw new NotSupportedException();
        public override Task Termination => throw new NotSupportedException();
        public override Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout) => throw new NotSupportedException();
        public override bool AwaitTermination(TimeSpan timeout) => throw new NotSupportedException();
    }

    private static IEventExecutor[] Children(int count) => Enumerable.Range(0, count).Select(_ => (IEventExecutor)new Child()).ToArray();

    // Reach integer boundaries without billions of invocations. The same fixture
    // runs against the former atomic wrappers and the native primitive fields.
    private static void Seed(IEventExecutorChooser chooser, long value)
    {
        FieldInfo field = chooser.GetType().GetField("idx", BindingFlags.Instance | BindingFlags.NonPublic);
        object counter = field.GetValue(chooser);
        if (counter is AtomicInteger integer) integer.Set(checked((int)value));
        else if (counter is AtomicLong wide) wide.Set(value);
        else field.SetValue(chooser, field.FieldType == typeof(int) ? (object)checked((int)value) : value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void RoundRobinPreservesChildIdentityAndIndependentChooserPositions(int count)
    {
        IEventExecutor[] children = Children(count);
        var first = DefaultEventExecutorChooserFactory.INSTANCE.NewChooser(children);
        var second = DefaultEventExecutorChooserFactory.INSTANCE.NewChooser(children);
        for (int i = 0; i < count * 3; i++) Assert.Same(children[i % count], first.Next());
        Assert.Same(children[0], second.Next());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public void PowerOfTwoSelectionWrapsAtTheSignedIntBoundary(int count)
    {
        IEventExecutor[] children = Children(count);
        var chooser = DefaultEventExecutorChooserFactory.INSTANCE.NewChooser(children);
        const int seed = int.MaxValue - 1;
        Seed(chooser, seed);
        for (uint offset = 0; offset < 6; offset++)
        {
            uint ticket = unchecked((uint)seed + offset);
            Assert.Same(children[(int)(ticket & (uint)(count - 1))], chooser.Next());
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void GenericSelectionDoesNotReverseAtTheIntBoundary(int count)
    {
        IEventExecutor[] children = Children(count);
        var chooser = DefaultEventExecutorChooserFactory.INSTANCE.NewChooser(children);
        long seed = (long)int.MaxValue - 1;
        Seed(chooser, seed);
        for (int offset = 0; offset < 8; offset++) Assert.Same(children[(int)((seed + offset) % count)], chooser.Next());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void GenericSelectionPreservesPinnedRemainderOrderAtTheLongBoundary(int count)
    {
        IEventExecutor[] children = Children(count);
        var chooser = DefaultEventExecutorChooserFactory.INSTANCE.NewChooser(children);
        const long seed = long.MaxValue - 1;
        Seed(chooser, seed);
        for (int offset = 0; offset < 6; offset++)
        {
            long ticket = unchecked(seed + offset);
            int remainder = (int)(ticket % count);
            int slot = remainder < 0 ? -remainder : remainder;
            Assert.Same(children[slot], chooser.Next());
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public void ConcurrentSelectionDistributesOneAtomicTicketPerCall(int count)
    {
        IEventExecutor[] children = Children(count);
        var chooser = DefaultEventExecutorChooserFactory.INSTANCE.NewChooser(children);
        var selected = new int[count];
        const int perChild = 2048;
        Parallel.For(0, count * perChild, _ =>
        {
            int slot = Array.IndexOf(children, chooser.Next());
            Assert.InRange(slot, 0, count - 1);
            Interlocked.Increment(ref selected[slot]);
        });
        Assert.All(selected, total => Assert.Equal(perChild, total));
    }

    [Fact]
    public void EmptySelectionRemainsDeferredForTheAutoScalingZeroActiveSnapshot()
    {
        var chooser = DefaultEventExecutorChooserFactory.INSTANCE.NewChooser(Array.Empty<IEventExecutor>());
        Assert.Throws<IndexOutOfRangeException>(() => chooser.Next());
    }

    [Fact]
    public void MissingChildArrayUsesTheNativeArgumentException()
    {
        var error = Assert.Throws<ArgumentNullException>(() => DefaultEventExecutorChooserFactory.INSTANCE.NewChooser(null));
        Assert.Equal("executors", error.ParamName);
    }
}
