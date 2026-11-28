/*
 * Copyright 2014 The Netty Project
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

using System.Collections.Generic;
using System.Reflection;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Dns.Tests;

// CLR: Class.getFields() is Type.GetFields(), which "returns all the public fields of the current Type"
// (https://learn.microsoft.com/en-us/dotnet/api/system.type.getfields?view=net-10.0); Modifier.STATIC is
// FieldInfo.IsStatic, and the JUnit message overloads use XunitAssertMessages.AssertM.
public class DnsRecordTypeTest
{

    private static List<DnsRecordType> AllTypes()
    {
        List<DnsRecordType> result = new List<DnsRecordType>();

        foreach (FieldInfo field in typeof(DnsRecordType).GetFields())
        {
            if (field.IsStatic && field.FieldType == typeof(DnsRecordType))
            {
                result.Add((DnsRecordType) field.GetValue(null));
            }
        }

        Assert.False(result.Count == 0);
        return result;
    }

    [Fact]
    public void TestSanity()
    {
        Assert.Equal(AllTypes().Count, new HashSet<DnsRecordType>(AllTypes()).Count,
                "More than one type has the same int value");
    }

    /**
     * Test of hashCode method, of class DnsRecordType.
     */
    [Fact]
    public void TestHashCode()
    {
        foreach (DnsRecordType t in AllTypes())
        {
            Assert.Equal(t.IntValue(), t.GetHashCode());
        }
    }

    /**
     * Test of equals method, of class DnsRecordType.
     */
    [Fact]
    public void TestEquals()
    {
        foreach (DnsRecordType t1 in AllTypes())
        {
            foreach (DnsRecordType t2 in AllTypes())
            {
                if (t1 != t2)
                {
                    Assert.NotEqual(t1, t2);
                }
            }
        }
    }

    /**
     * Test of find method, of class DnsRecordType.
     */
    [Fact]
    public void TestFind()
    {
        foreach (DnsRecordType t in AllTypes())
        {
            DnsRecordType found = DnsRecordType.ValueOf(t.IntValue());
            Assert.Same(t, found);
            found = DnsRecordType.ValueOf(t.Name());
            Assert.Same(t, found, t.Name());
        }
    }

    /**
     * Pins the code points against the IANA "Resource Record (RR) TYPEs" registry. {@link #testFind()}
     * proves a constant is registered, but a transposed number would still round-trip through
     * {@link DnsRecordType#valueOf(int)}, so the values themselves are asserted literally.
     */
    [Fact]
    public void TestCodePointsMatchIanaRegistry()
    {
        Assert.Equal(43, DnsRecordType.DS.IntValue());
        Assert.Equal(46, DnsRecordType.RRSIG.IntValue());
        Assert.Equal(47, DnsRecordType.NSEC.IntValue());
        Assert.Equal(48, DnsRecordType.DNSKEY.IntValue());
        Assert.Equal(50, DnsRecordType.NSEC3.IntValue());
        Assert.Equal(51, DnsRecordType.NSEC3PARAM.IntValue());
        Assert.Equal(52, DnsRecordType.TLSA.IntValue());
        Assert.Equal(53, DnsRecordType.SMIMEA.IntValue());
        Assert.Equal(59, DnsRecordType.CDS.IntValue());
        Assert.Equal(60, DnsRecordType.CDNSKEY.IntValue());
        Assert.Equal(61, DnsRecordType.OPENPGPKEY.IntValue());
        Assert.Equal(62, DnsRecordType.CSYNC.IntValue());
        Assert.Equal(63, DnsRecordType.ZONEMD.IntValue());
    }

    /**
     * An unregistered code point must still decode, as an unregistered instance rather than by throwing.
     * See {@link DnsRecordType#valueOf(int)}.
     */
    [Fact]
    public void TestUnregisteredCodePointDecodesAsUnknown()
    {
        // 0xff00 is in the private-use range of the registry, so IANA will never assign it and this
        // test cannot start failing when the registry grows.
        DnsRecordType unassigned = DnsRecordType.ValueOf(0xff00);
        Assert.Equal(0xff00, unassigned.IntValue());
        Assert.Equal("UNKNOWN", unassigned.Name());
    }
}
