/*
 * Copyright 2026 The Netty Project
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
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Dns;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Resolver.Dns.Tests;

/**
 * Regression test for the missing {@code return;} in
 * {@link DnsQueryContext#writeQuery(boolean)} after
 * {@code finishFailure()} is called when the query-ID space is exhausted.
 *
 * <p>When {@code queryContextManager.add(...)} returns {@code -1} the method
 * must fail the promise and return immediately.  Without the fix, execution
 * falls through: a second {@code queryLifecycleObserver.queryWritten()} call
 * is made, {@code newQuery(-1, ...)} builds a bogus datagram with id 65535,
 * and {@code sendQuery()} actually writes it to the channel.
 */
// CLR: channel.eventLoop().newPromise() is a TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> (the
// promise type of DnsQueryContext); isDone() is Task.IsCompleted and cause() the exception of the faulted Task.
// new InetSocketAddress(InetAddress.getLoopbackAddress(), 53) is IPEndPoint(IPAddress.Loopback, 53) (127.0.0.1, the
// loopback address InetAddress.getLoopbackAddress() returns when IPv4 is preferred). IllegalStateException is
// InvalidOperationException. AssertJ's assertThat(x).as(format, args).isGreaterThanOrEqualTo(0) is Assert.True with the
// formatted description; the JUnit asserts with messages use XunitAssertMessages.AssertM and assertInstanceOf is
// IsAssignableFrom. readOutbound() is ReadOutbound() and finishAndReleaseAll() FinishAndReleaseAll().
public class DnsQueryContextTest
{

    /**
     * Regression test: when the DNS transaction-ID space is exhausted,
     * {@code writeQuery} must NOT write any message to the channel.
     *
     * <p>The bug: a missing {@code return;} after {@code finishFailure()} caused
     * fall-through into {@code sendQuery()}, writing a query with id -1 (encoded
     * as 65535) to the channel even though the promise was already failed.
     *
     * <p>This test is intentionally RED against the unfixed code: with the bug
     * present, {@code channel.readOutbound()} returns a non-null
     * {@link io.netty.handler.codec.dns.DatagramDnsQuery}, causing the assertion
     * {@code assertNull(outbound)} to fail.
     */
    [Fact]
    public void WriteQueryMustNotSendWhenIdSpaceExhausted()
    {
        EmbeddedChannel channel = new EmbeddedChannel();
        try
        {
            IPEndPoint nameServerAddr =
                    new IPEndPoint(IPAddress.Loopback, 53);

            DnsQueryContextManager manager = new DnsQueryContextManager();

            // Exhaust the entire ID space for nameServerAddr.
            // DnsQueryIdSpace.maxUsableIds() == 65536 (IDs 0..65535).
            // We need one dummy context object to fill the manager; reusing the
            // same instance is safe because DnsQueryContextMap stores contexts
            // by ID (which is unique per add()), not by identity.
            //
            // We create the dummy with a separate promise so that it is never
            // written and does not interfere with the context-under-test.
            TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> dummyPromise =
                    new TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>>();
            DatagramDnsQueryContext dummyCtx = new DatagramDnsQueryContext(
                    channel,
                    nameServerAddr,
                    manager,
                    NoopDnsQueryLifecycleObserver.INSTANCE,
                    /* maxPayLoadSize */ 4096,
                    /* recursionDesired */ true,
                    /* queryTimeoutMillis */ 5000,
                    new DefaultDnsQuestion("dummy.netty.io.", DnsRecordType.A),
                    new IDnsRecord[0],
                    dummyPromise,
                    /* socketBootstrap */ null,
                    /* retryWithTcpOnTimeout */ false);

            // Drain all 65536 IDs from the id space for nameServerAddr.
            DnsQueryIdSpace idSpace = new DnsQueryIdSpace();
            int maxIds = idSpace.MaxUsableIds(); // 65536

            for (int i = 0; i < maxIds; i++)
            {
                int assignedId = manager.Add(nameServerAddr, dummyCtx);
                Assert.True(assignedId >= 0,
                        string.Format(CultureInfo.InvariantCulture,
                                "Expected a valid id during exhaustion, got -1 at iteration {0}", i));
            }

            // The next add must return -1 — the space is now full.
            int overflowId = manager.Add(nameServerAddr, dummyCtx);
            Assert.Equal(-1, overflowId,
                    "Expected -1 when ID space is exhausted");

            // Build the context-under-test that will be subjected to writeQuery.
            TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>> testPromise =
                    new TaskCompletionSource<IAddressedEnvelope<IDnsResponse, IPEndPoint>>();
            DatagramDnsQueryContext ctx = new DatagramDnsQueryContext(
                    channel,
                    nameServerAddr,
                    manager,
                    NoopDnsQueryLifecycleObserver.INSTANCE,
                    /* maxPayLoadSize */ 4096,
                    /* recursionDesired */ true,
                    /* queryTimeoutMillis */ 5000,
                    new DefaultDnsQuestion("test.netty.io.", DnsRecordType.A),
                    new IDnsRecord[0],
                    testPromise,
                    /* socketBootstrap */ null,
                    /* retryWithTcpOnTimeout */ false);

            // Invoke writeQuery — this should detect id == -1, call finishFailure(),
            // and return WITHOUT writing anything to the channel.
            ctx.WriteQuery(true);

            // ---- Assertions ----

            // 1. The promise must be completed and failed (correct behaviour both
            //    before and after the fix).
            Assert.True(testPromise.Task.IsCompleted,
                    "Promise should be done after writeQuery with exhausted ID space");
            Exception cause = testPromise.Task.Exception?.InnerException;
            Assert.IsAssignableFrom<DnsNameResolverException>(cause,
                    "Promise should be failed with a DnsNameResolverException when ID space is exhausted");
            Assert.IsAssignableFrom<InvalidOperationException>(cause.InnerException,
                    "The root cause should be the IllegalStateException reporting the exhausted ID space");

            // 2. No outbound message should have been written to the channel.
            //    With the bug present the fall-through reaches sendQuery(), which
            //    calls channel.writeAndFlush(query) with a bogus id (-1/65535),
            //    so readOutbound() returns a non-null DatagramDnsQuery — the test
            //    fails here on unfixed code, confirming the RED state.
            object outbound = channel.ReadOutbound();
            // Release the outbound message regardless of outcome so that the
            // reference-counted DatagramDnsQuery (written by the buggy fall-through)
            // does not trigger a secondary memory-leak error that would obscure the
            // primary assertion failure.
            ReferenceCountUtil.Release(outbound);
            Assert.Null(outbound,
                    "No DNS query must be written to the channel when ID space is exhausted, " +
                    "but found: " + outbound);
        }
        finally
        {
            channel.FinishAndReleaseAll();
        }
    }
}
