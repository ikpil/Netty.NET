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
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl;

/**
 * This is a special purpose implementation of a {@link X509Certificate} which allows
 * the user to pass PEM/PKCS#8 encoded data straight into {@link OpenSslContext} without
 * having to parse and re-encode bytes in Java land.
 *
 * All methods other than what's implemented in {@link PemEncoded}'s throw
 * {@link UnsupportedOperationException}s.
 *
 * @see PemEncoded
 * @see OpenSslContext
 * @see #valueOf(byte[])
 * @see #valueOf(ByteBuf)
 */
// CLR: java.security.cert.X509Certificate is X509Certificate2 in this port (ssl design decision 3,
// docs/handler-ssl-design.md). An X509Certificate2 is always a parsed certificate: its constructors that are not
// obsolete take another certificate or a native handle, and loading data "through the constructor or Import is
// obsolete. Use X509CertificateLoader instead"
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2.-ctor?view=net-10.0),
// while this class holds PEM bytes that are not validated and may hold a whole chain. So it is a reference counted holder
// that is not an X509Certificate2; the 25 X509Certificate overrides of the source (getEncoded() ... getPublicKey()), which
// only throw UnsupportedOperationException, have no base to implement and are not ported. refCnt/retain/release/touch
// forward to the content as in the source; the IByteBufHolder members follow PemValue. equals/hashCode/toString are
// Equals/GetHashCode/ToString; CharsetUtil.US_ASCII/UTF_8 are Encoding.ASCII/UTF8 (docs/codec-smtp-porting.md). The
// OpenSslContext of the Javadoc is library-not-ported (ssl design decision 4); this holder only feeds it.
public sealed class PemX509Certificate : IPemEncoded
{

    private static readonly byte[] BEGIN_CERT = Encoding.ASCII.GetBytes("-----BEGIN CERTIFICATE-----\n");
    private static readonly byte[] END_CERT = Encoding.ASCII.GetBytes("\n-----END CERTIFICATE-----\n");

    /**
     * Creates a {@link PemEncoded} value from the {@link X509Certificate}s.
     */
    // CLR: the chain holds objects, because a PemX509Certificate is not an X509Certificate here: an IPemEncoded element
    // is appended as PEM and an X509Certificate (the base of X509Certificate2) is encoded from GetRawCertData(), which
    // "Returns the raw data for the entire X.509v3 certificate as an array of bytes"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate?view=net-10.0),
    // the DER form that getEncoded() returns; an element of any other type fails the cast. checkNonEmpty(chain) and
    // Arrays.toString(chain) are ObjectUtil.CheckNonEmpty and a "[a, b]" join with "null" for null elements.
    // CertificateEncodingException has no counterpart: GetRawCertData does not fail.
    internal static IPemEncoded ToPEM(IByteBufAllocator allocator, bool useDirect,
            params object[] chain)
    {

        ObjectUtil.CheckNonEmpty(chain, "chain");

        // We can take a shortcut if there is only one certificate and
        // it already happens to be a PemEncoded instance. This is the
        // ideal case and reason why all this exists. It allows the user
        // to pass pre-encoded bytes straight into OpenSSL without having
        // to do any of the extra work.
        if (chain.Length == 1)
        {
            object first = chain[0];
            if (first is IPemEncoded)
            {
                return (IPemEncoded) ((IPemEncoded) first).Retain();
            }
        }

        bool success = false;
        ByteBuf pem = null;
        try
        {
            foreach (object cert in chain)
            {

                if (cert == null)
                {
                    throw new ArgumentException("Null element in chain: " +
                            "[" + string.Join(", ", chain.Select(c => c?.ToString() ?? "null")) + "]");
                }

                if (cert is IPemEncoded)
                {
                    pem = Append(allocator, useDirect, (IPemEncoded) cert, chain.Length, pem);
                }
                else
                {
                    pem = Append(allocator, useDirect, (X509Certificate) cert, chain.Length, pem);
                }
            }

            PemValue value = new PemValue(pem, false);
            success = true;
            return value;
        }
        finally
        {
            // Make sure we never leak the PEM's ByteBuf in the event of an Exception
            if (!success && pem != null)
            {
                pem.Release();
            }
        }
    }

    /**
     * Appends the {@link PemEncoded} value to the {@link ByteBuf} (last arg) and returns it.
     * If the {@link ByteBuf} didn't exist yet it'll create it using the {@link ByteBufAllocator}.
     */
    private static ByteBuf Append(IByteBufAllocator allocator, bool useDirect,
            IPemEncoded encoded, int count, ByteBuf pem)
    {

        ByteBuf content = encoded.Content;

        if (pem == null)
        {
            // see the other append() method
            pem = NewBuffer(allocator, useDirect, content.ReadableBytes * count);
        }

        pem.WriteBytes(content.Slice());
        return pem;
    }

