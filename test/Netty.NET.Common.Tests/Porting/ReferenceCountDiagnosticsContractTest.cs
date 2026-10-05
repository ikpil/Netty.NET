using System;
using System.Globalization;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class ReferenceCountDiagnosticsContractTest
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-EG")]
    [InlineData("tr-TR")]
    public void SingleCountMessagesUseInvariantAsciiSigns(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var custom = (CultureInfo)CultureInfo.GetCultureInfo(culture).Clone();
        custom.NumberFormat.NegativeSign = "NEG";
        try
        {
            CultureInfo.CurrentCulture = custom;
            Assert.Equal("refCnt: -1", new IllegalReferenceCountException(-1).Message);
            Assert.Equal("refCnt: -2147483648", new IllegalReferenceCountException(int.MinValue).Message);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-EG")]
    [InlineData("tr-TR")]
    public void IncrementAndDecrementMessagesUseInvariantAsciiSigns(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var custom = (CultureInfo)CultureInfo.GetCultureInfo(culture).Clone();
        custom.NumberFormat.NegativeSign = "NEG";
        try
        {
            CultureInfo.CurrentCulture = custom;
            Assert.Equal("refCnt: -1, increment: 2", new IllegalReferenceCountException(-1, 2).Message);
            Assert.Equal("refCnt: -1, decrement: 2", new IllegalReferenceCountException(-1, -2).Message);
            Assert.Equal("refCnt: 0, decrement: -2147483648", new IllegalReferenceCountException(0, int.MinValue).Message);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(0, int.MinValue, "refCnt: 0, decrement: -2147483648")]
    [InlineData(int.MinValue, 0, "refCnt: -2147483648, decrement: 0")]
    [InlineData(1, -1, "refCnt: 1, decrement: 1")]
    [InlineData(1, -2147483647, "refCnt: 1, decrement: 2147483647")]
    [InlineData(int.MaxValue, int.MaxValue, "refCnt: 2147483647, increment: 2147483647")]
    public void ConstructorMessagesRetainPinnedInt32BoundaryTextInCheckedBuilds(int count, int delta, string expected)
    {
        Assert.Equal(expected, new IllegalReferenceCountException(count, delta).Message);
    }

    [Fact]
    public void NativeExceptionConstructorsPreserveExplicitMessagesAndCauseIdentity()
    {
        var cause = new InvalidOperationException("original failure");
        var messageOnly = new IllegalReferenceCountException("explicit failure");
        var withCause = new IllegalReferenceCountException("explicit failure", cause);
        Assert.IsAssignableFrom<InvalidOperationException>(withCause);
        Assert.Equal("explicit failure", messageOnly.Message);
        Assert.Null(messageOnly.InnerException);
        Assert.Equal("explicit failure", withCause.Message);
        Assert.Same(cause, withCause.InnerException);
        var causeOnly = new IllegalReferenceCountException(cause);
        Assert.Same(cause, causeOnly.InnerException);
        Assert.Equal(new IllegalReferenceCountException((string)null).Message, causeOnly.Message);
        Assert.Null(new IllegalReferenceCountException((Exception)null).InnerException);
        Assert.NotNull(new IllegalReferenceCountException().Message);
    }

    [Fact]
    public void ActualNativeCounterFailuresKeepTheirTypeTextAndOwnershipState()
    {
        int count = 1;
        var overRelease = Assert.Throws<IllegalReferenceCountException>(() => ReferenceCountUpdater.Release(ref count, 2));
        Assert.Equal("refCnt: 1, decrement: 2", overRelease.Message);
        Assert.Equal(1, count);
        ReferenceCountUpdater.Retain(ref count, int.MaxValue - 1);
        var overflow = Assert.Throws<IllegalReferenceCountException>(() => ReferenceCountUpdater.Retain(ref count));
        Assert.Equal("refCnt: 2147483647, increment: 1", overflow.Message);
        Assert.Equal(int.MaxValue, count);
        Assert.True(ReferenceCountUpdater.Release(ref count, int.MaxValue));
        var released = Assert.Throws<IllegalReferenceCountException>(() => ReferenceCountUpdater.Retain(ref count));
        Assert.Equal("refCnt: 0, increment: 1", released.Message);
        Assert.Equal(0, count);
    }
}
