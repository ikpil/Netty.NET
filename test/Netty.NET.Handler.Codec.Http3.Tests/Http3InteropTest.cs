// CLR-only: no Java counterpart (a supplemental test of this port, not one of codec-http3's original tests). It
// checks the HTTP/3 port end to end against.NET's own HTTP/3 client: a server built as
// io.netty.handler.codec.http3.example.Http3ServerExample (examples/Netty.NET.Handler.Codec.Http3.Examples) binds
// a QuicEndpointChannel on a loopback ephemeral port (msquic, docs/codec-classes-quic-porting.md, Stage 2), and
// System.Net.Http.HttpClient sends a GET and a POST with HttpRequestMessage.Version = HttpVersion.Version30 and
// VersionPolicy = RequestVersionExact, so the request fails instead of falling back to HTTP/1.1 or HTTP/2. Its
// certificate validation accepts the self-signed ECDSA P-256 certificate (built as the SelfSignedCertificate of
// Http3FrameToHttpObjectCodecTest). The server's request-stream handler records the request (method, path and DATA
// bytes) and answers as the example does (status 404, server: netty, content-length and "Hello World!\r\n"). The
// tests are skipped where System.Net.Quic is not supported (QuicListener.IsSupported/QuicConnection.IsSupported
// false: macOS, Linux without libmsquic, Windows before 11 / Server 2022).
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Quic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Handler.Codec.Http3.Tests;

public class Http3InteropTest
{
    private const int TIMEOUT = 60000;
    private static readonly byte[] CONTENT = Encoding.ASCII.GetBytes("Hello World!\r\n");

