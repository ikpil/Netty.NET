/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link Http2Settings}.
 */
// CLR: @BeforeEach is the constructor ("xUnit creates a new instance of the test class for every test that is run",
// docs/codec-http-porting.md, Shared CLR adaptations). @ParameterizedTest @ValueSource is a [Theory] with one
// InlineData row per value; Long.MIN_VALUE/MAX_VALUE and Integer.MIN_VALUE/MAX_VALUE are long/int MinValue/MaxValue.
// Http2Settings is a Dictionary<char, long> (owner decision 2026-11-19): size() is Count and the inherited
// get(char) of a present key is the indexer; put(char, Long) is Put(char, long), so the (Long) casts have no
// counterpart (xUnit2013 asks for Assert.Empty/Assert.Single in place of a size comparison). The Java Long/Integer/Boolean getters return long?/int?/bool?, whose (long)/(int) casts take the place
// of Java's unboxing casts. assertThrows(IllegalArgumentException.class) is Assert.ThrowsAny<ArgumentException>.
public class Http2SettingsTest
{

    private readonly Http2Settings settings;

    public Http2SettingsTest()
    {
        settings = new Http2Settings();
    }

    [Fact]
    public void StandardSettingsShouldBeNotSet()
    {
        Assert.Empty(settings);
        Assert.Null(settings.HeaderTableSize());
        Assert.Null(settings.InitialWindowSize());
        Assert.Null(settings.MaxConcurrentStreams());
        Assert.Null(settings.PushEnabled());
        Assert.Null(settings.MaxFrameSize());
        Assert.Null(settings.MaxHeaderListSize());
        Assert.Null(settings.ConnectProtocolEnabled());
    }

    [Fact]
    public void StandardSettingsShouldBeSet()
    {
        settings.InitialWindowSize(1);
        settings.MaxConcurrentStreams(2);
        settings.PushEnabled(true);
        settings.HeaderTableSize(3);
        settings.MaxFrameSize(MAX_FRAME_SIZE_UPPER_BOUND);
        settings.MaxHeaderListSize(4);
        settings.ConnectProtocolEnabled(true);
        Assert.Equal(1, (int) settings.InitialWindowSize());
        Assert.Equal(2L, (long) settings.MaxConcurrentStreams());
        Assert.True(settings.PushEnabled());
        Assert.Equal(3L, (long) settings.HeaderTableSize());
        Assert.Equal(MAX_FRAME_SIZE_UPPER_BOUND, (int) settings.MaxFrameSize());
        Assert.Equal(4L, (long) settings.MaxHeaderListSize());
        Assert.True(settings.ConnectProtocolEnabled());
    }

    [Fact]
    public void SettingsShouldSupportUnsignedShort()
    {
        char key = (char) (short.MaxValue + 1);
        settings.Put(key, 123L);
        Assert.Equal(123L, settings[key]);
    }

    [Theory]
    [InlineData(MIN_HEADER_LIST_SIZE)]
    [InlineData(MIN_HEADER_LIST_SIZE + 1L)]
    [InlineData(MAX_HEADER_LIST_SIZE - 1L)]
    [InlineData(MAX_HEADER_LIST_SIZE)]
    public void HeaderListSize(long value)
    {
        settings.MaxHeaderListSize(value);
        Assert.Equal(value, (long) settings.MaxHeaderListSize());
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(MIN_HEADER_LIST_SIZE - 1L)]
    [InlineData(MAX_HEADER_LIST_SIZE + 1L)]
    [InlineData(long.MaxValue)]
    public void HeaderListSizeBoundCheck(long value)
    {
        Assert.ThrowsAny<ArgumentException>(() => settings.MaxHeaderListSize(value));
    }

