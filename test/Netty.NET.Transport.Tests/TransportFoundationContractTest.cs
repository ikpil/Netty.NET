using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

public class TransportFoundationContractTest
{
    [Fact]
    public void MetadataAndWaterMarksPreserveDefaultsAndBounds()
    {
        var metadata = new ChannelMetadata(true);
        Assert.True(metadata.HasDisconnect());
        Assert.Equal(16, metadata.DefaultMaxMessagesPerRead());
        Assert.Equal(32 * 1024, WriteBufferWaterMark.DEFAULT.Low());
        Assert.Equal(64 * 1024, WriteBufferWaterMark.DEFAULT.High());
        Assert.Equal(0, new WriteBufferWaterMark(0, 0).High());
        Assert.Equal(int.MaxValue, new WriteBufferWaterMark(int.MaxValue, int.MaxValue).Low());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChannelMetadata(false, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChannelMetadata(false, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WriteBufferWaterMark(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WriteBufferWaterMark(2, 1));
    }

    [Fact]
    public void WaterMarkFormattingDoesNotDependOnCallerCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal("WriteBufferWaterMark(low: 123, high: 456)", new WriteBufferWaterMark(123, 456).ToString());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void OptionRegistryIsSharedAcrossClosedGenericTypesAndValidatesStoredType()
    {
        string name = Guid.NewGuid().ToString();
        var option = ChannelOption.ValueOf<int>(name);
        Assert.Same(option, ChannelOption<int>.ValueOf(name));
        Assert.True(ChannelOption<string>.Exists(name));
        Assert.Throws<ArgumentException>(() => ChannelOption.ValueOf<string>(name));
        Assert.Throws<ArgumentException>(() => ChannelOption.ValueOf<int>(""));
        Assert.Throws<ArgumentNullException>(() => ChannelOption.ValueOf<int>(null));
        Assert.Throws<ArgumentNullException>(() => ChannelOption.ValueOf<int>(null, "part"));
        Assert.Throws<ArgumentNullException>(() => ChannelOption.ValueOf<int>(typeof(int), null));
        option.Validate(0);
        Assert.Throws<ArgumentNullException>(() => ChannelOption.ValueOf<string>(Guid.NewGuid().ToString()).Validate(null));
    }

    [Fact]
    public void OptionNamesAndIdsAreUniqueUnderConcurrentPublication()
    {
        string prefix = Guid.NewGuid().ToString();
        var same = new ChannelOption<int>[100];
        Parallel.For(0, same.Length, i => same[i] = ChannelOption.ValueOf<int>(prefix));
        Assert.All(same, value => Assert.Same(same[0], value));
        var separate = Enumerable.Range(0, 50).Select(i => ChannelOption.ValueOf<int>(prefix + i)).ToArray();
        Assert.Equal(50, separate.Select(o => o.Id).Distinct().Count());
        Assert.NotEqual(0, separate[0].CompareTo(separate[1]));
        Assert.Equal(0, same[0].CompareTo(same[1]));
        Assert.Same(ChannelOption.AUTO_READ, ChannelOption<bool>.ValueOf("AUTO_READ"));
        Assert.Same(ChannelOption.TCP_FASTOPEN, ChannelOption.ValueOf<int>(typeof(ChannelOption), "TCP_FASTOPEN"));
    }

    [Fact]
    public void EstimatorCountsReadableMemoryAndNotFilePayload()
    {
        var estimator = DefaultMessageSizeEstimator.DEFAULT;
        Assert.Same(estimator.NewHandle(), estimator.NewHandle());
        var buffer = Unpooled.Buffer(32);
        buffer.WriteBytes(new byte[10]);
        buffer.ReaderIndex = 3;
        var holder = new DefaultByteBufHolder(buffer);
        var file = new DefaultFileRegion(new FileInfo("unused-region-file"), 0, long.MaxValue);
        try
        {
            Assert.Equal(7, estimator.NewHandle().Size(buffer));
            Assert.Equal(7, estimator.NewHandle().Size(holder));
            Assert.Equal(0, estimator.NewHandle().Size(file));
            Assert.Equal(8, estimator.NewHandle().Size(null));
            Assert.Equal(8, estimator.NewHandle().Size(new object()));
            Assert.Equal(12, new DefaultMessageSizeEstimator(12).NewHandle().Size("unknown"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultMessageSizeEstimator(-1));
        }
        finally { holder.Release(); file.Release(); }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(2, 4, 0)]
    [InlineData(2, 4, 2)]
    [InlineData(2, 4, 4)]
    public void FileRegionTransfersOnlySelectedRangeAndPreservesSourcePosition(int start, int count, int relative)
    {
        string path = Path.GetTempFileName();
        byte[] content = Enumerable.Range(0, 10).Select(i => (byte)i).ToArray();
        try
        {
            File.WriteAllBytes(path, content);
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            source.Position = 7;
            var region = new DefaultFileRegion(source, start, count);
            using var target = new MemoryStream();
            try
            {
                Assert.Equal(count - relative, region.TransferTo(target, relative));
                Assert.Equal(content.Skip(start + relative).Take(count - relative), target.ToArray());
                Assert.Equal(7, source.Position);
                Assert.Equal(count - relative, region.Transferred());
                Assert.Equal(start, region.Position());
                Assert.Equal(count, region.Count());
            }
            finally { region.Release(); }
            Assert.False(region.IsOpen());
            Assert.Throws<ObjectDisposedException>(() => source.ReadByte());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LazyRegionDoesNotOpenOnZeroTransferOrAfterRelease()
    {
        var region = new DefaultFileRegion(new FileInfo("nonexistent-" + Guid.NewGuid()), 0, 1);
        Assert.False(region.IsOpen());
        using var target = new MemoryStream();
        Assert.Equal(0, region.TransferTo(target, 1));
        Assert.False(region.IsOpen());
        Assert.Throws<ArgumentOutOfRangeException>(() => region.TransferTo(target, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => region.TransferTo(target, 2));
        Assert.True(region.Release());
        region.Open();
        Assert.False(region.IsOpen());
        Assert.Equal(0, region.TransferTo(target, 1));
        Assert.Throws<IllegalReferenceCountException>(() => region.TransferTo(target, 0));
    }

    [Fact]
    public void RetainAndTouchWorkThroughBothRegionAndCommonInterfaces()
    {
        IFileRegion region = new DefaultFileRegion(new FileInfo("unused-file"), 0, 0);
        IReferenceCounted counted = region;
        Assert.Same(region, region.Retain());
        Assert.Same(region, counted.Retain(2));
        Assert.Equal(4, region.ReferenceCount);
        Assert.Same(region, region.Touch());
        Assert.Same(region, region.Touch("hint"));
        Assert.Same(region, counted.Touch());
        Assert.Equal(4, region.ReferenceCount);
        Assert.False(region.Release(3));
        Assert.True(region.Release());
        Assert.Throws<IllegalReferenceCountException>(() => region.Retain());
    }

    [Fact]
    public void FileRegionValidatesArgumentsAndTruncatedEmptyRangeWithoutOverflow()
    {
        Assert.Throws<ArgumentNullException>(() => new DefaultFileRegion((FileInfo)null, 0, 0));
        Assert.Throws<ArgumentNullException>(() => new DefaultFileRegion((FileStream)null, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultFileRegion(new FileInfo("unused"), -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultFileRegion(new FileInfo("unused"), 0, -1));
        string path = Path.GetTempFileName();
        try
        {
            var region = new DefaultFileRegion(new FileInfo(path), long.MaxValue, long.MaxValue);
            using var target = new MemoryStream();
            try
            {
                Assert.Throws<IOException>(() => region.TransferTo(target, 1));
                Assert.Throws<IOException>(() => region.TransferTo(target, 0));
                Assert.Equal(0, region.Transferred());
            }
            finally { region.Release(); }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FailedTargetWriteDoesNotCountUnacceptedBytes()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[100]);
            var region = new DefaultFileRegion(new FileInfo(path), 0, 100);
            using var target = new ThrowingStream();
            try
            {
                Assert.Throws<IOException>(() => region.TransferTo(target, 0));
                Assert.Equal(0, region.Transferred());
            }
            finally { region.Release(); }
        }
        finally { File.Delete(path); }
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("write failed");
    }
}
