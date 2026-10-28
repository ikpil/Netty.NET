using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using Moq;

namespace Netty.NET.Resolver.Tests;

public class HostsFileContractTest
{
    [Fact]
    public void InvalidLinesAreIgnoredWithoutDnsOrPermissiveLegacyIpParsing()
    {
        var entries = HostsFileEntriesProvider.Parser().Parse(new StringReader(
            "# comment\n127.1 abbreviated\n0x7f000001 hexadecimal\n2130706433 integer\n" +
            "not-an-ip invalid\n127.0.0.1\n\n::1 good6 alias6\n127.0.0.1 good4 alias4\n"));
        Assert.Equal(2, entries.Ipv4Entries().Count);
        Assert.Equal(2, entries.Ipv6Entries().Count);
        Assert.Equal(IPAddress.Loopback, Assert.Single(entries.Ipv4Entries()["good4"]));
        Assert.Equal(IPAddress.IPv6Loopback, Assert.Single(entries.Ipv6Entries()["alias6"]));
    }

    [Fact]
    public void MappedIpv6HasJavaInet4AddressClassification()
    {
        var entries = HostsFileEntriesProvider.Parser().Parse(new StringReader("::ffff:192.0.2.1 mapped"));
        Assert.Equal(IPAddress.Parse("192.0.2.1"), Assert.Single(entries.Ipv4Entries()["mapped"]));
        Assert.Empty(entries.Ipv6Entries());
    }

    [Fact]
    public void ParserClosesSuppliedReaderOnSuccessAndFailure()
    {
        var reader = new TrackingReader("127.0.0.1 host", false);
        HostsFileEntriesProvider.Parser().Parse(reader);
        Assert.True(reader.Closed);
        var failing = new TrackingReader("", true);
        Assert.Throws<IOException>(() => HostsFileEntriesProvider.Parser().Parse(failing));
        Assert.True(failing.Closed);
    }