    [Theory]
    [InlineData(MIN_HEADER_TABLE_SIZE)]
    [InlineData(MIN_HEADER_TABLE_SIZE + 1L)]
    [InlineData(MAX_HEADER_TABLE_SIZE - 1L)]
    [InlineData(MAX_HEADER_TABLE_SIZE)]
    public void HeaderTableSize(long value)
    {
        settings.HeaderTableSize(value);
        Assert.Equal(value, (long) settings.HeaderTableSize());
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(MIN_HEADER_TABLE_SIZE - 1L)]
    [InlineData(MAX_HEADER_TABLE_SIZE + 1L)]
    [InlineData(long.MaxValue)]
    public void HeaderTableSizeBoundCheck(long value)
    {
        Assert.ThrowsAny<ArgumentException>(() => settings.HeaderTableSize(value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PushEnabled(bool value)
    {
        settings.PushEnabled(value);
        Assert.Equal(value, settings.PushEnabled());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    public void EnablePush(long value)
    {
        settings.Put(Http2CodecUtil.SETTINGS_ENABLE_PUSH, value);
        Assert.Equal(value, settings[Http2CodecUtil.SETTINGS_ENABLE_PUSH]);
        Assert.Equal(value == 1, settings.PushEnabled());
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    [InlineData(2L)]
    [InlineData(long.MaxValue)]
    public void EnablePushBoundCheck(long value)
    {
        Assert.ThrowsAny<ArgumentException>(() => settings.Put(Http2CodecUtil.SETTINGS_ENABLE_PUSH, value));
    }

    [Theory]
    [InlineData(MIN_CONCURRENT_STREAMS)]
    [InlineData(MIN_CONCURRENT_STREAMS + 1L)]
    [InlineData(MAX_CONCURRENT_STREAMS - 1L)]
    [InlineData(MAX_CONCURRENT_STREAMS)]
    public void MaxConcurrentStreams(long value)
    {
        settings.MaxConcurrentStreams(value);
        Assert.Equal(value, (long) settings.MaxConcurrentStreams());
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(MIN_CONCURRENT_STREAMS - 1L)]
    [InlineData(MAX_CONCURRENT_STREAMS + 1L)]
    [InlineData(long.MaxValue)]
    public void MaxConcurrentStreamsBoundCheck(long value)
    {
        Assert.ThrowsAny<ArgumentException>(() => settings.MaxConcurrentStreams(value));
    }

    [Theory]
    [InlineData(MIN_INITIAL_WINDOW_SIZE)]
    [InlineData(MIN_INITIAL_WINDOW_SIZE + 1)]
    [InlineData(MAX_INITIAL_WINDOW_SIZE - 1)]
    [InlineData(MAX_INITIAL_WINDOW_SIZE)]
    public void InitialWindowSize(int value)
    {
        settings.InitialWindowSize(value);
        Assert.Equal(value, (int) settings.InitialWindowSize());
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(MIN_INITIAL_WINDOW_SIZE - 1)]
    public void InitialWindowSizeIntBoundCheck(int value)
    {
        Assert.ThrowsAny<ArgumentException>(() => settings.InitialWindowSize(value));
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(MIN_INITIAL_WINDOW_SIZE - 1L)]
    [InlineData(MAX_INITIAL_WINDOW_SIZE + 1L)]
    [InlineData(long.MaxValue)]
    public void InitialWindowSizeBoundCheck(long value)
    {
        Assert.ThrowsAny<ArgumentException>(() => settings.Put(Http2CodecUtil.SETTINGS_INITIAL_WINDOW_SIZE, value));
    }

    [Theory]
    [InlineData(MAX_FRAME_SIZE_LOWER_BOUND)]
    [InlineData(MAX_FRAME_SIZE_LOWER_BOUND + 1)]
    [InlineData(MAX_FRAME_SIZE_UPPER_BOUND - 1)]
    [InlineData(MAX_FRAME_SIZE_UPPER_BOUND)]
    public void MaxFrameSize(int value)
    {
        settings.MaxFrameSize(value);
        Assert.Equal(value, (int) settings.MaxFrameSize());
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(MAX_FRAME_SIZE_LOWER_BOUND - 1)]
    [InlineData(MAX_FRAME_SIZE_UPPER_BOUND + 1)]
    [InlineData(int.MaxValue)]
    public void MaxFrameSizeIntBoundCheck(int value)
    {
        Assert.ThrowsAny<ArgumentException>(() => settings.MaxFrameSize(value));
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(0L)]
    [InlineData(MAX_FRAME_SIZE_LOWER_BOUND - 1L)]
    [InlineData(MAX_FRAME_SIZE_UPPER_BOUND + 1L)]
    [InlineData((long) int.MaxValue)]
    [InlineData(long.MaxValue)]
    public void MaxFrameSizeBoundCheck(long value)
    {
        Assert.ThrowsAny<ArgumentException>(() => settings.Put(Http2CodecUtil.SETTINGS_MAX_FRAME_SIZE, value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConnectProtocolEnabled(bool value)
    {
        settings.ConnectProtocolEnabled(value);
        Assert.Equal(value, settings.ConnectProtocolEnabled());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    public void EnableConnectProtocol(long value)
    {
        settings.Put(Http2CodecUtil.SETTINGS_ENABLE_CONNECT_PROTOCOL, value);
        Assert.Equal(value, settings[Http2CodecUtil.SETTINGS_ENABLE_CONNECT_PROTOCOL]);
        Assert.Equal(value == 1, settings.ConnectProtocolEnabled());
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    [InlineData(2L)]
    [InlineData(long.MaxValue)]
    public void EnableConnectProtocolBoundCheck(long value)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => settings.Put(Http2CodecUtil.SETTINGS_ENABLE_CONNECT_PROTOCOL, value));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(123L)]
    [InlineData((long) int.MaxValue)]
    [InlineData(MAX_UNSIGNED_INT - 1L)]
    [InlineData(MAX_UNSIGNED_INT)]
    public void NonStandardSetting(long value)
    {
        char key = (char) 0;
        settings.Put(key, value);
        Assert.Equal(value, settings[key]);
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData((long) int.MinValue)]
    [InlineData(-1L)]
    [InlineData(MAX_UNSIGNED_INT + 1L)]
    [InlineData(long.MaxValue)]
    public void NonStandardSettingBoundCheck(long value)
    {
        char key = (char) 0;
        Assert.ThrowsAny<ArgumentException>(() => settings.Put(key, value));
    }
}
