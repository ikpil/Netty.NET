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
 * unit tests for {@link Http3Settings}.
 */
// CLR: the package-private JUnit methods are public, because xUnit runs public test methods. Http3Settings keeps the
// Java members (docs/codec-http3-porting.md): Iterable<Map.Entry<Long, Long>> is IEnumerable<KeyValuePair<long, long>>,
// iterator() is GetEnumerator() and hasNext() is MoveNext(), and a Long/Boolean result that may be null is long?/bool?,
// so assertEquals(Boolean.TRUE, x) and assertEquals(4096L, x) compare nullable values and assertNull checks the nullable.
// A long key or value is never null, so assertNotNull(e.getKey()) checks the boxed value, as Java checks the boxed Long.
// assertEquals(Object, Object) and assertNotEquals(Object, Object) call equals: Http3Settings is an IEnumerable, which
// xUnit's Assert.Equal would compare entry by entry, so AssertObjectEquals/AssertObjectNotEquals keep JUnit's comparison
// (as AssertSettingsEquals in codec-http2's InboundHttp2ToHttpAdapterTest). AtomicInteger, which only counts in a
// for-each loop, is an int. Iterable.forEach(Consumer) and Set.forEach(Consumer) are foreach loops over the same
// elements. The lambdas (id, v) -> ... given to the Http3Settings(NonStandardHttp3SettingsValidator) constructor are
// the private nested class FuncValidator over a Func (INonStandardHttp3SettingsValidator is an interface, not a
// delegate). Http3SettingsFrame is IHttp3SettingsFrame, whose default member Put is called through the interface.
// assertThrows(IllegalArgumentException.class, ...) is Assert.ThrowsAny<ArgumentException> and checkNotNull's
// NullPointerException is ArgumentNullException (docs/codec-http-porting.md, Core test notes and Shared CLR
// adaptations). Long.MAX_VALUE/MIN_VALUE are long.MaxValue/MinValue. testSpliterator (Iterable.spliterator(), a JVM
// stream API) and testNullValueRejected (put(1, null) with a long value) are recorded as CLR-inapplicable in the
// manifest.
public class Http3SettingsTest
{

    [Fact]
    public void TestDefaultSettingsValues()
    {
        Http3Settings settings = Http3Settings.DefaultSettings();

        Assert.Equal(0L, settings.QpackMaxTableCapacity());
        Assert.Equal(0L, settings.QpackBlockedStreams());
        Assert.Equal(false, settings.ConnectProtocolEnabled());
        Assert.Equal(Http3CodecUtils.DEFAULT_MAX_FIELD_SECTION_SIZE, settings.MaxFieldSectionSize());
        Assert.Equal(false, settings.H3DatagramEnabled());
    }

    [Fact]
    public void TestPutAndGetSettings()
    {
        Http3Settings settings = new Http3Settings();

        settings.QpackMaxTableCapacity(128)
                .QpackBlockedStreams(4)
                .EnableConnectProtocol(true)
                .MaxFieldSectionSize(4096);

        Assert.Equal(128L, settings.QpackMaxTableCapacity());
        Assert.Equal(4L, settings.QpackBlockedStreams());
        Assert.Equal(true, settings.ConnectProtocolEnabled());
        Assert.Equal(4096L, settings.MaxFieldSectionSize());
        Assert.Null(settings.H3DatagramEnabled());
    }

    [Fact]
    public void TestEqualsAndHashCode()
    {
        Http3Settings s1 = new Http3Settings()
                .EnableH3Datagram(true)
                .QpackMaxTableCapacity(256)
                .QpackBlockedStreams(8)
                .EnableConnectProtocol(true);

        Http3Settings s2 = new Http3Settings()
                .QpackMaxTableCapacity(256)
                .QpackBlockedStreams(8)
                .EnableConnectProtocol(true)
                .EnableH3Datagram(true);

        Http3Settings s3 = new Http3Settings().QpackMaxTableCapacity(999);

        AssertObjectEquals(s1, s2);
        Assert.Equal(s1.GetHashCode(), s2.GetHashCode());
        AssertObjectNotEquals(s1, s3);
        AssertObjectNotEquals(null, s1);
        AssertObjectNotEquals("not-a-settings", s1);
    }

    [Fact]
    public void TestIteratorProvidesEntries()
    {
        Http3Settings settings = new Http3Settings()
                .QpackMaxTableCapacity(100)
                .QpackBlockedStreams(2);

        int count = 0;

        foreach (KeyValuePair<long, long> e in settings)
        {
            Assert.NotNull((object) e.Key);
            Assert.NotNull((object) e.Value);
            count++;
        }

        Assert.True(count == 2);
    }

    [Fact]
    public void TestDefaultGet()
    {
        Http3Settings settings = new Http3Settings();

        Assert.Null(settings.Get(Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id()));

        Assert.Equal(long.MaxValue, settings
                .GetOrDefault(
                        Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id(),
                        long.MaxValue)
        );
        Assert.Equal(long.MinValue, settings
                .GetOrDefault(
                        Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id(),
                        long.MinValue)
        );

        Assert.Null(settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id(), 1000L));

