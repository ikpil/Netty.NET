/*
 * Copyright 2025 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

/**
 * unit tests for {@link DefaultHttp3SettingsFrame}.
 */
// CLR: the package-private JUnit class and methods are public, because xUnit runs public test classes and methods.
// DefaultHttp3SettingsFrame's @Deprecated overrides get(long) and put(long, Long) ("use settings() and manipulate the
// Http3Settings directly") are not ported (owner decision 2026-11-16; the DefaultHttp3SettingsFrame entry of
// docs/codec-http3-porting-manifest.json): frame.get(key) is its named replacement frame.Settings().Get(key), which is
// what the override returned (settings.get(key)), and frame.put(key, value) is the IHttp3SettingsFrame default member
// Put, which calls settings().put(key, value) as the override did; a default interface member is called through the
// interface, hence ((IHttp3SettingsFrame) frame).Put(...). testDeprecatedMethodsStillWork, which exercises only that
// deprecated get, is recorded as CLR-inapplicable in the manifest; its original comments are kept at its place below.
// Map.Entry<Long, Long> is KeyValuePair<long, long> and a Long that may be null is long?; frame.iterator() is
// GetEnumerator() and hasNext()/next() are MoveNext()/Current; a long key or value is never null, so
// assertNotNull(entry.getKey()) checks the boxed value, as Java checks the boxed Long. assertEquals(Object, Object) calls
// equals: DefaultHttp3SettingsFrame is an IEnumerable, which xUnit's Assert.Equal would compare entry by entry, so
// AssertObjectEquals/AssertObjectNotEquals keep JUnit's comparison (as AssertSettingsEquals in codec-http2's
// InboundHttp2ToHttpAdapterTest). assertThrows(IllegalArgumentException.class, ...) is
// Assert.ThrowsAny<ArgumentException> and IllegalStateException is InvalidOperationException (docs/codec-http-porting.md,
// Core test notes).
public class DefaultHttp3SettingsFrameTest
{

    [Fact]
    public void TestDefaultFrameHasEmptySettings()
    {
        DefaultHttp3SettingsFrame frame = new DefaultHttp3SettingsFrame();

        Assert.NotNull(frame.Settings());
        Assert.False(frame.GetEnumerator().MoveNext());
        Assert.Null(frame.Settings().QpackMaxTableCapacity());
        Assert.Null(frame.Settings().Get(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id()));
    }

    [Fact]
    public void TestPutAndGetDelegatesToSettings()
    {
        DefaultHttp3SettingsFrame frame = new DefaultHttp3SettingsFrame();

        frame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(), 256L);
        Assert.Equal(256L, frame.Settings().QpackMaxTableCapacity());

