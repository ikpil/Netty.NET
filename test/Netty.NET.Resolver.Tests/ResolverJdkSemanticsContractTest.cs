using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Resolver.Tests;

// Supplemental cases pinning JDK behavior that DefaultNameResolver and the hosts file parser reproduce.
// Expected values were observed on JDK 21.0.10 with InetAddress.getByName/getAllByName and InputStreamReader.
public class ResolverJdkSemanticsContractTest
{
    [Theory]
    [InlineData("010.0.0.1", "10.0.0.1")] // decimal, where IPAddress.TryParse reads octal 8.0.0.1
    [InlineData("0127.0.0.1", "127.0.0.1")] // IPAddress.TryParse reads 87.0.0.1
    [InlineData("1.2.3", "1.2.0.3")]
    [InlineData("127.1", "127.0.0.1")]
    [InlineData("2130706433", "127.0.0.1")]
    [InlineData("1.2.3.04", "1.2.3.4")]
    [InlineData("[::1]", "::1")]
    [InlineData("::1", "::1")]
    [InlineData("::ffff:1.2.3.4", "1.2.3.4")]
    public async Task LiteralsFollowInetAddress(string host, string expected)
    {
        using var resolver = new DefaultNameResolver(ImmediateEventExecutor.INSTANCE);
        Assert.Equal(IPAddress.Parse(expected), await resolver.ResolveAsync(host));
        Assert.Equal(IPAddress.Parse(expected), Assert.Single(await resolver.ResolveAllAsync(host)));
    }

    [Theory]
    [InlineData("0x7f.0.0.1", "0x7f.0.0.1")]
    [InlineData("[::1]:80", "[::1]:80: invalid IPv6 address literal")]
    [InlineData("[1.2.3.4]", "[1.2.3.4]: invalid IPv6 address literal")]
    [InlineData("[::1", "[::1: invalid IPv6 address literal")]
    public async Task RejectedLiteralsCarryTheHost(string host, string message)
    {
        using var resolver = new DefaultNameResolver(ImmediateEventExecutor.INSTANCE);
        var error = await Assert.ThrowsAsync<UnknownHostException>(() => resolver.ResolveAsync(host));
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public async Task EmptyRoundRobinResultCarriesTheHost()
    {
        var name = new Mock<INameResolver<IPAddress>>();
        name.Setup(r => r.ResolveAllAsync("example.invalid", default))
            .ReturnsAsync(Array.Empty<IPAddress>());
        using var resolver = new RoundRobinInetAddressResolver(ImmediateEventExecutor.INSTANCE, name.Object);
        var error = await Assert.ThrowsAsync<UnknownHostException>(() => resolver.ResolveAsync("example.invalid"));
        Assert.Equal("example.invalid", error.Message);
    }

    [Fact]
    public void JavaUtf16DetectsTheByteOrderMarkAndDefaultsToBigEndian()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Bytes("127.0.0.1 host\n", new UnicodeEncoding(true, false)));
            Assert.Single(Parse(path, HostsFileEntriesProvider.UTF_16).Ipv4Entries()["host"]);
            File.WriteAllBytes(path, Bom(new byte[] { 0xFF, 0xFE }, Bytes("127.0.0.1 host\n", new UnicodeEncoding(false, false))));
            Assert.Single(Parse(path, HostsFileEntriesProvider.UTF_16).Ipv4Entries()["host"]);
            // Without a BOM Java UTF_16 reads big-endian, so a little-endian file yields no entries.
            File.WriteAllBytes(path, Bytes("127.0.0.1 host\n", new UnicodeEncoding(false, false)));
            Assert.Empty(Parse(path, HostsFileEntriesProvider.UTF_16).Ipv4Entries());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Utf8KeepsAByteOrderMarkLikeJavaUtf8()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Bom(new byte[] { 0xEF, 0xBB, 0xBF }, Bytes("127.0.0.1 first\n127.0.0.2 second\n", new UTF8Encoding(false))));
            // Java UTF_8 keeps U+FEFF, so the first address is invalid and only the second line remains.
            var entries = Parse(path, new UTF8Encoding(false)).Ipv4Entries();
            Assert.False(entries.ContainsKey("first"));
            Assert.Single(entries["second"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EveryAliasOwnsItsAddressInstance()
    {
        var entries = HostsFileEntriesProvider.Parser().Parse(new StringReader("fe80::1 a b\n")).Ipv6Entries();
        IPAddress a = Assert.Single(entries["a"]);
        IPAddress b = Assert.Single(entries["b"]);
        Assert.NotSame(a, b);
        a.ScopeId = 7;
        Assert.Equal(0, b.ScopeId);
    }

    [Fact]
    public void ParseChecksTheFileSystemEachTime()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var file = new FileInfo(path);
        try
        {
            Assert.Same(HostsFileEntriesProvider.Empty, HostsFileEntriesProvider.Parser().Parse(file));
            File.WriteAllText(path, "127.0.0.1 host\n");
            Assert.Single(HostsFileEntriesProvider.Parser().Parse(file).Ipv4Entries()["host"]);
        }
        finally { File.Delete(path); }
    }

    private static HostsFileEntriesProvider Parse(string path, Encoding charset)
        => HostsFileEntriesProvider.Parser().Parse(new FileInfo(path), charset);

    private static byte[] Bytes(string text, Encoding encoding) => encoding.GetBytes(text);

    private static byte[] Bom(byte[] bom, byte[] body)
    {
        var result = new byte[bom.Length + body.Length];
        bom.CopyTo(result, 0);
        body.CopyTo(result, bom.Length);
        return result;
    }
}