    [Fact(Timeout = TIMEOUT)]
    public async Task TestHttpClientGet()
    {
        Assert.SkipUnless(IsQuicSupported(), "System.Net.Quic (msquic) is not supported on this platform.");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TaskCompletionSource<RecordedRequest> received =
                new TaskCompletionSource<RecordedRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunWithServer(received, async (client, port) =>
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, Uri(port, "/hello"));
            request.Version = HttpVersion.Version30;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            await AssertExampleResponse(response, cancellationToken);

            RecordedRequest recorded = await received.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            Assert.Equal("GET", recorded.Method);
            Assert.Equal("/hello", recorded.Path);
            Assert.Empty(recorded.Body);
        }, cancellationToken);
    }

    [Fact(Timeout = TIMEOUT)]
    public async Task TestHttpClientPost()
    {
        Assert.SkipUnless(IsQuicSupported(), "System.Net.Quic (msquic) is not supported on this platform.");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TaskCompletionSource<RecordedRequest> received =
                new TaskCompletionSource<RecordedRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        // More than one QUIC packet and, with HttpClient's buffer size, more than one DATA frame.
        byte[] body = Enumerable.Range(0, 100000).Select(i => (byte) ('a' + i % 26)).ToArray();
        await RunWithServer(received, async (client, port) =>
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, Uri(port, "/echo?x=1"));
            request.Version = HttpVersion.Version30;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            request.Content = new ByteArrayContent(body);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            await AssertExampleResponse(response, cancellationToken);

            RecordedRequest recorded = await received.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            Assert.Equal("POST", recorded.Method);
            Assert.Equal("/echo?x=1", recorded.Path);
            Assert.Equal(body, recorded.Body);
        }, cancellationToken);
    }

    private static bool IsQuicSupported()
    {
        // The platform checks guard the System.Net.Quic members for the platform analyzer (CA1416).
        return (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) &&
                QuicListener.IsSupported && QuicConnection.IsSupported;
    }

    private static Uri Uri(int port, string pathAndQuery)
    {
        return new Uri("https://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + pathAndQuery);
    }

    private static async Task AssertExampleResponse(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpVersion.Version30, response.Version);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("netty", Assert.Single(response.Headers.Server).Product.Name);
        Assert.Equal(CONTENT.Length, response.Content.Headers.ContentLength);
        Assert.Equal(CONTENT, await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    // Starts the server of Http3ServerExample on a loopback ephemeral port, runs the client part with an HTTP/3
    // HttpClient and closes both.
    private static async Task RunWithServer(TaskCompletionSource<RecordedRequest> received,
                                            Func<HttpClient, int, Task> clientPart, CancellationToken cancellationToken)
    {
        IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

        try
        {
            SelfSignedCertificate cert = new SelfSignedCertificate();
            QuicSslContext sslContext = QuicSslContextBuilder.ForServer(cert.Key(), null, cert.Cert())
                    .ApplicationProtocols(Http3.SupportedApplicationProtocols()).Build();
            IChannelHandler codec = Http3.NewQuicServerCodecBuilder()
                    .SslContext(sslContext)
                    .MaxIdleTimeout(TimeSpan.FromMilliseconds(5000))
                    .InitialMaxData(10000000)
                    .InitialMaxStreamDataBidirectionalLocal(1000000)
                    .InitialMaxStreamDataBidirectionalRemote(1000000)
                    .InitialMaxStreamsBidirectional(100)
                    .TokenHandler(InsecureQuicTokenHandler.INSTANCE)
                    .Handler(new ActionChannelInitializer<IQuicChannel>(ch =>
                    {
                        ch.Pipeline.AddLast(new Http3ServerConnectionHandler(
                                new ActionChannelInitializer<IQuicStreamChannel>(stream =>
                                {
                                    stream.Pipeline.AddLast(new RecordingRequestStreamHandler(received));
                                })));
                    })).Build();
            IChannel server = await new Bootstrap().Group(group)
                    .Channel<QuicEndpointChannel>()
                    .Handler(codec)
                    .BindAsync(new IPEndPoint(IPAddress.Loopback, 0), cancellationToken);

            try
            {
                int port = ((IPEndPoint) server.LocalAddress).Port;
                Assert.NotEqual(0, port);
                using SocketsHttpHandler handler = new SocketsHttpHandler();
                // The self-signed certificate is accepted, as InsecureTrustManagerFactory does.
                handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                using HttpClient client = new HttpClient(handler);
                await clientPart(client, port);
            }
            finally
            {
                await server.CloseAsync();
            }
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    private sealed record RecordedRequest(string Method, string Path, byte[] Body);

    // The request-stream handler of Http3ServerExample, which also records the request.
    private sealed class RecordingRequestStreamHandler : Http3RequestStreamInboundHandler
    {
        private readonly TaskCompletionSource<RecordedRequest> received;
        private readonly MemoryStream body = new MemoryStream();
        private string method;
        private string path;

        internal RecordingRequestStreamHandler(TaskCompletionSource<RecordedRequest> received)
        {
            this.received = received;
        }

        protected override void ChannelRead(IChannelHandlerContext ctx, IHttp3HeadersFrame frame)
        {
            method = frame.Headers().Method()?.ToString();
            path = frame.Headers().Path()?.ToString();
            ReferenceCountUtil.Release(frame);
        }

        protected override void ChannelRead(IChannelHandlerContext ctx, IHttp3DataFrame frame)
        {
            body.Write(ByteBufUtil.GetBytes(frame.Content));
            ReferenceCountUtil.Release(frame);
        }

        protected override void ChannelInputClosed(IChannelHandlerContext ctx)
        {
            received.TrySetResult(new RecordedRequest(method, path, body.ToArray()));
            IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
            headersFrame.Headers().Status(new StringCharSequence("404"));
            headersFrame.Headers().Add(new StringCharSequence("server"), new StringCharSequence("netty"));
            headersFrame.Headers().AddInt(new StringCharSequence("content-length"), CONTENT.Length);
            _ = ctx.WriteAsync(headersFrame);
            IQuicStreamChannel stream = (IQuicStreamChannel) ctx.Channel;
            _ = ctx.WriteAndFlushAsync(new DefaultHttp3DataFrame(Unpooled.WrappedBuffer(CONTENT)))
                    .ContinueWith(_ => stream.ShutdownOutputAsync(), TaskScheduler.Default);
        }
    }

    // A self-signed ECDSA P-256 certificate for "localhost", as the SelfSignedCertificate of
    // Http3FrameToHttpObjectCodecTest (io.netty.handler.ssl.util.SelfSignedCertificate reduced to key() and cert()).
    private sealed class SelfSignedCertificate
    {
        private readonly ECDsa key;
        private readonly X509Certificate2 cert;

        internal SelfSignedCertificate()
        {
            key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            CertificateRequest request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
            SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            request.CertificateExtensions.Add(san.Build());
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            cert = X509CertificateLoader.LoadCertificate(selfSigned.RawData);
        }

        internal AsymmetricAlgorithm Key()
        {
            return key;
        }

        internal X509Certificate2 Cert()
        {
            return cert;
        }
    }
}
