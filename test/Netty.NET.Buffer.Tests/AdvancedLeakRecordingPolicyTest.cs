/*
 * Copyright 2013 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer.Tests;

public class AdvancedLeakRecordingPolicyTest
{
    private const string Property = "io.netty.leakDetection.acquireAndReleaseOnly";
    private sealed class Tracker(ByteBuf expected) : IResourceLeakTracker<ByteBuf>
    {
        internal int Records, Closes;
        internal object Hint;
        public void Record() => ++Records;
        public void Record(object hint) { ++Records; Hint = hint; }
        public bool Close(ByteBuf value) { Assert.Same(expected, value); ++Closes; return true; }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ConfigurationIsAppliedInAFreshProcessWithoutChangingTheHost(bool referenceOnly)
    {
        string hostValue = Environment.GetEnvironmentVariable(Property);
        string report = Path.Combine(Path.GetTempPath(), "netty-leak-policy-" + Guid.NewGuid().ToString("N") + ".xml");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(typeof(AdvancedLeakRecordingPolicyTest).Assembly.Location)!
        };
        foreach (string argument in new[] { typeof(AdvancedLeakRecordingPolicyTest).Assembly.Location, "-method",
                     typeof(AdvancedLeakRecordingPolicyTest).FullName + ".RecordingPolicyProbe", "-noLogo", "-noAutoReporters", "-result-xml", report })
            start.ArgumentList.Add(argument);
        start.Environment[Property] = referenceOnly ? "true" : "false";
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        bool started = false;
        try
        {
            started = process.Start();
            Assert.True(started);
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string log = await output + await error;
            Assert.True(process.ExitCode == 0, log);
            XElement assembly = XDocument.Load(report).Descendants("assembly").Single();
            Assert.Equal("160", assembly.Attribute("total")?.Value);
            Assert.Equal("160", assembly.Attribute("passed")?.Value);
            Assert.Equal("0", assembly.Attribute("failed")?.Value);
            Assert.Equal("0", assembly.Attribute("skipped")?.Value);
            Assert.Equal(hostValue, Environment.GetEnvironmentVariable(Property));
        }
        finally
        {
            if (started && !process.HasExited) process.Kill(entireProcessTree: true);
            File.Delete(report);
        }
    }
    public static IEnumerable<object[]> Cases()
    {
        for (int provider = 0; provider < 4; ++provider)
        for (int depth = 1; depth <= 2; ++depth)
        for (int operation = 0; operation < 20; ++operation) yield return [provider, depth, operation];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RecordingPolicyProbe(int provider, int depth, int operation)
    {
        bool referenceOnly = SystemPropertyUtil.GetBoolean(Property, false), composite = provider >= 2;
        var native = new NativeMemoryAllocator(); var allocator = new UnpooledByteBufAllocator(false, true, native);
        ByteBuf Data(int count)
        {
            ByteBuf b = (provider & 1) != 0 ? allocator.DirectBuffer(count, 64) : allocator.HeapBuffer(count, 64);
            for (int i = 0; i < count; ++i) b.WriteByte(i + 1);
            return b;
        }
        ByteBuf root = composite ? new CompositeByteBuf(allocator, (provider & 1) != 0, 16, Data(32), Data(32)) : Data(64);
        root.SetIndex(2, 32); ByteBuf b = root; var trackers = new List<Tracker>();
        for (int i = 0; i < depth; ++i)
        {
            var tracker = new Tracker(b); trackers.Add(tracker);
            b = composite ? new AdvancedLeakAwareCompositeByteBuf((CompositeByteBuf)b, tracker) : new AdvancedLeakAwareByteBuf(b, tracker);
        }
        using var output = new MemoryStream(); using var input = new MemoryStream(new byte[] { 65, 66, 67, 68 });
        object hint = new();
        try
        {
            switch (operation)
            {
                case 0: Assert.Equal(4, b.GetByte(3)); break;
                case 1: Assert.Throws<ArgumentOutOfRangeException>(() => b.GetByte(-1)); break;
                case 2: Assert.Same(b, b.SetInt(3, 0x01020304)); break;
                case 3: Assert.Equal(3, b.ReadByte()); break;
                case 4: Assert.Same(b, b.WriteByte(17)); break;
                case 5: Assert.Equal(4, b.AsReadOnlyMemory(3, 4).Length); break;
                case 6: Assert.Equal(4, b.GetString(3, 4, Encoding.ASCII).Length); break;
                case 7: ByteBuf copy = b.Copy(3, 4); try { Assert.Equal(4, copy.ReadableBytes); } finally { copy.Release(); } break;
                case 8: Assert.Equal(4, b.Slice(3, 4).Capacity); break;
                case 9: Assert.Same(b, b.Retain()); break;
                case 10: Assert.Throws<ArgumentException>(() => b.Retain(0)); break;
                case 11: Assert.Throws<ArgumentException>(() => b.Release(0)); break;
                case 12: Assert.Same(b, b.Touch()); break;
                case 13: Assert.Same(b, b.Touch(hint)); break;
                case 14: Assert.Equal(-1, b.ForEachByte(3, 4, _ => true)); break;
                case 15: b.GetBytes(3, output, 4); Assert.Equal(4, output.Length); break;
                case 16: Assert.Equal(4, b.SetBytes(3, input, 4)); break;
                case 17: await b.GetBytesAsync(3, output, 4, TestContext.Current.CancellationToken); Assert.Equal(4, output.Length); break;
                case 18: Assert.Equal(4, await b.SetBytesAsync(3, input, 4, TestContext.Current.CancellationToken)); break;
                default: Assert.False(b.IsReadOnly); break;
            }
            int expected = operation is >= 9 and <= 13 ? 1 : operation == 19 && !composite || referenceOnly ? 0 : 1;
            // Touch describes the outer facade only; it intentionally does not delegate.
            for (int i = 0; i < trackers.Count; ++i)
            {
                int records = operation is 12 or 13 && i < depth - 1 ? 0 : expected;
                Assert.Equal(records, trackers[i].Records);
                Assert.Equal(0, trackers[i].Closes);
                if (operation == 13 && i == depth - 1) Assert.Same(hint, trackers[i].Hint);
            }
            Assert.Equal((operation == 3 ? 3 : 2, operation == 4 ? 33 : 32, operation == 9 ? 2 : 1),
                (root.ReaderIndex, root.WriterIndex, root.ReferenceCount));
        }
        finally { if (root.ReferenceCount > 0) b.Release(root.ReferenceCount); Assert.Equal(0, native.ReservedBytes); }
        Assert.All(trackers, t => Assert.Equal(1, t.Closes));
    }
}
