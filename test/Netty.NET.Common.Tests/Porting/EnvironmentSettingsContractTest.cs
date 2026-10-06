using System;
using System.Globalization;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

[Collection("System properties")]
public class EnvironmentSettingsContractTest : IDisposable
{
    private readonly string key = "NETTY_PORT_SETTINGS_" + Guid.NewGuid().ToString("N");
    public void Dispose() => Environment.SetEnvironmentVariable(key, null);

    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-EG")]
    [InlineData("tr-TR")]
    public void DecimalSettingsUseInvariantSignsInsteadOfCurrentCulture(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        var custom = (CultureInfo)CultureInfo.GetCultureInfo(cultureName).Clone();
        custom.NumberFormat.NegativeSign = "NEG";
        custom.NumberFormat.PositiveSign = "POS";
        try
        {
            CultureInfo.CurrentCulture = custom;
            Set("-123");
            Assert.Equal(-123, SystemPropertyUtil.GetInt(key, 17));
            Assert.Equal(-123L, SystemPropertyUtil.GetLong(key, 19));
            Set("+123");
            Assert.Equal(123, SystemPropertyUtil.GetInt(key, 17));
            Assert.Equal(123L, SystemPropertyUtil.GetLong(key, 19));
            Set("NEG123");
            Assert.Equal(17, SystemPropertyUtil.GetInt(key, 17));
            Assert.Equal(19L, SystemPropertyUtil.GetLong(key, 19));
            Set("POS123");
            Assert.Equal(17, SystemPropertyUtil.GetInt(key, 17));
            Assert.Equal(19L, SystemPropertyUtil.GetLong(key, 19));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void MissingPresentAndUpdatedValuesRemainLiveNativeEnvironmentReads()
    {
        Assert.False(SystemPropertyUtil.Contains(key));
        Assert.Null(SystemPropertyUtil.Get(key));
        Assert.Equal("fallback", SystemPropertyUtil.Get(key, "fallback"));
        Set("first");
        Assert.True(SystemPropertyUtil.Contains(key));
        Assert.Equal("first", SystemPropertyUtil.Get(key, "fallback"));
        Set("second");
        Assert.Equal("second", SystemPropertyUtil.Get(key));
        Assert.Equal(Environment.GetEnvironmentVariable(key), SystemPropertyUtil.Get(key));
        Set(null);
        Assert.False(SystemPropertyUtil.Contains(key));
        Assert.Equal(17, SystemPropertyUtil.GetInt(key, 17));
        Assert.Equal(19L, SystemPropertyUtil.GetLong(key, 19));
    }

    [Fact]
    public void DecimalBoundsAndMalformedValuesUseTheSpecifiedDefaults()
    {
        var cases = new (string Text, int Int, long Long)[]
        {
            ("-2147483648", int.MinValue, -2147483648L),
            ("2147483647", int.MaxValue, 2147483647L),
            ("2147483648", 17, 2147483648L),
            ("-9223372036854775808", 17, long.MinValue),
            ("9223372036854775807", 17, long.MaxValue),
            ("9223372036854775808", 17, 19),
            ("-9223372036854775809", 17, 19),
            (" \t+12\r\n", 12, 12),
            ("1,234", 17, 19), ("1.2", 17, 19), ("1e3", 17, 19),
            ("0x10", 17, 19), ("+", 17, 19), ("--1", 17, 19)
        };
        foreach (var item in cases)
        {
            Set(item.Text);
            Assert.Equal(item.Int, SystemPropertyUtil.GetInt(key, 17));
            Assert.Equal(item.Long, SystemPropertyUtil.GetLong(key, 19));
        }
    }

    [Fact]
    public void NativeUnicodeWhitespaceAndAsciiDigitsAreExplicitConfigurationPolicy()
    {
        Set("\u00a0-12\u2003");
        Assert.Equal(-12, SystemPropertyUtil.GetInt(key, 17));
        Assert.Equal(-12L, SystemPropertyUtil.GetLong(key, 19));
        Set("\u0661\u0662");
        Assert.Equal(17, SystemPropertyUtil.GetInt(key, 17));
        Assert.Equal(19L, SystemPropertyUtil.GetLong(key, 19));
    }

    [Fact]
    public void BooleanTokensUseOrdinalCaseFoldingAndPreserveBothFallbacks()
    {
        foreach (string token in new[] { "true", "TRUE", " YeS ", "1", "\u00a0TRUE\u2003" })
        {
            Set(token);
            Assert.True(SystemPropertyUtil.GetBoolean(key, false));
        }
        foreach (string token in new[] { "false", "FALSE", " No ", "0" })
        {
            Set(token);
            Assert.False(SystemPropertyUtil.GetBoolean(key, true));
        }
        foreach (string token in new[] { " ", "invalid", "2", "\u0661" })
        {
            Set(token);
            Assert.True(SystemPropertyUtil.GetBoolean(key, true));
            Assert.False(SystemPropertyUtil.GetBoolean(key, false));
        }
    }

    [Fact]
    public void WholeEnvironmentEnumerationUsesTheRuntimeInsteadOfADuplicateFacade()
    {
        Assert.Null(typeof(SystemPropertyUtil).GetMethod("GetProperties"));
        Set("native-value");
        var snapshot = Environment.GetEnvironmentVariables();
        Assert.Equal("native-value", snapshot[key]);
        Set("changed");
        Assert.Equal("native-value", snapshot[key]);
        Assert.Equal("changed", Environment.GetEnvironmentVariable(key));
    }

    private void Set(string value) => Environment.SetEnvironmentVariable(key, value);
}