    [Fact]
    public void MissingFilesAndEmptyContentUseSharedEmptySnapshots()
    {
        var parser = HostsFileEntriesProvider.Parser();
        Assert.Same(HostsFileEntriesProvider.Empty, parser.Parse(new StringReader("# no entries\n")));
        Assert.Same(HostsFileEntriesProvider.Empty, parser.Parse(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()))));
        Assert.Same(HostsFileEntries.Empty, HostsFileParser.Parse(new StringReader("invalid")));
    }

    [Fact]
    public void FileParserTriesEncodingsInOrderAndStopsAtFirstNonemptyResult()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "127.0.0.1 host\n::1 host\n", new UnicodeEncoding(true, false));
            var entries = HostsFileEntriesProvider.Parser().Parse(new FileInfo(path), Encoding.UTF8, Encoding.Latin1, Encoding.BigEndianUnicode);
            Assert.Equal(IPAddress.Loopback, Assert.Single(entries.Ipv4Entries()["host"]));
            Assert.Equal(IPAddress.IPv6Loopback, Assert.Single(entries.Ipv6Entries()["host"]));
            File.WriteAllText(path, "127.0.0.1 host\n", new UTF8Encoding(false));
            // A successful first parse must never inspect later encodings.
            Assert.Single(HostsFileEntriesProvider.Parser().Parse(new FileInfo(path), Encoding.UTF8, null).Ipv4Entries());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Utf16BomDoesNotHideFirstEntry()
    {
        string path = Path.GetTempFileName();
        try
        {
            foreach (bool bigEndian in new[] { false, true })
            {
                var encoding = new UnicodeEncoding(bigEndian, true);
                File.WriteAllText(path, "127.0.0.1 host\n", encoding);
                var entries = HostsFileEntriesProvider.Parser().Parse(new FileInfo(path), Encoding.UTF8, encoding);
                Assert.Equal(IPAddress.Loopback, Assert.Single(entries.Ipv4Entries()["host"]));
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ProviderSnapshotsMapAndLegacyParserKeepsOnlyFirstAddress()
    {
        var map = new Dictionary<string, IReadOnlyList<IPAddress>> { ["host"] = new[] { IPAddress.Loopback } };
        var entries = new HostsFileEntriesProvider(map, new Dictionary<string, IReadOnlyList<IPAddress>>());
        map.Clear();
        Assert.Single(entries.Ipv4Entries());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, IReadOnlyList<IPAddress>>)entries.Ipv4Entries()).Clear());
        var legacy = HostsFileParser.Parse(new StringReader("192.0.2.1 host\n192.0.2.2 HOST\n"));
        Assert.Equal(IPAddress.Parse("192.0.2.1"), legacy.Inet4Entries()["host"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, IPAddress>)legacy.Inet4Entries()).Clear());
    }

    [Fact]
    public void HostNormalizationDoesNotDependOnCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var provider = HostsFileEntriesProvider.Parser().Parse(new StringReader("127.0.0.1 FILE\n"));
            var parser = new Mock<HostsFileEntriesProvider.IParser>();
            parser.Setup(p => p.ParseSilently()).Returns(provider);
            parser.Setup(p => p.ParseSilently(It.IsAny<Encoding[]>())).Returns(provider);
            var resolver = new DefaultHostsFileEntriesResolver(parser.Object, 0);
            Assert.Equal(IPAddress.Loopback, resolver.Address("FILE", ResolvedAddressTypes.Ipv4Only));
            Assert.Equal(IPAddress.Loopback, resolver.Address("file", ResolvedAddressTypes.Ipv4Only));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void RefreshDisabledUsesOneSnapshotAndInvalidConfigurationFails()
    {
        var parser = new Mock<HostsFileEntriesProvider.IParser>();
        parser.Setup(p => p.ParseSilently()).Returns(HostsFileEntriesProvider.Empty);
        parser.Setup(p => p.ParseSilently(It.IsAny<Encoding[]>())).Returns(HostsFileEntriesProvider.Empty);
        var resolver = new DefaultHostsFileEntriesResolver(parser.Object, 0);
        resolver.Address("host", ResolvedAddressTypes.Ipv4Only);
        resolver.Address("host", ResolvedAddressTypes.Ipv6Only);
        Assert.Single(parser.Invocations);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultHostsFileEntriesResolver(parser.Object, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Address("host", (ResolvedAddressTypes)99));
        Assert.Throws<ArgumentNullException>(() => resolver.Address(null, ResolvedAddressTypes.Ipv4Only));
    }

    [Theory]
    [InlineData(ResolvedAddressTypes.Ipv4Preferred)]
    [InlineData(ResolvedAddressTypes.Ipv6Preferred)]
    public void PreferenceIncludesEveryAddressInOriginalFamilyOrder(ResolvedAddressTypes preference)
    {
        var provider = HostsFileEntriesProvider.Parser().Parse(new StringReader(
            "192.0.2.1 host\n2001:db8::1 host\n192.0.2.2 host\n2001:db8::2 host\n"));
        var parser = new Mock<HostsFileEntriesProvider.IParser>();
        parser.Setup(p => p.ParseSilently()).Returns(provider);
        parser.Setup(p => p.ParseSilently(It.IsAny<Encoding[]>())).Returns(provider);
        var resolver = new DefaultHostsFileEntriesResolver(parser.Object, 0);
        var v4 = new[] { IPAddress.Parse("192.0.2.1"), IPAddress.Parse("192.0.2.2") };
        var v6 = new[] { IPAddress.Parse("2001:db8::1"), IPAddress.Parse("2001:db8::2") };
        var expected = new List<IPAddress>();
        expected.AddRange(preference == ResolvedAddressTypes.Ipv4Preferred ? v4 : v6);
        expected.AddRange(preference == ResolvedAddressTypes.Ipv4Preferred ? v6 : v4);
        Assert.Equal(expected, resolver.Addresses("host", preference));
        Assert.Equal(expected[0], resolver.Address("HOST", preference));
    }

    private sealed class TrackingReader(string text, bool failRead) : StringReader(text)
    {
        internal bool Closed;
        public override string ReadLine() => failRead ? throw new IOException("read") : base.ReadLine();
        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }
}
