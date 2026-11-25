// CLR: test-only stand-in for org.apache.directory.server.dns.io.decoder.DnsMessageDecoder of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/io/decoder/DnsMessageDecoder.java
// with the record decoders it maps (io/decoder/RecordDecoder.java, AddressRecordDecoder.java, IPv6RecordDecoder.java,
// NameServerRecordDecoder.java and MailExchangeRecordDecoder.java), which are private nested types here because no
// caller outside this decoder uses them. The behaviour the resolver tests meet is kept exactly:
// - a name is read label by label and a '.' is appended only when the byte after a label is not zero, so a query for
//   "netty.io." is decoded as "netty.io" (no trailing dot) and the root name as "";
// - a compression pointer computes its offset as "length & ~(0xc0) << 8", where the shift binds first in Java and in
//   C#, so the offset is always 0 and only the low pointer byte is used, as in the source;
// - getLabel appends (char) of the Java signed byte, which sign-extends a byte above 0x7F to ﾀ..￿ (JLS 5.1.4,
//   https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html); the C# byte is cast to sbyte first so the
//   conversion to char sign-extends the same way (unchecked integral conversion, sign extension for a signed source,
//   https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions);
// - the four counts are read with getShort() (signed): a count above 0x7FFF makes new ArrayList<>(count) throw
//   IllegalArgumentException, as new List<T>(count) throws ArgumentOutOfRangeException here; the record data length is
//   a signed short too (a negative length fails the byte[] allocation in both runtimes);
// - RecordType.convert/RecordClass.convert throw for an unknown value, and a record type other than A, NS, MX and AAAA
//   throws IllegalArgumentException (ArgumentException), so a query that carries an EDNS OPT record (class = UDP
//   payload size) is not decoded and the server sends no response (the MINA session closes on the exception).
// InetAddress.getByAddress(bytes) is new IPAddress(bytes), which accepts the same 4- and 16-byte lengths and throws
// ArgumentException where Java throws UnknownHostException; the source's IPv4-mapped 16-byte to Inet4Address
// conversion is not reproduced (only the decoded request reaches these decoders, and the resolver sends no address
// records in a query). The SLF4J logger is Common's InternalLoggerFactory logger; "throws IOException" is only
// documented.

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

/**
 * A decoder for DNS messages.  The primary usage of the DnsMessageDecoder is by
 * calling the <code>decode(ByteBuffer)</code> method which will read the
 * message from the incoming ByteBuffer and build a <code>DnsMessage</code>
 * from it according to the DnsMessage encoding in RFC-1035.
 */
