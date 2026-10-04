using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Tests.Porting;

public class NotificationRecursionContractTest
{
    [Theory]
    [InlineData(false, 64)]
    [InlineData(true, 64)]
    [InlineData(false, 10_000)]
    [InlineData(true, 10_000)]
    public void CrossInstanceReportsBoundRecursionAndRetainDeliveryOrder(bool throughCompletion, int count)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reporters = new ExecutorProgress[count];
        var completions = new ExecutorCompletion[count];
        int calls = 0, depth = 0, maximum = 0;
        var delivered = new List<int>();
        void Visit(int index)
        {
            maximum = Math.Max(maximum, ++depth);
            try
            {
                ++calls;
                delivered.Add(index);
                if (index + 1 == count) return;
                if (throughCompletion)
                    completions[index].Register(_ => reporters[index + 1].Report(new TransferProgress(1, 2)));
                else reporters[index + 1].Report(new TransferProgress(1, 2));
            }
            finally { --depth; }
        }
        try
        {
            for (int index = 0; index < count; index++)
            {
                int captured = index;
                reporters[index] = new ExecutorProgress(ImmediateEventExecutor.INSTANCE, source.Task, _ => Visit(captured));
                if (throughCompletion)
                    completions[index] = new ExecutorCompletion(ImmediateEventExecutor.INSTANCE, Task.CompletedTask);
            }
            reporters[0].Report(new TransferProgress(1, 2));
            Assert.Equal(count, calls);
            Assert.Equal(Enumerable.Range(0, count), delivered);
            Assert.InRange(maximum, 1, 16);
            Assert.Equal(0, depth);
        }
        finally
        {
            foreach (var reporter in reporters) reporter?.Dispose();
            foreach (var completion in completions) completion?.Dispose();
        }
    }
}
