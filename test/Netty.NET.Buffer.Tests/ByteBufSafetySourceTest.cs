/*
 * Copyright 2012 The Netty Project
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

namespace Netty.NET.Buffer.Tests;

public class ByteBufSafetySourceTest
{
    public static IEnumerable<object[]> Cases()
    {
        for (int provider = 0; provider < 6; ++provider)
        for (int form = 0; form < 4; ++form)
        for (int operation = 0; operation < 16; ++operation) yield return [provider, form, operation];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void GuardProbe(int provider, int form, int operation)
    {
        using var storage = new ByteBufUtilitySourceTest.Storage(provider, 16);
        ByteBuf root = storage.Root.SetIndex(2, 4);
        ByteBuf b = form switch { 1 => root.Slice(0, 16), 2 => new ByteBufUtilitySourceTest.Wrapper(root), 3 => root.AsReadOnly(), _ => root };
        b.SetIndex(2, 4).MarkReaderIndex().MarkWriterIndex();
        if (operation is >= 12 and <= 14) Assert.True(root.Release());
        void Access()
        {
            switch (operation)
            {
                case 0: b.GetByte(-1); break; case 1: b.GetInt(int.MaxValue); break;
                case 2: b.AsReadOnlyMemory(17, 0); break; case 3: b.AsReadOnlyMemory(1, int.MaxValue); break;
                case 4: b.GetBytes(-1, Span<byte>.Empty); break; case 5: b.SetIndex(9, 8); break;
                case 6: b.ReadLong(); break; case 7: b.ReadBytes(int.MaxValue); break;
                case 8: b.SkipBytes(-1); break; case 9: b.SkipBytes(int.MaxValue); break;
                case 10: b.GetString(1, int.MaxValue, Encoding.UTF8); break;
                case 11: b.ForEachByte(0, int.MaxValue, _ => true); break;
                case 12: b.GetByte(0); break; case 13: b.AsReadOnlyMemory(0, 0); break;
                case 14: b.ReadBytes(0); break;
                default: b.AsReadOnly().SetBytes(0, ReadOnlySpan<byte>.Empty); break;
            }
        }
        if (operation is >= 12 and <= 14) Assert.Throws<IllegalReferenceCountException>(Access);
        else if (operation == 15) Assert.Throws<NotSupportedException>(Access);
        else Assert.Throws<ArgumentOutOfRangeException>(Access);
        Assert.Equal((2, 4), (b.ReaderIndex, b.WriterIndex));
        if (root.ReferenceCount > 0) { b.ResetReaderIndex().ResetWriterIndex(); Assert.Equal((2, 4), (b.ReaderIndex, b.WriterIndex)); }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ClrValidationRemainsEnabledRegardlessOfJvmOptOutFlags(bool enabled)
    {
        string[] properties = ["io.netty.buffer.checkBounds", "io.netty.buffer.checkAccessible", "io.netty.buffer.bytebuf.checkAccessible"];
        string[] before = properties.Select(Environment.GetEnvironmentVariable).ToArray();
        string report = Path.Combine(Path.GetTempPath(), "netty-buffer-safety-" + Guid.NewGuid().ToString("N") + ".xml");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(typeof(ByteBufSafetySourceTest).Assembly.Location)!
        };
        foreach (string arg in new[] { typeof(ByteBufSafetySourceTest).Assembly.Location, "-method",
                     typeof(ByteBufSafetySourceTest).FullName + ".GuardProbe", "-noLogo", "-noAutoReporters", "-result-xml", report }) start.ArgumentList.Add(arg);
        foreach (string property in properties) start.Environment[property] = enabled ? "true" : "false";
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30)); bool started = false;
        try
        {
            started = process.Start(); Assert.True(started);
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token), error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string log = await output + await error; Assert.True(process.ExitCode == 0, log);
            XElement assembly = XDocument.Load(report).Descendants("assembly").Single();
            Assert.Equal("384", assembly.Attribute("total")?.Value); Assert.Equal("384", assembly.Attribute("passed")?.Value);
            Assert.Equal("0", assembly.Attribute("failed")?.Value); Assert.Equal("0", assembly.Attribute("skipped")?.Value);
            Assert.Equal(before, properties.Select(Environment.GetEnvironmentVariable).ToArray());
        }
        finally { if (started && !process.HasExited) process.Kill(entireProcessTree: true); File.Delete(report); }
    }
}
