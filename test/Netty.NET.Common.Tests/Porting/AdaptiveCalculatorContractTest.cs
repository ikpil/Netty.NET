using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class AdaptiveCalculatorContractTest
{
    private static readonly Func<AdaptiveCalculator, int> ReadNext =
        typeof(AdaptiveCalculator).GetProperty("NextSize").GetMethod.CreateDelegate<Func<AdaptiveCalculator, int>>();

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

    // Rows printed by the unmodified pinned AdaptiveCalculator (JDK 21) for the same feedback sequence: the initial
    // nextSize, then "r<size>><nextSize>" per record call, stopping at the first exception. They pin the source
    // 16-byte first bucket, the 2^30 last bucket, the asymmetric rounding (growth caps at maximum, decrease floors at
    // minimum) and the out-of-range sizes that bounds between two buckets produce.
    [Theory]
    [InlineData("1,1,16 ctor!IndexOutOfRangeException")]
    [InlineData("8,15,23 ctor!IndexOutOfRangeException")]
    [InlineData("8,8,65536 ctor!IndexOutOfRangeException")]
    [InlineData("15,22,30 16 r16>16 r16>16 r0>16 r0>16 r0>16 r0>16 r700>16 r16>16 r15>16 r1>16 r30>16 r-2147483648>16 r-1>16 r16>16 r16>16 r0>16 r0>16 r30>16 r30>16 r0>16 r0>16 r0>16 r0>16")]
    [InlineData("16,16,31 16 r16>16 r16>16 r0>16 r0>16 r0>16 r0>16 r700>16 r16>16 r15>16 r1>16 r31>16 r-2147483648>16 r-1>16 r16>16 r16>16 r0>16 r0>16 r31>16 r31>16 r0>16 r0>16 r0>16 r0>16")]
    [InlineData("16,16,65536 16 r16>16 r16>16 r0>16 r0>16 r0>16 r0>16 r700>80 r16>80 r15>64 r1>64 r65536>128 r-2147483648>128 r-1>112 r112>176 r176>240 r0>240 r0>224 r65536>288 r65536>352 r0>352 r0>336 r0>336 r0>320")]
    [InlineData("64,32800,65536 32768 r32768>65536 r65536>65536 r0>65536 r0>32768 r0>32768 r0>16384 r700>16384 r16>8192 r15>8192 r1>4096 r65536>65536 r-2147483648>65536 r-1>32768 r32768>65536 r65536>65536 r0>65536 r0>32768 r65536>65536 r65536>65536 r0>65536 r0>32768 r0>32768 r0>16384")]
    [InlineData("81,81,81 81 r81>80 r80>80 r0>80 r0>96 r0>96 r0>96 r700>80 r16>80 r15>96 r1>96 r81>96 r-2147483648>96 r-1>96 r96>80 r80>80 r0>80 r0>96 r81>96 r81>96 r0>96 r0>96 r0>96 r0>96")]
    [InlineData("81,88,96 81 r81>96 r96>96 r0>96 r0>96 r0>96 r0>96 r700>96 r16>96 r15>96 r1>96 r96>96 r-2147483648>96 r-1>96 r96>96 r96>96 r0>96 r0>96 r96>96 r96>96 r0>96 r0>96 r0>96 r0>96")]
    [InlineData("1000,1007,1015 1000 r1000>512 r512>512 r0>512 r0>1024 r0>1024 r0>1024 r700>1024 r16>1024 r15>1024 r1>1024 r1015>1024 r-2147483648>1024 r-1>1024 r1024>512 r512>512 r0>512 r0>1024 r1015>1024 r1015>1024 r0>1024 r0>1024 r0>1024 r0>1024")]
    [InlineData("1073741824,1073741824,1073741824 1073741824 r1073741824>1073741824 r1073741824>1073741824 r0>1073741824 r0>1073741824 r0>1073741824 r0>1073741824 r700>1073741824 r16>1073741824 r15>1073741824 r1>1073741824 r1073741824>1073741824 r-2147483648>1073741824 r-1>1073741824 r1073741824>1073741824 r1073741824>1073741824 r0>1073741824 r0>1073741824 r1073741824>1073741824 r1073741824>1073741824 r0>1073741824 r0>1073741824 r0>1073741824 r0>1073741824")]
    [InlineData("1073741825,1073741825,1073741825 1073741825 r1073741825>1073741824 r1073741824>1073741824 r0>1073741824 r0!IndexOutOfRangeException")]
    [InlineData("2147483647,2147483647,2147483647 2147483647 r2147483647>1073741824 r1073741824>1073741824 r0>1073741824 r0!IndexOutOfRangeException")]
    public void FeedbackSequenceMatchesPinnedSourceRows(string expected)
    {
        string[] bounds = expected.Substring(0, expected.IndexOf(' ')).Split(',');
        int minimum = int.Parse(bounds[0], CultureInfo.InvariantCulture);
        int initial = int.Parse(bounds[1], CultureInfo.InvariantCulture);
        int maximum = int.Parse(bounds[2], CultureInfo.InvariantCulture);
        var row = new StringBuilder(FormattableString.Invariant($"{minimum},{initial},{maximum}"));
        AdaptiveCalculator calculator;
        try
        {
            calculator = new AdaptiveCalculator(minimum, initial, maximum);
        }
        catch (Exception e)
        {
            row.Append(" ctor!").Append(e.GetType().Name);
            Assert.Equal(expected, row.ToString());
            return;
        }

        row.Append(' ').Append(ReadNext(calculator).ToString(CultureInfo.InvariantCulture));
        // -2 stands for "record the current nextSize".
        int[] feedback = { -2, -2, 0, 0, 0, 0, 700, 16, 15, 1, maximum, int.MinValue, -1, -2, -2, 0, 0, maximum, maximum, 0, 0, 0, 0 };
        foreach (int f in feedback)
        {
            int size = f == -2 ? ReadNext(calculator) : f;
            row.Append(" r").Append(size.ToString(CultureInfo.InvariantCulture));
            try
            {
                calculator.Record(size);
            }
            catch (Exception e)
            {
                row.Append('!').Append(e.GetType().Name);
                break;
            }
            row.Append('>').Append(ReadNext(calculator).ToString(CultureInfo.InvariantCulture));
        }
        Assert.Equal(expected, row.ToString());
    }

    [Fact]
    public void FullSmallestBucketIsTreatedAsUnderfilledLikeTheSource()
    {
        // record() compares with SIZE_TABLE[max(0, index - 1)], so a full first bucket only arms the decrease.
        var calculator = new AdaptiveCalculator(16, 16, 128);
        calculator.Record(16);
        Assert.Equal(16, ReadNext(calculator));
        calculator.Record(17);
        Assert.Equal(80, ReadNext(calculator));
    }

    [Theory]
    [InlineData(0, 1, 2, "minimum : 0 (expected: > 0)")]
    [InlineData(-1, 1, 2, "minimum : -1 (expected: > 0)")]
    [InlineData(64, 63, 128, "initial: 63")]
    [InlineData(64, 128, 127, "maximum: 127")]
    public void InvalidConstructorBoundsUseSourceMessages(int minimum, int initial, int maximum, string message)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
        try
        {
            var e = Assert.Throws<ArgumentException>(() => new AdaptiveCalculator(minimum, initial, maximum));
            Assert.Equal(message, e.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
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