    /**
     * Appends the {@link X509Certificate} value to the {@link ByteBuf} (last arg) and returns it.
     * If the {@link ByteBuf} didn't exist yet it'll create it using the {@link ByteBufAllocator}.
     */
    private static ByteBuf Append(IByteBufAllocator allocator, bool useDirect,
            X509Certificate cert, int count, ByteBuf pem)
    {

        ByteBuf encoded = Unpooled.WrappedBuffer(cert.GetRawCertData());
        try
        {
            ByteBuf base64 = SslUtils.ToBase64(allocator, encoded);
            try
            {
                if (pem == null)
                {
                    // We try to approximate the buffer's initial size. The sizes of
                    // certificates can vary a lot so it'll be off a bit depending
                    // on the number of elements in the array (count argument).
                    pem = NewBuffer(allocator, useDirect,
                            (BEGIN_CERT.Length + base64.ReadableBytes + END_CERT.Length) * count);
                }

                pem.WriteBytes(BEGIN_CERT);
                pem.WriteBytes(base64);
                pem.WriteBytes(END_CERT);
            }
            finally
            {
                base64.Release();
            }
        }
        finally
        {
            encoded.Release();
        }

        return pem;
    }

    private static ByteBuf NewBuffer(IByteBufAllocator allocator, bool useDirect, int initialCapacity)
    {
        return useDirect ? allocator.DirectBuffer(initialCapacity) : allocator.Buffer(initialCapacity);
    }

    /**
     * Creates a {@link PemX509Certificate} from raw {@code byte[]}.
     *
     * ATTENTION: It's assumed that the given argument is a PEM/PKCS#8 encoded value.
     * No input validation is performed to validate it.
     */
    public static PemX509Certificate ValueOf(byte[] key)
    {
        return ValueOf(Unpooled.WrappedBuffer(key));
    }

    /**
     * Creates a {@link PemX509Certificate} from raw {@code ByteBuf}.
     *
     * ATTENTION: It's assumed that the given argument is a PEM/PKCS#8 encoded value.
     * No input validation is performed to validate it.
     */
    public static PemX509Certificate ValueOf(ByteBuf key)
    {
        return new PemX509Certificate(key);
    }

    private readonly ByteBuf content;

    private PemX509Certificate(ByteBuf content)
    {
        this.content = ObjectUtil.CheckNotNull(content, "content");
    }

    public bool IsSensitive()
    {
        // There is no sensitive information in a X509 Certificate
        return false;
    }

    public int RefCnt => content.RefCnt;

    public ByteBuf Content
    {
        get
        {
            int count = RefCnt;
            if (count <= 0)
            {
                throw new IllegalReferenceCountException(count);
            }

            return content;
        }
    }

    public PemX509Certificate Copy()
    {
        return Replace(content.Copy());
    }

    public PemX509Certificate Duplicate()
    {
        return Replace(content.Duplicate());
    }

    public PemX509Certificate RetainedDuplicate()
    {
        return Replace(content.RetainedDuplicate());
    }

    public PemX509Certificate Replace(ByteBuf content)
    {
        return new PemX509Certificate(content);
    }

    public PemX509Certificate Retain()
    {
        content.Retain();
        return this;
    }

    public PemX509Certificate Retain(int increment)
    {
        content.Retain(increment);
        return this;
    }

    public PemX509Certificate Touch()
    {
        content.Touch();
        return this;
    }

    public PemX509Certificate Touch(object hint)
    {
        content.Touch(hint);
        return this;
    }

    public bool Release()
    {
        return content.Release();
    }

    public bool Release(int decrement)
    {
        return content.Release(decrement);
    }

    public override bool Equals(object o)
    {
        if (o == this)
        {
            return true;
        }
        if (!(o is PemX509Certificate))
        {
            return false;
        }

        PemX509Certificate other = (PemX509Certificate) o;
        return content.Equals(other.content);
    }

    public override int GetHashCode()
    {
        return content.GetHashCode();
    }

    public override string ToString()
    {
        return content.ToString(Encoding.UTF8);
    }

    IByteBufHolder IByteBufHolder.Copy()
    {
        return Copy();
    }

    IByteBufHolder IByteBufHolder.Duplicate()
    {
        return Duplicate();
    }

    IByteBufHolder IByteBufHolder.RetainedDuplicate()
    {
        return RetainedDuplicate();
    }

    IByteBufHolder IByteBufHolder.Replace(ByteBuf content)
    {
        return Replace(content);
    }

    IByteBufHolder IByteBufHolder.Retain()
    {
        return Retain();
    }

    IByteBufHolder IByteBufHolder.Retain(int increment)
    {
        return Retain(increment);
    }

    IByteBufHolder IByteBufHolder.Touch()
    {
        return Touch();
    }

    IByteBufHolder IByteBufHolder.Touch(object hint)
    {
        return Touch(hint);
    }

    IReferenceCounted IReferenceCounted.Retain()
    {
        return Retain();
    }

    IReferenceCounted IReferenceCounted.Retain(int increment)
    {
        return Retain(increment);
    }

    IReferenceCounted IReferenceCounted.Touch()
    {
        return Touch();
    }

    IReferenceCounted IReferenceCounted.Touch(object hint)
    {
        return Touch(hint);
    }
}
