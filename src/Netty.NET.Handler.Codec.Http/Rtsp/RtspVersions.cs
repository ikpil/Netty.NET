/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;

namespace Netty.NET.Handler.Codec.Rtsp;

/**
 * The version of RTSP.
 */
// CLR: the final class with a private constructor is a static class. IllegalArgumentException is ArgumentException
// (docs/codec-base-porting.md), toUpperCase(Locale.US) is ToUpperInvariant, whose invariant casing never maps 'i' to
// U+0130 (https://learn.microsoft.com/en-us/dotnet/api/system.string.toupperinvariant?view=net-10.0, the mapping core
// HttpVersion uses), and String.equals is the ordinal string.Equals.
public static class RtspVersions
{

    /**
     * RTSP/1.0
     */
    public static readonly HttpVersion RTSP_1_0 = new HttpVersion("RTSP", 1, 0, true);

    /**
     * Returns an existing or new {@link HttpVersion} instance which matches to
     * the specified RTSP version string.  If the specified {@code text} is
     * equal to {@code "RTSP/1.0"}, {@link #RTSP_1_0} will be returned.
     * Otherwise, a new {@link HttpVersion} instance will be returned.
     */
    public static HttpVersion ValueOf(string text)
    {
        ObjectUtil.CheckNotNull(text, "text");

        if (text.Length == 0)
        {
            throw new ArgumentException("text must not be empty");
        }
        // toUpperCase() must specify Locale.US so the comparison against "RTSP/1.0" is not
        // affected by the JVM default locale (e.g. Turkish, where 'i' uppercases to 'İ').
        string upper = text.ToUpperInvariant();
        if ("RTSP/1.0".Equals(upper, StringComparison.Ordinal))
        {
            return RTSP_1_0;
        }

        return new HttpVersion(upper, true);
    }
}
