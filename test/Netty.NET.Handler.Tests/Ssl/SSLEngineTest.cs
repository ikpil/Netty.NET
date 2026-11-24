/*
 * Copyright 2015 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Ssl;
using Netty.NET.Handler.Ssl.Util;
using Netty.NET.PkiTesting;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using AuthenticationException = System.Security.Authentication.AuthenticationException;
using AuthSslProtocols = System.Security.Authentication.SslProtocols;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: the abstract fixture of the SSLEngine suite; xUnit runs the [Theory]/[Fact] methods of an abstract base class for
// each derived test class, as JUnit does (test/Netty.NET.Handler.Codec.Compression.Tests/AbstractIntegrationTest.cs), and
// the only concrete fixture is JdkSslEngineTest (the OpenSSL, Conscrypt and Amazon Corretto fixtures are
// library-not-ported, owner decision 4). The engine is SslStream behind SslHandler (owner decisions 2026-11-24,
// docs/handler-ssl-design.md), which has no SSLEngine facade: the tests that drive an SSLEngine directly (wrap/unwrap,
// beginHandshake/closeInbound/closeOutbound, SSLSession, SSLSessionContext, enabled/supported protocols and cipher suites,
// SSLParameters, delegated tasks, handshake-session trust managers) are CLR-inapplicable and recorded in
// docs/handler-porting-manifest.json; SSLEngineTest.NotApplicable.cs keeps their original comments. The tests that run
// through SslContext, SslHandler and a socket pipeline are ported here:
// - @TestInstance(PER_CLASS) with @BeforeEach setup()/@AfterEach tearDown(): xUnit creates an instance per test, and
//   setup()/tearDown() run from IAsyncLifetime.InitializeAsync/DisposeAsync, "Called immediately after the class has been
//   created, before it is used" (https://raw.githubusercontent.com/xunit/xunit/main/src/xunit.v3.core/IAsyncLifetime.cs,
//   as AbstractDecoderTest of codec-compression).
// - @MethodSource("newTestParams") is [MemberData(nameof(NewTestParams))]; xUnit member data is "The name of the public
//   static member on the test class that will provide the test data"
//   (https://raw.githubusercontent.com/xunit/xunit/main/src/xunit.v3.core/Attributes/MemberDataAttributeBase.cs), so
//   newTestParams() is static and asks the JDK provider (the only fixture) whether TLSv1.3 is supported, which is the
//   tlsv13Supported that JdkSslEngineTest passed to the constructor; the constructor parameter and field are therefore
//   not ported. The parameter types are public because a public theory takes them (CS0051).
// - The delegate=true parameters ran the delegated SSLEngine tasks on the DelayingExecutor through
//   newHandler(alloc, Executor), which is jvm-only-replaced (SslStream has no delegated tasks, see SslContext and
//   SslHandler), so only delegate=false is generated and the delegatingExecutor field with its tearDown check is not
//   ported.
// - The JCA Provider hooks clientSslContextProvider()/serverSslContextProvider() and SslContextBuilder.sslContextProvider
//   (jvm-only-replaced) have no counterpart: the engine is always SslStream; wrapEngine(SSLEngine) and unwrapEngine only
//   serve the SSLEngine tests.
// - SelfSignedCertificate/CachedSelfSignedCertificate are @Deprecated ("Use the CertificateBuilder from netty-pkitesting
//   instead") and not ported: the certificate is built as CertificateBuilderCertGenerator builds it (a self-signed CA
//   "CN=localhost" with an RSA 2048 key) with Netty.NET.PkiTesting.CertificateBuilder; ssc.cert()/key() are
//   GetCertificate()/GetKeyPair() and the files ssc.certificate()/privateKey() are ToTempCertChainPem()/
//   ToTempPrivateKeyPem().
// - SSLException and SSLHandshakeException are both AuthenticationException, which "The NegotiateStream and SslStream
//   classes throw ... when the client or server cannot be authenticated"
//   (https://learn.microsoft.com/en-us/dotnet/api/system.security.authentication.authenticationexception?view=net-10.0;
//   the mapping of the ssl port, src/Netty.NET.Handler/Ssl/SslHandler.cs), Throwable is Exception, getCause() is
//   InnerException, IllegalStateException is InvalidOperationException, java.lang.Error is UnreachableException, and
//   ClosedChannelException is the Transport ClosedChannelException.
// - The Transport bootstrap API is asynchronous: bind(...)/connect(...).sync() are awaited BindAsync/ConnectAsync,
//   close().sync() an awaited CloseAsync, Future.await(timeout) a Task.WhenAny with a delay, and shutdownGracefully(0, 0,
//   MILLISECONDS) is ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero). CountDownLatch is CountdownEvent
//   (CountDown below), BlockingQueue<ByteBuf> a BlockingCollection<ByteBuf> ("The default collection type for
//   BlockingCollection<T> is ConcurrentQueue<T>", FIFO, as LinkedBlockingQueue;
//   https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.blockingcollection-1?view=net-10.0) and the
//   onNextMessages queue a ConcurrentQueue. The anonymous ChannelInitializer/ChannelInboundHandlerAdapter classes are
//   named nested classes.
// - Windows has no CipherSuitesPolicy (docs/handler-ssl-design.md): a context built with cipher suites fails in
//   SslContext.NewHandler with PlatformNotSupportedException, so a ported test whose handlers come from such a context
//   calls Assert.Skip there (xUnit v3: "Three new assertions have been added to support dynamically skipping a test at
//   runtime", https://xunit.net/docs/getting-started/v3/whats-new). The tests that configure param.ciphers() and then
//   ciphers(null, IdentityCipherSuiteFilter.INSTANCE) build contexts without cipher suites and run everywhere.
public abstract partial class SSLEngineTest : IAsyncLifetime
{

    private const string PRINCIPAL_NAME = "CN=e8ac02fa0d65a84219016045db8b05c485b4ecdf.netty.test";

    // CLR: the logger and the NOOP Runnable of the source only serve the CLR-inapplicable session-resumption tests
    // (mustCallResumeTrustedOnSessionResumption and the mustFail*/mustNot* tests), so they are not ported.

    protected MessageReceiver serverReceiver;
    protected MessageReceiver clientReceiver;

    protected volatile Exception serverException;
    protected volatile Exception clientException;
    protected SslContext serverSslCtx;
    protected SslContext clientSslCtx;
    protected ServerBootstrap sb;
    protected Bootstrap cb;
    protected IChannel serverChannel;
    protected IChannel serverConnectedChannel;
    protected IChannel clientChannel;
    protected CountdownEvent serverLatch;
    protected CountdownEvent clientLatch;
    protected volatile Task<IChannel> serverSslHandshakeFuture;
    protected volatile Task<IChannel> clientSslHandshakeFuture;

    // CLR: the source caches one SelfSignedCertificate per class loader (CachedSelfSignedCertificate); the cached bundle is
    // built once per process, as CachedSelfSignedCertificate does.
    private static readonly Lazy<X509Bundle> CACHED_CERTIFICATE = new Lazy<X509Bundle>(NewSelfSignedCertificate);

    // CLR: new SelfSignedCertificate() with its defaults ("localhost", RSA 2048), as CertificateBuilderCertGenerator
    // builds it (setIsCertificateAuthority(true), subject "CN=" + fqdn, Algorithm.rsa2048).
    internal static X509Bundle NewSelfSignedCertificate()
    {
        return new CertificateBuilder()
                .SetIsCertificateAuthority(true)
                .Subject("CN=localhost")
                .Rsa2048()
                .BuildSelfSigned();
    }

    // CLR: CachedSelfSignedCertificate.getCachedCertificate().
    internal static X509Bundle GetCachedCertificate()
    {
        return CACHED_CERTIFICATE.Value;
    }

    // CLR: public, as the protected fields serverReceiver/clientReceiver of this public class have this type (CS0052).
    public sealed class MessageReceiver
    {
        internal readonly BlockingCollection<ByteBuf> messages = new BlockingCollection<ByteBuf>();
        internal readonly ConcurrentQueue<IOnNextMessage> onNextMessages = new ConcurrentQueue<IOnNextMessage>();

        internal void MessageReceived(IChannelHandlerContext ctx, ByteBuf msg)
        {
            messages.Add(msg);
            if (onNextMessages.TryDequeue(out IOnNextMessage onNextMessage))
            {
                onNextMessage.MessageReceived(ctx, msg);
            }
        }
    }

    internal interface IOnNextMessage
    {
        void MessageReceived(IChannelHandlerContext ctx, ByteBuf msg);
    }

    protected sealed class MessageDelegatorChannelHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly MessageReceiver receiver;
        private readonly CountdownEvent latch;

        public MessageDelegatorChannelHandler(MessageReceiver receiver, CountdownEvent latch) : base(false)
        {
            this.receiver = receiver;
            this.latch = latch;
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            receiver.MessageReceived(ctx, msg);
            CountDown(latch);
        }
    }

    // CLR: CountDownLatch.countDown() does nothing once the count is zero, while CountdownEvent.Signal() throws
    // InvalidOperationException when "The current instance is already set"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.countdownevent.signal?view=net-10.0); the latches of
    // this suite are counted down from more than one handler, so the check and the signal run under a lock.
    protected static void CountDown(CountdownEvent latch)
    {
        lock (latch)
        {
            if (latch.CurrentCount > 0)
            {
                latch.Signal();
            }
        }
    }

    public enum BufferType
    {
        Direct,
        Heap,
        Mixed
    }

    public sealed class ProtocolCipherCombo
    {
        private static readonly ProtocolCipherCombo TLSV12 = new ProtocolCipherCombo(
                SslProtocols.TLS_v1_2, "TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256");
        private static readonly ProtocolCipherCombo TLSV13 = new ProtocolCipherCombo(
                SslProtocols.TLS_v1_3, "TLS_AES_128_GCM_SHA256");
        internal readonly string protocol;
        internal readonly string cipher;

        private ProtocolCipherCombo(string protocol, string cipher)
        {
            this.protocol = protocol;
            this.cipher = cipher;
        }

        internal static ProtocolCipherCombo Tlsv12()
        {
            return TLSV12;
        }

        internal static ProtocolCipherCombo Tlsv13()
        {
            return TLSV13;
        }

        public override string ToString()
        {
            return "ProtocolCipherCombo{" +
                   "protocol='" + protocol + '\'' +
                   ", cipher='" + cipher + '\'' +
                   '}';
        }
    }

    public class SSLEngineTestParam
    {
        private readonly BufferType type;
        internal readonly ProtocolCipherCombo protocolCipherCombo;
        // CLR: always false here (see the class comment); kept so that the parameters print as in the source.
        private readonly bool @delegate;

        internal SSLEngineTestParam(BufferType type, ProtocolCipherCombo protocolCipherCombo, bool @delegate)
        {
            this.type = type;
            this.protocolCipherCombo = protocolCipherCombo;
            this.@delegate = @delegate;
        }

        internal BufferType Type()
        {
            return type;
        }

        internal ProtocolCipherCombo Combo()
        {
            return protocolCipherCombo;
        }

        internal bool Delegate()
        {
            return @delegate;
        }

        // CLR: Collections.singletonList is a one-element read-only list.
        internal IList<string> Protocols()
        {
            return new[] { protocolCipherCombo.protocol };
        }

        internal IList<string> Ciphers()
        {
            return new[] { protocolCipherCombo.cipher };
        }

        public override string ToString()
        {
            return "SslEngineTestParam{" +
                    "type=" + Type() +
                    ", protocolCipherCombo=" + Combo() +
                    ", delegate=" + (Delegate() ? "true" : "false") +
                    '}';
        }
    }

    public static TheoryData<SSLEngineTestParam> NewTestParams()
    {
        TheoryData<SSLEngineTestParam> @params = new TheoryData<SSLEngineTestParam>();
        bool tlsv13Supported = SslProvider.IsTlsv13Supported(SslProvider.JDK);
        foreach (BufferType type in Enum.GetValues<BufferType>())
        {
            // CLR: the delegate=true parameters are not generated (see the class comment).
            @params.Add(new SSLEngineTestParam(type, ProtocolCipherCombo.Tlsv12(), false));

            if (tlsv13Supported)
            {
                @params.Add(new SSLEngineTestParam(type, ProtocolCipherCombo.Tlsv13(), false));
            }
        }
        return @params;
    }

    // CLR: allocateBuffer(BufferType, int) allocates the NIO ByteBuffers of the SSLEngine tests, which are
    // CLR-inapplicable, so it is not ported.

    private sealed class TestByteBufAllocator : IByteBufAllocator
    {

        private readonly IByteBufAllocator allocator;
        private readonly BufferType type;

        internal TestByteBufAllocator(IByteBufAllocator allocator, BufferType type)
        {
            this.allocator = allocator;
            this.type = type;
        }

        // CLR: the Buffer port folds buffer(), buffer(int) and buffer(int, int) into one method with the default
        // capacities (src/Netty.NET.Buffer/IByteBufAllocator.cs), and the same for the other allocation methods; the
        // no-argument Java calls pass those defaults. ThreadLocalRandom.current().nextBoolean() is Random.Shared
        // (docs/common-clr-design.md, "Native random consumers and scalar counters").
        public ByteBuf Buffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        {
            switch (type)
            {
                case BufferType.Direct:
                    return allocator.DirectBuffer(initialCapacity, maxCapacity);
                case BufferType.Heap:
                    return allocator.HeapBuffer(initialCapacity, maxCapacity);
                case BufferType.Mixed:
                    return Random.Shared.Next(2) == 0 ?
                            allocator.DirectBuffer(initialCapacity, maxCapacity) :
                            allocator.HeapBuffer(initialCapacity, maxCapacity);
                default:
                    throw new UnreachableException("Unexpected buffer type: " + type);
            }
        }

        public ByteBuf IoBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        {
            return allocator.IoBuffer(initialCapacity, maxCapacity);
        }

        public ByteBuf HeapBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        {
            return allocator.HeapBuffer(initialCapacity, maxCapacity);
        }

        public ByteBuf DirectBuffer(int initialCapacity = 256, int maxCapacity = int.MaxValue)
        {
            return allocator.DirectBuffer(initialCapacity, maxCapacity);
        }

        public CompositeByteBuf CompositeBuffer(int maxNumComponents = 16)
        {
            switch (type)
            {
                case BufferType.Direct:
                    return allocator.CompositeDirectBuffer(maxNumComponents);
                case BufferType.Heap:
                    return allocator.CompositeHeapBuffer(maxNumComponents);
                case BufferType.Mixed:
                    return Random.Shared.Next(2) == 0 ?
                            allocator.CompositeDirectBuffer(maxNumComponents) :
                            allocator.CompositeHeapBuffer(maxNumComponents);
                default:
                    throw new UnreachableException("Unexpected buffer type: " + type);
            }
        }

        public CompositeByteBuf CompositeHeapBuffer(int maxNumComponents = 16)
        {
            return allocator.CompositeHeapBuffer(maxNumComponents);
        }

        public CompositeByteBuf CompositeDirectBuffer(int maxNumComponents = 16)
        {
            return allocator.CompositeDirectBuffer(maxNumComponents);
        }

        public bool IsDirectBufferPooled => allocator.IsDirectBufferPooled;

        public int CalculateNewCapacity(int minNewCapacity, int maxCapacity)
        {
            return allocator.CalculateNewCapacity(minNewCapacity, maxCapacity);
        }
    }

    public ValueTask InitializeAsync()
    {
        Setup();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await TearDown();
    }

    public void Setup()
    {
        serverLatch = new CountdownEvent(1);
        clientLatch = new CountdownEvent(1);
        serverReceiver = new MessageReceiver();
        clientReceiver = new MessageReceiver();
    }

    public async Task TearDown()
    {
        Task clientCloseFuture = null;
        Task serverConnectedCloseFuture = null;
        Task serverCloseFuture = null;
        if (clientChannel != null)
        {
            clientCloseFuture = clientChannel.CloseAsync();
            clientChannel = null;
        }
        if (serverConnectedChannel != null)
        {
            serverConnectedCloseFuture = serverConnectedChannel.CloseAsync();
            serverConnectedChannel = null;
        }
        if (serverChannel != null)
        {
            serverCloseFuture = serverChannel.CloseAsync();
            serverChannel = null;
        }
        // We must wait for the Channel cleanup to finish. In the case if the ReferenceCountedOpenSslEngineTest
        // the ReferenceCountedOpenSslEngine depends upon the SslContext and so we must wait the cleanup the
        // SslContext to avoid JVM core dumps!
        //
        // See https://github.com/netty/netty/issues/5692
        if (clientCloseFuture != null)
        {
            await clientCloseFuture;
        }
        if (serverConnectedCloseFuture != null)
        {
            await serverConnectedCloseFuture;
        }
        if (serverCloseFuture != null)
        {
            await serverCloseFuture;
        }
        if (serverSslCtx != null)
        {
            CleanupServerSslContext(serverSslCtx);
            serverSslCtx = null;
        }
        if (clientSslCtx != null)
        {
            CleanupClientSslContext(clientSslCtx);
            clientSslCtx = null;
        }
        Task serverGroupShutdownFuture = null;
        Task serverChildGroupShutdownFuture = null;
        Task clientGroupShutdownFuture = null;
        if (sb != null)
        {
            serverGroupShutdownFuture = sb.Config.Group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            serverChildGroupShutdownFuture = sb.Config.ChildGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        }
        if (cb != null)
        {
            clientGroupShutdownFuture = cb.Config.Group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
        }
        if (serverGroupShutdownFuture != null)
        {
            await serverGroupShutdownFuture;
            await serverChildGroupShutdownFuture;
        }
        if (clientGroupShutdownFuture != null)
        {
            await clientGroupShutdownFuture;
        }
        serverException = null;
        clientException = null;
    }

    [Theory]
    [MemberData(nameof(NewTestParams))]
    public async Task TestMutualAuthSameCerts(SSLEngineTestParam param)
    {
        await MySetupMutualAuth(param, ResourcesUtil.GetFile(GetType(), "test_unencrypted.pem"),
                ResourcesUtil.GetFile(GetType(), "test.crt"),
                null);
        await RunTest(null);
        Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Exception cause = serverException;
        if (cause != null)
        {
            // CLR: throw cause rethrows the stored exception; ExceptionDispatchInfo keeps its original stack.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(cause);
        }
    }

    [Theory]
    [MemberData(nameof(NewTestParams))]
    public async Task TestMutualAuthDiffCerts(SSLEngineTestParam param)
    {
        FileInfo serverKeyFile =  ResourcesUtil.GetFile(GetType(), "test_encrypted.pem");
        FileInfo serverCrtFile = ResourcesUtil.GetFile(GetType(), "test.crt");
        string serverKeyPassword = "12345";
        FileInfo clientKeyFile = ResourcesUtil.GetFile(GetType(), "test2_encrypted.pem");
        FileInfo clientCrtFile = ResourcesUtil.GetFile(GetType(), "test2.crt");
        string clientKeyPassword = "12345";
        await MySetupMutualAuth(param, clientCrtFile, serverKeyFile, serverCrtFile, serverKeyPassword,
                          serverCrtFile, clientKeyFile, clientCrtFile, clientKeyPassword);
        await RunTest(null);
        Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(NewTestParams))]
    public async Task TestMutualAuthDiffCertsServerFailure(SSLEngineTestParam param)
    {
        FileInfo serverKeyFile = ResourcesUtil.GetFile(GetType(), "test_encrypted.pem");
        FileInfo serverCrtFile = ResourcesUtil.GetFile(GetType(), "test.crt");
        string serverKeyPassword = "12345";
        FileInfo clientKeyFile = ResourcesUtil.GetFile(GetType(), "test2_encrypted.pem");
        FileInfo clientCrtFile = ResourcesUtil.GetFile(GetType(), "test2.crt");
        string clientKeyPassword = "12345";
        // Client trusts server but server only trusts itself
        await MySetupMutualAuth(param, serverCrtFile, serverKeyFile, serverCrtFile, serverKeyPassword,
                          serverCrtFile, clientKeyFile, clientCrtFile, clientKeyPassword);
        Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.IsAssignableFrom<AuthenticationException>(serverException);
    }

    [Theory]
    [MemberData(nameof(NewTestParams))]
    public async Task TestMutualAuthDiffCertsClientFailure(SSLEngineTestParam param)
    {
        FileInfo serverKeyFile = ResourcesUtil.GetFile(GetType(), "test_unencrypted.pem");
        FileInfo serverCrtFile = ResourcesUtil.GetFile(GetType(), "test.crt");
        string serverKeyPassword = null;
        FileInfo clientKeyFile = ResourcesUtil.GetFile(GetType(), "test2_unencrypted.pem");
        FileInfo clientCrtFile = ResourcesUtil.GetFile(GetType(), "test2.crt");
        string clientKeyPassword = null;
        // Server trusts client but client only trusts itself
        await MySetupMutualAuth(param, clientCrtFile, serverKeyFile, serverCrtFile, serverKeyPassword,
                          clientCrtFile, clientKeyFile, clientCrtFile, clientKeyPassword);
        Assert.True(clientLatch.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.IsAssignableFrom<AuthenticationException>(clientException);
    }

    [Theory]
    [MemberData(nameof(NewTestParams))]
    public Task TestMutualAuthInvalidIntermediateCASucceedWithOptionalClientAuth(SSLEngineTestParam param)
    {
        return TestMutualAuthInvalidClientCertSucceed(param, ClientAuth.NONE);
    }

    [Theory]
    [MemberData(nameof(NewTestParams))]
    public Task TestMutualAuthInvalidIntermediateCAFailWithOptionalClientAuth(SSLEngineTestParam param)
    {
        return TestMutualAuthClientCertFail(param, ClientAuth.OPTIONAL);
    }

    [Theory]
    [MemberData(nameof(NewTestParams))]
    public Task TestMutualAuthInvalidIntermediateCAFailWithRequiredClientAuth(SSLEngineTestParam param)
    {
        return TestMutualAuthClientCertFail(param, ClientAuth.REQUIRE);
    }

    [Theory]
    [MemberData(nameof(NewTestParams))]
    public Task TestMutualAuthValidClientCertChainTooLongFailOptionalClientAuth(SSLEngineTestParam param)
    {
        return TestMutualAuthValidClientCertChainTooLongFailOptionalClientAuth0(param);
    }

    // CLR: the body of the test above. JdkSslEngineTest disables the test with an @Disabled override; a C# override of
    // the [Theory] method would be a second declaration of the test, so the theory is declared once and calls this virtual
    // body, which the fixture overrides with Assert.Skip.
    protected virtual Task TestMutualAuthValidClientCertChainTooLongFailOptionalClientAuth0(SSLEngineTestParam param)
    {
        return TestMutualAuthClientCertFail(param, ClientAuth.OPTIONAL, "mutual_auth_client.p12", true);
    }

    [Theory]
    [MemberData(nameof(NewTestParams))]
    public Task TestMutualAuthValidClientCertChainTooLongFailRequireClientAuth(SSLEngineTestParam param)
    {
        return TestMutualAuthValidClientCertChainTooLongFailRequireClientAuth0(param);
    }

    // CLR: see TestMutualAuthValidClientCertChainTooLongFailOptionalClientAuth0.
    protected virtual Task TestMutualAuthValidClientCertChainTooLongFailRequireClientAuth0(SSLEngineTestParam param)
    {
        return TestMutualAuthClientCertFail(param, ClientAuth.REQUIRE, "mutual_auth_client.p12", true);
    }

    // CLR: KeyStore.getInstance("PKCS12").load(getResourceAsStream(name), password) is the PKCS #12 bytes of the resource
    // file (SslContext keeps a KeyStore as its PKCS #12 encoding, see SslContext.BuildKeyStore), and
    // KeyManagerFactory.getInstance(getDefaultAlgorithm()).init(keyStore, password) is SslContext.BuildKeyManagerFactory(
    // keyStore, keyAlgorithm, password, kmf), which loads the key entry back with X509CertificateLoader.LoadPkcs12Collection
    // and creates the SslStreamCertificateContext of the leaf that carries the key with the other certificates as its
    // chain (src/Netty.NET.Handler/Ssl/SslContext.cs); the default algorithm and the factory have no counterpart (null).
    private static SslStreamCertificateContext NewKeyManagerFactory(byte[] keyStore, char[] password)
    {
        return SslContext.BuildKeyManagerFactory(keyStore, null, password, null);
    }

    // CLR: the try-with-resources getResourceAsStream(name) of the key stores; the resources are files next to the test
    // assembly (ResourcesUtil.GetFile).
    private byte[] LoadKeyStore(string name)
    {
        using (System.IO.Stream resourceAsStream = File.OpenRead(ResourcesUtil.GetFile(GetType(), name).FullName))
        {
            MemoryStream keyStore = new MemoryStream();
            resourceAsStream.CopyTo(keyStore);
            return keyStore.ToArray();
        }
    }

    private async Task TestMutualAuthInvalidClientCertSucceed(SSLEngineTestParam param, ClientAuth auth)
    {
        char[] password = "example".ToCharArray();
        byte[] serverKeyStore = LoadKeyStore("mutual_auth_server.p12");
        byte[] clientKeyStore = LoadKeyStore("mutual_auth_invalid_client.p12");
        SslStreamCertificateContext serverKeyManagerFactory = NewKeyManagerFactory(serverKeyStore, password);
        SslStreamCertificateContext clientKeyManagerFactory = NewKeyManagerFactory(clientKeyStore, password);
        FileInfo commonCertChain = ResourcesUtil.GetFile(GetType(), "mutual_auth_ca.pem");

        await MySetupMutualAuth(param, serverKeyManagerFactory, commonCertChain, clientKeyManagerFactory, commonCertChain,
                auth, false, false);
        Assert.True(clientLatch.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        RethrowIfNotNull(clientException);
        Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        RethrowIfNotNull(serverException);
    }

    private Task TestMutualAuthClientCertFail(SSLEngineTestParam param, ClientAuth auth)
    {
        return TestMutualAuthClientCertFail(param, auth, "mutual_auth_invalid_client.p12", false);
    }

    private async Task TestMutualAuthClientCertFail(SSLEngineTestParam param, ClientAuth auth, string clientCert,
                                              bool serverInitEngine)
    {
        char[] password = "example".ToCharArray();
        byte[] serverKeyStore = LoadKeyStore("mutual_auth_server.p12");
        byte[] clientKeyStore = LoadKeyStore(clientCert);
        SslStreamCertificateContext serverKeyManagerFactory = NewKeyManagerFactory(serverKeyStore, password);
        SslStreamCertificateContext clientKeyManagerFactory = NewKeyManagerFactory(clientKeyStore, password);
        FileInfo commonCertChain = ResourcesUtil.GetFile(GetType(), "mutual_auth_ca.pem");

        await MySetupMutualAuth(param, serverKeyManagerFactory, commonCertChain, clientKeyManagerFactory, commonCertChain,
                          auth, true, serverInitEngine);
        Assert.True(clientLatch.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(MySetupMutualAuthServerIsValidClientException(clientException),
                "unexpected exception: " + clientException);
        Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(MySetupMutualAuthServerIsValidServerException(serverException),
                "unexpected exception: " + serverException);
    }

    protected static bool CausedBySSLException(Exception cause)
    {
        Exception next = cause;
        do
        {
            if (next is AuthenticationException)
            {
                return true;
            }
            next = next.InnerException;
        } while (next != null);
        return false;
    }

    protected virtual bool MySetupMutualAuthServerIsValidServerException(Exception cause)
    {
        return MySetupMutualAuthServerIsValidException(cause);
    }

    protected virtual bool MySetupMutualAuthServerIsValidClientException(Exception cause)
    {
        return MySetupMutualAuthServerIsValidException(cause);
    }

    protected virtual bool MySetupMutualAuthServerIsValidException(Exception cause)
    {
        // As in TLSv1.3 the handshake is sent without an extra roundtrip an SSLException is valid as well.
        return cause is AuthenticationException || cause is ClosedChannelException;
    }

    protected virtual void MySetupMutualAuthServerInitSslHandler(SslHandler handler)
    {
    }

    protected async Task MySetupMutualAuth(SSLEngineTestParam param, SslStreamCertificateContext serverKMF,
                                     FileInfo serverTrustManager,
                                     SslStreamCertificateContext clientKMF, FileInfo clientTrustManager,
                                     ClientAuth clientAuth, bool failureExpected,
                                     bool serverInitEngine)
    {
        serverSslCtx = WrapContext(param, SslContextBuilder.ForServer(serverKMF)
                .Protocols(param.Protocols())
                .Ciphers(param.Ciphers())
                .SslProvider(SslServerProvider())
                .TrustManager(serverTrustManager)
                .ClientAuth(clientAuth)
                .Ciphers(null, IdentityCipherSuiteFilter.INSTANCE)
                .SessionCacheSize(0)
                .SessionTimeout(0).Build());

        clientSslCtx = WrapContext(param, SslContextBuilder.ForClient()
                .Protocols(param.Protocols())
                .Ciphers(param.Ciphers())
                .SslProvider(SslClientProvider())
                .TrustManager(clientTrustManager)
                .KeyManager(clientKMF)
                .Ciphers(null, IdentityCipherSuiteFilter.INSTANCE)
                .SessionCacheSize(0)
                .EndpointIdentificationAlgorithm(null)
                .SessionTimeout(0).Build());

        serverConnectedChannel = null;
        sb = new ServerBootstrap();
        cb = new Bootstrap();

        sb.Group(new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory()));
        sb.Channel<NioServerSocketChannel>();
        sb.ChildHandler(new MutualAuthKeyManagerServerInitializer(this, param, failureExpected, serverInitEngine));

        cb.Group(new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory()));
        cb.Channel<NioSocketChannel>();
        cb.Handler(new MutualAuthKeyManagerClientInitializer(this, param, failureExpected));

        serverChannel = await sb.BindAsync(WildcardAddress(0), TestContext.Current.CancellationToken);
        int port = ((IPEndPoint) serverChannel.LocalAddress).Port;

        Task<IChannel> ccf = cb.ConnectAsync(new IPEndPoint(NetUtil.LOCALHOST, port), TestContext.Current.CancellationToken);
        Assert.True(await AwaitUninterruptibly(ccf));
        clientChannel = ccf.Result;
    }

    // CLR: newHandler(alloc, delegatingExecutor) is jvm-only-replaced (see the class comment).
    private sealed class MutualAuthKeyManagerServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SSLEngineTest test;
        private readonly SSLEngineTestParam param;
        private readonly bool failureExpected;
        private readonly bool serverInitEngine;

        internal MutualAuthKeyManagerServerInitializer(SSLEngineTest test, SSLEngineTestParam param,
                                                       bool failureExpected, bool serverInitEngine)
        {
            this.test = test;
            this.param = param;
            this.failureExpected = failureExpected;
            this.serverInitEngine = serverInitEngine;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Config.SetAllocator(new TestByteBufAllocator(ch.Config.GetAllocator(), param.Type()));

            IChannelPipeline p = ch.Pipeline;
            SslHandler handler = test.serverSslCtx.NewHandler(ch.Alloc());
            if (serverInitEngine)
            {
                test.MySetupMutualAuthServerInitSslHandler(handler);
            }
            p.AddLast(handler);
            p.AddLast(new MessageDelegatorChannelHandler(test.serverReceiver, test.serverLatch));
            p.AddLast(new MutualAuthKeyManagerServerHandler(test, failureExpected));
            test.serverConnectedChannel = ch;
        }
    }

    private sealed class MutualAuthKeyManagerServerHandler : ChannelInboundHandlerAdapter
    {
        private readonly SSLEngineTest test;
        private readonly bool failureExpected;

        internal MutualAuthKeyManagerServerHandler(SSLEngineTest test, bool failureExpected)
        {
            this.test = test;
            this.failureExpected = failureExpected;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == SslHandshakeCompletionEvent.SUCCESS)
            {
                if (failureExpected)
                {
                    test.serverException = new InvalidOperationException("handshake complete. expected failure");
                }
                CountDown(test.serverLatch);
            }
            else if (evt is SslHandshakeCompletionEvent)
            {
                test.serverException = ((SslHandshakeCompletionEvent) evt).Cause();
                CountDown(test.serverLatch);
            }
            ctx.FireUserEventTriggered(evt);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause.InnerException is AuthenticationException)
            {
                test.serverException = cause.InnerException;
                CountDown(test.serverLatch);
            }
            else
            {
                test.serverException = cause;
                ctx.FireExceptionCaught(cause);
            }
        }
    }

    private sealed class MutualAuthKeyManagerClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SSLEngineTest test;
        private readonly SSLEngineTestParam param;
        private readonly bool failureExpected;

        internal MutualAuthKeyManagerClientInitializer(SSLEngineTest test, SSLEngineTestParam param,
                                                       bool failureExpected)
        {
            this.test = test;
            this.param = param;
            this.failureExpected = failureExpected;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Config.SetAllocator(new TestByteBufAllocator(ch.Config.GetAllocator(), param.Type()));
            IChannelPipeline p = ch.Pipeline;

            SslHandler handler = test.clientSslCtx.NewHandler(ch.Alloc());
            p.AddLast(handler);
            p.AddLast(new MessageDelegatorChannelHandler(test.clientReceiver, test.clientLatch));
            p.AddLast(new MutualAuthKeyManagerClientHandler(test, failureExpected));
        }
    }

    private sealed class MutualAuthKeyManagerClientHandler : ChannelInboundHandlerAdapter
    {
        private readonly SSLEngineTest test;
        private readonly bool failureExpected;

        internal MutualAuthKeyManagerClientHandler(SSLEngineTest test, bool failureExpected)
        {
            this.test = test;
            this.failureExpected = failureExpected;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == SslHandshakeCompletionEvent.SUCCESS)
            {
                // With TLS1.3 a mutal auth error will not be propagated as a handshake error most of the
                // time as the handshake needs NO extra roundtrip.
                if (!failureExpected)
                {
                    CountDown(test.clientLatch);
                }
            }
            else if (evt is SslHandshakeCompletionEvent)
            {
                test.clientException = ((SslHandshakeCompletionEvent) evt).Cause();
                CountDown(test.clientLatch);
            }
            ctx.FireUserEventTriggered(evt);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause.InnerException is AuthenticationException)
            {
                test.clientException = cause.InnerException;
                CountDown(test.clientLatch);
            }
            else
            {
                ctx.FireExceptionCaught(cause);
            }
        }
    }

    // CLR: fail(message, cause) attaches the cause to the AssertionFailedError; Assert.Fail takes a message only, so the
    // cause is appended to it.
    protected static void RethrowIfNotNull(Exception error)
    {
        if (error != null)
        {
            Assert.Fail("Expected no error: " + error);
        }
    }

    [Theory]
    [MemberData(nameof(NewTestParams))]
    public async Task TestClientHostnameValidationSuccess(SSLEngineTestParam param)
    {
        await MySetupClientHostnameValidation(param, ResourcesUtil.GetFile(GetType(),  "localhost_server.pem"),
                                        ResourcesUtil.GetFile(GetType(), "localhost_server.key"),
                                        ResourcesUtil.GetFile(GetType(), "mutual_auth_ca.pem"),
                                        false);
        Assert.True(clientLatch.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        RethrowIfNotNull(clientException);
        Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        RethrowIfNotNull(serverException);
    }

    [Theory]
    [MemberData(nameof(NewTestParams))]
    public async Task TestClientHostnameValidationFail(SSLEngineTestParam param)
    {
        Task clientWriteFuture =
            await MySetupClientHostnameValidation(param, ResourcesUtil.GetFile(GetType(),  "notlocalhost_server.pem"),
                                            ResourcesUtil.GetFile(GetType(), "notlocalhost_server.key"),
                                            ResourcesUtil.GetFile(GetType(), "mutual_auth_ca.pem"),
                                            true);
        Assert.True(clientLatch.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(MySetupMutualAuthServerIsValidClientException(clientException),
                "unexpected exception: " + clientException);
        Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(MySetupMutualAuthServerIsValidServerException(serverException),
                "unexpected exception: " + serverException);

        // Verify that any pending writes are failed with the cached handshake exception and not a general SSLException.
        await AwaitUninterruptibly(clientWriteFuture);
        Exception actualCause = Cause(clientWriteFuture);
        Assert.Same(clientException, actualCause);
    }

    private async Task<Task> MySetupClientHostnameValidation(SSLEngineTestParam param, FileInfo serverCrtFile,
                                                         FileInfo serverKeyFile,
                                                         FileInfo clientTrustCrtFile,
                                                         bool failureExpected)
    {
        string expectedHost = "localhost";
        serverSslCtx = WrapContext(param, SslContextBuilder.ForServer(serverCrtFile, serverKeyFile, null)
                .SslProvider(SslServerProvider())
                .Protocols(param.Protocols())
                .Ciphers(param.Ciphers())
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                .Ciphers(null, IdentityCipherSuiteFilter.INSTANCE)
                .SessionCacheSize(0)
                .SessionTimeout(0)
                .Build());

        // CLR: the source sets the endpoint identification algorithm "HTTPS" on the engine of the client handler
        // (getSSLParameters/setSSLParameters, see the client initializer); the handler's SslStream options are created
        // inside newHandler and are not reachable, so the client context is built with that algorithm, which is the
        // client default (SslContext.defaultEndpointVerificationAlgorithm).
        clientSslCtx = WrapContext(param, SslContextBuilder.ForClient()
                .SslProvider(SslClientProvider())
                .Protocols(param.Protocols())
                .Ciphers(param.Ciphers())
                .TrustManager(clientTrustCrtFile)
                .Ciphers(null, IdentityCipherSuiteFilter.INSTANCE)
                .SessionCacheSize(0)
                .SessionTimeout(0)
                .EndpointIdentificationAlgorithm("HTTPS")
                .Build());

        serverConnectedChannel = null;
        sb = new ServerBootstrap();
        cb = new Bootstrap();

        sb.Group(new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory()));
        sb.Channel<NioServerSocketChannel>();
        sb.ChildHandler(new HostnameValidationServerInitializer(this, param, failureExpected));

        // CLR: ImmediateEventExecutor.INSTANCE.newPromise() is a TaskCompletionSource.
        TaskCompletionSource clientWritePromise = new TaskCompletionSource();
        cb.Group(new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory()));
        cb.Channel<NioSocketChannel>();
        cb.Handler(new HostnameValidationClientInitializer(this, param, expectedHost, failureExpected,
                clientWritePromise));

        // CLR: new InetSocketAddress(expectedHost, port) resolves the host name; BindAsync/ConnectAsync(string, int)
        // resolve it the same way (AbstractBootstrap.SocketAddress and the Bootstrap resolver).
        serverChannel = await sb.BindAsync(expectedHost, 0, TestContext.Current.CancellationToken);
        int port = ((IPEndPoint) serverChannel.LocalAddress).Port;

        Task<IChannel> ccf = cb.ConnectAsync(expectedHost, port, TestContext.Current.CancellationToken);
        Assert.True(await AwaitUninterruptibly(ccf));
        clientChannel = ccf.Result;
        return clientWritePromise.Task;
    }

    private sealed class HostnameValidationServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SSLEngineTest test;
        private readonly SSLEngineTestParam param;
        private readonly bool failureExpected;

        internal HostnameValidationServerInitializer(SSLEngineTest test, SSLEngineTestParam param, bool failureExpected)
        {
            this.test = test;
            this.param = param;
            this.failureExpected = failureExpected;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Config.SetAllocator(new TestByteBufAllocator(ch.Config.GetAllocator(), param.Type()));
            IChannelPipeline p = ch.Pipeline;

            SslHandler handler = test.serverSslCtx.NewHandler(ch.Alloc());
            p.AddLast(handler);
            p.AddLast(new MessageDelegatorChannelHandler(test.serverReceiver, test.serverLatch));
            p.AddLast(new HostnameValidationServerHandler(test, failureExpected));
            p.AddLast(new SilenceExceptionHandler()
                    .MessageContains("Received fatal alert: internal_error"));
            test.serverConnectedChannel = ch;
        }
    }

    private sealed class HostnameValidationServerHandler : ChannelInboundHandlerAdapter
    {
        private readonly SSLEngineTest test;
        private readonly bool failureExpected;

        internal HostnameValidationServerHandler(SSLEngineTest test, bool failureExpected)
        {
            this.test = test;
            this.failureExpected = failureExpected;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == SslHandshakeCompletionEvent.SUCCESS)
            {
                if (failureExpected)
                {
                    // CLR: the source fails here because the client's alert fails the server's handshake. SslStream
                    // verifies the remote certificate after the handshake messages were exchanged (ForceAuthenticationAsync
                    // calls CompleteHandshake, which "Optionally verifies the Remote Certificate", after the handshake loop,
                    // and a failed verification then sends its alert and throws,
                    // https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Net.Security/src/System/Net/Security/SslStream.IO.cs),
                    // so the server's handshake completes before the client rejects the certificate, and the server's
                    // failure is the read of that alert, an AuthenticationException ("The decryption operation failed",
                    // measured for TLSv1.2 and TLSv1.3 on this host) that exceptionCaught below records. The success is
                    // therefore not an error here, and the latch waits for that failure.
                    ctx.FireUserEventTriggered(evt);
                    return;
                }
                CountDown(test.serverLatch);
            }
            else if (evt is SslHandshakeCompletionEvent)
            {
                test.serverException = ((SslHandshakeCompletionEvent) evt).Cause();
                CountDown(test.serverLatch);
            }
            ctx.FireUserEventTriggered(evt);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause.InnerException is AuthenticationException)
            {
                test.serverException = cause.InnerException;
                CountDown(test.serverLatch);
            }
            else
            {
                test.serverException = cause;
                ctx.FireExceptionCaught(cause);
            }
        }
    }

    private sealed class HostnameValidationClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SSLEngineTest test;
        private readonly SSLEngineTestParam param;
        private readonly string expectedHost;
        private readonly bool failureExpected;
        private readonly TaskCompletionSource clientWritePromise;

        internal HostnameValidationClientInitializer(SSLEngineTest test, SSLEngineTestParam param, string expectedHost,
                                                     bool failureExpected, TaskCompletionSource clientWritePromise)
        {
            this.test = test;
            this.param = param;
            this.expectedHost = expectedHost;
            this.failureExpected = failureExpected;
            this.clientWritePromise = clientWritePromise;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Config.SetAllocator(new TestByteBufAllocator(ch.Config.GetAllocator(), param.Type()));
            IChannelPipeline p = ch.Pipeline;

            // CLR: the source reads the server address here (remoteAddress) without using it.
            SslHandler sslHandler = test.clientSslCtx.NewHandler(ch.Alloc(), expectedHost, 0);

            // CLR: engine().getSSLParameters()/setSSLParameters(parameters) are the SslStream options that NewHandler
            // created for this handler; SslHandler keeps them private (only the SslStream is reachable, through the
            // internal Engine(), and its TargetHostName is only set when the handshake starts), so the endpoint
            // identification algorithm "HTTPS" is set on the client context (see MySetupClientHostnameValidation) and the
            // SNI host name check of getServerNames() has no counterpart here. Its branch does not run in the source
            // either: "localhost" has no dot, so it is no valid SNI host name, which is checked instead.
            Assert.False(SslUtils.IsValidHostNameForSNI(expectedHost));
            p.AddLast(sslHandler);
            p.AddLast(new MessageDelegatorChannelHandler(test.clientReceiver, test.clientLatch));
            p.AddLast(new HostnameValidationClientHandler(test, failureExpected, clientWritePromise));
        }
    }

    private sealed class HostnameValidationClientHandler : ChannelInboundHandlerAdapter
    {
        private readonly SSLEngineTest test;
        private readonly bool failureExpected;
        private readonly TaskCompletionSource clientWritePromise;

        internal HostnameValidationClientHandler(SSLEngineTest test, bool failureExpected,
                                                 TaskCompletionSource clientWritePromise)
        {
            this.test = test;
            this.failureExpected = failureExpected;
            this.clientWritePromise = clientWritePromise;
        }

        public override void HandlerAdded(IChannelHandlerContext ctx)
        {
            // Only write if there is a failure expected. We don't actually care about the write going
            // through we just want to verify the local failure condition. This way we don't have to worry
            // about verifying the payload and releasing the content on the server side.
            if (failureExpected)
            {
                Task f = ctx.WriteAsync(ctx.Alloc().Buffer(1).WriteByte(1));
                Cascade(f, clientWritePromise);
            }
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == SslHandshakeCompletionEvent.SUCCESS)
            {
                if (failureExpected)
                {
                    test.clientException = new InvalidOperationException("handshake complete. expected failure");
                }
                CountDown(test.clientLatch);
            }
            else if (evt is SslHandshakeCompletionEvent)
            {
                test.clientException = ((SslHandshakeCompletionEvent) evt).Cause();
                CountDown(test.clientLatch);
            }
            ctx.FireUserEventTriggered(evt);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause.InnerException is AuthenticationException)
            {
                test.clientException = cause.InnerException;
                CountDown(test.clientLatch);
            }
            else
            {
                ctx.FireExceptionCaught(cause);
            }
        }
    }

    // CLR: PromiseNotifier.cascade(future, promise) completes the promise with the outcome of the future; the Task
    // continuation runs synchronously when the write completes.
    private static void Cascade(Task future, TaskCompletionSource promise)
    {
        _ = future.ContinueWith(static (f, state) =>
        {
            TaskCompletionSource p = (TaskCompletionSource) state;
            if (f.IsCanceled)
            {
                p.TrySetCanceled();
            }
            else if (f.IsFaulted)
            {
                p.TrySetException(f.Exception.InnerException);
            }
            else
            {
                p.TrySetResult();
            }
        }, promise, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // CLR: Future.awaitUninterruptibly() waits without throwing; ConfigureAwaitOptions.SuppressThrowing "Avoids throwing an
    // exception at the completion of awaiting a Task that ends in the Faulted or Canceled state"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.configureawaitoptions?view=net-10.0). The
    // result is Future.isSuccess().
    protected static async Task<bool> AwaitUninterruptibly(Task future)
    {
        await future.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return future.IsCompletedSuccessfully;
    }

    // CLR: Future.cause(): the exception of a faulted Task, a TaskCanceledException for a cancelled one, else null.
    protected static Exception Cause(Task future)
    {
        if (future.IsCanceled)
        {
            return new TaskCanceledException(future);
        }
        return future.Exception?.InnerException;
    }

    // CLR: Future.await(timeout, unit): true when the future completed in time, whatever its outcome.
    private static async Task<bool> Await(Task future, TimeSpan timeout)
    {
        return await Task.WhenAny(future, Task.Delay(timeout)) == future;
    }

    private Task MySetupMutualAuth(SSLEngineTestParam param, FileInfo keyFile, FileInfo crtFile, string keyPassword)
    {
        return MySetupMutualAuth(param, crtFile, keyFile, crtFile, keyPassword, crtFile, keyFile, crtFile, keyPassword);
    }

    // CLR: the SSLSession of engine.getSession() is the SslStream of the handler (SslHandler.Engine(), internal), whose
    // LocalCertificate/RemoteCertificate are "the certificate used to authenticate the local/remote endpoint",
    // NegotiatedCipherSuite "the cipher suite that was negotiated" and SslProtocol "the security protocol used to
    // authenticate this connection" (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0).
    // getLocalPrincipal()/getPeerPrincipal().getName() are the certificates' Subject, and getPeerCertificates()/
    // getLocalCertificates() hold one certificate each, which SslStream reports as RemoteCertificate/LocalCertificate
    // (getEncoded() is GetRawCertData()). SslStream has no member for the session id, the application buffer size, the
    // creation/last access times, isValid() or the deprecated javax getPeerCertificateChain() (which throws
    // UnsupportedOperationException on Java 15+, JDK-8241039), so those checks have no counterpart. The source expects the
    // cipher suite of the parameter because the contexts enable Netty's default cipher suites (ciphers(null, ...)) in
    // Netty's order; SslStream applies no cipher suite order of its own (the operating system chooses; no
    // CipherSuitesPolicy is set, docs/handler-ssl-design.md), so the negotiated suite is checked against the cipher suites
    // the context enables. The protocol name is the JSSE name of SslProtocol (ProtocolName).
    private void VerifySSLSessionForMutualAuth(
            SSLEngineTestParam param, SslStream session, FileInfo certFile, string principalName)
    {
        Assert.Equal(principalName, session.LocalCertificate.Subject);
        Assert.Equal(principalName, session.RemoteCertificate.Subject);
        Assert.Contains(session.NegotiatedCipherSuite.ToString(), serverSslCtx.CipherSuites());
        Assert.Equal(param.Combo().protocol, ProtocolName(session.SslProtocol));

        // CLR: SslContext.X509_CERT_FACTORY.generateCertificate(in).getEncoded() is X509CertificateLoader.LoadCertificate
        // of the PEM file (PEM or DER, as src/Netty.NET.Handler/Ssl/Util/LazyX509Certificate.cs reads it) and RawData.
        byte[] certBytes = X509CertificateLoader.LoadCertificate(File.ReadAllBytes(certFile.FullName)).RawData;

        // Verify session
        Assert.NotNull(session.RemoteCertificate);
        Assert.Equal(certBytes, session.RemoteCertificate.GetRawCertData());

        // CLR: getPeerCertificateChain() (javax, deprecated for removal) has no SslStream counterpart; the source accepts
        // its UnsupportedOperationException on Java 15+:
        // See https://bugs.openjdk.java.net/browse/JDK-8241039

        Assert.NotNull(session.LocalCertificate);
        Assert.Equal(certBytes, session.LocalCertificate.GetRawCertData());
    }

    // CLR: SSLSession.getProtocol() returns the JSSE standard protocol name; SslStream.SslProtocol is the
    // System.Security.Authentication.SslProtocols value, whose names JdkSslContext.ToSslProtocols maps from the JSSE names.
    internal static string ProtocolName(AuthSslProtocols protocol)
    {
        switch (protocol)
        {
            case AuthSslProtocols.Tls13:
                return SslProtocols.TLS_v1_3;
            case AuthSslProtocols.Tls12:
                return SslProtocols.TLS_v1_2;
            default:
                return protocol.ToString();
        }
    }

    private async Task MySetupMutualAuth(SSLEngineTestParam param,
            FileInfo servertTrustCrtFile, FileInfo serverKeyFile, FileInfo serverCrtFile, string serverKeyPassword,
            FileInfo clientTrustCrtFile, FileInfo clientKeyFile, FileInfo clientCrtFile, string clientKeyPassword)
    {
        // CLR: the server handler wraps serverSslCtx.newEngine(alloc) after setUseClientMode(false) and
        // setNeedClientAuth(true); the SslStream options of a handler are created by the context (no SSLEngine facade,
        // owner decision 2026-11-24), so a server context is already in server mode and setNeedClientAuth(true) is
        // ClientAuth.REQUIRE here (JdkSslContext maps REQUIRE to SslServerAuthenticationOptions.ClientCertificateRequired
        // with SslStream's rejection of a missing certificate).
        serverSslCtx = WrapContext(param, SslContextBuilder.ForServer(serverCrtFile, serverKeyFile, serverKeyPassword)
                .SslProvider(SslServerProvider())
                .Protocols(param.Protocols())
                .Ciphers(param.Ciphers())
                .TrustManager(servertTrustCrtFile)
                .ClientAuth(ClientAuth.REQUIRE)
                .Ciphers(null, IdentityCipherSuiteFilter.INSTANCE)
                .SessionCacheSize(0)
                .SessionTimeout(0).Build());
        clientSslCtx = WrapContext(param, SslContextBuilder.ForClient()
                .SslProvider(SslClientProvider())
                .Protocols(param.Protocols())
                .Ciphers(param.Ciphers())
                .TrustManager(clientTrustCrtFile)
                .KeyManager(clientCrtFile, clientKeyFile, clientKeyPassword)
                .Ciphers(null, IdentityCipherSuiteFilter.INSTANCE)
                .SessionCacheSize(0)
                .EndpointIdentificationAlgorithm(null)
                .SessionTimeout(0).Build());

        serverConnectedChannel = null;
        sb = new ServerBootstrap();
        cb = new Bootstrap();

        sb.Group(new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory()));
        sb.Channel<NioServerSocketChannel>();
        sb.ChildHandler(new MutualAuthServerInitializer(this, param, serverCrtFile));

        cb.Group(new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory()));
        cb.Channel<NioSocketChannel>();
        cb.Handler(new MutualAuthClientInitializer(this, param, clientCrtFile));

        serverChannel = await sb.BindAsync(WildcardAddress(0), TestContext.Current.CancellationToken);
        int port = ((IPEndPoint) serverChannel.LocalAddress).Port;

        Task<IChannel> ccf = cb.ConnectAsync(new IPEndPoint(NetUtil.LOCALHOST, port), TestContext.Current.CancellationToken);
        Assert.True(await AwaitUninterruptibly(ccf));
        clientChannel = ccf.Result;
    }

    private sealed class MutualAuthServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SSLEngineTest test;
        private readonly SSLEngineTestParam param;
        private readonly FileInfo serverCrtFile;

        internal MutualAuthServerInitializer(SSLEngineTest test, SSLEngineTestParam param, FileInfo serverCrtFile)
        {
            this.test = test;
            this.param = param;
            this.serverCrtFile = serverCrtFile;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Config.SetAllocator(new TestByteBufAllocator(ch.Config.GetAllocator(), param.Type()));

            IChannelPipeline p = ch.Pipeline;
            // CLR: new SslHandler(engine) of the server engine; see MySetupMutualAuth.
            SslHandler handler = test.serverSslCtx.NewHandler(ch.Alloc());
            SslStream engine = handler.Engine();

            p.AddLast(handler);
            p.AddLast(new MessageDelegatorChannelHandler(test.serverReceiver, test.serverLatch));
            p.AddLast(new MutualAuthServerHandler(test, param, engine, serverCrtFile));
            p.AddLast(new SilenceExceptionHandler()
                    .MessageContains("Received fatal alert: internal_error"));
            test.serverConnectedChannel = ch;
        }
    }

    private sealed class MutualAuthServerHandler : ChannelInboundHandlerAdapter
    {
        private readonly SSLEngineTest test;
        private readonly SSLEngineTestParam param;
        private readonly SslStream engine;
        private readonly FileInfo serverCrtFile;

        internal MutualAuthServerHandler(SSLEngineTest test, SSLEngineTestParam param, SslStream engine,
                                         FileInfo serverCrtFile)
        {
            this.test = test;
            this.param = param;
            this.engine = engine;
            this.serverCrtFile = serverCrtFile;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause.InnerException is AuthenticationException)
            {
                test.serverException = cause.InnerException;
                CountDown(test.serverLatch);
            }
            else
            {
                test.serverException = cause;
                ctx.FireExceptionCaught(cause);
            }
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == SslHandshakeCompletionEvent.SUCCESS)
            {
                try
                {
                    test.VerifySSLSessionForMutualAuth(
                            param, engine, serverCrtFile, PRINCIPAL_NAME);
                }
                catch (Exception cause)
                {
                    test.serverException = cause;
                }
            }
        }
    }

    private sealed class MutualAuthClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SSLEngineTest test;
        private readonly SSLEngineTestParam param;
        private readonly FileInfo clientCrtFile;

        internal MutualAuthClientInitializer(SSLEngineTest test, SSLEngineTestParam param, FileInfo clientCrtFile)
        {
            this.test = test;
            this.param = param;
            this.clientCrtFile = clientCrtFile;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Config.SetAllocator(new TestByteBufAllocator(ch.Config.GetAllocator(), param.Type()));

            SslHandler handler = test.clientSslCtx.NewHandler(ch.Alloc());

            // CLR: handler.engine().setNeedClientAuth(true) on a client engine has no effect (SSLEngine.setNeedClientAuth
            // "Configures the engine to require client authentication", an option of server mode), and SslClient-
            // AuthenticationOptions has no such option, so there is no call here.
            IChannelPipeline p = ch.Pipeline;
            p.AddLast(handler);
            p.AddLast(new MessageDelegatorChannelHandler(test.clientReceiver, test.clientLatch));
            p.AddLast(new MutualAuthClientHandler(test, param, handler, clientCrtFile));
            p.AddLast(new SilenceExceptionHandler()
                    .MessageContains("TLSV1_ALERT_CERTIFICATE_REQUIRED"));
        }
    }

    private sealed class MutualAuthClientHandler : ChannelInboundHandlerAdapter
    {
        private readonly SSLEngineTest test;
        private readonly SSLEngineTestParam param;
        private readonly SslHandler handler;
        private readonly FileInfo clientCrtFile;

        internal MutualAuthClientHandler(SSLEngineTest test, SSLEngineTestParam param, SslHandler handler,
                                         FileInfo clientCrtFile)
        {
            this.test = test;
            this.param = param;
            this.handler = handler;
            this.clientCrtFile = clientCrtFile;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == SslHandshakeCompletionEvent.SUCCESS)
            {
                try
                {
                    test.VerifySSLSessionForMutualAuth(
                            param, handler.Engine(), clientCrtFile, PRINCIPAL_NAME);
                }
                catch (Exception cause)
                {
                    test.clientException = cause;
                }
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause.InnerException is AuthenticationException)
            {
                test.clientException = cause.InnerException;
                CountDown(test.clientLatch);
            }
            else
            {
                ctx.FireExceptionCaught(cause);
            }
        }
    }

    // CLR: String.getBytes() uses the platform charset; the ASCII messages encode the same in UTF-8.
    protected virtual async Task RunTest(string expectedApplicationProtocol)
    {
        ByteBuf clientMessage = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes("I am a client"));
        ByteBuf serverMessage = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes("I am a server"));
        try
        {
            await WriteAndVerifyReceived(clientMessage.Retain(), clientChannel, serverLatch, serverReceiver);
            await WriteAndVerifyReceived(serverMessage.Retain(), serverConnectedChannel, clientLatch, clientReceiver);
            VerifyApplicationLevelProtocol(clientChannel, expectedApplicationProtocol);
            VerifyApplicationLevelProtocol(serverConnectedChannel, expectedApplicationProtocol);
        }
        finally
        {
            clientMessage.Release();
            serverMessage.Release();
        }
    }

    // CLR: the JDK 9 ALPN accessor of JdkAlpnSslEngine (jvm-only-replaced) is SslStream.NegotiatedApplicationProtocol,
    // "The negotiated application protocol in TLS handshake"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0), whose protocol bytes
    // are the UTF-8 name (empty when nothing was negotiated, as getApplicationProtocol() returns ""); every engine of this
    // port is an SslStream, so the instanceof check has no counterpart.
    private static void VerifyApplicationLevelProtocol(IChannel channel, string expectedApplicationProtocol)
    {
        SslHandler handler = channel.Pipeline.Get<SslHandler>();
        Assert.NotNull(handler);
        string appProto = handler.ApplicationProtocol();
        Assert.Equal(expectedApplicationProtocol, appProto);

        SslStream engine = handler.Engine();
        // Also verify the Java9 exposed method.
        Assert.Equal(expectedApplicationProtocol == null ? StringUtil.EMPTY_STRING : expectedApplicationProtocol,
                Encoding.UTF8.GetString(engine.NegotiatedApplicationProtocol.Protocol.Span));
    }

    private static async Task WriteAndVerifyReceived(ByteBuf message, IChannel sendChannel,
                                                     CountdownEvent receiverLatch, MessageReceiver receiver)
    {
        ByteBuf buf = null;
        try
        {
            Assert.True(await Await(sendChannel.WriteAndFlushAsync(message.Duplicate()), TimeSpan.FromSeconds(10)));
            receiverLatch.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.NotEmpty(receiver.messages);

            buf = Unpooled.Buffer();
            ByteBuf buffer = receiver.messages.Take();
            for (;;)
            {
                buf.WriteBytes(buffer);
                buffer.Release();
                if (buf.ReadableBytes < message.ReadableBytes)
                {
                    if (!receiver.messages.TryTake(out buffer, TimeSpan.FromSeconds(1)))
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            Assert.Equal(message, buf);
        }
        finally
        {
            if (buf != null)
            {
                buf.Release();
                // CLR: the source releases every queued buffer and then clears the queue; BlockingCollection has no
                // Clear(), so each buffer is taken and released.
                while (receiver.messages.TryTake(out ByteBuf data))
                {
                    data.Release();
                }
            }
        }
    }

    protected abstract SslProvider SslClientProvider();

    protected abstract SslProvider SslServerProvider();

    /**
     * Called from the test cleanup code and can be used to release the {@code ctx} if it must be done manually.
     */
    protected virtual void CleanupClientSslContext(SslContext ctx)
    {
        ReferenceCountUtil.Release(ctx);
    }

    /**
     * Called from the test cleanup code and can be used to release the {@code ctx} if it must be done manually.
     */
    protected virtual void CleanupServerSslContext(SslContext ctx)
    {
        ReferenceCountUtil.Release(ctx);
    }

    protected Task SetupHandlers(SSLEngineTestParam param, ApplicationProtocolConfig apn)
    {
        return SetupHandlers(param, apn, apn);
    }

    protected Task SetupHandlers(SSLEngineTestParam param,
                                 ApplicationProtocolConfig serverApn, ApplicationProtocolConfig clientApn)
    {
        X509Bundle ssc = GetCachedCertificate();

        SslContextBuilder serverCtxBuilder = SslContextBuilder.ForServer(ssc.ToTempCertChainPem(),
                        ssc.ToTempPrivateKeyPem(), null)
                .SslProvider(SslServerProvider())
                .Ciphers(null, IdentityCipherSuiteFilter.INSTANCE)
                .ApplicationProtocolConfig(serverApn)
                .SessionCacheSize(0)
                .SessionTimeout(0);
        if (serverApn.Protocol_() == ApplicationProtocolConfig.Protocol.NPN ||
                serverApn.Protocol_() == ApplicationProtocolConfig.Protocol.NPN_AND_ALPN)
        {
            // NPN is not really well supported with TLSv1.3 so force to use TLSv1.2
            // See https://github.com/openssl/openssl/issues/3665
            serverCtxBuilder.Protocols(SslProtocols.TLS_v1_2);
        }

        SslContextBuilder clientCtxBuilder = SslContextBuilder.ForClient()
                .SslProvider(SslClientProvider())
                .ApplicationProtocolConfig(clientApn)
                .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                .Ciphers(null, IdentityCipherSuiteFilter.INSTANCE)
                .SessionCacheSize(0)
                .SessionTimeout(0);

        if (clientApn.Protocol_() == ApplicationProtocolConfig.Protocol.NPN ||
                clientApn.Protocol_() == ApplicationProtocolConfig.Protocol.NPN_AND_ALPN)
        {
            // NPN is not really well supported with TLSv1.3 so force to use TLSv1.2
            // See https://github.com/openssl/openssl/issues/3665
            clientCtxBuilder.Protocols(SslProtocols.TLS_v1_2);
        }

        return SetupHandlers(param.Type(), param.Delegate(),
                WrapContext(param, serverCtxBuilder.Build()), WrapContext(param, clientCtxBuilder.Build()));
    }

    protected async Task SetupHandlers(BufferType type, bool @delegate,
                                 SslContext serverCtx, SslContext clientCtx)
    {
        serverSslCtx = serverCtx;
        clientSslCtx = clientCtx;

        await SetupServer(type, @delegate);

        SetupClient(type, @delegate, null, 0);

        Task<IChannel> ccf = cb.ConnectAsync(ConnectAddress(serverChannel.LocalAddress), TestContext.Current.CancellationToken);
        await ccf;
        Assert.True(ccf.IsCompletedSuccessfully);
        clientChannel = ccf.Result;
    }

    // CLR: new InetSocketAddress(0) is the wildcard address 0.0.0.0, which the JDK binds as the IPv6 wildcard on a
    // dual-stack socket (net_util_md.c NET_InetAddressToSockaddr: for INADDR_ANY "we would always prefer IPv6 wildcard
    // address", https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/unix/native/libnet/net_util_md.c),
    // so the server accepts the IPv6 NetUtil.LOCALHOST the client connects to. The Transport server socket is an IPv6
    // dual-mode socket when the OS supports IPv6 (NioSocketChannel.NewIpChannel), where IPAddress.Any would only accept
    // IPv4 peers, so the wildcard is IPv6Any there.
    private static IPEndPoint WildcardAddress(int port)
    {
        return new IPEndPoint(System.Net.Sockets.Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, port);
    }

    // CLR: the server is bound to the wildcard address, and the source connects the client to serverChannel's local
    // address; the JDK replaces a wildcard remote address with the loopback address (SocketChannelImpl.checkRemote: "if
    // (address.isAnyLocalAddress())" returns an InetSocketAddress of Net.inet4LoopbackAddress()/inet6LoopbackAddress(),
    // https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/sun/nio/ch/SocketChannelImpl.java),
    // while a CLR socket cannot connect to 0.0.0.0 on Windows, so the same replacement is made here (as
    // test/Netty.NET.Handler.Tests/Flow/FlowControlHandlerTest.cs does).
    private static EndPoint ConnectAddress(EndPoint address)
    {
        IPEndPoint endPoint = (IPEndPoint) address;
        if (endPoint.Address.Equals(IPAddress.Any))
        {
            return new IPEndPoint(IPAddress.Loopback, endPoint.Port);
        }
        if (endPoint.Address.Equals(IPAddress.IPv6Any))
        {
            return new IPEndPoint(IPAddress.IPv6Loopback, endPoint.Port);
        }
        return address;
    }

    // CLR: the delegate parameter selected newHandler(alloc, delegatingExecutor), which is jvm-only-replaced (see the class
    // comment); it is always false here.
    private async Task SetupServer(BufferType type, bool @delegate)
    {
        serverConnectedChannel = null;
        sb = new ServerBootstrap();
        sb.Group(new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory()));
        sb.Channel<NioServerSocketChannel>();
        sb.ChildHandler(new SetupServerInitializer(this, type));

        serverChannel = await sb.BindAsync(WildcardAddress(0), TestContext.Current.CancellationToken);
    }

    private sealed class SetupServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly SSLEngineTest test;
        private readonly BufferType type;

        internal SetupServerInitializer(SSLEngineTest test, BufferType type)
        {
            this.test = test;
            this.type = type;
        }

        protected override void InitChannel(IChannel ch)
        {
            ch.Config.SetAllocator(new TestByteBufAllocator(ch.Config.GetAllocator(), type));

            IChannelPipeline p = ch.Pipeline;

            SslHandler sslHandler = test.serverSslCtx.NewHandler(ch.Alloc());
            test.serverSslHandshakeFuture = sslHandler.HandshakeFuture();
            p.AddLast(sslHandler);
            p.AddLast(new MessageDelegatorChannelHandler(test.serverReceiver, test.serverLatch));
            p.AddLast(new SetupServerHandler(test));
            test.serverConnectedChannel = ch;
        }
    }

    private sealed class SetupServerHandler : ChannelInboundHandlerAdapter
    {
        private readonly SSLEngineTest test;

        internal SetupServerHandler(SSLEngineTest test)
        {
            this.test = test;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause.InnerException is AuthenticationException)
            {
                test.serverException = cause.InnerException;
                CountDown(test.serverLatch);
            }
            else
            {
                ctx.FireExceptionCaught(cause);
            }
        }
    }

    private void SetupClient(BufferType type, bool @delegate, string host, int port)
    {
        cb = new Bootstrap();
        cb.Group(new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory()));
        cb.Channel<NioSocketChannel>();
        cb.Handler(new SetupClientInitializer(this, type, host, port));
    }

    private sealed class SetupClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly SSLEngineTest test;
        private readonly BufferType type;
        private readonly string host;
        private readonly int port;

        internal SetupClientInitializer(SSLEngineTest test, BufferType type, string host, int port)
        {
            this.test = test;
            this.type = type;
            this.host = host;
            this.port = port;
        }

        protected override void InitChannel(IChannel ch)
        {
            TestByteBufAllocator alloc = new TestByteBufAllocator(ch.Config.GetAllocator(), type);
            ch.Config.SetAllocator(alloc);

            IChannelPipeline p = ch.Pipeline;

            SslHandler sslHandler = host != null ? test.clientSslCtx.NewHandler(alloc, host, port) :
                    test.clientSslCtx.NewHandler(alloc);
            test.clientSslHandshakeFuture = sslHandler.HandshakeFuture();

            p.AddLast(sslHandler);
            p.AddLast(new MessageDelegatorChannelHandler(test.clientReceiver, test.clientLatch));
            p.AddLast(new SetupClientHandler(test));
        }
    }

    private sealed class SetupClientHandler : ChannelInboundHandlerAdapter
    {
        private readonly SSLEngineTest test;

        internal SetupClientHandler(SSLEngineTest test)
        {
            this.test = test;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (cause.InnerException is AuthenticationException)
            {
                test.clientException = cause.InnerException;
                CountDown(test.clientLatch);
            }
            else
            {
                ctx.FireExceptionCaught(cause);
            }
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            CountDown(test.clientLatch);
        }
    }

    [Theory(Timeout = 30000)]
    [MemberData(nameof(NewTestParams))]
    public async Task ClientInitiatedRenegotiationWithFatalAlertDoesNotInfiniteLoopServer(SSLEngineTestParam param)
    {
        // CLR: assumeTrue(PlatformDependent.javaVersion() >= 11) has no counterpart. The contexts enable
        // param.ciphers(), which needs a CipherSuitesPolicy: Windows has none, and SslContext.NewHandler throws
        // PlatformNotSupportedException there (docs/handler-ssl-design.md, Known differences).
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("CipherSuitesPolicy is not supported on Windows (docs/handler-ssl-design.md); the contexts of " +
                        "this test configure cipher suites.");
        }
        X509Bundle ssc = GetCachedCertificate();
        serverSslCtx = WrapContext(param, SslContextBuilder.ForServer(ssc.ToTempCertChainPem(), ssc.ToTempPrivateKeyPem())
                                        .SslProvider(SslServerProvider())
                                        .Protocols(param.Protocols())
                                        .Ciphers(param.Ciphers())
                                        .Build());
        sb = new ServerBootstrap()
                .Group(new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory()))
                .Channel<NioServerSocketChannel>()
                .ChildHandler(new RenegotiationServerInitializer(this, param));

        serverChannel = await sb.BindAsync(WildcardAddress(0), TestContext.Current.CancellationToken);

        // CLR: SslProvider.JDK is the only provider. The client renegotiation of SslHandler fails at once with
        // NotSupportedException (SslStream renegotiates only as a server, NegotiateClientCertificateAsync), which fails
        // the renegotiation handshake and closes the client, so the server sees the channel become inactive without a
        // renegotiation loop (measured with the cipher suites left out: inactive within 15 ms for all six parameters).
        clientSslCtx = WrapContext(param, SslContextBuilder.ForClient()
                                        // OpenSslEngine doesn't support renegotiation on client side
                                        .SslProvider(SslProvider.JDK)
                                        .TrustManager(InsecureTrustManagerFactory.INSTANCE)
                                        .Protocols(param.Protocols())
                                        .Ciphers(param.Ciphers())
                                        .Build());

        cb = new Bootstrap();
        cb.Group(new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory()))
                .Channel<NioSocketChannel>()
                .Handler(new RenegotiationClientInitializer(this, param));

        Task<IChannel> ccf = cb.ConnectAsync(ConnectAddress(serverChannel.LocalAddress), TestContext.Current.CancellationToken);
        clientChannel = await ccf;
        Assert.True(ccf.IsCompletedSuccessfully);

        serverLatch.Wait(TestContext.Current.CancellationToken);
    }

    private sealed class RenegotiationServerInitializer : ChannelInitializer<ISocketChannel>
    {
        private readonly SSLEngineTest test;
        private readonly SSLEngineTestParam param;

        internal RenegotiationServerInitializer(SSLEngineTest test, SSLEngineTestParam param)
        {
            this.test = test;
            this.param = param;
        }

        protected override void InitChannel(ISocketChannel ch)
        {
            ch.Config.SetAllocator(new TestByteBufAllocator(ch.Config.GetAllocator(), param.Type()));

            IChannelPipeline p = ch.Pipeline;

            SslHandler handler = test.serverSslCtx.NewHandler(ch.Alloc());

            p.AddLast(handler);
            p.AddLast(new RenegotiationServerHandler(test));
            test.serverConnectedChannel = ch;
        }
    }

    private sealed class RenegotiationServerHandler : ChannelInboundHandlerAdapter
    {
        private readonly SSLEngineTest test;

        internal RenegotiationServerHandler(SSLEngineTest test)
        {
            this.test = test;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SslHandshakeCompletionEvent &&
                    ((SslHandshakeCompletionEvent) evt).IsSuccess())
            {
                // This data will be sent to the client before any of the re-negotiation data can be
                // sent. The client will read this, detect that it is not the response to
                // renegotiation which was expected, and respond with a fatal alert.
                _ = ctx.WriteAndFlushAsync(ctx.Alloc().Buffer(1).WriteByte(100));
            }
            ctx.FireUserEventTriggered(evt);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
            // The server then attempts to trigger a flush operation once the application data is
            // received from the client. The flush will encrypt all data and should not result in
            // deadlock.
            _ = ctx.Channel.EventLoop.ScheduleAsync(() =>
            {
                _ = ctx.WriteAndFlushAsync(ctx.Alloc().Buffer(1).WriteByte(101));
            }, TimeSpan.FromMilliseconds(500));
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            CountDown(test.serverLatch);
        }
    }

    private sealed class RenegotiationClientInitializer : ChannelInitializer<ISocketChannel>
    {
        private readonly SSLEngineTest test;
        private readonly SSLEngineTestParam param;

        internal RenegotiationClientInitializer(SSLEngineTest test, SSLEngineTestParam param)
        {
            this.test = test;
            this.param = param;
        }

        protected override void InitChannel(ISocketChannel ch)
        {
            ch.Config.SetAllocator(new TestByteBufAllocator(ch.Config.GetAllocator(), param.Type()));

            IChannelPipeline p = ch.Pipeline;

            SslHandler sslHandler = test.clientSslCtx.NewHandler(ch.Alloc());

            // The renegotiate is not expected to succeed, so we should stop trying in a timely manner so
            // the unit test can terminate relativley quicly.
            sslHandler.SetHandshakeTimeout(TimeSpan.FromSeconds(1));
            p.AddLast(sslHandler);
            p.AddLast(new RenegotiationClientHandler());
            p.AddLast(new SilenceExceptionHandler()
                    .MessageContains("no_renegotiation"));
        }
    }

    private sealed class RenegotiationClientHandler : ChannelInboundHandlerAdapter
    {
        private int handshakeCount;

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            // OpenSSL SSLEngine sends a fatal alert for the renegotiation handshake because the
            // user data read as part of the handshake. The client receives this fatal alert and is
            // expected to shutdown the connection. The "invalid data" during the renegotiation
            // handshake is also delivered to channelRead(..) on the server.
            // JDK SSLEngine completes the renegotiation handshake and delivers the "invalid data"
            // is also delivered to channelRead(..) on the server. JDK SSLEngine does not send a
            // fatal error and so for testing purposes we close the connection after we have
            // completed the first renegotiation handshake (which is the second handshake).
            if (evt is SslHandshakeCompletionEvent && ++handshakeCount == 2)
            {
                _ = ctx.CloseAsync();
                return;
            }
            ctx.FireUserEventTriggered(evt);
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ReferenceCountUtil.Release(msg);
            // Simulate a request that the server's application logic will think is invalid.
            _ = ctx.WriteAndFlushAsync(ctx.Alloc().Buffer(1).WriteByte(102));
            _ = ctx.Pipeline.Get<SslHandler>().Renegotiate();
        }
    }

    [Fact]
    public void TestInvalidCipher()
    {
        X509Bundle cert = GetCachedCertificate();
        // CLR: SSLSocketFactory.getDefault().getDefaultCipherSuites() (the JSSE default cipher suites) has no counterpart:
        // SslStream leaves the cipher suites to the operating system and lists none, so Netty's default cipher suite list
        // (SslUtils.DEFAULT_CIPHER_SUITES) stands in for it.
        List<string> cipherList = new List<string>();
        cipherList.AddRange(SslUtils.DEFAULT_CIPHER_SUITES);
        cipherList.Add("InvalidCipher");
        SslHandler server = null;
        try
        {
            // CLR: newEngine(alloc) is NewHandler(alloc) (the SslHandler owns the SslStream options); the context
            // rejects the unknown name there with ArgumentException ("Unsupported CipherSuite: InvalidCipher"),
            // before the CipherSuitesPolicy that Windows lacks would be created (JdkSslContext.SetCipherSuitesPolicy).
            serverSslCtx = WrapContext(null, SslContextBuilder.ForServer(
                            (AsymmetricAlgorithm) cert.GetKeyPair(), cert.GetCertificate())
                    .SslProvider(SslServerProvider())
                    .Ciphers(cipherList).Build());
            server = serverSslCtx.NewHandler(UnpooledByteBufAllocator.Default);
            Assert.Fail();
        }
        catch (Exception expected) when (expected is ArgumentException || expected is AuthenticationException)
        {
            // expected when invalid cipher is used.
        }
        finally
        {
            // CLR: cleanupServerSslEngine releases the engine; the SslStream of a handler that never joined a pipeline
            // is disposed through the internal Engine(), as SniHandler/OptionalSslHandler do.
            server?.Engine().Dispose();
        }
    }

    // IMPORTANT: If this test fails, try rerunning the 'generate-certificate.sh' script.
    [Theory]
    [MemberData(nameof(NewTestParams))]
    public async Task TestRSASSAPSS(SSLEngineTestParam param)
    {
        char[] password = "password".ToCharArray();

        byte[] serverKeyStore = LoadKeyStore("rsaValidations-server-keystore.p12");

        byte[] clientKeyStore = LoadKeyStore("rsaValidation-user-certs.p12");

        SslStreamCertificateContext serverKeyManagerFactory = NewKeyManagerFactory(serverKeyStore, password);
        SslStreamCertificateContext clientKeyManagerFactory = NewKeyManagerFactory(clientKeyStore, password);

        FileInfo commonChain = ResourcesUtil.GetFile(GetType(), "rsapss-ca-cert.cert");
        ClientAuth auth = ClientAuth.REQUIRE;

        await MySetupMutualAuth(param, serverKeyManagerFactory, commonChain, clientKeyManagerFactory, commonChain,
                auth, false, true);

        Assert.True(clientLatch.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        RethrowIfNotNull(clientException);
        Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        RethrowIfNotNull(serverException);
    }

    protected virtual SslContext WrapContext(SSLEngineTestParam param, SslContext context)
    {
        return context;
    }
}
