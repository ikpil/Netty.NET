using System;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Thread-local globals")]
public class CsvStorageContractTest : IDisposable
{
    public CsvStorageContractTest() => FastThreadLocal.RemoveAll();
    public void Dispose() => FastThreadLocal.RemoveAll();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidAndInvalidCsvLeaveNoWorkerScratchState(bool multiple)
    {
        string content = new string('x', 100_000) + "\"\0\ud800,\r\n";
        string encoded = "\"" + content.Replace("\"", "\"\"") + "\"";
        if (multiple)
        {
            var fields = StringUtil.UnescapeCsvFields(encoded + ",tail,");
            Assert.Equal(new[] { content, "tail", "" }, fields);
            Assert.Throws<ArgumentException>(() => StringUtil.UnescapeCsvFields("\"bad\"x"));
            Assert.Equal(new[] { "", "" }, StringUtil.UnescapeCsvFields(","));
            Assert.Equal(content, fields[0]);
        }
        else
        {
            string decoded = StringUtil.UnescapeCsv(encoded);
            Assert.Equal(content, decoded);
            Assert.Throws<ArgumentException>(() => StringUtil.UnescapeCsv("\"bad\"x\""));
            string unchanged = new string('y', 3);
            Assert.Same(unchanged, StringUtil.UnescapeCsv(unchanged));
            Assert.Equal(content, decoded);
        }
        Assert.Null(InternalThreadLocalMap.GetIfSet());
        Assert.Equal(0, FastThreadLocal.Size());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullCsvFailsWithNativeArgumentErrorBeforeCreatingWorkerState(bool multiple)
    {
        var error = Assert.Throws<ArgumentNullException>(() =>
        {
            if (multiple) StringUtil.UnescapeCsvFields(null);
            else StringUtil.UnescapeCsv(null);
        });
        Assert.Equal("value", error.ParamName);
        Assert.Null(InternalThreadLocalMap.GetIfSet());
    }
}
