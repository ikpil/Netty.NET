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

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl.Util;

// CLR: java.security.cert.X509Certificate is X509Certificate2 (ssl design decision 3, docs/handler-ssl-design.md), and an
// X509Certificate2 subclass keeps this type usable wherever a certificate is expected. X509Certificate2 holds the parsed
// certificate from its construction: the constructors that load data are obsolete ("Use X509CertificateLoader instead
// to load certificates") and X509Certificate2(X509Certificate) is not
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2.-ctor?view=net-10.0),
// so the bytes are parsed in the constructor with X509CertificateLoader.LoadCertificate, which "Loads a single X.509
// certificate from data, in either the PEM or DER encoding"
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificateloader.loadcertificate?view=net-10.0),
// and the parse cannot be deferred to the first use: bytes that are not a certificate fail in the constructor (an
// InvalidOperationException around the CryptographicException, the IllegalStateException of unwrap()) instead of at the
// first getter. The parsed certificate is the wrapped field, disposed with this instance through the protected virtual
// Dispose(bool)
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate.dispose?view=net-10.0).
// The JCA CertificateFactory and the Recycler of factory handles (CERT_FACTORIES, CertFactoryHandle) have no counterpart:
// the loader is a static method. The X509Certificate overrides that delegate to unwrap() are the inherited
// X509Certificate2 members (Version, SerialNumber/SerialNumberBytes, Issuer/IssuerName, Subject/SubjectName, NotBefore,
// NotAfter, SignatureAlgorithm, PublicKey, Extensions for the key usage, extended key usage, basic constraints and
// alternative names, RawData, ToString()), so they are not repeated; getTBSCertificate(), getSignature(),
// getSigAlgParams(), the unique IDs and the verify overloads (verify(PublicKey, Provider) takes a JCA Provider) have no
// X509Certificate2 member. getEncoded() returns a copy of the bytes given to the constructor, as in the source.
public sealed class LazyX509Certificate : X509Certificate2
{

    private readonly byte[] bytes;
    private readonly X509Certificate2 wrapped;

    /**
     * Creates a new instance which will lazy parse the given bytes. Be aware that the bytes will not be cloned.
     */
    public LazyX509Certificate(byte[] bytes) : this(ObjectUtil.CheckNotNull(bytes, "bytes"), Unwrap(bytes))
    {
    }

    private LazyX509Certificate(byte[] bytes, X509Certificate2 wrapped) : base(wrapped)
    {
        this.bytes = bytes;
        this.wrapped = wrapped;
    }

    public byte[] GetEncoded()
    {
        return (byte[]) bytes.Clone();
    }

    // CLR: unwrap() parses through a CertificateFactory and wraps a CertificateException in an IllegalStateException,
    // whose message is the cause's toString() (class name, ": ", message; Common's ThrowableUtil.ThrowableToString).
    private static X509Certificate2 Unwrap(byte[] bytes)
    {
        try
        {
            return X509CertificateLoader.LoadCertificate(bytes);
        }
        catch (CryptographicException e)
        {
            throw new InvalidOperationException(ThrowableUtil.ThrowableToString(e), e);
        }
    }

    // CLR: releases the parsed certificate of the constructor (see the class comment).
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            wrapped.Dispose();
        }

        base.Dispose(disposing);
    }
}
