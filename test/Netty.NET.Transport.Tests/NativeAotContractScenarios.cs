/*
 * Copyright 2022 The Netty Project
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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;
using Skip = Netty.NET.Transport.Channels.ChannelHandlerMask.SkipAttribute;

namespace Netty.NET.Transport.Tests;

// Supplemental CLR contracts shared by xUnit and an actual NativeAOT consumer executable.
// The original GraalVM JSON/configuration comparison is a separate JVM-only test contract.
internal static class NativeAotContractScenarios
{
    internal const int Count = 11;
    internal static void Run(int scenario)
    {
        switch (scenario)
        {
            case 0: InheritedSkipDoesNotHideAnOverride(); break;
            case 1: ExplicitSkipUsesTheActualInterfaceTarget(); break;
            case 2: ExplicitActiveInterfaceTargetIsInvoked(); break;
            case 3: OutboundOverridesKeepWriteAndFlush(); break;
            case 4: AHiddenNewSlotDoesNotReplaceTheInterfaceTarget(); break;
            case 5: CachedMasksDoNotCaptureTheFirstInstance(); break;
            case 6: DefaultForwardersAndActiveMasksStayPrecise(); break;
            case 7: ExclusionsKeepUnusedPublicAndPrivateMethods(); break;
            case 8: ExceptionOnlyHandlersStillReceiveTheirCause(); break;
            case 9: StreamWriteOwnershipSurvivesNativeCompilation(); break;
            case 10: DirectImplementationsKeepTheirMetadata(); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Read(IChannelHandler handler, Func<int> reads, int expected)
    {
        var channel = new EmbeddedChannel(handler);
        try
        {
            Check(channel.WriteInbound("message"), "Inbound data was lost.");
            Check(reads() == expected, "The wrong inbound implementation was selected.");
            Check(channel.ReadInbound<string>() == "message", "Inbound data changed.");
        }
        finally { channel.FinishAndReleaseAll(); }
    }
    private static void InheritedSkipDoesNotHideAnOverride()
    { var handler = new ActiveRead(); Read(handler, () => handler.Reads, 1); }
    private static void ExplicitSkipUsesTheActualInterfaceTarget()
    { var handler = new ExplicitSkippedRead(); Read(handler, () => handler.Reads, 0); }
    private static void ExplicitActiveInterfaceTargetIsInvoked()
    { var handler = new ExplicitRead(); Read(handler, () => handler.Reads, 1); }
    private static void AHiddenNewSlotDoesNotReplaceTheInterfaceTarget()
    {
        var handler = new HiddenRead();
        Read(handler, () => handler.Reads, 1);
        Check(handler.HiddenReads == 0, "A hidden new slot replaced the interface implementation.");
    }
    private static void OutboundOverridesKeepWriteAndFlush()
    {
        var handler = new ActiveWrite();
        var channel = new EmbeddedChannel(handler);
        try
        {
            channel.WriteAndFlushAsync("message").GetAwaiter().GetResult();
            Check(handler.Writes == 1 && handler.Flushes == 1, "Outbound overrides were lost.");
            Check(channel.ReadOutbound<string>() == "message", "Outbound data changed.");
        }
        finally { channel.FinishAndReleaseAll(); }
    }
    private static void CachedMasksDoNotCaptureTheFirstInstance()
    {
        var first = new ActiveRead(); var second = new ActiveRead();
        Read(first, () => first.Reads, 1);
        Check(second.Reads == 0, "A cached delegate captured another instance.");
        Read(second, () => second.Reads, 1);
        Check(first.Reads == 1, "The second instance dispatched to the first.");
    }
    private static void DefaultForwardersAndActiveMasksStayPrecise()
    {
        Check(ChannelHandlerMask.Mask(new ChannelDuplexHandler()) == 0, "Default forwarders were not skipped.");
        Check(ChannelHandlerMask.Mask(new ActiveRead()) == ChannelHandlerMask.MaskChannelRead, "An inherited skip hid an active read.");
        Check(ChannelHandlerMask.Mask(new ExplicitSkippedRead()) == 0, "The explicit skip attribute was lost.");
        Check(ChannelHandlerMask.Mask(new ActiveWrite()) == (ChannelHandlerMask.MaskWrite | ChannelHandlerMask.MaskFlush), "Outbound mask changed.");
    }
    private static void ExclusionsKeepUnusedPublicAndPrivateMethods()
    {
        ResourceLeakDetector.AddExclusions(typeof(Exclusions), "UnusedPublic", "UnusedPrivate");
        try { ResourceLeakDetector.AddExclusions(typeof(Exclusions), "MissingMethod"); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Missing exclusion methods must still be rejected.");
    }
    private static void ExceptionOnlyHandlersStillReceiveTheirCause()
    {
        var handler = new ErrorHandler(); var channel = new EmbeddedChannel(new ChannelInboundHandlerAdapter(), handler);
        var cause = new Exception("native exception");
        try { channel.Pipeline.FireExceptionCaught(cause); Check(ReferenceEquals(cause, handler.Cause), "Exception callback was lost."); }
        finally { channel.FinishAndReleaseAll(); }
    }
    private static void StreamWriteOwnershipSurvivesNativeCompilation()
    {
        var inherited = new DerivedSocket(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp));
        var overridden = new CovariantSocket(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp));
        try
        {
            Check(inherited.SupportsGatheringWrites, "Inherited native stream writes were lost.");
            Check(!overridden.SupportsGatheringWrites, "A covariant whole-message override lost ownership.");
        }
        finally { inherited.CloseForcibly(); overridden.CloseForcibly(); }
    }
    private static void DirectImplementationsKeepTheirMetadata()
    { var handler = new DirectRead(); Read(handler, () => handler.Reads, 1); }

    private class SkippedRead : ChannelInboundHandlerAdapter
    {
        [Skip] public override void ChannelRead(ChannelHandlerContext ctx, object msg) => ctx.FireChannelRead(msg);
    }
    private class ActiveRead : SkippedRead
    {
        internal int Reads;
        public override void ChannelRead(ChannelHandlerContext ctx, object msg) { Reads++; ctx.FireChannelRead(msg); }
    }
    private sealed class HiddenRead : ActiveRead
    {
        internal int HiddenReads;
        [Skip] public new void ChannelRead(ChannelHandlerContext ctx, object msg) { HiddenReads++; ctx.FireChannelRead(msg); }
    }
    private sealed class ExplicitSkippedRead : ChannelInboundHandlerAdapter, IChannelInboundHandler
    {
        internal int Reads;
        [Skip] void IChannelInboundHandler.ChannelRead(ChannelHandlerContext ctx, object msg) { Reads++; ctx.FireChannelRead(msg); }
    }
    private sealed class ExplicitRead : ChannelInboundHandlerAdapter, IChannelInboundHandler
    {
        internal int Reads;
        void IChannelInboundHandler.ChannelRead(ChannelHandlerContext ctx, object msg) { Reads++; ctx.FireChannelRead(msg); }
    }
    private sealed class ActiveWrite : ChannelDuplexHandler
    {
        internal int Writes, Flushes;
        public override Task WriteAsync(ChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        { Writes++; return ctx.WriteAsync(msg, cancellationToken); }
        public override void Flush(ChannelHandlerContext ctx) { Flushes++; ctx.Flush(); }
    }
    private sealed class Exclusions
    {
        public static void UnusedPublic() { }
        private static void UnusedPrivate() { }
    }
    private sealed class ErrorHandler : ChannelHandlerAdapter
    {
        internal Exception Cause;
        public override void ExceptionCaught(ChannelHandlerContext ctx, Exception cause) => Cause = cause;
    }
    private sealed class DirectRead : IChannelInboundHandler
    {
        internal int Reads;
        public void HandlerAdded(ChannelHandlerContext ctx) { }
        public void HandlerRemoved(ChannelHandlerContext ctx) { }
        public void ExceptionCaught(ChannelHandlerContext ctx, Exception cause) => ctx.FireExceptionCaught(cause);
        public void ChannelRegistered(ChannelHandlerContext ctx) => ctx.FireChannelRegistered();
        public void ChannelUnregistered(ChannelHandlerContext ctx) => ctx.FireChannelUnregistered();
        public void ChannelActive(ChannelHandlerContext ctx) => ctx.FireChannelActive();
        public void ChannelInactive(ChannelHandlerContext ctx) => ctx.FireChannelInactive();
        public void ChannelRead(ChannelHandlerContext ctx, object msg) { Reads++; ctx.FireChannelRead(msg); }
        public void ChannelReadComplete(ChannelHandlerContext ctx) => ctx.FireChannelReadComplete();
        public void UserEventTriggered(ChannelHandlerContext ctx, object evt) => ctx.FireUserEventTriggered(evt);
        public void ChannelWritabilityChanged(ChannelHandlerContext ctx) => ctx.FireChannelWritabilityChanged();
    }
    private sealed class DerivedSocket(Socket socket) : TcpSocketChannel(socket);
    private sealed class CovariantSocket(Socket socket) : TcpSocketChannel(socket)
    {
        protected override Task<int> DoWrite(object msg) => Task.FromResult(0);
    }
}
