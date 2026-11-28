using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Testsuite.Http2.Tests;

// CLR supplemental: testsuite-http2 declares no JUnit test; its Maven build runs h2spec against Http2Server
// (h2spec-maven-plugin, pom.xml). These cases bind the ported Http2ServerInitializer on a loopback port and take each
// path through it once: HTTP/1.1 without an upgrade attempt (keep-alive and Connection: close), the cleartext upgrade
// answered on stream 1, and HTTP/2 with prior knowledge (a GET answered by onHeadersRead and a body echoed by
// onDataRead). They show that the port serves the protocol; they are not a conformance run.
public sealed class Http2ServerSmokeTest : IAsyncLifetime
{
    private const byte DATA = 0x0;
    private const byte HEADERS = 0x1;
    private const byte SETTINGS = 0x4;
    private const byte END_STREAM = 0x1;
    private const byte ACK = 0x1;
    private const byte END_HEADERS = 0x4;
    private static readonly byte[] Preface = Encoding.ASCII.GetBytes("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n");

    private readonly MultiThreadIoEventLoopGroup group = new(NioIoHandler.NewFactory());
    private IChannel serverChannel;
    private int port;

    public async ValueTask InitializeAsync()
    {
        ServerBootstrap b = new ServerBootstrap()
                .Group(group)
                .Channel<NioServerSocketChannel>()
                .ChildHandler(new Http2ServerInitializer());
        serverChannel = await b.BindAsync(new IPEndPoint(IPAddress.Loopback, 0), TestContext.Current.CancellationToken);
        port = ((IPEndPoint) serverChannel.LocalAddress).Port;
    }