        Assert.Equal(1000L, settings.Get(Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id()));
    }

    [Fact]
    public void TestForEachConsumer()
    {
        Http3Settings settings = new Http3Settings()
                .QpackMaxTableCapacity(5)
                .QpackBlockedStreams(7)
                .EnableH3Datagram(true)
                .MaxFieldSectionSize(1);

        List<long> keys = new List<long>();

        foreach (KeyValuePair<long, long> entry in settings)
        {
            keys.Add(entry.Key);
        }

        Assert.True(keys.Contains(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id()));
        Assert.True(keys.Contains(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id()));
        Assert.True(keys.Contains(Http3SettingIdentifier.HTTP3_SETTINGS_H3_DATAGRAM.Id()));
        Assert.True(keys.Contains(Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id()));
    }

    // CLR: testSpliterator (settings.spliterator() is non-null and its estimateSize() is positive) is CLR-inapplicable:
    // Iterable.spliterator() is the JVM's java.util.Spliterator, which IEnumerable<KeyValuePair<long, long>> does not
    // have (see the class comment).

    [Fact]
    public void TestCopyFrom()
    {
        Http3Settings src = new Http3Settings()
                .QpackMaxTableCapacity(512)
                .EnableConnectProtocol(true)
                .QpackBlockedStreams(9);
        Http3Settings dst = new Http3Settings();
        dst.PutAll(src);
        AssertObjectEquals(src, dst);

        src.QpackMaxTableCapacity(100);
        AssertObjectNotEquals(src, dst);
    }

    [Fact]
    public void TestValidationRejectsInvalidValues()
    {
        Http3Settings settings = new Http3Settings();

        // Negative value tests
        Assert.ThrowsAny<ArgumentException>(
                () => settings.QpackBlockedStreams(-1));
        Assert.ThrowsAny<ArgumentException>(
                () => settings.QpackMaxTableCapacity(-10));
        Assert.ThrowsAny<ArgumentException>(
                () => settings.MaxFieldSectionSize(-5));

        // Invalid ENABLE_CONNECT_PROTOCOL (must be 0 or 1)
        Assert.ThrowsAny<ArgumentException>(
                () => settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL.Id(), 5L));

        Assert.ThrowsAny<ArgumentException>(
                () => settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL.Id(), 15L));
    }

    [Fact]
    public void TestVerifyStandardSettingValidCases()
    {
        Http3Settings settings = new Http3Settings();
        settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL.Id(), 1L);
        settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL.Id(), 1L);
        settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(), 0L);
        settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id(), 5L);
        settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id(), 4096L);

        Assert.Equal(true, settings.ConnectProtocolEnabled());
        Assert.Equal(4096L, settings.MaxFieldSectionSize());
    }

    [Fact]
    public void TestCustomSettingsIgnored()
    {
        Http3Settings settings = new Http3Settings();
        long customKey = 0xdeadbeefL;
        settings.Put(customKey, 123L);
        Assert.Null(settings.Get(customKey));
    }

    [Fact]
    public void TestCustomSettingsNotIgnoredWithValidator()
    {
        long customKey = 0xdeadbeefL;
        Http3Settings settings = new Http3Settings(new FuncValidator((id, v) => customKey == id));
        settings.Put(customKey, 123L);
        Assert.NotNull(settings.Get(customKey));
    }

    [Fact]
    public void TestCustomSettingsValidatorAllowsAndRejectsMultipleKeys()
    {
        ISet<long> allowedKeys = new HashSet<long>(new long[] {
                0xdeadbeefL,
                0xcafebabeL
        });

        long rejectedKey = 0xfeedfaceL;

        Http3Settings settings = new Http3Settings(
                new FuncValidator((id, v) => allowedKeys.Contains(id))
        );

        // Allowed keys
        foreach (long key in allowedKeys)
        {
            settings.Put(key, 1L);
            Assert.NotNull(settings.Get(key));
        }

        // Rejected key
        settings.Put(rejectedKey, 1L);
        Assert.Null(settings.Get(rejectedKey));
    }

    [Fact]
    public void TestDefaultSettingsBuilderPattern()
    {
        Http3Settings s = new Http3Settings()
                .QpackMaxTableCapacity(10)
                .EnableConnectProtocol(false)
                .QpackBlockedStreams(1)
                .MaxFieldSectionSize(100);

        Assert.Equal(10L, s.QpackMaxTableCapacity());
        Assert.Equal(false, s.ConnectProtocolEnabled());
        Assert.Equal(1L, s.QpackBlockedStreams());
        Assert.Equal(100L, s.MaxFieldSectionSize());
    }

    [Fact]
    public void TestToStringAndKeyToStringIndirectly()
    {
        Http3Settings settings = new Http3Settings()
                .QpackMaxTableCapacity(1)
                .QpackBlockedStreams(2)
                .EnableConnectProtocol(true);

        string @out = settings.ToString();

        Assert.True(@out.Contains("0x1"));
        Assert.True(@out.Contains("0x7"));
        Assert.True(@out.Contains("0x8"));
    }

    [Fact]
    public void TestEmptySettingsEquality()
    {
        Http3Settings s1 = new Http3Settings();
        Http3Settings s2 = new Http3Settings();
        AssertObjectEquals(s1, s2);
    }

    [Fact]
    public void TestDefaultSettingsStaticFactoryCreatesNewInstance()
    {
        Http3Settings s1 = Http3Settings.DefaultSettings();
        Http3Settings s2 = Http3Settings.DefaultSettings();
        Assert.NotSame(s1, s2);
        AssertObjectEquals(s1, s2);
    }

    [Fact]
    public void TestEnableConnectProtocolNullBehaviour()
    {
        Http3Settings settings = new Http3Settings();
        Assert.Null(settings.ConnectProtocolEnabled());
        settings.EnableConnectProtocol(true);
        Assert.Equal(true, settings.ConnectProtocolEnabled());
        settings.EnableConnectProtocol(false);
        Assert.NotEqual(true, settings.ConnectProtocolEnabled());
    }

    [Fact]
    public void TestIteratorEmptyDoesNotFail()
    {
        Http3Settings settings = new Http3Settings();
        IEnumerator<KeyValuePair<long, long>> iterator = settings.GetEnumerator();
        Assert.False(iterator.MoveNext());
    }

    [Fact]
    public void TestPutOverridesExistingKey()
    {
        Http3Settings settings = new Http3Settings();
        settings.QpackMaxTableCapacity(10);
        settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(), 20L);
        Assert.Equal(20L, settings.QpackMaxTableCapacity());
    }

    // CLR: testNullValueRejected (put(1, null) throws NullPointerException) is CLR-inapplicable: Put takes the value as a
    // long, so a null value cannot be passed (see the class comment).

    [Fact]
    public void TestCopyFromNullThrows()
    {
        Http3Settings settings = new Http3Settings();
        Assert.ThrowsAny<ArgumentNullException>(() => settings.PutAll(null));
    }

    [Fact]
    public void DuplicateSettingsValuesInsideFrameTest()
    {
        IHttp3SettingsFrame settingsFrame = new DefaultHttp3SettingsFrame();
        Assert.Null(settingsFrame.Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(), 100L));
        Assert.Null(settingsFrame.Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id(), 1L));
        Assert.Null(settingsFrame.Put(Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id(), 128L));
        Assert.Null(settingsFrame.Put(Http3SettingIdentifier.HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL.Id(), 0L));
        Assert.Null(settingsFrame.Put(Http3SettingIdentifier.HTTP3_SETTINGS_H3_DATAGRAM.Id(), 1L));
        //known headers should not contain duplicate so give non-null
        // which is used in http3framecodec to throw error
        Assert.NotNull(settingsFrame.Put(Http3SettingIdentifier.HTTP3_SETTINGS_H3_DATAGRAM.Id(), 1L));
        // Ensure we can encode and decode all sizes correctly.
        // unknown settings id/key will be ignored
        Assert.Null(settingsFrame.Put(63, 63L));
        Assert.Null(settingsFrame.Put(16383, 16383L));
        Assert.Null(settingsFrame.Put(1073741823, 1073741823L));
        Assert.Null(settingsFrame.Put(4611686018427387903L, 4611686018427387903L));
        //even duplicates of unknown ignored as we ignore unknown
        Assert.Null(settingsFrame.Put(4611686018427387903L, 4611686018427387903L));
    }

    [Fact]
    public void DuplicateSettingsValuesInsideHttp3SettingsTest()
    {
        Http3Settings http3Settings = new Http3Settings();
        Assert.Null(http3Settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(), 100L));
        Assert.Null(http3Settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id(), 1L));
        Assert.Null(http3Settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id(), 128L));
        Assert.Null(http3Settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL.Id(), 0L));
        Assert.Null(http3Settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_H3_DATAGRAM.Id(), 1L));
        //known headers should not contain duplicate so give non-null
        // which is used in http3framecodec to throw error
        Assert.NotNull(http3Settings.Put(Http3SettingIdentifier.HTTP3_SETTINGS_H3_DATAGRAM.Id(), 1L));
        // Ensure we can encode and decode all sizes correctly.
        // unknown settings id/key will be ignored
        Assert.Null(http3Settings.Put(63, 63L));
        Assert.Null(http3Settings.Put(16383, 16383L));
        Assert.Null(http3Settings.Put(1073741823, 1073741823L));
        Assert.Null(http3Settings.Put(4611686018427387903L, 4611686018427387903L));
        //even duplicates of unknown ignored as we ignore unknown
        Assert.Null(http3Settings.Put(4611686018427387903L, 4611686018427387903L));
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

    // CLR: the lambdas (id, v) -> ... of the NonStandardHttp3SettingsValidator (see the class comment).
    private sealed class FuncValidator : Http3Settings.INonStandardHttp3SettingsValidator
    {
        private readonly Func<long, long, bool> validate;

        internal FuncValidator(Func<long, long, bool> validate)
        {
            this.validate = validate;
        }

        public bool Validate(long id, long value)
        {
            return validate(id, value);
        }
    }
}
