/*
 * Copyright 2016 The Netty Project
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

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using Netty.NET.Common;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;
using Netty.NET.Resolver.Dns.Tests.ApacheDs;

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: owner decision 2026-11-25: the apacheds-protocol-dns 2.0.0.AM27 DnsServer on Apache MINA 2.2.3 that this class
// extends is the test-only C# stand-in in ApacheDs/ (namespace Netty.NET.Resolver.Dns.Tests.ApacheDs), with the same
// type names (interfaces with the I prefix) and Java member names; each stand-in file names the apacheds/MINA source
// it reproduces. The Java imports of org.apache.directory.server.* and org.apache.mina.* are that namespace.
// - start(dropRecordType, address): UdpTransport is created with address.getHostName(). IPEndPoint carries no host
//   name (docs/handler-porting.md, Addresses): NetUtil.LOCALHOST4 is InetAddress.getByAddress("localhost", bytes)
//   (common NetUtilInitializations.createLocalhost4), so Java passes "localhost", and getHostName() of the
//   ServerSocket address in DnsNameResolverTest may reverse-look the literal up
//   (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetSocketAddress.html); UdpTransport then
//   resolves the name again. The C# endpoint only has the address, so its text is passed and the UDP socket binds that
//   address (127.0.0.1 for LOCALHOST4, the address "localhost" resolves to first when IPv4 is preferred).
// - The anonymous DnsProtocolHandler subclass is the nested TestDnsProtocolHandler, the anonymous DnsUdpEncoder and
//   ProtocolDecoderAdapter of TestDnsProtocolUdpCodecFactory are its nested TestDnsUdpEncoder and TestDnsUdpDecoder,
//   and the inner (non-static) TestDnsProtocolUdpCodecFactory takes the enclosing server as a constructor argument,
//   because a C# nested class has no enclosing instance. The anonymous encoder's "encoder" is the factory's field (the
//   private field of DnsUdpEncoder is not visible to a subclass in either language).
// - Java lets the top-level class and its nested classes use each other's private members (JLS 6.6.1,
//   https://docs.oracle.com/javase/specs/jls/se17/html/jls-6.html); C# opens private members only to the declaring type
//   and the types nested in it (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/private),
//   so the TestRecordStore constructor and nextIp(), which the enclosing class and MapRecordStoreA call, are internal.
//   The package-private class, constructors and nested classes are internal; the protected static record factories,
//   which the tests of the package call (static import of newARecord), are protected internal, the C# accessibility
//   that adds the assembly to protected
//   (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/protected-internal).
// - A switch over the RecordType enum is "case var type when type == RecordType.X" (the stand-in's RecordType is a
//   sealed class, ApacheDs/RecordType.cs). ThreadLocalRandom is Random.Shared (resolver-dns mapping):
//   Math.abs(nextInt()) is Next(), which already returns a non-negative int
//   (https://learn.microsoft.com/en-us/dotnet/api/system.random.next?view=net-10.0), and nextBoolean() is
//   Next(2) != 0, as codec-compression's FastLzIntegrationTest maps it.
// - HashMap is Dictionary (its keySet order only feeds the random IPv6 pick), LinkedHashSet is Common's LinkedHashSet,
//   and Collections.singleton (an immutable one-element set,
//   https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Collections.html) is ImmutableHashSet.Create,
//   as codec-base DefaultHeaders maps the immutable Java sets. HashMap.get(null) returns null where Dictionary throws,
//   so TestAAAARecordEncoder looks up a missing address as a missing key. toLowerCase(Locale.US) of the ASCII
//   DnsAttribute constants is ToLowerInvariant() and toLowerCase() (the default locale) is
//   ToLower(CultureInfo.CurrentCulture). System.identityHashCode is RuntimeHelpers.GetHashCode, the identity hash that
//   ignores overrides (https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.runtimehelpers.gethashcode?view=net-10.0).
// - IllegalStateException is InvalidOperationException; new IllegalStateException(e) takes e.toString() as its
//   message, which is ThrowableUtil.ThrowableToString (owner decision 2026-11-24). IOException stays System.IO's.
//   "throws IOException" is only documented: C# has no checked exceptions.
internal class TestDnsServer : DnsServer
{
    private static readonly Dictionary<string, byte[]> BYTES = new Dictionary<string, byte[]>();
    private static readonly string[] IPV6_ADDRESSES;

    static TestDnsServer()
    {
        BYTES["::1"] = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 };
        BYTES["0:0:0:0:0:0:1:1"] = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1 };
        BYTES["0:0:0:0:0:1:1:1"] = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1 };
        BYTES["0:0:0:0:1:1:1:1"] = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 1 };
        BYTES["0:0:0:1:1:1:1:1"] = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1 };
        BYTES["0:0:1:1:1:1:1:1"] = new byte[] { 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1 };
        BYTES["0:1:1:1:1:1:1:1"] = new byte[] { 0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1 };
        BYTES["1:1:1:1:1:1:1:1"] = new byte[] { 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1 };

        IPV6_ADDRESSES = BYTES.Keys.ToArray();
    }

    private readonly IRecordStore store;

    internal TestDnsServer(ISet<string> domains) : this(new TestRecordStore(domains))
    {
    }

    internal TestDnsServer(IRecordStore store)
    {
        this.store = store;
    }

    public override void Start()
    {
        Start(null);
    }

    /**
     * Start the {@link TestDnsServer} but drop all {@link RecordType} queries
     * and not send any response to these at all.
     */
    public void Start(RecordType dropRecordType)
    {
        Start(dropRecordType, new IPEndPoint(NetUtil.LOCALHOST4, 0));
    }

    public void Start(RecordType dropRecordType, IPEndPoint address)
    {
        // CLR: address.getHostName() is the address text (see the header).
        UdpTransport transport = new UdpTransport(address.Address.ToString(), address.Port);
        SetTransports(transport);

        IDatagramAcceptor acceptor = transport.GetAcceptor();

        acceptor.SetHandler(new TestDnsProtocolHandler(this, store, dropRecordType));

        ((IDatagramSessionConfig)acceptor.GetSessionConfig()).SetReuseAddress(true);

        // Start the listener
        acceptor.Bind();
    }

    // CLR: the anonymous DnsProtocolHandler subclass of start(RecordType, InetSocketAddress).
    private sealed class TestDnsProtocolHandler : DnsProtocolHandler
    {
        private readonly TestDnsServer server;
        private readonly RecordType dropRecordType;

        internal TestDnsProtocolHandler(TestDnsServer server, IRecordStore store, RecordType dropRecordType)
            : base(server, store)
        {
            this.server = server;
            this.dropRecordType = dropRecordType;
        }

        public override void SessionCreated(IIoSession session)
        {
            // USe our own codec to support AAAA testing
            session.GetFilterChain()
                    .AddFirst("codec", new ProtocolCodecFilter(
                            new TestDnsProtocolUdpCodecFactory(server, dropRecordType)));
        }
    }

    public IPEndPoint LocalAddress()
    {
        return (IPEndPoint)GetTransports()[0].GetAcceptor().GetLocalAddress();
    }

    protected virtual DnsMessage FilterMessage(DnsMessage message)
    {
        return message;
    }

    protected internal static IResourceRecord NewARecord(string name, string ipAddress)
    {
        return NewAddressRecord(name, RecordType.A, ipAddress);
    }

    protected internal static IResourceRecord NewNsRecord(string dnsname, string domainName)
    {
        ResourceRecordModifier rm = new ResourceRecordModifier();
        rm.SetDnsClass(RecordClass.IN);
        rm.SetDnsName(dnsname);
        rm.SetDnsTtl(100);
        rm.SetDnsType(RecordType.NS);
        rm.Put(DnsAttribute.DOMAIN_NAME, domainName);

        return rm.GetEntry();
    }

    protected internal static IResourceRecord NewAddressRecord(string name, RecordType type, string address)
    {
        ResourceRecordModifier rm = new ResourceRecordModifier();
        rm.SetDnsClass(RecordClass.IN);
        rm.SetDnsName(name);
        rm.SetDnsTtl(100);
        rm.SetDnsType(type);
        rm.Put(DnsAttribute.IP_ADDRESS, address);

        return rm.GetEntry();
    }

    /**
     * {@link ProtocolCodecFactory} which allows to test AAAA resolution.
     */
    private sealed class TestDnsProtocolUdpCodecFactory : IProtocolCodecFactory
    {
        private readonly TestDnsServer server;
        private readonly DnsMessageEncoder encoder = new DnsMessageEncoder();
        private readonly TestAAAARecordEncoder recordEncoder = new TestAAAARecordEncoder();
        private readonly RecordType dropRecordType;

        internal TestDnsProtocolUdpCodecFactory(TestDnsServer server, RecordType dropRecordType)
        {
            this.server = server;
            this.dropRecordType = dropRecordType;
        }

        public IProtocolEncoder GetEncoder(IIoSession session)
        {
            return new TestDnsUdpEncoder(this);
        }

        // CLR: the anonymous DnsUdpEncoder subclass of getEncoder(IoSession).
        private sealed class TestDnsUdpEncoder : DnsUdpEncoder
        {
            private readonly TestDnsProtocolUdpCodecFactory factory;

            internal TestDnsUdpEncoder(TestDnsProtocolUdpCodecFactory factory)
            {
                this.factory = factory;
            }

            public override void Encode(IIoSession session, object message, IProtocolEncoderOutput @out)
            {
                IoBuffer buf = IoBuffer.Allocate(4096);
                DnsMessage dnsMessage = factory.server.FilterMessage((DnsMessage)message);
                if (dnsMessage != null)
                {
                    factory.encoder.Encode(buf, dnsMessage);

                    factory.EncodeAAAA(dnsMessage.GetAnswerRecords(), buf);
                    factory.EncodeAAAA(dnsMessage.GetAuthorityRecords(), buf);
                    factory.EncodeAAAA(dnsMessage.GetAdditionalRecords(), buf);
                    buf.Flip();

                    @out.Write(buf);
                }
            }
        }

        private void EncodeAAAA(IList<IResourceRecord> records, IoBuffer @out)
        {
            foreach (IResourceRecord record in records)
            {
                // This is a hack to allow to also test for AAAA resolution as DnsMessageEncoder
                // does not support it and it is hard to extend, because the interesting methods
                // are private...
                // In case of RecordType.AAAA we need to encode the RecordType by ourselves.
                if (record.GetRecordType() == RecordType.AAAA)
                {
                    try
                    {
                        recordEncoder.Put(@out, record);
                    }
                    catch (IOException e)
                    {
                        // Should never happen
                        throw new InvalidOperationException(ThrowableUtil.ThrowableToString(e), e);
                    }
                }
            }
        }

        public IProtocolDecoder GetDecoder(IIoSession session)
        {
            return new TestDnsUdpDecoder(this);
        }

        // CLR: the anonymous ProtocolDecoderAdapter of getDecoder(IoSession).
        private sealed class TestDnsUdpDecoder : ProtocolDecoderAdapter
        {
            private readonly TestDnsProtocolUdpCodecFactory factory;
            private readonly DnsMessageDecoder decoder = new DnsMessageDecoder();

            internal TestDnsUdpDecoder(TestDnsProtocolUdpCodecFactory factory)
            {
                this.factory = factory;
            }

            public override void Decode(IIoSession session, IoBuffer @in, IProtocolDecoderOutput @out)
            {
                DnsMessage message = decoder.Decode(@in);

                if (factory.dropRecordType != null)
                {
                    foreach (QuestionRecord record in message.GetQuestionRecords())
                    {
                        if (record.GetRecordType() == factory.dropRecordType)
                        {
                            return;
                        }
                    }
                }

                @out.Write(message);
            }
        }

        private sealed class TestAAAARecordEncoder : ResourceRecordEncoder
        {
            protected override void PutResourceRecordData(IoBuffer ioBuffer, IResourceRecord resourceRecord)
            {
                // CLR: HashMap.get(null) is null; Dictionary rejects a null key (see the header).
                string ipAddress = resourceRecord.Get(DnsAttribute.IP_ADDRESS);
                byte[] bytes = ipAddress == null ? null : BYTES.GetValueOrDefault(ipAddress);
                if (bytes == null)
                {
                    throw new InvalidOperationException(resourceRecord.Get(DnsAttribute.IP_ADDRESS));
                }

                // encode the ::1
                ioBuffer.Put(bytes);
            }
        }
    }

    internal sealed class MapRecordStoreA : IRecordStore
    {
        private readonly Dictionary<string, List<string>> domainMap;

        internal MapRecordStoreA(ISet<string> domains, int length)
        {
            domainMap = new Dictionary<string, List<string>>(domains.Count);

            foreach (string domain in domains)
            {
                List<string> addresses = new List<string>(length);

                for (int i = 0; i < length; i++)
                {
                    addresses.Add(TestRecordStore.NextIp());
                }

                domainMap[domain] = addresses;
            }
        }

        internal MapRecordStoreA(ISet<string> domains) : this(domains, 1)
        {
        }

        public string GetAddress(string domain)
        {
            return domainMap.GetValueOrDefault(domain)[0];
        }

        public IList<string> GetAddresses(string domain)
        {
            return domainMap.GetValueOrDefault(domain);
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord questionRecord)
        {
            string name = questionRecord.GetDomainName();
            List<string> addresses = domainMap.GetValueOrDefault(name);
            if (addresses != null && questionRecord.GetRecordType() == RecordType.A)
            {
                ISet<IResourceRecord> records = new LinkedHashSet<IResourceRecord>();

                foreach (string address in addresses)
                {
                    IDictionary<string, object> attributes = new Dictionary<string, object>();
                    attributes[DnsAttribute.IP_ADDRESS.ToLower(CultureInfo.CurrentCulture)] = address;
                    records.Add(new TestResourceRecord(name, questionRecord.GetRecordType(), attributes));
                }

                return records;
            }

            return null;
        }
    }

    private sealed class TestRecordStore : IRecordStore
    {
        private static readonly int[] NUMBERS = new int[254];
        private static readonly char[] CHARS = new char[26];

        static TestRecordStore()
        {
            for (int i = 0; i < NUMBERS.Length; i++)
            {
                NUMBERS[i] = i + 1;
            }

            for (int i = 0; i < CHARS.Length; i++)
            {
                CHARS[i] = (char)('a' + i);
            }
        }

        private static int Index(int arrayLength)
        {
            return Math.Abs(Random.Shared.Next()) % arrayLength;
        }

        private static string NextDomain()
        {
            return CHARS[Index(CHARS.Length)] + ".netty.io";
        }

        internal static string NextIp()
        {
            return IpPart() + "." + IpPart() + '.' + IpPart() + '.' + IpPart();
        }

        private static int IpPart()
        {
            return NUMBERS[Index(NUMBERS.Length)];
        }

        private static string NextIp6()
        {
            return IPV6_ADDRESSES[Index(IPV6_ADDRESSES.Length)];
        }

        private readonly ISet<string> domains;

        internal TestRecordStore(ISet<string> domains)
        {
            this.domains = domains;
        }

        public ISet<IResourceRecord> GetRecords(QuestionRecord questionRecord)
        {
            string name = questionRecord.GetDomainName();
            if (domains.Contains(name))
            {
                IDictionary<string, object> attr = new Dictionary<string, object>();

                switch (questionRecord.GetRecordType())
                {
                    case var type when type == RecordType.A:
                        do
                        {
                            attr[DnsAttribute.IP_ADDRESS.ToLowerInvariant()] = NextIp();
                        } while (Random.Shared.Next(2) != 0);

                        break;
                    case var type when type == RecordType.AAAA:
                        do
                        {
                            attr[DnsAttribute.IP_ADDRESS.ToLowerInvariant()] = NextIp6();
                        } while (Random.Shared.Next(2) != 0);

                        break;
                    case var type when type == RecordType.MX:
                        int priority = 0;

                        do
                        {
                            attr[DnsAttribute.DOMAIN_NAME.ToLowerInvariant()] = NextDomain();
                            attr[DnsAttribute.MX_PREFERENCE.ToLowerInvariant()] =
                                (++priority).ToString(CultureInfo.InvariantCulture);
                        } while (Random.Shared.Next(2) != 0);

                        break;
                    default:
                        return null;
                }

                return ImmutableHashSet.Create<IResourceRecord>(
                        new TestResourceRecord(name, questionRecord.GetRecordType(), attr));
            }

            return null;
        }
    }

    internal sealed class TestResourceRecord : ResourceRecordImpl
    {
        internal TestResourceRecord(string domainName, RecordType recordType, IDictionary<string, object> attributes)
            : base(domainName, recordType, RecordClass.IN, 100, attributes)
        {
        }

        public override int GetHashCode()
        {
            return RuntimeHelpers.GetHashCode(this);
        }

        public override bool Equals(object o)
        {
            return o == this;
        }
    }
}