public class DnsMessageDecoder
{
    private readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(DnsMessageDecoder));

    /**
     * A Hashed Adapter mapping record types to their encoders.
     */
    private static readonly IDictionary<RecordType, IRecordDecoder> DEFAULT_DECODERS = CreateDefaultDecoders();

    private static IDictionary<RecordType, IRecordDecoder> CreateDefaultDecoders()
    {
        Dictionary<RecordType, IRecordDecoder> map = new Dictionary<RecordType, IRecordDecoder>();

        map[RecordType.A] = new AddressRecordDecoder();
        map[RecordType.NS] = new NameServerRecordDecoder();
        map[RecordType.MX] = new MailExchangeRecordDecoder();
        map[RecordType.AAAA] = new IPv6RecordDecoder();

        return map;
    }

    /**
     * Decode the {@link IoBuffer} into a {@link DnsMessage}.
     *
     * @param in
     * @return The {@link DnsMessage}.
     * @throws IOException
     */
    public virtual DnsMessage Decode(IoBuffer @in)
    {
        DnsMessageModifier modifier = new DnsMessageModifier();

        modifier.SetTransactionId(@in.GetUnsignedShort());

        byte header = @in.Get();
        modifier.SetMessageType(DecodeMessageType(header));
        modifier.SetOpCode(DecodeOpCode(header));
        modifier.SetAuthoritativeAnswer(DecodeAuthoritativeAnswer(header));
        modifier.SetTruncated(DecodeTruncated(header));
        modifier.SetRecursionDesired(DecodeRecursionDesired(header));

        header = @in.Get();
        modifier.SetRecursionAvailable(DecodeRecursionAvailable(header));
        modifier.SetResponseCode(DecodeResponseCode(header));

        short questionCount = @in.GetShort();
        short answerCount = @in.GetShort();
        short authorityCount = @in.GetShort();
        short additionalCount = @in.GetShort();

        logger.Debug("decoding {} question records", questionCount);
        modifier.SetQuestionRecords(GetQuestions(@in, questionCount));

        logger.Debug("decoding {} answer records", answerCount);
        modifier.SetAnswerRecords(GetRecords(@in, answerCount));

        logger.Debug("decoding {} authority records", authorityCount);
        modifier.SetAuthorityRecords(GetRecords(@in, authorityCount));

        logger.Debug("decoding {} additional records", additionalCount);
        modifier.SetAdditionalRecords(GetRecords(@in, additionalCount));

        return modifier.GetDnsMessage();
    }

    private IList<IResourceRecord> GetRecords(IoBuffer byteBuffer, short recordCount)
    {
        List<IResourceRecord> records = new List<IResourceRecord>(recordCount);

        for (int ii = 0; ii < recordCount; ii++)
        {
            string domainName = GetDomainName(byteBuffer);
            RecordType recordType = RecordType.Convert(byteBuffer.GetShort());
            RecordClass recordClass = RecordClass.Convert(byteBuffer.GetShort());

            int timeToLive = byteBuffer.GetInt();
            short dataLength = byteBuffer.GetShort();

            IDictionary<string, object> attributes = Decode(byteBuffer, recordType, dataLength);
            records.Add(new ResourceRecordImpl(domainName, recordType, recordClass, timeToLive, attributes));
        }

        return records;
    }

    private IDictionary<string, object> Decode(IoBuffer byteBuffer, RecordType type, short length)
    {
        if (!DEFAULT_DECODERS.TryGetValue(type, out IRecordDecoder recordDecoder))
        {
            throw new ArgumentException("ERR_600 Decoder unavailable for " + type);
        }

        return recordDecoder.Decode(byteBuffer, length);
    }

    private IList<QuestionRecord> GetQuestions(IoBuffer byteBuffer, short questionCount)
    {
        List<QuestionRecord> questions = new List<QuestionRecord>(questionCount);

        for (int ii = 0; ii < questionCount; ii++)
        {
            string domainName = GetDomainName(byteBuffer);

            RecordType recordType = RecordType.Convert(byteBuffer.GetShort());
            RecordClass recordClass = RecordClass.Convert(byteBuffer.GetShort());

            questions.Add(new QuestionRecord(domainName, recordType, recordClass));
        }

        return questions;
    }

    internal static string GetDomainName(IoBuffer byteBuffer)
    {
        StringBuilder domainName = new StringBuilder();
        RecurseDomainName(byteBuffer, domainName);

        return domainName.ToString();
    }

    internal static void RecurseDomainName(IoBuffer byteBuffer, StringBuilder domainName)
    {
        int length = byteBuffer.GetUnsigned();

        if (IsOffset(length))
        {
            int position = byteBuffer.GetUnsigned();
            int offset = length & ~(0xc0) << 8;
            int originalPosition = byteBuffer.Position();
            byteBuffer.Position(position + offset);

            RecurseDomainName(byteBuffer, domainName);

            byteBuffer.Position(originalPosition);
        }
        else if (IsLabel(length))
        {
            int labelLength = length;
            GetLabel(byteBuffer, domainName, labelLength);
            RecurseDomainName(byteBuffer, domainName);
        }
    }

    internal static bool IsOffset(int length)
    {
        return ((length & 0xc0) == 0xc0);
    }

    internal static bool IsLabel(int length)
    {
        return (length != 0 && (length & 0xc0) == 0);
    }

    internal static void GetLabel(IoBuffer byteBuffer, StringBuilder domainName, int labelLength)
    {
        for (int jj = 0; jj < labelLength; jj++)
        {
            // CLR: (char) of the Java signed byte sign-extends (see the header).
            char character = (char)(sbyte)byteBuffer.Get();
            domainName.Append(character);
        }

        if (byteBuffer.Get(byteBuffer.Position()) != 0)
        {
            domainName.Append(".");
        }
    }

    private MessageType DecodeMessageType(byte header)
    {
        return MessageType.Convert((byte)((header & 0x80) >>> 7));
    }

    private OpCode DecodeOpCode(byte header)
    {
        return OpCode.Convert((byte)((header & 0x78) >>> 3));
    }

    private bool DecodeAuthoritativeAnswer(byte header)
    {
        return ((header & 0x04) >>> 2) == 1;
    }

    private bool DecodeTruncated(byte header)
    {
        return ((header & 0x02) >>> 1) == 1;
    }

    private bool DecodeRecursionDesired(byte header)
    {
        return (header & 0x01) == 1;
    }

    private bool DecodeRecursionAvailable(byte header)
    {
        return ((header & 0x80) >>> 7) == 1;
    }

    private ResponseCode DecodeResponseCode(byte header)
    {
        return ResponseCode.Convert((byte)(header & 0x0F));
    }

    // CLR: io/decoder/RecordDecoder.java.
    private interface IRecordDecoder
    {
        /**
         * Decodes the given length of resource record data into attributes.  The
         * type and number of attributes depends on the type of the resource record.
         *
         * @param byteBuffer
         * @param length
         * @return The map of attributes.
         * @throws IOException
         */
        IDictionary<string, object> Decode(IoBuffer byteBuffer, short length);
    }

    // CLR: io/decoder/AddressRecordDecoder.java.
    private sealed class AddressRecordDecoder : IRecordDecoder
    {
        public IDictionary<string, object> Decode(IoBuffer byteBuffer, short length)
        {
            IDictionary<string, object> attributes = new Dictionary<string, object>();
            byte[] addressBytes = new byte[length];
            byteBuffer.Get(addressBytes);
            attributes[Strings.ToLowerCaseAscii(DnsAttribute.IP_ADDRESS)] = new IPAddress(addressBytes);

            return attributes;
        }
    }

    // CLR: io/decoder/IPv6RecordDecoder.java.
    private sealed class IPv6RecordDecoder : IRecordDecoder
    {
        public IDictionary<string, object> Decode(IoBuffer byteBuffer, short length)
        {
            IDictionary<string, object> attributes = new Dictionary<string, object>();
            byte[] addressBytes = new byte[length];
            byteBuffer.Get(addressBytes);
            attributes[Strings.ToLowerCaseAscii(DnsAttribute.IP_ADDRESS)] = new IPAddress(addressBytes);

            return attributes;
        }
    }

    // CLR: io/decoder/NameServerRecordDecoder.java.
    private sealed class NameServerRecordDecoder : IRecordDecoder
    {
        public IDictionary<string, object> Decode(IoBuffer byteBuffer, short length)
        {
            IDictionary<string, object> map = new Dictionary<string, object>();
            map[Strings.ToLowerCaseAscii(DnsAttribute.DOMAIN_NAME)] = GetDomainName(byteBuffer);

            return map;
        }
    }

    // CLR: io/decoder/MailExchangeRecordDecoder.java.
    private sealed class MailExchangeRecordDecoder : IRecordDecoder
    {
        public IDictionary<string, object> Decode(IoBuffer byteBuffer, short length)
        {
            IDictionary<string, object> attributes = new Dictionary<string, object>();
            attributes[Strings.ToLowerCaseAscii(DnsAttribute.MX_PREFERENCE)] = byteBuffer.GetShort();
            attributes[Strings.ToLowerCaseAscii(DnsAttribute.DOMAIN_NAME)] = GetDomainName(byteBuffer);

            return attributes;
        }
    }
}
