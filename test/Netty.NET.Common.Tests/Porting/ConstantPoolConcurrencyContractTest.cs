using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class ConstantPoolConcurrencyContractTest
{
    private sealed class Constant(int id, string name) : AbstractConstant<Constant>(id, name);
    private sealed class Pool(Barrier rendezvous = null) : ConstantPool<Constant>
    {
        internal readonly ConcurrentBag<Constant> Created = new();
        protected override Constant NewConstant(int id, string name)
        {
            var value = new Constant(id, name);
            Created.Add(value);
            // Both calls must reach construction before either can publish.
            if (rendezvous != null) Assert.True(rendezvous.SignalAndWait(TimeSpan.FromSeconds(5)));
            return value;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompetingFactoriesPublishOneIdentityEvenWhenBothConstruct(bool createOnly)
    {
        using var rendezvous = new Barrier(2);
        var pool = new Pool(rendezvous);
        Constant Create()
        {
            try { return createOnly ? pool.NewInstance("shared") : pool.ValueOf("shared"); }
            catch (ArgumentException) when (createOnly) { return null; }
        }
        // Dedicated workers keep the barrier independent of thread-pool injection.
        Task<Constant> first = Task.Factory.StartNew(Create, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task<Constant> second = Task.Factory.StartNew(Create, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Constant[] returned = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, pool.Created.Count);
        Constant published = pool.ValueOf("shared");
        Assert.True(pool.Exists("shared"));
        if (createOnly)
        {
            Assert.Single(returned, value => value != null);
            Assert.Contains(published, returned);
        }
        else Assert.All(returned, value => Assert.Same(published, value));
        Assert.Equal(3, pool.NextId());
    }

    [Fact]
    public void ConcurrentIdAllocationIsUniqueAndStartsAtOne()
    {
        var pool = new Pool();
        var ids = new ConcurrentDictionary<int, byte>();
        Parallel.For(0, 10000, _ => Assert.True(ids.TryAdd(pool.NextId(), 0)));
        Assert.Equal(10000, ids.Count);
        Assert.Contains(1, ids.Keys);
        Assert.Contains(10000, ids.Keys);
        Assert.Equal(10001, pool.NextId());
    }
}
