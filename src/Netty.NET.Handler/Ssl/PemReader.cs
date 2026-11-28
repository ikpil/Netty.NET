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
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Base64Codec;

namespace Netty.NET.Handler.Ssl;

/**
 * Reads a PEM file and converts it into a list of DERs so that they are imported into a {@link KeyStore} easily.
 */
// CLR: java.io.File is FileInfo (docs/common-clr-design.md, as handler Stream ChunkedFile takes it) and InputStream is
// System.IO.Stream. CertificateException and KeyException are java.security.GeneralSecurityException subclasses, which
// the BCL reports as CryptographicException (as pkitesting Algorithms maps them, src/Netty.NET.PkiTesting/Algorithms.cs),
// with the same messages. FileInputStream throws FileNotFoundException when the file "does not exist, is a directory
// rather than a regular file, or for some other reason cannot be opened for reading"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/FileInputStream.html), while FileInfo.OpenRead()
// throws UnauthorizedAccessException when the name "is read-only or is a directory"
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.fileinfo.openread?view=net-10.0), so both that exception and
// IOException map to the "could not find" exceptions. Pattern/Matcher are Regex/Match: find(start) is Match(input,
// start), m.end() is Index + Length and usePattern(p) keeps the position by passing it to the next pattern's Match. The
// content is decoded with Encoding.ASCII, so every character is ASCII ('?' where Java's US-ASCII decoder puts U+FFFD for
// a byte above 127, neither of which a pattern below distinguishes); on ASCII text the .NET \s is Java's [ \t\n\x0B\f\r],
// and CASE_INSENSITIVE, which "assumes that only characters in the US-ASCII charset are being matched"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/regex/Pattern.html), is written as the explicit
// class [a-zA-Z0-9+/=]. The KeyStore of the Javadoc is the PKCS #12/X509Certificate2Collection use of the DER blocks
// (ssl design decision 3). The class is static: the private constructor only prevented instantiation.
internal static class PemReader
{

    private static readonly Regex CERT_HEADER = new Regex(
            "-+BEGIN\\s[^-\\r\\n]*CERTIFICATE[^-\\r\\n]*-+(?:\\s|\\r|\\n)+");
    private static readonly Regex CERT_FOOTER = new Regex(
            "-+END\\s[^-\\r\\n]*CERTIFICATE[^-\\r\\n]*-+(?:\\s|\\r|\\n)*");
    private static readonly Regex KEY_HEADER = new Regex(
            "-+BEGIN\\s[^-\\r\\n]*PRIVATE\\s+KEY[^-\\r\\n]*-+(?:\\s|\\r|\\n)+");
    private static readonly Regex KEY_FOOTER = new Regex(
            "-+END\\s[^-\\r\\n]*PRIVATE\\s+KEY[^-\\r\\n]*-+(?:\\s|\\r|\\n)*");
    private static readonly Regex BODY = new Regex("[a-zA-Z0-9+/=][a-zA-Z0-9+/=\\r\\n]*");

    internal static ByteBuf[] ReadCertificates(FileInfo file)
    {
        try
        {
            using (System.IO.Stream @in = file.OpenRead())
            {
                return ReadCertificates(@in);
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            throw new CryptographicException("could not find certificate file: " + file);
        }
    }

    internal static ByteBuf[] ReadCertificates(System.IO.Stream @in)
    {
        string content;

        try
        {
            content = ReadContent(@in);
        }
        catch (IOException e)
        {
            throw new CryptographicException("failed to read certificate input stream", e);
        }

        List<ByteBuf> certs = new List<ByteBuf>();
        Regex pattern = CERT_HEADER;
        Match m;
        int start = 0;

        try
        {
            for (;;)
            {
                m = pattern.Match(content, start);
                if (!m.Success)
                {
                    break;
                }

                // Here and below it's necessary to save the position as it is reset
                // after calling usePattern() on Android due to a bug.
                //
                // See https://issuetracker.google.com/issues/293206296
                start = m.Index + m.Length;
                pattern = BODY;
                m = pattern.Match(content, start);
                if (!m.Success)
                {
                    break;
                }

                ByteBuf base64 = Unpooled.CopiedBuffer(m.Value, Encoding.ASCII);

                try
                {
                    start = m.Index + m.Length;
                    pattern = CERT_FOOTER;
                    m = pattern.Match(content, start);
                    if (!m.Success)
                    {
                        // Certificate is incomplete.
                        break;
                    }

                    ByteBuf der = Base64.Decode(base64);
                    certs.Add(der);
                }
                finally
                {
                    base64.Release();
                }

                start = m.Index + m.Length;
                pattern = CERT_HEADER;
            }
        }
        catch (Exception)
        {
            foreach (ByteBuf cert in certs)
            {
                cert.Release();
            }

            throw;
        }

        if (certs.Count == 0)
        {
            throw new CryptographicException("found no certificates in input stream");
        }

        return certs.ToArray();
    }

    internal static ByteBuf ReadPrivateKey(FileInfo file)
    {
        try
        {
            using (System.IO.Stream @in = file.OpenRead())
            {
                return ReadPrivateKey(@in);
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            throw new CryptographicException("could not find key file: " + file);
        }
    }

    internal static ByteBuf ReadPrivateKey(System.IO.Stream @in)
    {
        string content;

        try
        {
            content = ReadContent(@in);
        }
        catch (IOException e)
        {
            throw new CryptographicException("failed to read key input stream", e);
        }

        int start = 0;
        Match m = KEY_HEADER.Match(content, start);
        if (!m.Success)
        {
            throw KeyNotFoundException();
        }

        start = m.Index + m.Length;
        m = BODY.Match(content, start);
        if (!m.Success)
        {
            throw KeyNotFoundException();
        }

        ByteBuf base64 = Unpooled.CopiedBuffer(m.Value, Encoding.ASCII);

        try
        {
            start = m.Index + m.Length;
            m = KEY_FOOTER.Match(content, start);
            if (!m.Success)
            {
                // Key is incomplete.
                throw KeyNotFoundException();
            }

            return Base64.Decode(base64);
        }
        finally
        {
            base64.Release();
        }
    }

    private static CryptographicException KeyNotFoundException()
    {
        return new CryptographicException("could not find a PKCS #8 private key in input stream" +
                " (see https://netty.io/wiki/sslcontextbuilder-and-private-key.html for more information)");
    }

    // CLR: in.read(buf) returns -1 at the end of the stream, Stream.Read returns 0; ByteArrayOutputStream is a
    // MemoryStream and out.toString("US-ASCII") is Encoding.ASCII.GetString.
    private static string ReadContent(System.IO.Stream @in)
    {
        MemoryStream @out = new MemoryStream();
        byte[] buf = new byte[8192];

        for (;;)
        {
            int ret = @in.Read(buf, 0, buf.Length);
            if (ret <= 0)
            {
                break;
            }

            @out.Write(buf, 0, ret);
        }

        return Encoding.ASCII.GetString(@out.GetBuffer(), 0, (int) @out.Length);
    }
}
