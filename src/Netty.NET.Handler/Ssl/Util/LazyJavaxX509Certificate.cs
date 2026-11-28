/*
 * Copyright 2015 The Netty Project
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

// CLR: javax.security.cert.X509Certificate is the JDK's "Abstract class for X.509 v1 certificates", which gives access to
// "all the version 1 attributes" and is deprecated for removal
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/security/cert/X509Certificate.html); its BCL
// counterpart is the base class System.Security.Cryptography.X509Certificates.X509Certificate ("Provides methods that
// help you use X.509 v.3 certificates"; "For most scenarios, you should use the X509Certificate2 class instead",
// https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate?view=net-10.0),
// whose members cover those attributes (Issuer, Subject, GetSerialNumber, GetEffectiveDateString,
// GetExpirationDateString, GetKeyAlgorithm, GetPublicKey, GetRawCertData) without the v3 extensions of X509Certificate2.
// Its X509Certificate(X509Certificate) constructor is not obsolete, unlike the ones that load data, so, as
// LazyX509Certificate does, the bytes are parsed in the constructor with X509CertificateLoader.LoadCertificate (PEM or
// DER) and the parse is not deferred; the parsed certificate is the wrapped field, disposed with this instance. The
// overrides that delegate to unwrap() are the inherited members; getVersion(), getSigAlgName(), getSigAlgOID(),
// getSigAlgParams() and the verify overloads have no member on the BCL X509Certificate. X509Certificate.getInstance and
// its javax CertificateException have no counterpart: a CryptographicException of the loader becomes the
// InvalidOperationException of unwrap() (IllegalStateException). getEncoded() returns a copy of the bytes.
public sealed class LazyJavaxX509Certificate : X509Certificate
{
    private readonly byte[] bytes;
    private readonly X509Certificate2 wrapped;

    /**
     * Creates a new instance which will lazy parse the given bytes. Be aware that the bytes will not be cloned.
     */
    public LazyJavaxX509Certificate(byte[] bytes) : this(ObjectUtil.CheckNotNull(bytes, "bytes"), Unwrap(bytes))
    {
    }

    private LazyJavaxX509Certificate(byte[] bytes, X509Certificate2 wrapped) : base(wrapped)
    {
        this.bytes = bytes;
        this.wrapped = wrapped;
    }

    public byte[] GetEncoded()
    {
        return (byte[]) bytes.Clone();
    }

    /**
     * Return the underyling {@code byte[]} without cloning it first. This {@code byte[]} <strong>must</strong> never
     * be mutated.
     */
    internal byte[] GetBytes()
    {
        return bytes;
    }

    // CLR: X509Certificate.getInstance(bytes) wrapped in an IllegalStateException, whose message is the cause's
    // toString() (class name, ": ", message; Common's ThrowableUtil.ThrowableToString).
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
