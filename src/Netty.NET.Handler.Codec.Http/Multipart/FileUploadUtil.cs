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
using System.Globalization;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.Multipart;

// CLR: The String.format patterns "0x%02x" and "%d" are the invariant-culture formats "x2" and the decimal
// integer. HttpHeaderValidationUtil.validateValidHeaderValue takes an ICharSequence, so the String is wrapped in
// StringCharSequence, as core's CombinedHttpHeaders does. IllegalArgumentException is ArgumentException.
internal static class FileUploadUtil
{

    internal static int HashCode(IFileUpload upload)
    {
        return upload.GetName().GetHashCode();
    }

    internal static bool Equals(IFileUpload upload1, IFileUpload upload2)
    {
        return string.Equals(upload1.GetName(), upload2.GetName(), StringComparison.OrdinalIgnoreCase);
    }

    internal static int CompareTo(IFileUpload upload1, IFileUpload upload2)
    {
        return string.Compare(upload1.GetName(), upload2.GetName(), StringComparison.OrdinalIgnoreCase);
    }

    /**
     * Control characters, the DEL character, double-quote, and backslash are either disallowed or strongly discouraged,
     * depending on which {@code multipart/form-data} specification you read.
     * This method conservatively rejects all of them, and is used for <em>outbound</em> (encoding) filenames.
     * @param filename The filename to check.
     * @return The validated filename, unchanged.
     */
    internal static string ValidateFileNameForMultiPart(string filename)
    {
        int length = ObjectUtil.CheckNotNull(filename, "filename").Length;

        for (int i = 0; i < length; i++)
        {
            char c = filename[i];
            if (c < HttpConstants.SP /*control character block*/ || c == HttpConstants.DEL ||
                c == HttpConstants.DOUBLE_QUOTE || c == HttpConstants.BACKSLASH)
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "Illegal filename character 0x{0:x2} at index {1}",
                        (int) c, i));
            }
        }

        return filename;
    }

    /**
     * Control characters and the DEL character are either disallowed or strongly discouraged in the
     * {@code Content-Type}.
     * Space and tab is allowed except for the first character, quotation and other special characters are allowed.
     * <p>
     * The Content-Type value MUST be structured in the form of {@code type/subtype parameters}.
     * See <a href="https://www.rfc-editor.org/rfc/rfc9110.html#section-8.3.1">RFC 9110 §8.3.1</a>,
     * and <a href="https://www.rfc-editor.org/info/rfc2045/#section-5.1">RFC 2045 §5.1</a>.
     * <p>
     * This is used for multipart <em>encoding</em>, so we refuse to emit a malformed content type.
     *
     * @param contentType The Content-Type header value to check.
     * @return The valid Content-Type header value, unchanged.
     */
    internal static string ValidateContentTypeForMultiPart(string contentType)
    {
        CheckContentTypeForMultiPart(
            ObjectUtil.CheckNotNull(contentType, "contentType"), true);
        return contentType;
    }

    /**
     * Determine if the given content type value is syntactically valid and well-formed.
     * <p>
     * The Content-Type value is valid if it is structured in the form of {@code type/subtype parameters}.
     * See <a href="https://www.rfc-editor.org/rfc/rfc9110.html#section-8.3.1">RFC 9110 §8.3.1</a>,
     * and <a href="https://www.rfc-editor.org/info/rfc2045/#section-5.1">RFC 2045 §5.1</a>.
     * <p>
     * This is used for multipart <em>decoding</em>, where
     * <a href="https://www.rfc-editor.org/info/rfc2045/#section-5.2">RFC 2045 §5.2</a>
     * recommends that syntactically invalid content type values be replaced with a default.
     *
     * @param contentType The Content-Type header value to check.
     * @return {@code true} if the Content-Type header value is valid, otherwise {@code false}.
     */
    internal static bool IsContentTypeForMultiPartValid(string contentType)
    {
        return contentType != null && CheckContentTypeForMultiPart(contentType, false);
    }

    private static bool CheckContentTypeForMultiPart(string contentType, bool throwIfInvalid)
    {
        int index = HttpHeaderValidationUtil.ValidateValidHeaderValue(new StringCharSequence(contentType));
        if (index != -1)
        {
            if (throwIfInvalid)
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "Illegal Content-Type character 0x{0:x2} at index {1}",
                        (int) contentType[index], index));
            }

            return false;
        }

        int slash = contentType.IndexOf('/');
        int semicolon = contentType.IndexOf(';');
        if (slash == -1 ||
            slash == 0 ||
            (semicolon == -1 && slash == contentType.Length - 1) ||
            (semicolon != -1 && slash + 1 >= semicolon))
        {
            if (throwIfInvalid)
            {
                throw new ArgumentException("Malformed content type value: " +
                    "index of '/' is " + slash + " content type length " + contentType.Length);
            }

            return false;
        }

        return true;
    }
}