    public async ValueTask DisposeAsync()
    {
        if (serverChannel != null)
        {
            await serverChannel.CloseAsync();
        }

        await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Fact(Timeout = 10000)]
    public async Task Http1WithoutUpgradeIsAnsweredByHelloWorldHttp1Handler()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/")
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        using HttpResponseMessage response = await client.SendAsync(request, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.Equal("text/plain; charset=UTF-8", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("keep-alive", response.Headers.Connection);
        Assert.Equal("Hello World - via HTTP/1.1 (Direct. No Upgrade Attempted.)",
                await response.Content.ReadAsStringAsync(token));
    }

    [Fact(Timeout = 10000)]
    public async Task Http1ConnectionCloseClosesTheChannelAfterTheResponse()
    {
        // ChannelFutureListener.CLOSE on the response write: the server closes, so reading to the end returns.
        CancellationToken token = TestContext.Current.CancellationToken;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, token);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"GET / HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: close\r\n\r\n"), token);

        using var reader = new StreamReader(stream, Encoding.ASCII);
        string response = await reader.ReadToEndAsync(token);

        Assert.StartsWith("HTTP/1.1 200 OK\r\n", response, StringComparison.Ordinal);
        Assert.DoesNotContain("keep-alive", response, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("\r\n\r\nHello World - via HTTP/1.1 (Direct. No Upgrade Attempted.)", response,
                StringComparison.Ordinal);
    }

    [Fact(Timeout = 10000)]
    public async Task CleartextUpgradeIsAnsweredOnStreamOneOverHttp2()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, token);
        NetworkStream stream = client.GetStream();
        // HTTP2-Settings: SETTINGS_MAX_CONCURRENT_STREAMS = 100, SETTINGS_INITIAL_WINDOW_SIZE = 65535 (base64url).
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"GET / HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nConnection: Upgrade, HTTP2-Settings\r\n" +
                "Upgrade: h2c\r\nHTTP2-Settings: AAMAAABkAAQAAP__\r\n\r\n"), token);

        string upgrade = await ReadHeaderBlockAsync(stream, token);
        Assert.StartsWith("HTTP/1.1 101 Switching Protocols\r\n", upgrade, StringComparison.Ordinal);
        Assert.Contains("upgrade: h2c\r\n", upgrade, StringComparison.OrdinalIgnoreCase);

        await stream.WriteAsync(Preface, token);
        await stream.WriteAsync(Frame(SETTINGS, 0, 0, default), token);
        (byte[] headers, byte[] body) = await ReadResponseAsync(stream, 1, token);

        // :status 200 is entry 8 of the HPACK static table (RFC 7541, Appendix A): one indexed field, 0x80 | 8.
        Assert.Equal(new byte[] { 0x88 }, headers);
        Assert.Equal("Hello World - via HTTP/2", Encoding.ASCII.GetString(body));
    }

    [Fact(Timeout = 10000)]
    public async Task PriorKnowledgeGetIsAnsweredOverHttp2()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        using HttpResponseMessage response = await client.SendAsync(request, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version20, response.Version);
        Assert.Equal("Hello World - via HTTP/2", await response.Content.ReadAsStringAsync(token));
    }

    [Fact(Timeout = 10000)]
    public async Task PriorKnowledgeRequestBodyIsEchoedByOnDataRead()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, token);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(Preface, token);
        await stream.WriteAsync(Frame(SETTINGS, 0, 0, default), token);
        // :method POST (static entry 3), :scheme http (6) and :path / (4), each one indexed field.
        await stream.WriteAsync(Frame(HEADERS, END_HEADERS, 1, new byte[] { 0x83, 0x86, 0x84 }), token);
        await stream.WriteAsync(Frame(DATA, END_STREAM, 1, Encoding.ASCII.GetBytes("echo me")), token);

        (byte[] headers, byte[] body) = await ReadResponseAsync(stream, 1, token);

        Assert.Equal(new byte[] { 0x88 }, headers);
        Assert.Equal("echo me", Encoding.ASCII.GetString(body));
    }

    private static byte[] Frame(byte type, byte flags, int streamId, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[9 + payload.Length];
        frame[0] = (byte) (payload.Length >> 16);
        frame[1] = (byte) (payload.Length >> 8);
        frame[2] = (byte) payload.Length;
        frame[3] = type;
        frame[4] = flags;
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(5), streamId);
        payload.CopyTo(frame.AsSpan(9));
        return frame;
    }

    // Reads frames up to END_STREAM on streamId and returns its header block and body; a SETTINGS frame of the server
    // is acknowledged on the way, as RFC 9113 section 6.5.3 asks.
    private static async Task<(byte[] Headers, byte[] Body)> ReadResponseAsync(Stream stream, int streamId,
            CancellationToken token)
    {
        byte[] headers = null;
        var body = new MemoryStream();
        byte[] header = new byte[9];

        while (true)
        {
            await stream.ReadExactlyAsync(header, token);
            int length = (header[0] << 16) | (header[1] << 8) | header[2];
            byte type = header[3];
            byte flags = header[4];
            int id = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(5)) & 0x7FFFFFFF;
            byte[] payload = new byte[length];
            await stream.ReadExactlyAsync(payload, token);

            if (type == SETTINGS && (flags & ACK) == 0)
            {
                await stream.WriteAsync(Frame(SETTINGS, ACK, 0, default), token);
            }

            if (id != streamId)
            {
                continue;
            }

            if (type == HEADERS)
            {
                Assert.Equal(END_HEADERS, flags & END_HEADERS);
                headers = payload;
            }
            else if (type == DATA)
            {
                Assert.NotNull(headers);
                body.Write(payload);
            }

            if ((type == HEADERS || type == DATA) && (flags & END_STREAM) != 0)
            {
                return (headers, body.ToArray());
            }
        }
    }

    private static async Task<string> ReadHeaderBlockAsync(Stream stream, CancellationToken token)
    {
        // One byte at a time, so that the HTTP/2 frames after the 101 response stay in the stream.
        var bytes = new List<byte>();
        byte[] one = new byte[1];

        while (bytes.Count < 4 || bytes[^4] != '\r' || bytes[^3] != '\n' || bytes[^2] != '\r' || bytes[^1] != '\n')
        {
            await stream.ReadExactlyAsync(one, token);
            bytes.Add(one[0]);
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }
}
