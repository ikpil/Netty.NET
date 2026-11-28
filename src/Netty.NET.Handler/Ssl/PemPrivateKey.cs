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
using System.Security.Cryptography;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl;

/**
 * This is a special purpose implementation of a {@link PrivateKey} which allows the
 * user to pass PEM/PKCS#8 encoded key material straight into {@link OpenSslContext}
 * without having to parse and re-encode bytes in Java land.
 *
 * All methods other than what's implemented in {@link PemEncoded} and {@link Destroyable}
 * throw {@link UnsupportedOperationException}s.
 *
 * @see PemEncoded
 * @see OpenSslContext
 * @see #valueOf(byte[])
 * @see #valueOf(ByteBuf)
 */
// CLR: java.security.PrivateKey is AsymmetricAlgorithm in this port (ssl design decision 3, docs/handler-ssl-design.md),
// an abstract class ("Represents the abstract base class from which all implementations of asymmetric algorithms must
// inherit", https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.asymmetricalgorithm?view=net-10.0).
// The class already derives from AbstractReferenceCounted, and C# has single class inheritance, so it is a reference
// counted holder of the PEM bytes that is not an AsymmetricAlgorithm. Of the PrivateKey (Key) members only getFormat(),
// which returns a value, is kept as GetFormat(); getEncoded() and getAlgorithm(), which only throw
// UnsupportedOperationException, have no base to implement and are not ported. destroy()/isDestroyed() of
// javax.security.auth.Destroyable, which release and inspect the reference count, are Destroy()/IsDestroyed(). The
// OpenSslContext of the Javadoc is library-not-ported (ssl design decision 4); this holder only feeds it.
// serialVersionUID is JVM serialization metadata and has no counterpart. The IByteBufHolder members follow PemValue.
// CharsetUtil.US_ASCII is Encoding.ASCII (docs/codec-smtp-porting.md).
public sealed class PemPrivateKey : AbstractReferenceCounted, IPemEncoded
{

    private static readonly byte[] BEGIN_PRIVATE_KEY = Encoding.ASCII.GetBytes("-----BEGIN PRIVATE KEY-----\n");
    private static readonly byte[] END_PRIVATE_KEY = Encoding.ASCII.GetBytes("\n-----END PRIVATE KEY-----\n");

    private const string PKCS8_FORMAT = "PKCS#8";

    /**
     * Creates a {@link PemEncoded} value from the {@link PrivateKey}.
     */
    // CLR: the key is an object, because a PemPrivateKey is not an AsymmetricAlgorithm here: an IPemEncoded takes the
    // shortcut, and the PKCS #8 encoding of an AsymmetricAlgorithm, which getEncoded() of a PrivateKey returns
    // (getFormat() "PKCS#8"), is ExportPkcs8PrivateKey(), which "Exports the current key in the PKCS#8 PrivateKeyInfo
    // format" (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.asymmetricalgorithm.exportpkcs8privatekey?view=net-10.0).
    // Any other object has no encoding, the source's getEncoded() == null case. Class.getName() is Type.FullName.
    internal static IPemEncoded ToPEM(IByteBufAllocator allocator, bool useDirect, object key)
    {
        // We can take a shortcut if the private key happens to be already
        // PEM/PKCS#8 encoded. This is the ideal case and reason why all
        // this exists. It allows the user to pass pre-encoded bytes straight
        // into OpenSSL without having to do any of the extra work.
        if (key is IPemEncoded)
        {
            return (IPemEncoded) ((IPemEncoded) key).Retain();
        }

        byte[] bytes = key is AsymmetricAlgorithm algorithm ? algorithm.ExportPkcs8PrivateKey() : null;
        if (bytes == null)
        {
            throw new ArgumentException(key.GetType().FullName + " does not support encoding");
        }

        return ToPEM(allocator, useDirect, bytes);
    }

