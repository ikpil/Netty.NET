/*
 * Copyright 2026 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Dns.Tests;

// CLR: the anonymous AbstractDnsOptPseudoRrRecord subclasses are the named nested class TestOptPseudoRrRecord;
// readUnsignedByte() is ByteBuf.ReadByte(), which returns the unsigned System.Byte, and readUnsignedShort() returns
// ushort (docs/buffer-porting.md); the long literals keep their L suffix.
public class AbstractDnsOptPseudoRrRecordTest
{

    // DNSSEC OK, see https://tools.ietf.org/html/rfc3225#section-3
    private const int DO = 0x8000;

    // Compact Answers OK, see https://www.rfc-editor.org/rfc/rfc9824.html#section-5.1
    private const int CO = 0x4000;

    private static IDnsOptPseudoRecord NewRecord(int maxPayloadSize, int extendedRcode, int version, int flags)
    {
        return new TestOptPseudoRrRecord(maxPayloadSize, extendedRcode, version, flags);
    }

    [Fact]
    public void TestFlagsCanCarryDo()
    {
        IDnsOptPseudoRecord record = NewRecord(4096, 0, 0, DO);
        Assert.Equal(DO, record.Flags());
        Assert.Equal(0, record.ExtendedRcode());
        Assert.Equal(0, record.Version());
    }

    [Fact]
    public void TestFlagsIsNotTruncatedToEightBits()
    {
        // The flags field is 16 bits wide, so bits 8..15 must survive as well.
        IDnsOptPseudoRecord record = NewRecord(4096, 0, 0, 0x7fff);
        Assert.Equal(0x7fff, record.Flags());
    }

    [Fact]
    public void TestFlagsCanCarryBitsOtherThanDo()
    {
        // Bits below DO are assigned by the IANA EDNS Header Flags registry, so they must not be masked off.
        Assert.Equal(CO, NewRecord(4096, 0, 0, CO).Flags());
        Assert.Equal(DO | CO, NewRecord(4096, 0, 0, DO | CO).Flags());
    }

    [Fact]
    public void TestTtlLayoutIsExtendedRcodeVersionFlags()
    {
        // Asymmetric values so that swapping or widening any of the three fields is detectable.
        IDnsOptPseudoRecord record = NewRecord(4096, 0x12, 0x34, 0x8abc);
        Assert.Equal(0x12348abcL, record.TimeToLive());
        Assert.Equal(0x12, record.ExtendedRcode());
        Assert.Equal(0x34, record.Version());
        Assert.Equal(0x8abc, record.Flags());
    }

    [Fact]
    public void TestAllFieldsAtMaximum()
    {
        IDnsOptPseudoRecord record = NewRecord(1232, 0xff, 0xff, 0xffff);
        Assert.Equal(0xff, record.ExtendedRcode());
        Assert.Equal(0xff, record.Version());
        Assert.Equal(0xffff, record.Flags());
        Assert.Equal(0xffffffffL, record.TimeToLive());
        Assert.Equal(1232, record.DnsClass());
    }

    [Fact]
    public void TestFlagsDoNotLeakIntoExtendedRcodeOrVersion()
    {
        IDnsOptPseudoRecord record = NewRecord(4096, 0, 0, 0xffff);
        Assert.Equal(0, record.ExtendedRcode());
        Assert.Equal(0, record.Version());
    }

    [Fact]
    public void TestExtendedRcodeAndVersionDoNotLeakIntoFlags()
    {
        IDnsOptPseudoRecord record = NewRecord(4096, 0xff, 0xff, 0);
        Assert.Equal(0, record.Flags());
    }

    [Fact]
    public void TestOutOfRangeValuesAreTruncatedToTheirFieldWidth()
    {
        // 0x01 for the extended RCODE so a bit bleeding out of an over-wide VERSION is visible.
        IDnsOptPseudoRecord record = NewRecord(4096, 0x01, 0x2ff, 0x3ffff);
        Assert.Equal(0x01, record.ExtendedRcode());
        Assert.Equal(0xff, record.Version());
        Assert.Equal(0xffff, record.Flags());
        Assert.Equal(0x01ffffffL, record.TimeToLive());

        // A negative value must never produce a negative TTL, which AbstractDnsRecord rejects.
        IDnsOptPseudoRecord negative = NewRecord(4096, -1, -1, -1);
        Assert.Equal(0xffffffffL, negative.TimeToLive());
        Assert.Equal(0xffff, negative.Flags());
    }

    [Fact]
    public void TestPreExistingConstructorsKeepTtlUnchanged()
    {
        // The constructors that predate the flags parameter must produce exactly the TTL they did before.
        IDnsOptPseudoRecord threeArg = new TestOptPseudoRrRecord(4096, 0xab, 0xcd);
        Assert.Equal(0xabcd0000L, threeArg.TimeToLive());
        Assert.Equal(0, threeArg.Flags());

        IDnsOptPseudoRecord oneArg = new TestOptPseudoRrRecord(4096);
        Assert.Equal(0L, oneArg.TimeToLive());
        Assert.Equal(0, oneArg.Flags());
    }

    [Fact]
    public void TestDoIsEncodedOnTheWire()
    {
        AssertEncoded(NewRecord(4096, 0, 0, DO), 4096, 0, 0, DO);
    }

    [Fact]
    public void TestExtendedRcodeVersionAndFlagsAreEncodedOnTheWire()
    {
        AssertEncoded(NewRecord(1232, 0x01, 0x02, DO | CO), 1232, 0x01, 0x02, DO | CO);
    }

    private static void AssertEncoded(IDnsOptPseudoRecord record, int maxPayloadSize,
                                      int extendedRcode, int version, int flags)
    {
        ByteBuf @out = Unpooled.Buffer();
        try
        {
            new DefaultDnsRecordEncoder().EncodeRecord(record, @out);

            // See https://tools.ietf.org/html/rfc6891#section-6.1.2
            Assert.Equal(0, @out.ReadByte()); // NAME must be the root domain.
            Assert.Equal(DnsRecordType.OPT.IntValue(), @out.ReadUnsignedShort()); // TYPE
            Assert.Equal(maxPayloadSize, @out.ReadUnsignedShort()); // CLASS holds the UDP payload size.
            Assert.Equal(extendedRcode, @out.ReadByte()); // TTL: EXTENDED-RCODE
            Assert.Equal(version, @out.ReadByte()); // TTL: VERSION
            Assert.Equal(flags, @out.ReadUnsignedShort()); // TTL: DO + Z
            Assert.Equal(0, @out.ReadUnsignedShort()); // RDLENGTH
            Assert.Equal(0, @out.ReadableBytes);
        }
        finally
        {
            @out.Release();
        }
    }

    private sealed class TestOptPseudoRrRecord : AbstractDnsOptPseudoRrRecord
    {
        internal TestOptPseudoRrRecord(int maxPayloadSize, int extendedRcode, int version, int flags) :
            base(maxPayloadSize, extendedRcode, version, flags)
        {
        }

        internal TestOptPseudoRrRecord(int maxPayloadSize, int extendedRcode, int version) :
            base(maxPayloadSize, extendedRcode, version)
        {
        }

        internal TestOptPseudoRrRecord(int maxPayloadSize) : base(maxPayloadSize)
        {
        }
    }
}
