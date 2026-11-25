// CLR: test-only stand-in for org.apache.directory.server.dns.io.encoder.DnsMessageEncoder of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/io/encoder/DnsMessageEncoder.java
// with the record encoders it maps (io/encoder/QuestionRecordEncoder.java, AddressRecordEncoder.java,
// NameServerRecordEncoder.java, CanonicalNameRecordEncoder.java, PointerRecordEncoder.java,
// MailExchangeRecordEncoder.java, ServerSelectionRecordEncoder.java, TextRecordEncoder.java and
// StartOfAuthorityRecordEncoder.java), which are private nested classes here because no caller outside this encoder
// uses them. The behaviour the resolver tests depend on is kept exactly: the header counts are the sizes of the four
// lists, but a record whose type has no encoder (AAAA among them) makes put throw an IOException that
// putResourceRecords catches and logs, so the record is counted but not written; TestDnsServer then appends the AAAA
// records after the last section ("This is a hack to allow to also test for AAAA resolution"). Only IOException is
// caught: a NumberFormatException of Short.parseShort (FormatException/OverflowException of short.Parse here) or a
// null attribute (NullReferenceException) escapes encode(), as in the source. AddressRecordEncoder writes
// InetAddress.getByName(ip).getAddress() and swallows UnknownHostException (no RDATA): getByName is Resolver's
// DefaultNameResolver, which keeps the JDK literal rules (an IPv4-mapped IPv6 literal gives 4 bytes, null gives the
// loopback address) and reports an unknown host with SocketException. The (byte)/(short) casts of the header and
// counts keep the low bits in C# as in Java. The SLF4J logger is Common's InternalLoggerFactory logger.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Resolver;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * An encoder for DNS messages.  The primary usage of the DnsMessageEncoder is
 * to call the <code>encode(ByteBuffer, DnsMessage)</code> method which will
 * write the message to the outgoing ByteBuffer according to the DnsMessage
 * encoding in RFC-1035.
 */
public class DnsMessageEncoder
{
    /** the log for this class */
    private static readonly IInternalLogger LOG = InternalLoggerFactory.GetInstance(typeof(DnsMessageEncoder));

    /**
     * A Hashed Adapter mapping record types to their encoders.
     */
    private static readonly IDictionary<RecordType, IRecordEncoder> DEFAULT_ENCODERS = CreateDefaultEncoders();

    private static readonly DefaultNameResolver NAME_RESOLVER = new DefaultNameResolver(ImmediateEventExecutor.INSTANCE);

    private static IDictionary<RecordType, IRecordEncoder> CreateDefaultEncoders()
    {
        Dictionary<RecordType, IRecordEncoder> map = new Dictionary<RecordType, IRecordEncoder>();

        map[RecordType.SOA] = new StartOfAuthorityRecordEncoder();
        map[RecordType.A] = new AddressRecordEncoder();
        map[RecordType.NS] = new NameServerRecordEncoder();
        map[RecordType.CNAME] = new CanonicalNameRecordEncoder();
        map[RecordType.PTR] = new PointerRecordEncoder();
        map[RecordType.MX] = new MailExchangeRecordEncoder();
        map[RecordType.SRV] = new ServerSelectionRecordEncoder();
        map[RecordType.TXT] = new TextRecordEncoder();

        return map;
    }

    /**
     * Encodes the {@link DnsMessage} into the {@link IoBuffer}.
     *
     * @param byteBuffer
     * @param message
     */
    public virtual void Encode(IoBuffer byteBuffer, DnsMessage message)
    {
        byteBuffer.PutShort((short)message.GetTransactionId());

        byte header = (byte)0x00;
        header |= EncodeMessageType(message.GetMessageType());
        header |= EncodeOpCode(message.GetOpCode());
        header |= EncodeAuthoritativeAnswer(message.IsAuthoritativeAnswer());
        header |= EncodeTruncated(message.IsTruncated());
        header |= EncodeRecursionDesired(message.IsRecursionDesired());
        byteBuffer.Put(header);

        header = (byte)0x00;
        header |= EncodeRecursionAvailable(message.IsRecursionAvailable());
        header |= EncodeResponseCode(message.GetResponseCode());
        byteBuffer.Put(header);

        byteBuffer
            .PutShort((short)(message.GetQuestionRecords() != null ? message.GetQuestionRecords().Count : 0));
        byteBuffer.PutShort((short)(message.GetAnswerRecords() != null ? message.GetAnswerRecords().Count : 0));
        byteBuffer.PutShort((short)(message.GetAuthorityRecords() != null ? message.GetAuthorityRecords().Count
            : 0));
        byteBuffer.PutShort((short)(message.GetAdditionalRecords() != null ? message.GetAdditionalRecords().Count
            : 0));

        PutQuestionRecords(byteBuffer, message.GetQuestionRecords());
        PutResourceRecords(byteBuffer, message.GetAnswerRecords());
        PutResourceRecords(byteBuffer, message.GetAuthorityRecords());
        PutResourceRecords(byteBuffer, message.GetAdditionalRecords());
    }