        frame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id(), 8L);
        Assert.Equal(8L, frame.Settings().QpackBlockedStreams());

        ((IHttp3SettingsFrame) frame).Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(), 512L);
        Assert.Equal(512L, frame.Settings().Get(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id()));

        ((IHttp3SettingsFrame) frame).Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id(), 16L);
        Assert.Equal(16L, frame.Settings().Get(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id()));
    }

    [Fact]
    public void TestPutRejectsReservedHttp2Settings()
    {
        DefaultHttp3SettingsFrame frame = new DefaultHttp3SettingsFrame();
        Assert.ThrowsAny<ArgumentException>(() => ((IHttp3SettingsFrame) frame).Put(0x4, 10L)); // HTTP/2 reserved key
    }

    [Fact]
    public void TestSettingsReferenceIsRetained()
    {
        Http3Settings settings = new Http3Settings().QpackMaxTableCapacity(512);
        DefaultHttp3SettingsFrame frame = new DefaultHttp3SettingsFrame(settings);

        Assert.Same(settings, frame.Settings());
        Assert.Equal(512L, frame.Settings().QpackMaxTableCapacity());

        // Modify settings externally and verify frame sees update
        settings.QpackBlockedStreams(3);
        Assert.Equal(3L, frame.Settings().Get(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id()));
        Assert.Equal(3L, frame.Settings().QpackBlockedStreams());
    }

    [Fact]
    public void TestEqualsAndHashCodeCompareSettings()
    {
        Http3Settings s1 = new Http3Settings().QpackMaxTableCapacity(128);
        Http3Settings s2 = new Http3Settings().QpackMaxTableCapacity(128);

        DefaultHttp3SettingsFrame f1 = new DefaultHttp3SettingsFrame(s1);
        DefaultHttp3SettingsFrame f2 = new DefaultHttp3SettingsFrame(s2);

        AssertObjectEquals(f1, f2);
        Assert.Equal(f1.GetHashCode(), f2.GetHashCode());
    }

    [Fact]
    public void TestIteratorReflectsSettingsEntries()
    {
        Http3Settings settings = new Http3Settings()
                .QpackMaxTableCapacity(64)
                .QpackBlockedStreams(2);
        DefaultHttp3SettingsFrame frame = new DefaultHttp3SettingsFrame(settings);

        IEnumerator<KeyValuePair<long, long>> it = frame.GetEnumerator();
        int count = 0;

        while (it.MoveNext())
        {
            KeyValuePair<long, long> entry = it.Current;
            Assert.NotNull((object) entry.Key);
            Assert.NotNull((object) entry.Value);
            count++;
        }

        Assert.True(count >= 2);
    }

    [Fact]
    public void TestToStringIncludesSettings()
    {
        Http3Settings settings = new Http3Settings()
                .QpackMaxTableCapacity(1)
                .EnableConnectProtocol(true);

        DefaultHttp3SettingsFrame frame = new DefaultHttp3SettingsFrame(settings);
        string output = frame.ToString();

        Assert.True(output.Contains("Http3Settings"));
        Assert.True(output.Contains("0x1"));
        Assert.True(output.Contains("settings="));
    }

    [Fact]
    public void TestCopyOfCreatesDeepCopy()
    {
        DefaultHttp3SettingsFrame original = new DefaultHttp3SettingsFrame(
                new Http3Settings().QpackMaxTableCapacity(10).EnableConnectProtocol(true));

        DefaultHttp3SettingsFrame copy = DefaultHttp3SettingsFrame.CopyOf(original);

        Assert.NotSame(original, copy);
        AssertObjectEquals(original, copy);

        // Modify original and ensure copy remains unchanged
        ((IHttp3SettingsFrame) original).Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id(), 5L);
        AssertObjectNotEquals(original, copy);
    }

    // CLR: testDeprecatedMethodsStillWork (frame.put(QPACK_BLOCKED_STREAMS, 5L), then assertEquals(5L, frame.get(...))
    // and assertEquals(5L, frame.settings().qpackBlockedStreams())) is CLR-inapplicable: the old get() API it exercises
    // is the @Deprecated DefaultHttp3SettingsFrame.get(long), which is not ported (see the class comment). Its original
    // comments:
    // Uses old get() API
    // New typed accessor matches

    [Fact]
    public void TestDuplicateKeyInSettingsFrameTriggersError()
    {
        DefaultHttp3SettingsFrame frame = new DefaultHttp3SettingsFrame();

        ((IHttp3SettingsFrame) frame).Put(0x1, 10L);
        Assert.ThrowsAny<InvalidOperationException>(() =>
        {
            // Simulate protocol behavior
            if (((IHttp3SettingsFrame) frame).Put(0x1, 20L) != null)
            {
                throw new InvalidOperationException("Duplicate SETTINGS key detected");
            }
        });
    }

    // CLR: assertEquals(Object, Object), which calls expected.equals(actual) (see the class comment).
    private static void AssertObjectEquals(object expected, object actual)
    {
        Assert.True(Equals(expected, actual), "expected: <" + expected + "> but was: <" + actual + ">");
    }

    // CLR: assertNotEquals(Object, Object), which calls unexpected.equals(actual) (see the class comment).
    private static void AssertObjectNotEquals(object unexpected, object actual)
    {
        Assert.False(Equals(unexpected, actual), "expected: not equal but was: <" + actual + ">");
    }

}
