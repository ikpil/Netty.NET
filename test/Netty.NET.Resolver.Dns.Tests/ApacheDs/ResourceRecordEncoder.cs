// CLR: test-only stand-in for org.apache.directory.server.dns.io.encoder.ResourceRecordEncoder of apacheds-protocol-dns
// 2.0.0.AM27:
// https://github.com/apache/directory-server/blob/2.0.0.AM27/protocol-dns/src/main/java/org/apache/directory/server/dns/io/encoder/ResourceRecordEncoder.java
// The abstract base of the per-type record encoders, which TestDnsServer's TestAAAARecordEncoder extends. The Java
// methods are virtual by default, so the public/protected ones are virtual here and putResourceRecordData stays
// abstract. putDomainName splits the name with String.split("\\.") semantics (trailing empty labels are dropped, so
// "a.b." and "a.b" encode alike and "." is the root name;
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html) and writes every label length and
// char with a (byte) cast, which keeps the low 8 bits in C# as in Java (unchecked conversion,
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions); the same holds
// for putCharacterString. No label compression is written, as in the source.

using System.Collections.Generic;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public abstract class ResourceRecordEncoder : IRecordEncoder
{
    public virtual void Put(IoBuffer byteBuffer, IResourceRecord record)
    {
        PutDomainName(byteBuffer, record.GetDomainName());
        PutRecordType(byteBuffer, record.GetRecordType());
        PutRecordClass(byteBuffer, record.GetRecordClass());

        byteBuffer.PutInt(record.GetTimeToLive());

        PutResourceRecord(byteBuffer, record);
    }

    protected abstract void PutResourceRecordData(IoBuffer byteBuffer, IResourceRecord record);

    protected virtual void PutResourceRecord(IoBuffer byteBuffer, IResourceRecord record)
    {
        int startPosition = byteBuffer.Position();
        byteBuffer.Position(startPosition + 2);

        PutResourceRecordData(byteBuffer, record);

        PutDataSize(byteBuffer, startPosition);
    }

    protected virtual void PutDataSize(IoBuffer byteBuffer, int startPosition)
    {
        int endPosition = byteBuffer.Position();
        short length = (short)(endPosition - startPosition - 2);

        byteBuffer.Position(startPosition);
        byteBuffer.PutShort(length);
        byteBuffer.Position(endPosition);
    }

    /**
     * &lt;domain-name&gt; is a domain name represented as a series of labels, and
     * terminated by a label with zero length.
     *
     * @param byteBuffer the ByteBuffer to encode the domain name into
     * @param domainName the domain name to encode
     */
    protected virtual void PutDomainName(IoBuffer byteBuffer, string domainName)
    {
        if (!Strings.IsEmpty(domainName))
        {
            string[] labels = SplitLabels(domainName);

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

    protected virtual void PutRecordType(IoBuffer byteBuffer, RecordType recordType)
    {
        byteBuffer.PutShort(recordType.Convert());
    }

    protected virtual void PutRecordClass(IoBuffer byteBuffer, RecordClass recordClass)
    {
        byteBuffer.PutShort(recordClass.Convert());
    }

    /**
     * &lt;character-string&gt; is a single length octet followed by that number
     * of characters.  &lt;character-string&gt; is treated as binary information,
     * and can be up to 256 characters in length (including the length octet).
     *
     * @param byteBuffer The byte buffer to encode the character string into.
     * @param characterString the character string to encode
     */
    protected virtual void PutCharacterString(IoBuffer byteBuffer, string characterString)
    {
        byteBuffer.Put((byte)characterString.Length);

        char[] characters = characterString.ToCharArray();

        for (int ii = 0; ii < characters.Length; ii++)
        {
            byteBuffer.Put((byte)characters[ii]);
        }
    }

    // CLR: String.split("\\.") with a zero limit: the parts between the dots, with the trailing empty strings removed;
    // a string without a dot is returned whole.
    internal static string[] SplitLabels(string domainName)
    {
        List<string> labels = new List<string>(domainName.Split('.'));
        while (labels.Count > 0 && labels[labels.Count - 1].Length == 0)
        {
            labels.RemoveAt(labels.Count - 1);
        }

        return labels.ToArray();
    }
}
