/*
 * Copyright 2022 The Netty Project
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

namespace Netty.NET.Handler.Ssl.Ocsp;

// CLR: java.util.Date is DateTimeOffset, and the optional nextUpdate is a nullable DateTimeOffset. status() is
// Status_(): a method cannot share the name of the nested Status enum, as pkitesting's algorithm(Algorithm) is
// Algorithm_(Algorithm). toString() formats the dates as Date.toString() does, "EEE MMM dd HH:mm:ss zzz yyyy"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Date.html#toString()), in UTC with the
// invariant culture; a missing nextUpdate is "null", as Java string concatenation writes it.
public class OcspResponse
{
    private readonly Status status;
    private readonly DateTimeOffset thisUpdate;
    private readonly DateTimeOffset? nextUpdate;

    // CLR: checkNotNull(status, "Status") and checkNotNull(thisUpdate, "ThisUpdate") have no counterpart: the enum and
    // DateTimeOffset are value types, which cannot be null.
    public OcspResponse(Status status, DateTimeOffset thisUpdate, DateTimeOffset? nextUpdate)
    {
        this.status = status;
        this.thisUpdate = thisUpdate;
        this.nextUpdate = nextUpdate; // The 'nextUpdate' field is optional.
    }

    /**
     * The OCSP response status, saying if the certificate is valid or not.
     */
    public Status Status_()
    {
        return status;
    }

    /**
     * The date of the given OCSP update, never {@code null}.
     */
    public DateTimeOffset ThisUpdate()
    {
        return thisUpdate;
    }

    /**
     * The future date of the next OCSP update, if any.
     * This field is optional, and if {@code null}, indicates that an update is available at any time.
     */
    public DateTimeOffset? NextUpdate()
    {
        return nextUpdate;
    }

    public override string ToString()
    {
        return "OcspResponse{" +
                "status=" + status +
                ", thisUpdate=" + JavaDateString(thisUpdate) +
                ", nextUpdate=" + (nextUpdate == null ? "null" : JavaDateString(nextUpdate.Value)) +
                '}';
    }

    private static string JavaDateString(DateTimeOffset date)
    {
        return date.UtcDateTime.ToString("ddd MMM dd HH:mm:ss 'UTC' yyyy", CultureInfo.InvariantCulture);
    }

    public enum Status
    {
        /**
         * Certificate is valid
         */
        VALID,

        /**
         * Certificate is revoked
         */
        REVOKED,

        /**
         * Certificate status is unknown
         */
        UNKNOWN
    }
}
