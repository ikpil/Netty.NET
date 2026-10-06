using System;
using System.Reflection;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class AdaptiveCalculatorContractTest
{
    // Bind either public query shape so the byte-identical regression fixture
    // can execute against the old method API and the native property API.
    private static readonly Func<AdaptiveCalculator, int> ReadNext =
        (typeof(AdaptiveCalculator).GetProperty("NextSize")?.GetMethod ??
         typeof(AdaptiveCalculator).GetMethod("NextSize")).CreateDelegate<Func<AdaptiveCalculator, int>>();

    [Fact]
    public void OriginalReceiveBufferRampKeepsFourBucketGrowthAndMaximumRounding()
    {
        var calculator = new AdaptiveCalculator(64, 512, 10 * 1024 * 1024);
        foreach (int expected in new[] { 512, 8192, 131072, 2097152, 8388608, 8388608 })
        {
            Assert.Equal(expected, ReadNext(calculator));
            calculator.Record(expected);
        }
    }

    [Fact]
    public void TwoUnderfilledSamplesShrinkAndGrowthClearsThePendingDecrease()
    {
        var calculator = new AdaptiveCalculator(64, 1024, 65536);
        calculator.Record(0);
        Assert.Equal(1024, ReadNext(calculator));
        calculator.Record(700);
        Assert.Equal(1024, ReadNext(calculator));
        calculator.Record(0);
        Assert.Equal(512, ReadNext(calculator));
        calculator.Record(0);
        calculator.Record(512);
        Assert.Equal(8192, ReadNext(calculator));
        calculator.Record(0);
        Assert.Equal(8192, ReadNext(calculator));
        calculator.Record(0);
        Assert.Equal(4096, ReadNext(calculator));
    }

    [Theory]
    [InlineData(1, 1, 15, 1)]
    [InlineData(1, 8, 15, 8)]
    [InlineData(81, 95, 95, 81)]
    [InlineData(1001, 1023, 1023, 1001)]
    [InlineData(1073741825, int.MaxValue, int.MaxValue, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)]
    public void ValidTinyNarrowAndTerminalBoundsStayInclusive(int minimum, int initial, int maximum, int expected)
    {
        var calculator = new AdaptiveCalculator(minimum, initial, maximum);
        Assert.Equal(expected, ReadNext(calculator));
        foreach (int size in new[] { 0, 0, maximum, maximum, 0, 0, int.MinValue, -1, maximum })
        {
            calculator.Record(size);
            Assert.InRange(ReadNext(calculator), minimum, maximum);
        }
    }

    [Fact]
    public void FullSmallestOriginalBucketCanRampUp()
    {
        var calculator = new AdaptiveCalculator(16, 16, 128);
        calculator.Record(16);
        Assert.Equal(80, ReadNext(calculator));
        var tiny = new AdaptiveCalculator(1, 1, 15);
        tiny.Record(1);
        Assert.Equal(5, ReadNext(tiny));
    }

    [Fact]
    public void InvalidConstructorBoundsAreArgumentFailures()
    {
        foreach (var bounds in new[] { (0, 1, 2), (-1, 1, 2), (64, 63, 128), (64, 128, 127) })
            Assert.ThrowsAny<ArgumentException>(() => new AdaptiveCalculator(bounds.Item1, bounds.Item2, bounds.Item3));
    }

    [Fact]
    public void DenseBoundaryFeedbackNeverViolatesBoundsOrReversesItsDirection()
    {
        foreach (int minimum in new[] { 1, 15, 16, 17, 81, 95, 511, 512, 513, 1000, 1073741824, int.MaxValue - 1, int.MaxValue })
        foreach (int maximum in new[] { minimum, (int)Math.Min((long)minimum + 15, int.MaxValue), int.MaxValue })
        foreach (int initial in new[] { minimum, (int)(((long)minimum + maximum) / 2), maximum })
        {
            var calculator = new AdaptiveCalculator(minimum, initial, maximum);
            Assert.InRange(ReadNext(calculator), minimum, initial);
            for (int cycle = 0; cycle < 8; cycle++)
            {
                int before = ReadNext(calculator);
                calculator.Record(0);
                calculator.Record(0);
                Assert.InRange(ReadNext(calculator), minimum, before);
                before = ReadNext(calculator);
                calculator.Record(before);
                Assert.InRange(ReadNext(calculator), before, maximum);
            }
        }
    }

    [Fact]
    public void NativeNextSizeIsAReadOnlyPropertyWithoutAGetterAlias()
    {
        var property = typeof(AdaptiveCalculator).GetProperty("NextSize");
        Assert.NotNull(property);
        Assert.Equal(typeof(int), property.PropertyType);
        Assert.True(property.CanRead);
        Assert.False(property.CanWrite);
        Assert.Null(typeof(AdaptiveCalculator).GetMethod("NextSize", BindingFlags.Instance | BindingFlags.Public));
    }
}
