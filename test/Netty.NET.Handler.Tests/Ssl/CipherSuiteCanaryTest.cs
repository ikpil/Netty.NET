/*
 * Copyright 2018 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version
 * 2.0 (the "License"); you may not use this file except in compliance with the
 * License. You may obtain a copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Handler.Tests.Ssl;

/**
 * The purpose of this unit test is to act as a canary and catch changes in supported cipher suites.
 */
// CLR: the static @BeforeAll init() and @AfterAll destroy() run from the class fixture BeforeAllAndAfterAll, which
// xUnit creates before the first test of the class and disposes after the last one. @ParameterizedTest with
// @MethodSource is [Theory] with [MemberData], and assumeTrue is Assert.SkipUnless. SslProvider is a sealed class
// (ssl context wave), so SslProvider.values() is its three constants in declaration order; OpenSsl is
// library-not-ported (netty-tcnative; owner decision 2026-11-24, docs/handler-ssl-design.md) and reports
// unavailable, so expand(...) keeps the JDK/JDK pair only. SslContext.newHandler(alloc, Executor) is not ported
// (SslStream has no delegated tasks; ssl context wave), so expand(...) generates no delegate rows and the test has
// no delegate parameter, executor or ExecutorService. CERT.getKeyPair().getPrivate() is the bundle's BCL key
// object and getCertificatePath() its X509Certificate2 path. Promise<Object> is a TaskCompletionSource<object>
// (cancel(true) is TrySetCanceled, trySuccess/tryFailure TrySetResult/TrySetException), await(5, SECONDS) waits
// for the Task with Task.WhenAny and a five-second delay, and sync()/syncUninterruptibly() are awaits; the
// bootstrap API is asynchronous. The anonymous handlers are named nested classes and the char literals of the
// byte[] initializers are cast to byte. The cipher suites become a CipherSuitesPolicy, which SslStream does not
// support on Windows (SslContext.newHandler throws PlatformNotSupportedException there,
// docs/handler-ssl-design.md, "No cipher-suite selection on Windows"), so the test is skipped on Windows and runs
// where CipherSuitesPolicy exists (Linux, macOS).
public class CipherSuiteCanaryTest : IClassFixture<CipherSuiteCanaryTest.BeforeAllAndAfterAll>
{

    private static IEventLoopGroup GROUP;

    private static X509Bundle CERT;

    public static IEnumerable<object[]> Parameters()
    {
       List<object[]> dst = new List<object[]>();
       dst.AddRange(Expand("TLS_DHE_RSA_WITH_AES_128_GCM_SHA256")); // DHE-RSA-AES128-GCM-SHA256
       return dst;
    }

    private static void Init()
    {
        GROUP = new MultiThreadIoEventLoopGroup(LocalIoHandler.NewFactory());
        CERT = new CertificateBuilder()
                .Rsa2048()
                .Subject("cn=localhost")
                .SetIsCertificateAuthority(true)
                .BuildSelfSigned();
    }

    private static void Destroy()
    {
        _ = GROUP.ShutdownGracefullyAsync();
    }

    public sealed class BeforeAllAndAfterAll : IDisposable
    {
        public BeforeAllAndAfterAll()
        {
            Init();
        }

        public void Dispose()
        {
            Destroy();
        }
    }

