using System;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Global executor")]
public class ThreadFactoryContractTest
{
    [Fact]
    public void GlobalWorkerDoesNotInheritCallerExecutionContext()
    {
        var executor = GlobalEventExecutor.INSTANCE;
        if (executor._thread != null) Assert.True(executor.AwaitInactivity(TimeSpan.FromSeconds(5)));
        var local = new AsyncLocal<object>();
        var marker = new object();
        object observed = marker;
        local.Value = marker;
        try
        {
            using var finished = new CountdownEvent(1);
            executor.Execute(Runnables.Create(() => { observed = local.Value; finished.Signal(); }));
            Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
            Assert.Null(observed);
            Assert.Same(marker, local.Value);
        }
        finally { local.Value = null; }
    }

    [Fact]
    public void NullFactoryGroupInheritsEachCreatorRatherThanTheFactoryCreator()
    {
        var factory = new DefaultThreadFactory("inherited", true);
        foreach (var name in new[] { "first", "second" })
        {
            var group = new ThreadGroup(name);
            Thread child = null;
            var creator = group.NewThread(() => child = factory.NewThread(() => { }));
            creator.Start();
            Assert.True(creator.Join(TimeSpan.FromSeconds(5)));
            Assert.Same(group, ThreadGroup.GetThreadGroup(child));
            Assert.True(child.IsBackground);
        }
    }

    [Fact]
    public void ExplicitFactoryGroupOverridesCreatorAndSurvivesTheNativeThreadStart()
    {
        var assigned = new ThreadGroup("assigned");
        var factory = new DefaultThreadFactory("explicit", true, ThreadPriority.Normal, assigned);
        ThreadGroup observed = null;
        var child = factory.NewThread(() => observed = ThreadGroup.CurrentThreadGroup());
        Assert.Same(assigned, ThreadGroup.GetThreadGroup(child));
        Assert.Equal(ThreadPriority.Normal, child.Priority);
        child.Start();
        Assert.True(child.Join(TimeSpan.FromSeconds(5)));
        Assert.Same(assigned, observed);
        Assert.StartsWith("explicit-", child.Name);
    }
}
