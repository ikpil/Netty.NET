using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Thread-local globals")]
public class NativeTypeResolutionContractTest : IDisposable
{
    public class GenericParent<T> { }

    public NativeTypeResolutionContractTest() => FastThreadLocal.RemoveAll();
    public void Dispose() => FastThreadLocal.RemoveAll();

    [Fact]
    public void ResolvingCollectibleTypesDoesNotKeepThemAliveThroughTheWorkerMap()
    {
        var map = InternalThreadLocalMap.Get();
        var types = ResolveCollectibleTypes();
        for (int attempt = 0; attempt < 12 && (types.Payload.IsAlive || types.Derived.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        GC.KeepAlive(map);
        Assert.False(types.Payload.IsAlive, "Type checks must not leave a collectible payload in a worker cache.");
        Assert.False(types.Derived.IsAlive, "Resolving metadata must not leave the collectible handler in a worker cache.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Payload, WeakReference Derived) ResolveCollectibleTypes()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("CollectibleMessage_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Messages");
        Type payload = module.DefineType("Payload", TypeAttributes.Public).CreateTypeInfo().AsType();
        Type derived = module.DefineType("Derived", TypeAttributes.Public,
            typeof(GenericParent<>).MakeGenericType(payload)).CreateTypeInfo().AsType();
        var matcher = ReflectionUtil.ResolveTypeParameter(Activator.CreateInstance(derived), typeof(GenericParent<>), "T");
        Assert.True(matcher.IsInstanceOfType(Activator.CreateInstance(payload)));
        return (new WeakReference(payload), new WeakReference(derived));
    }
}