    private void PutQuestionRecords(IoBuffer byteBuffer, IList<QuestionRecord> questions)
    {
        if (questions == null)
        {
            return;
        }

        QuestionRecordEncoder encoder = new QuestionRecordEncoder();

        foreach (QuestionRecord question in questions)
        {
            encoder.Put(byteBuffer, question);
        }
    }

    private void PutResourceRecords(IoBuffer byteBuffer, IList<IResourceRecord> records)
    {
        if (records == null)
        {
            return;
        }

        foreach (IResourceRecord record in records)
        {
            try
            {
                Put(byteBuffer, record);
            }
            catch (IOException ioe)
            {
                LOG.Error(ioe.Message, ioe);
            }
        }
    }

    private void Put(IoBuffer byteBuffer, IResourceRecord record)
    {
        RecordType type = record.GetRecordType();

        if (!DEFAULT_ENCODERS.TryGetValue(type, out IRecordEncoder encoder))
        {
            throw new IOException("ERR_597 Encoder unavailable for " + type);
        }

        encoder.Put(byteBuffer, record);
    }

    private byte EncodeMessageType(MessageType messageType)
    {
        byte oneBit = (byte)(messageType.Convert() & 0x01);
        return (byte)(oneBit << 7);
    }

    private byte EncodeOpCode(OpCode opCode)
    {
        byte fourBits = (byte)(opCode.Convert() & 0x0F);
        return (byte)(fourBits << 3);
    }

    private byte EncodeAuthoritativeAnswer(bool authoritative)
    {
        if (authoritative)
        {
            return (byte)((byte)0x01 << 2);
        }

        return (byte)0;
    }

    private byte EncodeTruncated(bool truncated)
    {
        if (truncated)
        {
            return (byte)((byte)0x01 << 1);
        }

        return 0;
    }

    private byte EncodeRecursionDesired(bool recursionDesired)
    {
        if (recursionDesired)
        {
            return (byte)0x01;
        }

        return 0;
    }

    private byte EncodeRecursionAvailable(bool recursionAvailable)
    {
        if (recursionAvailable)
        {
            return (byte)((byte)0x01 << 7);
        }

        return 0;
    }

    private byte EncodeResponseCode(ResponseCode responseCode)
    {
        return (byte)(responseCode.Convert() & 0x0F);
    }

    // CLR: io/encoder/QuestionRecordEncoder.java.
    private sealed class QuestionRecordEncoder
    {
        /**
         * Encodes the {@link QuestionRecord} into the {@link IoBuffer}.
         *
         * @param out
         * @param question
         */
        public void Put(IoBuffer @out, QuestionRecord question)
        {
            EncodeDomainName(@out, question.GetDomainName());
            EncodeRecordType(@out, question.GetRecordType());
            EncodeRecordClass(@out, question.GetRecordClass());
        }

        private void EncodeDomainName(IoBuffer byteBuffer, string domainName)
        {
            if (!Strings.IsEmpty(domainName))
            {
                string[] labels = ResourceRecordEncoder.SplitLabels(domainName);

                foreach (string label in labels)
                {
                    byteBuffer.Put((byte)label.Length);

                    char[] characters = label.ToCharArray();

                    foreach (char c in characters)
                    {
                        byteBuffer.Put((byte)c);
                    }
                }
            }

            byteBuffer.Put((byte)0x00);
        }

        private void EncodeRecordType(IoBuffer byteBuffer, RecordType recordType)
        {
            byteBuffer.PutShort(recordType.Convert());
        }

        private void EncodeRecordClass(IoBuffer byteBuffer, RecordClass recordClass)
        {
            byteBuffer.PutShort(recordClass.Convert());
        }
    }

    // CLR: io/encoder/AddressRecordEncoder.java.
    private sealed class AddressRecordEncoder : ResourceRecordEncoder
    {
        protected override void PutResourceRecordData(IoBuffer byteBuffer, IResourceRecord record)
        {
            string ipAddress = record.Get(DnsAttribute.IP_ADDRESS);

            try
            {
                byteBuffer.Put(GetByName(ipAddress).GetAddressBytes());
            }
            catch (SocketException)
            {
            }
        }

        // CLR: InetAddress.getByName through Resolver's DefaultNameResolver (blocking, as the JDK lookup is).
        private static IPAddress GetByName(string host)
        {
            return NAME_RESOLVER.ResolveAsync(host).GetAwaiter().GetResult();
        }
    }

