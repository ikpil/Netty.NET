using System;
using System.Reflection;
using System.Threading;

namespace Netty.NET.Common.Tests.Porting;

public class AttributeMapInterruptContractTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InterruptedDictionaryWritesFinishAndPreserveThePendingInterrupt(int operation)
    {
        var map = new DefaultAttributeMap();
        var key = AttributeKey.ValueOf<object>(Guid.NewGuid().ToString());
        IAttribute<object> old = operation == 0 ? null : map.Attr(key);
        var payload = new object();
        old?.Set(payload);
        if (operation == 1)
        {
            // Pause after detaching the owner, before removal, to force Attr's
            // replacement path. Publication and removal use the public operations.
            old.GetType().GetField("attributeMap", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(old, null);
        }
        object dictionary = Field(map, "attributes");
        var locks = (object[])Field(Field(dictionary, "_tables"), "_locks");
        IAttribute<object> current = null;
        object removed = null;
        Exception failure = null;
        bool pendingInterrupt = false;
        bool waited = false;
        var worker = new Thread(() =>
        {
            try
            {
                if (operation == 2) removed = old.GetAndRemove();
                else current = map.Attr(key);
                try { Thread.Sleep(0); }
                catch (ThreadInterruptedException) { pendingInterrupt = true; }
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        int acquired = 0;
        bool started = false;
        try
        {
            // Hold the actual native bucket monitors; reflection schedules the
            // contention only, and no other owner shares this dictionary.
            foreach (object gate in locks) { Monitor.Enter(gate); acquired++; }
            worker.Start();
            started = true;
            waited = SpinWait.SpinUntil(() => (worker.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5));
            if (waited) worker.Interrupt();
        }
        finally
        {
            while (acquired > 0) Monitor.Exit(locks[--acquired]);
            if (started) Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        }
        Assert.True(waited);
        Assert.Null(failure);
        Assert.True(pendingInterrupt);
        if (operation == 2)
        {
            Assert.Same(payload, removed);
            Assert.Null(old.Get());
            Assert.False(map.HasAttr(key));
        }
        else
        {
            Assert.NotNull(current);
            Assert.Same(current, map.Attr(key));
            Assert.Null(current.Get());
            Assert.True(map.HasAttr(key));
            if (operation == 1) Assert.NotSame(old, current);
        }
    }

    private static object Field(object owner, string name) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
}
