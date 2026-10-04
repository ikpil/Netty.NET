using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Thread-local globals")]
public class ThreadLocalCacheContractTest : IDisposable
{
    public ThreadLocalCacheContractTest() => FastThreadLocal.RemoveAll();
    public void Dispose() => FastThreadLocal.RemoveAll();

    [Fact]
    public void HandlerCacheDoesNotRootCollectibleTypes()
    {
        var map = InternalThreadLocalMap.Get();
        WeakReference type = CacheCollectibleType(map);
        for (int attempt = 0; attempt < 12 && type.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        GC.KeepAlive(map);
        Assert.False(type.IsAlive, "A live thread-local cache must not retain a collectible handler type.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CacheCollectibleType(InternalThreadLocalMap map)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("CollectibleHandler_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        Type type = assembly.DefineDynamicModule("Handlers")
            .DefineType("Handler", TypeAttributes.Public).CreateTypeInfo().AsType();
        map.HandlerSharableCache().Add(type, new StrongBox<bool>(true));
        return new WeakReference(type);
    }

    [Fact]
    public void HandlerCacheKeysUseReferenceIdentity()
    {
        var first = new EquivalentType();
        var second = new EquivalentType();
        Assert.Equal(first, second);
        var cache = InternalThreadLocalMap.Get().HandlerSharableCache();
        cache.Add(first, new StrongBox<bool>(true));
        cache.Add(second, new StrongBox<bool>(false));
        Assert.True(cache.TryGetValue(first, out var firstValue));
        Assert.True(cache.TryGetValue(second, out var secondValue));
        Assert.True(firstValue.Value);
        Assert.False(secondValue.Value);
    }

    private sealed class EquivalentType() : TypeDelegator(typeof(object))
    {
        public override bool Equals(Type other) => other is EquivalentType;
        public override bool Equals(object other) => other is EquivalentType;
        public override int GetHashCode() => 1;
    }
}
