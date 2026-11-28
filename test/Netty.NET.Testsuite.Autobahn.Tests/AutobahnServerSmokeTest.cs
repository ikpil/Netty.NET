using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Testsuite.Autobahn.Tests;

// CLR supplemental: testsuite-autobahn declares no JUnit test; its Maven build runs the Autobahn fuzzingclient
// against AutobahnServer (autobahntestsuite-maven-plugin, pom.xml). These cases bind the ported
// AutobahnServerInitializer on a loopback port, with the child allocator AutobahnServer sets, and take each branch of
// AutobahnServerHandler once with the BCL ClientWebSocket or a raw request: the echo of text, binary and fragmented
// messages, ping/pong, the close handshake, a refused method and an unsupported WebSocket version. They show that the
// port serves the protocol; they are not a conformance run.
public sealed class AutobahnServerSmokeTest : IAsyncLifetime
{
    private readonly MultiThreadIoEventLoopGroup group = new(NioIoHandler.NewFactory());
    private IChannel serverChannel;
    private int port;

    public async ValueTask InitializeAsync()
    {
        ServerBootstrap b = new ServerBootstrap()
                .Group(group)
                .Channel<NioServerSocketChannel>()
                .ChildOption(ChannelOption.ALLOCATOR, PooledByteBufAllocator.Default)
                .ChildHandler(new AutobahnServerInitializer());
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

    private async Task<ClientWebSocket> ConnectAsync(Action<ClientWebSocketOptions> configure, CancellationToken token)
    {
        var socket = new ClientWebSocket();
        configure?.Invoke(socket.Options);
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), token);
        return socket;
    }

    private static async Task<(WebSocketMessageType Type, byte[] Data)> ReceiveMessageAsync(ClientWebSocket socket,
            CancellationToken token)
    {
        var message = new MemoryStream();
        byte[] buffer = new byte[1024];

        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, token);
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return (result.MessageType, message.ToArray());
            }
        }
    }

    [Fact(Timeout = 10000)]
    public async Task TextAndBinaryMessagesAreEchoed()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using ClientWebSocket socket = await ConnectAsync(null, token);

        await socket.SendAsync(Encoding.UTF8.GetBytes("hello autobahn"), WebSocketMessageType.Text, true, token);
        (WebSocketMessageType type, byte[] data) = await ReceiveMessageAsync(socket, token);
        Assert.Equal(WebSocketMessageType.Text, type);
        Assert.Equal("hello autobahn", Encoding.UTF8.GetString(data));

        byte[] binary = { 0, 1, 2, 253, 254, 255 };
        await socket.SendAsync(binary, WebSocketMessageType.Binary, true, token);
        (type, data) = await ReceiveMessageAsync(socket, token);
        Assert.Equal(WebSocketMessageType.Binary, type);
        Assert.Equal(binary, data);
    }

    [Fact(Timeout = 10000)]
    public async Task FragmentedMessageIsEchoedThroughContinuationFrames()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using ClientWebSocket socket = await ConnectAsync(null, token);

        await socket.SendAsync(Encoding.UTF8.GetBytes("frag"), WebSocketMessageType.Text, false, token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("mented"), WebSocketMessageType.Text, true, token);

        (WebSocketMessageType type, byte[] data) = await ReceiveMessageAsync(socket, token);
        Assert.Equal(WebSocketMessageType.Text, type);
        Assert.Equal("fragmented", Encoding.UTF8.GetString(data));
    }

    [Fact(Timeout = 10000)]
    public async Task PingIsAnsweredWithPong()
    {
        // With KeepAliveTimeout the client sends PING frames and aborts the connection when no PONG arrives in time
        // (https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.clientwebsocketoptions.keepalivetimeout).
        CancellationToken token = TestContext.Current.CancellationToken;
        using ClientWebSocket socket = await ConnectAsync(options =>
        {
            options.KeepAliveInterval = TimeSpan.FromMilliseconds(50);
            options.KeepAliveTimeout = TimeSpan.FromMilliseconds(500);
        }, token);

        // The receive keeps the client reading pongs while several keep-alive intervals pass.
        Task<(WebSocketMessageType, byte[])> receive = ReceiveMessageAsync(socket, token);
        await Task.Delay(TimeSpan.FromMilliseconds(1500), token);
        Assert.Equal(WebSocketState.Open, socket.State);
        await socket.SendAsync(Encoding.UTF8.GetBytes("still open"), WebSocketMessageType.Text, true, token);
        (WebSocketMessageType type, byte[] data) = await receive;
        Assert.Equal(WebSocketMessageType.Text, type);
        Assert.Equal("still open", Encoding.UTF8.GetString(data));
    }

    [Fact(Timeout = 10000)]
    public async Task CloseFrameIsEchoedByTheHandshaker()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using ClientWebSocket socket = await ConnectAsync(null, token);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", token);

        Assert.Equal(WebSocketState.Closed, socket.State);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.CloseStatus);
        Assert.Equal("bye", socket.CloseStatusDescription);
    }

    [Fact(Timeout = 10000)]
    public async Task NonGetRequestIsForbiddenAndClosed()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, token);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"POST / HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nContent-Length: 0\r\n\r\n"), token);

        using var reader = new StreamReader(stream, Encoding.ASCII);
        string response = await reader.ReadToEndAsync(token);

        Assert.StartsWith("HTTP/1.1 403 Forbidden\r\n", response, StringComparison.Ordinal);
        Assert.Contains("content-length: 13\r\n", response, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("\r\n\r\n403 Forbidden", response, StringComparison.Ordinal);
    }

    [Fact(Timeout = 10000)]
    public async Task UnsupportedWebSocketVersionIsAnsweredWithUpgradeRequired()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, token);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
                $"GET / HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 99\r\n\r\n"), token);

        string response = await ReadHeaderBlockAsync(stream, token);

        Assert.StartsWith("HTTP/1.1 426 Upgrade Required\r\n", response, StringComparison.Ordinal);
        Assert.Contains("sec-websocket-version: 13\r\n", response, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadHeaderBlockAsync(Stream stream, CancellationToken token)
    {
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
