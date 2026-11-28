/*
 * Copyright 2014 The Netty Project
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
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Handler.IpFilter;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Tests.IpFilter;

// CLR: InetSocketAddress is IPEndPoint and SocketUtils.socketAddress of a literal is new
// IPEndPoint(IPAddress.Parse(...), port), as in the codec ports. The protected accept(...) is reached through the
// internal IpSubnetFilter.InvokeAccept forwarder (Java package access). The anonymous IpFilterRule,
// RuleBasedIpFilter and EmbeddedChannel subclasses are named nested classes; IllegalArgumentException is
// ArgumentException, ArrayList is List, the anonymous Executables are lambdas and String.format("%d") is the
// invariant-culture text. readOutbound() is ReadOutbound<ByteBuf>() and the Java byte loop compares the values
// 1..7, which have the same value as a System.Byte (no sign is observable). close().isSuccess() is
// CloseAsync().IsCompletedSuccessfully; the embedded channel completes the close synchronously.
public class IpSubnetFilterTest
{

    // CLR: Inet4Address.getLoopbackAddress() and Inet6Address.getLoopbackAddress() both call the static
    // InetAddress.getLoopbackAddress() ("public static InetAddress getLoopbackAddress()",
    // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetAddress.html#getLoopbackAddress()),
    // which tries 127.0.0.1 first unless java.net.preferIPv6Addresses selects ::1 (Inet6AddressImpl.loopbackAddress(),
    // https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/java/net/Inet6AddressImpl.java).
    // With the default setting both calls give 127.0.0.1 (a ::1 address would match the "::1/16" rule of
    // noClassCastExceptionIpv6RuleOnly and fail its assertFalse), so both are IPAddress.Loopback.
    [Fact]
    public void NoClassCastExceptionIpv4RuleOnly()
    {
        IpSubnetFilterRule rule = new IpSubnetFilterRule("10.10.0.0/16", IpFilterRuleType.ACCEPT);
        IpSubnetFilter filter = new IpSubnetFilter(false, rule);
        Assert.False(filter.InvokeAccept(null, new IPEndPoint(IPAddress.Loopback, 80)));
        Assert.False(filter.InvokeAccept(null, new IPEndPoint(IPAddress.Loopback, 80)));
    }

    [Fact]
    public void NoClassCastExceptionIpv6RuleOnly()
    {
        IpSubnetFilterRule rule = new IpSubnetFilterRule("::1/16", IpFilterRuleType.ACCEPT);
        IpSubnetFilter filter = new IpSubnetFilter(false, rule);
        Assert.False(filter.InvokeAccept(null, new IPEndPoint(IPAddress.Loopback, 80)));
        Assert.False(filter.InvokeAccept(null, new IPEndPoint(IPAddress.Loopback, 80)));
    }

    [Fact]
    public void TestIpv4DefaultRoute()
    {
        IpSubnetFilterRule rule = new IpSubnetFilterRule("0.0.0.0", 0, IpFilterRuleType.ACCEPT);
        Assert.True(rule.Matches(NewSockAddress("91.114.240.43")));
        Assert.True(rule.Matches(NewSockAddress("10.0.0.3")));
        Assert.True(rule.Matches(NewSockAddress("192.168.93.2")));
    }

    [Fact]
    public void TestIpv4SubnetMaskCorrectlyHandlesIpv6()
    {
        IpSubnetFilterRule rule = new IpSubnetFilterRule("0.0.0.0", 0, IpFilterRuleType.ACCEPT);
        Assert.False(rule.Matches(NewSockAddress("2001:db8:abcd:0000::1")));
    }

    [Fact]
    public void TestIpv6SubnetMaskCorrectlyHandlesIpv4()
    {
        IpSubnetFilterRule rule = new IpSubnetFilterRule("::", 0, IpFilterRuleType.ACCEPT);
        Assert.False(rule.Matches(NewSockAddress("91.114.240.43")));
    }

    [Fact]
    public void TestIp4SubnetFilterRule()
    {
        IpSubnetFilterRule rule = new IpSubnetFilterRule("192.168.56.1", 24, IpFilterRuleType.ACCEPT);

        for (int i = 0; i <= 255; i++)
        {
            Assert.True(rule.Matches(NewSockAddress(string.Format(CultureInfo.InvariantCulture, "192.168.56.{0}", i))));
        }

        Assert.False(rule.Matches(NewSockAddress("192.168.57.1")));

        rule = new IpSubnetFilterRule("91.114.240.1", 23, IpFilterRuleType.ACCEPT);
        Assert.True(rule.Matches(NewSockAddress("91.114.240.43")));
        Assert.True(rule.Matches(NewSockAddress("91.114.240.255")));
        Assert.True(rule.Matches(NewSockAddress("91.114.241.193")));
        Assert.True(rule.Matches(NewSockAddress("91.114.241.254")));
        Assert.False(rule.Matches(NewSockAddress("91.115.241.2")));
    }

    [Fact]
    public void TestIp6SubnetFilterRule()
    {
        IpSubnetFilterRule rule;

        rule = new IpSubnetFilterRule("2001:db8:abcd:0000::", 52, IpFilterRuleType.ACCEPT);
        Assert.True(rule.Matches(NewSockAddress("2001:db8:abcd:0000::1")));
        Assert.True(rule.Matches(NewSockAddress("2001:db8:abcd:0fff:ffff:ffff:ffff:ffff")));
        Assert.False(rule.Matches(NewSockAddress("2001:db8:abcd:1000::")));
    }

    [Fact]
    public void TestIp6SubnetFilterDefaultRule()
    {
        IIpFilterRule rule = new IpSubnetFilterRule("::", 0, IpFilterRuleType.ACCEPT);
        Assert.True(rule.Matches(NewSockAddress("7FFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF:FFFF")));
        Assert.True(rule.Matches(NewSockAddress("8000::")));
    }

    // CLR: the deprecated RuleBasedIpFilter(IpFilterRule...) is not ported; it called this(true, rules), so the
    // handlers use its replacement RuleBasedIpFilter(true, filter0) with the same arguments. remoteAddress.getHostName()
    // reverse-resolves the address of an InetSocketAddress built from a literal ("may trigger a name service reverse
    // lookup", https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetSocketAddress.html) and gives
    // back the literal for these private test addresses; an IPEndPoint carries no host name, so the literal is
    // Address.ToString().
    [Fact]
    public void TestIpFilterRuleHandler()
    {
        IIpFilterRule filter0 = new HostNameRejectRule();

        RuleBasedIpFilter denyHandler = new DenyHandler(filter0);
        EmbeddedChannel chDeny = NewEmbeddedInetChannel("192.168.57.1", denyHandler);
        ByteBuf @out = chDeny.ReadOutbound<ByteBuf>();
        Assert.Equal(7, @out.ReadableBytes);

        for (byte i = 1; i <= 7; i++)
        {
            Assert.Equal(i, @out.ReadByte());
        }

        Assert.False(chDeny.IsActive);
        Assert.False(chDeny.IsOpen);

        RuleBasedIpFilter allowHandler = new AllowHandler(filter0);
        EmbeddedChannel chAllow = NewEmbeddedInetChannel("192.168.57.2", allowHandler);
        Assert.True(chAllow.IsActive);
        Assert.True(chAllow.IsOpen);
    }

    private sealed class HostNameRejectRule : IIpFilterRule
    {
        public bool Matches(IPEndPoint remoteAddress)
        {
            return "192.168.57.1".Equals(remoteAddress.Address.ToString());
        }

        public IpFilterRuleType RuleType()
        {
            return IpFilterRuleType.REJECT;
        }
    }

    private sealed class DenyHandler : RuleBasedIpFilter
    {
        private readonly byte[] message = {1, 2, 3, 4, 5, 6, 7};

        internal DenyHandler(IIpFilterRule filter0) : base(true, filter0)
        {
        }

        protected override Task ChannelRejected(IChannelHandlerContext ctx, IPEndPoint remoteAddress)
        {
            Assert.True(ctx.Channel.IsActive);
            Assert.True(ctx.Channel.IsWritable);
            Assert.Equal("192.168.57.1", remoteAddress.Address.ToString());

            return ctx.WriteAndFlushAsync(Unpooled.WrappedBuffer(message));
        }
    }

    private sealed class AllowHandler : RuleBasedIpFilter
    {
        internal AllowHandler(IIpFilterRule filter0) : base(true, filter0)
        {
        }

        protected override Task ChannelRejected(IChannelHandlerContext ctx, IPEndPoint remoteAddress)
        {
            Assert.Fail("");
            return null;
        }
    }

    // CLR: UniqueIpFilter forgets the address in a CloseFuture continuation, which runs on a thread-pool thread shortly
    // after finish() closes ch1, while the Java closeFuture() listener runs during the close (see UniqueIpFilter.cs). So
    // the channel for the address is created again until it is accepted, bounded by 10 seconds, before the source's
    // assertion; a rejected channel changes no filter state, because only an accepted address is added.
    [Fact]
    public async Task TestUniqueIpFilterHandler()
    {
        UniqueIpFilter handler = new UniqueIpFilter();

        EmbeddedChannel ch1 = NewEmbeddedInetChannel("91.92.93.1", handler);
        Assert.True(ch1.IsActive);
        EmbeddedChannel ch2 = NewEmbeddedInetChannel("91.92.93.2", handler);
        Assert.True(ch2.IsActive);
        EmbeddedChannel ch3 = NewEmbeddedInetChannel("91.92.93.1", handler);
        Assert.False(ch3.IsActive);

        // false means that no data is left to read/write
        Assert.False(ch1.Finish());

        EmbeddedChannel ch4 = NewEmbeddedInetChannel("91.92.93.1", handler);

        for (long deadline = Environment.TickCount64 + 10000;
             !ch4.IsActive && Environment.TickCount64 < deadline;)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
            ch4 = NewEmbeddedInetChannel("91.92.93.1", handler);
        }

        Assert.True(ch4.IsActive);
    }

    [Fact]
    public void TestBinarySearch()
    {
        List<IpSubnetFilterRule> ipSubnetFilterRuleList = new List<IpSubnetFilterRule>();
        ipSubnetFilterRuleList.Add(BuildRejectIP("1.2.3.4", 32));
        ipSubnetFilterRuleList.Add(BuildRejectIP("1.1.1.1", 8));
        ipSubnetFilterRuleList.Add(BuildRejectIP("200.200.200.200", 32));
        ipSubnetFilterRuleList.Add(BuildRejectIP("108.0.0.0", 4));
        ipSubnetFilterRuleList.Add(BuildRejectIP("10.10.10.10", 8));
        ipSubnetFilterRuleList.Add(BuildRejectIP("2001:db8:abcd:0000::", 52));

        // 1.0.0.0/8
        EmbeddedChannel ch1 = NewEmbeddedInetChannel("1.1.1.1", new IpSubnetFilter(ipSubnetFilterRuleList));
        Assert.False(ch1.IsActive);
        Assert.True(ch1.CloseAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);

        // Nothing applies here
        EmbeddedChannel ch2 = NewEmbeddedInetChannel("2.2.2.2", new IpSubnetFilter(ipSubnetFilterRuleList));
        Assert.True(ch2.IsActive);
        Assert.True(ch2.CloseAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);

        // 108.0.0.0/4
        EmbeddedChannel ch3 = NewEmbeddedInetChannel("97.100.100.100", new IpSubnetFilter(ipSubnetFilterRuleList));
        Assert.False(ch3.IsActive);
        Assert.True(ch3.CloseAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);

        // 200.200.200.200/32
        EmbeddedChannel ch4 = NewEmbeddedInetChannel("200.200.200.200", new IpSubnetFilter(ipSubnetFilterRuleList));
        Assert.False(ch4.IsActive);
        Assert.True(ch4.CloseAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);

        // Nothing applies here
        EmbeddedChannel ch5 = NewEmbeddedInetChannel("127.0.0.1", new IpSubnetFilter(ipSubnetFilterRuleList));
        Assert.True(ch5.IsActive);
        Assert.True(ch5.CloseAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);

        // 10.0.0.0/8
        EmbeddedChannel ch6 = NewEmbeddedInetChannel("10.1.1.2", new IpSubnetFilter(ipSubnetFilterRuleList));
        Assert.False(ch6.IsActive);
        Assert.True(ch6.CloseAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);

        //2001:db8:abcd:0000::/52
        EmbeddedChannel ch7 = NewEmbeddedInetChannel("2001:db8:abcd:0000::1",
                new IpSubnetFilter(ipSubnetFilterRuleList));
        Assert.False(ch7.IsActive);
        Assert.True(ch7.CloseAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);
    }

    [Fact]
    public void TestIpv6MaskCorrectlyApplied()
    {
        IpSubnetFilterRule rule = new IpSubnetFilterRule("2001:db8:abcd:0000::", 52, IpFilterRuleType.ACCEPT);

        EmbeddedChannel ch = NewEmbeddedInetChannel("2001:db8:ffff:0000::",
                new IpSubnetFilter(false, Array.AsReadOnly(new[] { rule })));
        Assert.False(ch.IsActive);
        Assert.True(ch.CloseAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);
    }

    [Fact]
    public void TestIpv6MatchesNoFalsePositiveForAllOnesNetworkBits()
    {
        // FFFF:FFFF::1 is NOT in 2001:db8::/32, which will be the case if the comparison is made unsigned.
        IpSubnetFilterRule rule = new IpSubnetFilterRule("2001:db8::", 32, IpFilterRuleType.ACCEPT);
        Assert.False(rule.Matches(NewSockAddress("FFFF:FFFF::1")));
     }

    [Fact]
    public void TestOverlappingRulesWithDifferentRuleTypeThrows()
    {
        // ACCEPT 10.0.0.0/8 followed by REJECT 10.5.0.0/16: the REJECT rule is fully contained within
        // the broader ACCEPT supernet. Silently dropping it during de-duplication would turn the REJECT
        // into an implicit ACCEPT, so construction must fail loudly instead.
        List<IpSubnetFilterRule> rules = new List<IpSubnetFilterRule>();
        rules.Add(new IpSubnetFilterRule("10.0.0.0", 8, IpFilterRuleType.ACCEPT));
        rules.Add(new IpSubnetFilterRule("10.5.0.0", 16, IpFilterRuleType.REJECT));

        Assert.Throws<ArgumentException>(() =>
        {
            new IpSubnetFilter(rules);
        });
    }

    [Fact]
    public void TestOverlappingRulesWithSameRuleTypeDoesNotThrow()
    {
        // Both rules are ACCEPT, so coalescing the more specific one is semantically safe.
        List<IpSubnetFilterRule> rules = new List<IpSubnetFilterRule>();
        rules.Add(new IpSubnetFilterRule("10.0.0.0", 8, IpFilterRuleType.ACCEPT));
        rules.Add(new IpSubnetFilterRule("10.5.0.0", 16, IpFilterRuleType.ACCEPT));

        IpSubnetFilter filter = new IpSubnetFilter(false, rules);
        Assert.True(filter.InvokeAccept(null, NewSockAddress("10.5.1.1")));
        Assert.True(filter.InvokeAccept(null, NewSockAddress("10.1.1.1")));
    }

    private static IpSubnetFilterRule BuildRejectIP(string ipAddress, int mask)
    {
        return new IpSubnetFilterRule(ipAddress, mask, IpFilterRuleType.REJECT);
    }

    private static EmbeddedChannel NewEmbeddedInetChannel(string ipAddress, params IChannelHandler[] handlers)
    {
        return new InetEmbeddedChannel(ipAddress, handlers);
    }

    // CLR: the anonymous EmbeddedChannel captures ipAddress, which remoteAddress0() already reads while the
    // EmbeddedChannel constructor registers the channel (the filters decide at registration, as the isActive()
    // assertions right after the constructor show). A C# field initializer runs "before the invocation of the base
    // class instance constructor" and may read a primary constructor parameter
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#15114-constructor-execution),
    // so the address is stored before the EmbeddedChannel constructor runs.
    private sealed class InetEmbeddedChannel(string ipAddress, IChannelHandler[] handlers) : EmbeddedChannel(handlers)
    {
        private readonly string ipAddress = ipAddress;

        protected override EndPoint RemoteAddress0()
        {
            return IsActive? NewSockAddress(ipAddress, 5421) : null;
        }
    }

    private static IPEndPoint NewSockAddress(string ipAddress)
    {
        return NewSockAddress(ipAddress, 1234);
    }

    // CLR: SocketUtils.socketAddress(ipAddress, port) of a literal (see the class comment).
    private static IPEndPoint NewSockAddress(string ipAddress, int port)
    {
        return new IPEndPoint(IPAddress.Parse(ipAddress), port);
    }
}