    // CLR: SSLContext.getDefault().createSSLEngine().getSupportedCipherSuites() is the set of TlsCipherSuite names, the
    // cipher suites JdkSslContext reports as supported (its SupportedCiphers(); "field names and values are based on the
    // TLS Cipher Suites list from the Internet Assigned Numbers Authority (IANA)",
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.security.tlsciphersuite?view=net-10.0).
    // OpenSsl.isCipherSuiteAvailable is library-not-ported, so the other providers report the cipher as unsupported.
    private static void AssumeCipherAvailable(SslProvider provider, string cipher)
    {
        bool cipherSupported = false;

        if (provider == SslProvider.JDK)
        {
            foreach (string c in Enum.GetNames<TlsCipherSuite>())
            {
               if (cipher.Equals(c))
               {
                   cipherSupported = true;
                   break;
               }
            }
        }
        else
        {
            cipherSupported = false;
        }

        Assert.SkipUnless(cipherSupported, "Unsupported cipher: " + cipher);
    }

    private static SslHandler NewSslHandler(SslContext sslCtx, IByteBufAllocator allocator)
    {
        return sslCtx.NewHandler(allocator);
    }

    [Theory]
    [MemberData(nameof(Parameters))]
    public async Task TestHandshake(SslProvider serverSslProvider, SslProvider clientSslProvider,
                              string rfcCipherName)
    {
        // Check if the cipher is supported at all which may not be the case for various JDK versions and OpenSSL API
        // implementations.
        AssumeCipherAvailable(serverSslProvider, rfcCipherName);
        AssumeCipherAvailable(clientSslProvider, rfcCipherName);
        Assert.SkipWhen(OperatingSystem.IsWindows(),
                "CipherSuitesPolicy is not supported on Windows (SslContextBuilder.ciphers cannot be applied)");

        List<string> ciphers = new List<string> { rfcCipherName };

        AsymmetricAlgorithm privateKey = (AsymmetricAlgorithm) CERT.GetKeyPair();
        X509Certificate2[] certChain = CERT.GetCertificatePath();
        SslContext sslServerContext = SslContextBuilder.ForServer(privateKey, certChain)
                .SslProvider(serverSslProvider)
                .Ciphers(ciphers)
                // As this is not a TLSv1.3 cipher we should ensure we talk something else.
                .Protocols(Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_2)
                .Build();

        try
        {
            SslContext sslClientContext = SslContextBuilder.ForClient()
                    .SslProvider(clientSslProvider)
                    .Ciphers(ciphers)
                    // As this is not a TLSv1.3 cipher we should ensure we talk something else.
                    .Protocols(Netty.NET.Handler.Ssl.SslProtocols.TLS_v1_2)
                    .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                    .Build();

            try
            {
                TaskCompletionSource<object> serverPromise =
                        new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource<object> clientPromise =
                        new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

                IChannelHandler serverHandler = new ServerInitializer(sslServerContext, serverPromise);

                LocalAddress address = new LocalAddress("test-" + serverSslProvider
                        + '-' + clientSslProvider + '-' + rfcCipherName);

                IChannel server = await Server(address, serverHandler);

                try
                {
                    IChannelHandler clientHandler = new ClientInitializer(sslClientContext, clientPromise);

                    IChannel client = await Client(server, clientHandler);

                    try
                    {
                        await client.WriteAndFlushAsync(
                                Unpooled.WrappedBuffer(new byte[] {(byte) 'P', (byte) 'I', (byte) 'N', (byte) 'G'}),
                                TestContext.Current.CancellationToken);

                        Assert.True(await Task.WhenAny(clientPromise.Task, Task.Delay(TimeSpan.FromSeconds(5L),
                                TestContext.Current.CancellationToken)) == clientPromise.Task, "client timeout");
                        Assert.True(await Task.WhenAny(serverPromise.Task, Task.Delay(TimeSpan.FromSeconds(5L),
                                TestContext.Current.CancellationToken)) == serverPromise.Task, "server timeout");

                        await clientPromise.Task;
                        await serverPromise.Task;
                    }
                    finally
                    {
                        await client.CloseAsync(TestContext.Current.CancellationToken);
                    }
                }
                finally
                {
                    await server.CloseAsync(TestContext.Current.CancellationToken);
                }
            }
            finally
            {
                ReferenceCountUtil.Release(sslClientContext);
            }
        }
        finally
        {
            ReferenceCountUtil.Release(sslServerContext);
        }
    }

    private sealed class ServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslServerContext;
        private readonly TaskCompletionSource<object> serverPromise;

        internal ServerInitializer(SslContext sslServerContext, TaskCompletionSource<object> serverPromise)
        {
            this.sslServerContext = sslServerContext;
            this.serverPromise = serverPromise;
        }

        protected override void InitChannel(IChannel ch)
        {
            IChannelPipeline pipeline = ch.Pipeline;
            pipeline.AddLast(NewSslHandler(sslServerContext, ch.Alloc()));

            pipeline.AddLast(new ServerHandler(serverPromise));
        }
    }

    private sealed class ServerHandler : SimpleChannelInboundHandler<object>
    {
        private readonly TaskCompletionSource<object> serverPromise;

        internal ServerHandler(TaskCompletionSource<object> serverPromise)
        {
            this.serverPromise = serverPromise;
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            serverPromise.TrySetCanceled();
            ctx.FireChannelInactive();
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            if (serverPromise.TrySetResult(null))
            {
                _ = ctx.WriteAndFlushAsync(
                        Unpooled.WrappedBuffer(new byte[] {(byte) 'P', (byte) 'O', (byte) 'N', (byte) 'G'}));
            }

            _ = ctx.CloseAsync();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (!serverPromise.TrySetException(cause))
            {
                ctx.FireExceptionCaught(cause);
            }
        }
    }

    private sealed class ClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SslContext sslClientContext;
        private readonly TaskCompletionSource<object> clientPromise;

        internal ClientInitializer(SslContext sslClientContext, TaskCompletionSource<object> clientPromise)
        {
            this.sslClientContext = sslClientContext;
            this.clientPromise = clientPromise;
        }

        protected override void InitChannel(IChannel ch)
        {
            IChannelPipeline pipeline = ch.Pipeline;
            pipeline.AddLast(NewSslHandler(sslClientContext, ch.Alloc()));

            pipeline.AddLast(new ClientHandler(clientPromise));
        }
    }

    private sealed class ClientHandler : SimpleChannelInboundHandler<object>
    {
        private readonly TaskCompletionSource<object> clientPromise;

        internal ClientHandler(TaskCompletionSource<object> clientPromise)
        {
            this.clientPromise = clientPromise;
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            clientPromise.TrySetCanceled();
            ctx.FireChannelInactive();
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
        {
            clientPromise.TrySetResult(null);
            _ = ctx.CloseAsync();
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (!clientPromise.TrySetException(cause))
            {
                ctx.FireExceptionCaught(cause);
            }
        }
    }

    private static async Task<IChannel> Server(LocalAddress address, IChannelHandler handler)
    {
        ServerBootstrap bootstrap = new ServerBootstrap()
                .Channel<LocalServerChannel>()
                .Group(GROUP)
                .ChildHandler(handler);

        return await bootstrap.BindAsync(address, TestContext.Current.CancellationToken);
    }

    private static async Task<IChannel> Client(IChannel server, IChannelHandler handler)
    {
        EndPoint remoteAddress = server.LocalAddress;

        Bootstrap bootstrap = new Bootstrap()
                .Channel<LocalChannel>()
                .Group(GROUP)
                .Handler(handler);

        return await bootstrap.ConnectAsync(remoteAddress, TestContext.Current.CancellationToken);
    }

    private static List<object[]> Expand(string rfcCipherName)
    {
        List<object[]> dst = new List<object[]>();
        SslProvider[] sslProviders = { SslProvider.JDK, SslProvider.OPENSSL, SslProvider.OPENSSL_REFCNT };

        for (int i = 0; i < sslProviders.Length; i++)
        {
            SslProvider serverSslProvider = sslProviders[i];

            for (int j = 0; j < sslProviders.Length; j++)
            {
                SslProvider clientSslProvider = sslProviders[j];

                if (serverSslProvider != SslProvider.JDK || clientSslProvider != SslProvider.JDK)
                {
                    continue;
                }

                dst.Add(new object[]{serverSslProvider, clientSslProvider, rfcCipherName});
            }
        }

        if (dst.Count == 0)
        {
            throw new InvalidOperationException();
        }

        return dst;
    }
}
