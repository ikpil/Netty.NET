using System;
using System.Collections.Concurrent;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Thread-local globals")]
public class MapOwnershipContractTest : IDisposable
{
    public MapOwnershipContractTest() => FastThreadLocal.RemoveAll();
    public void Dispose() => FastThreadLocal.RemoveAll();

    [Theory]
    [InlineData(0)] // Ordinary CLR thread.
    [InlineData(1)] // Explicit support scope on an ordinary CLR thread.
    [InlineData(2)] // Native thread owned by the Netty factory.
    public void LogicalContextDoesNotTransferMapsAndDestroyOnlyDetachesFallbackStorage(int mode)
    {
        var marker = new AsyncLocal<string> { Value = "parent context" };
        var local = new TrackingLocal();
        local.Set("parent binding");
        var parentMap = InternalThreadLocalMap.Get();
        var parentCache = parentMap.HandlerSharableCache();
        var context = ExecutionContext.Capture();
        Exception failure = null;
        void Body()
        {
            Assert.Equal("parent context", marker.Value);
            Assert.Null(InternalThreadLocalMap.GetIfSet());
            Assert.Null(local.GetIfExists());
            Assert.Equal(mode != 0, FastThreadLocalThread.CurrentThreadHasFastThreadLocal());
            marker.Value = "child context";
            local.Set("old child binding");
            var map = InternalThreadLocalMap.Get();
            Assert.NotSame(parentMap, map);
            Assert.NotSame(parentCache, map.HandlerSharableCache());
            Assert.Equal(0, map.LocalChannelReaderStackDepth());
            map.SetLocalChannelReaderStackDepth(2);
            Assert.Equal(3, map.Size()); // Binding, cache presence, nonzero reader depth.

            FastThreadLocal.Destroy();
            if (mode == 2) Assert.Same(map, InternalThreadLocalMap.GetIfSet());
            else Assert.Null(InternalThreadLocalMap.GetIfSet());
            InternalThreadLocalMap.Remove();
            Assert.Null(InternalThreadLocalMap.GetIfSet());
            Assert.True(local.IsSet(map));
            Assert.Empty(local.Removed); // Map detachment alone does not invoke cleanup callbacks.
            local.Remove(map); // The detached map still belongs to this physical worker.
            Assert.False(local.IsSet(map));
            Assert.Single(local.Removed);
            Assert.Equal(2, map.Size());

            local.Set("new child binding");
            Assert.NotSame(map, InternalThreadLocalMap.Get());
            Assert.Equal("new child binding", local.Get());
            FastThreadLocal.RemoveAll();
            Assert.Null(InternalThreadLocalMap.GetIfSet());
            Assert.Equal(2, local.Removed.Count);
        }
        void Execute()
        {
            try
            {
                ExecutionContext.Run(context.CreateCopy(), _ =>
                {
                    if (mode == 1) FastThreadLocalThread.RunWithFastThreadLocal(Body);
                    else Body();
                }, null);
            }
            catch (Exception error) { failure = error; }
            finally { FastThreadLocal.RemoveAll(); }
        }
        Thread worker = mode == 2
            ? new DefaultThreadFactory(typeof(MapOwnershipContractTest), true).NewThread(Execute)
            : new Thread(Execute) { IsBackground = true };
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.Equal("parent context", marker.Value);
        Assert.Same(parentMap, InternalThreadLocalMap.GetIfSet());
        Assert.Same(parentCache, parentMap.HandlerSharableCache());
        Assert.Equal(0, parentMap.LocalChannelReaderStackDepth());
        Assert.Equal("parent binding", local.Get());
        Assert.Equal(2, FastThreadLocal.Size());
        Assert.Equal(new[] { "old child binding", "new child binding" }, local.Removed.ToArray());
    }

    private sealed class TrackingLocal : FastThreadLocal<string>
    {
        public readonly ConcurrentQueue<string> Removed = new();
        protected override void OnRemoval(string value) => Removed.Enqueue(value);
    }
}