    // CLR: io/encoder/NameServerRecordEncoder.java.
    private sealed class NameServerRecordEncoder : ResourceRecordEncoder
    {
        protected override void PutResourceRecordData(IoBuffer byteBuffer, IResourceRecord record)
        {
            string domainName = record.Get(DnsAttribute.DOMAIN_NAME);
            PutDomainName(byteBuffer, domainName);
        }
    }

    // CLR: io/encoder/CanonicalNameRecordEncoder.java.
    private sealed class CanonicalNameRecordEncoder : ResourceRecordEncoder
    {
        protected override void PutResourceRecordData(IoBuffer byteBuffer, IResourceRecord record)
        {
            string domainName = record.Get(DnsAttribute.DOMAIN_NAME);
            PutDomainName(byteBuffer, domainName);
        }
    }

    // CLR: io/encoder/PointerRecordEncoder.java.
    private sealed class PointerRecordEncoder : ResourceRecordEncoder
    {
        protected override void PutResourceRecordData(IoBuffer byteBuffer, IResourceRecord record)
        {
            string domainName = record.Get(DnsAttribute.DOMAIN_NAME);
            PutDomainName(byteBuffer, domainName);
        }
    }

    // CLR: io/encoder/MailExchangeRecordEncoder.java; Short.parseShort is short.Parse with the invariant culture.
    private sealed class MailExchangeRecordEncoder : ResourceRecordEncoder
    {
        protected override void PutResourceRecordData(IoBuffer byteBuffer, IResourceRecord record)
        {
            byteBuffer.PutShort(short.Parse(record.Get(DnsAttribute.MX_PREFERENCE), CultureInfo.InvariantCulture));
            PutDomainName(byteBuffer, record.Get(DnsAttribute.DOMAIN_NAME));
        }
    }

    // CLR: io/encoder/ServerSelectionRecordEncoder.java; Short.parseShort is short.Parse with the invariant culture.
    private sealed class ServerSelectionRecordEncoder : ResourceRecordEncoder
    {
        protected override void PutResourceRecordData(IoBuffer byteBuffer, IResourceRecord record)
        {
            byteBuffer.PutShort(short.Parse(record.Get(DnsAttribute.SERVICE_PRIORITY), CultureInfo.InvariantCulture));
            byteBuffer.PutShort(short.Parse(record.Get(DnsAttribute.SERVICE_WEIGHT), CultureInfo.InvariantCulture));
            byteBuffer.PutShort(short.Parse(record.Get(DnsAttribute.SERVICE_PORT), CultureInfo.InvariantCulture));
            PutDomainName(byteBuffer, record.Get(DnsAttribute.DOMAIN_NAME));
        }
    }

    // CLR: io/encoder/TextRecordEncoder.java.
    private sealed class TextRecordEncoder : ResourceRecordEncoder
    {
        protected override void PutResourceRecordData(IoBuffer byteBuffer, IResourceRecord record)
        {
            PutCharacterString(byteBuffer, record.Get(DnsAttribute.CHARACTER_STRING));
        }
    }

    // CLR: io/encoder/StartOfAuthorityRecordEncoder.java; Long.parseLong and Integer.parseInt are long.Parse and
    // int.Parse with the invariant culture, and the (int) casts of serial and minimum keep the low 32 bits.
    private sealed class StartOfAuthorityRecordEncoder : ResourceRecordEncoder
    {
        protected override void PutResourceRecordData(IoBuffer byteBuffer, IResourceRecord record)
        {
            string mName = record.Get(DnsAttribute.SOA_M_NAME);
            string rName = record.Get(DnsAttribute.SOA_R_NAME);
            long serial = long.Parse(record.Get(DnsAttribute.SOA_SERIAL), CultureInfo.InvariantCulture);
            int refresh = int.Parse(record.Get(DnsAttribute.SOA_REFRESH), CultureInfo.InvariantCulture);
            int retry = int.Parse(record.Get(DnsAttribute.SOA_RETRY), CultureInfo.InvariantCulture);
            int expire = int.Parse(record.Get(DnsAttribute.SOA_EXPIRE), CultureInfo.InvariantCulture);
            long minimum = long.Parse(record.Get(DnsAttribute.SOA_MINIMUM), CultureInfo.InvariantCulture);

            PutDomainName(byteBuffer, mName);
            PutDomainName(byteBuffer, rName);

            byteBuffer.PutInt((int)serial);

            byteBuffer.PutInt(refresh);
            byteBuffer.PutInt(retry);
            byteBuffer.PutInt(expire);

            byteBuffer.PutInt((int)minimum);
        }
    }
}