    internal static IPemEncoded ToPEM(IByteBufAllocator allocator, bool useDirect, byte[] bytes)
    {
        ByteBuf encoded = Unpooled.WrappedBuffer(bytes);

        try
        {
            ByteBuf base64 = SslUtils.ToBase64(allocator, encoded);

            try
            {
                int size = BEGIN_PRIVATE_KEY.Length + base64.ReadableBytes + END_PRIVATE_KEY.Length;

                bool success = false;
                ByteBuf pem = useDirect ? allocator.DirectBuffer(size) : allocator.Buffer(size);

                try
                {
                    pem.WriteBytes(BEGIN_PRIVATE_KEY);
                    pem.WriteBytes(base64);
                    pem.WriteBytes(END_PRIVATE_KEY);

                    PemValue value = new PemValue(pem, true);
                    success = true;
                    return value;
                }
                finally
                {
                    // Make sure we never leak that PEM ByteBuf if there's an Exception.
                    if (!success)
                    {
                        SslUtils.ZerooutAndRelease(pem);
                    }
                }
            }
            finally
            {
                SslUtils.ZerooutAndRelease(base64);
            }
        }
        finally
        {
            SslUtils.ZerooutAndRelease(encoded);
        }
    }

    /**
     * Creates a {@link PemPrivateKey} from raw {@code byte[]}.
     *
     * ATTENTION: It's assumed that the given argument is a PEM/PKCS#8 encoded value.
     * No input validation is performed to validate it.
     */
    public static PemPrivateKey ValueOf(byte[] key)
    {
        return ValueOf(Unpooled.WrappedBuffer(key));
    }

    /**
     * Creates a {@link PemPrivateKey} from raw {@code ByteBuf}.
     *
     * ATTENTION: It's assumed that the given argument is a PEM/PKCS#8 encoded value.
     * No input validation is performed to validate it.
     */
    public static PemPrivateKey ValueOf(ByteBuf key)
    {
        return new PemPrivateKey(key);
    }

    private readonly ByteBuf content;

    private PemPrivateKey(ByteBuf content)
    {
        this.content = ObjectUtil.CheckNotNull(content, "content");
    }

    public bool IsSensitive()
    {
        return true;
    }

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

    public PemPrivateKey Copy()
    {
        return Replace(content.Copy());
    }

    public PemPrivateKey Duplicate()
    {
        return Replace(content.Duplicate());
    }

    public PemPrivateKey RetainedDuplicate()
    {
        return Replace(content.RetainedDuplicate());
    }

    public PemPrivateKey Replace(ByteBuf content)
    {
        return new PemPrivateKey(content);
    }

    public override PemPrivateKey Touch()
    {
        content.Touch();
        return this;
    }

    public override PemPrivateKey Touch(object hint)
    {
        content.Touch(hint);
        return this;
    }

    public override PemPrivateKey Retain()
    {
        return (PemPrivateKey) base.Retain();
    }

    public override PemPrivateKey Retain(int increment)
    {
        return (PemPrivateKey) base.Retain(increment);
    }

    protected override void Deallocate()
    {
        // Private Keys are sensitive. We need to zero the bytes
        // before we're releasing the underlying ByteBuf
        SslUtils.ZerooutAndRelease(content);
    }

    public string GetFormat()
    {
        return PKCS8_FORMAT;
    }

    /**
     * NOTE: This is a JDK8 interface/method. Due to backwards compatibility
     * reasons it's not possible to slap the {@code @Override} annotation onto
     * this method.
     *
     * @see Destroyable#destroy()
     */
    public void Destroy()
    {
        Release(RefCnt);
    }

    /**
     * NOTE: This is a JDK8 interface/method. Due to backwards compatibility
     * reasons it's not possible to slap the {@code @Override} annotation onto
     * this method.
     *
     * @see Destroyable#isDestroyed()
     */
    public bool IsDestroyed()
    {
        return RefCnt == 0;
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
}
