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
using System.Globalization;
using System.Text;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.Cookie.CookieUtil;
using SameSite = Netty.NET.Handler.Codec.Http.Cookie.CookieHeaderNames.SameSite;

namespace Netty.NET.Handler.Codec.Http.Cookie;

/**
 * The default {@link Cookie} implementation.
 */
// CLR: the public members of the non-final Java class are virtual; Comparable.compareTo is CompareTo, equals/hashCode
// are Equals/GetHashCode (String.hashCode is string.GetHashCode: equal names hash equally, the value differs from
// Java's). String.compareTo is the ordinal UTF-16 comparison string.CompareOrdinal (as HttpVersion). String.equals is the
// ordinal string.Equals. equalsIgnoreCase and compareToIgnoreCase fold every char with Character.toLowerCase(
// Character.toUpperCase(c)) ("case differences have been eliminated by calling
// Character.toLowerCase(Character.toUpperCase(int)) on each Unicode code point",
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html); the private CompareToIgnoreCase
// keeps that fold with char.ToUpperInvariant/ToLowerInvariant (the invariant culture casing rules,
// https://learn.microsoft.com/en-us/dotnet/api/system.string.toupperinvariant?view=net-10.0), because
// StringComparison.OrdinalIgnoreCase only uppercases and orders '_' and the other chars between 'Z' and 'a' after the
// letters (measured: Compare("_", "a", OrdinalIgnoreCase) is positive, the Java result negative). One measured
// difference remains: ToUpperInvariant leaves U+0131 (dotless i) unchanged, where Character.toUpperCase maps it to 'I'.
// ObjectUtil.checkNotNull is the Common CheckNotNull (ArgumentNullException). The stringBuilder() of CookieUtil
// is an operation-owned StringBuilder (see CookieUtil), and the long maxAge is appended as invariant-culture text.
// UNDEFINED_MAX_AGE is the interface constant ICookie.UNDEFINED_MAX_AGE.
public class DefaultCookie : ICookie
{

    private readonly string name;
    private string value;
    private bool wrap;
    private string domain;
    private string path;
    private long maxAge = ICookie.UNDEFINED_MAX_AGE;
    private bool secure;
    private bool httpOnly;
    private SameSite sameSite;
    private bool partitioned;

    /**
     * Creates a new cookie with the specified name and value.
     */
    public DefaultCookie(string name, string value)
    {
        this.name = CheckNonEmptyAfterTrim(name, "name");
        SetValue(value);
    }

    public virtual string Name()
    {
        return name;
    }

    public virtual string Value()
    {
        return value;
    }

    public virtual void SetValue(string value)
    {
        this.value = CheckNotNull(value, "value");
    }

    public virtual bool Wrap()
    {
        return wrap;
    }

    public virtual void SetWrap(bool wrap)
    {
        this.wrap = wrap;
    }

    public virtual string Domain()
    {
        return domain;
    }

    public virtual void SetDomain(string domain)
    {
        this.domain = ValidateAttributeValue("domain", domain);
    }

    public virtual string Path()
    {
        return path;
    }

    public virtual void SetPath(string path)
    {
        this.path = ValidateAttributeValue("path", path);
    }

    public virtual long MaxAge()
    {
        return maxAge;
    }

    public virtual void SetMaxAge(long maxAge)
    {
        this.maxAge = maxAge;
    }

    public virtual bool IsSecure()
    {
        return secure;
    }

    public virtual void SetSecure(bool secure)
    {
        this.secure = secure;
    }

    public virtual bool IsHttpOnly()
    {
        return httpOnly;
    }

    public virtual void SetHttpOnly(bool httpOnly)
    {
        this.httpOnly = httpOnly;
    }

    /**
     * Checks to see if this {@link Cookie} can be sent along cross-site requests.
     * For more information, please look
     * <a href="https://tools.ietf.org/html/draft-ietf-httpbis-rfc6265bis-05">here</a>
     * @return <b>same-site-flag</b> value
     */
    public virtual SameSite SameSite()
    {
        return sameSite;
    }

    /**
     * Determines if this this {@link Cookie} can be sent along cross-site requests.
     * For more information, please look
     *  <a href="https://tools.ietf.org/html/draft-ietf-httpbis-rfc6265bis-05">here</a>
     * @param sameSite <b>same-site-flag</b> value
     */
    public virtual void SetSameSite(SameSite sameSite)
    {
        this.sameSite = sameSite;
    }

    /**
     * Checks to see if this {@link Cookie} is partitioned
     *
     * @return True if this {@link Cookie} is partitioned, otherwise false
     */
    public virtual bool IsPartitioned()
    {
        return partitioned;
    }

    /**
     * Sets the {@code Partitioned} attribute of this {@link Cookie}
     *
     * @param partitioned True if this {@link Cookie} is to be partitioned, otherwise false
     */
    public virtual void SetPartitioned(bool partitioned)
    {
        this.partitioned = partitioned;
    }

    public override int GetHashCode()
    {
        return Name().GetHashCode();
    }

    public override bool Equals(object o)
    {
        if (this == o)
        {
            return true;
        }

        if (!(o is ICookie))
        {
            return false;
        }

        ICookie that = (ICookie) o;
        if (!Name().Equals(that.Name()))
        {
            return false;
        }

        if (Path() == null)
        {
            if (that.Path() != null)
            {
                return false;
            }
        }
        else if (that.Path() == null)
        {
            return false;
        }
        else if (!Path().Equals(that.Path()))
        {
            return false;
        }

        if (Domain() == null)
        {
            if (that.Domain() != null)
            {
                return false;
            }
        }
        else
        {
            // CLR: String.equalsIgnoreCase (null is not equal), with the Java case fold of CompareToIgnoreCase.
            return that.Domain() != null && CompareToIgnoreCase(Domain(), that.Domain()) == 0;
        }

        return true;
    }

    public virtual int CompareTo(ICookie c)
    {
        int v = string.CompareOrdinal(Name(), c.Name());
        if (v != 0)
        {
            return v;
        }

        if (Path() == null)
        {
            if (c.Path() != null)
            {
                return -1;
            }
        }
        else if (c.Path() == null)
        {
            return 1;
        }
        else
        {
            v = string.CompareOrdinal(Path(), c.Path());
            if (v != 0)
            {
                return v;
            }
        }

        if (Domain() == null)
        {
            if (c.Domain() != null)
            {
                return -1;
            }
        }
        else if (c.Domain() == null)
        {
            return 1;
        }
        else
        {
            v = CompareToIgnoreCase(Domain(), c.Domain());
            return v;
        }

        return 0;
    }

    // CLR: String.compareToIgnoreCase: the first difference of the folded chars, else the length difference.
    private static int CompareToIgnoreCase(string s1, string s2)
    {
        int n1 = s1.Length;
        int n2 = s2.Length;
        int min = Math.Min(n1, n2);

        for (int i = 0; i < min; i++)
        {
            char c1 = s1[i];
            char c2 = s2[i];
            if (c1 != c2)
            {
                c1 = char.ToLowerInvariant(char.ToUpperInvariant(c1));
                c2 = char.ToLowerInvariant(char.ToUpperInvariant(c2));
                if (c1 != c2)
                {
                    return c1 - c2;
                }
            }
        }

        return n1 - n2;
    }

    /**
     * Validate a cookie attribute value, throws a {@link IllegalArgumentException} otherwise.
     * Only intended to be used by {@link io.netty.handler.codec.http.DefaultCookie}.
     * @param name attribute name
     * @param value attribute value
     * @return the trimmed, validated attribute value
     * @deprecated CookieUtil is package private, will be removed once old Cookie API is dropped
     */
    // CLR: @deprecated names no replacement, so it is ported with [Obsolete].
    [Obsolete("CookieUtil is package private, will be removed once old Cookie API is dropped")]
    protected virtual string ValidateValue(string name, string value)
    {
        return ValidateAttributeValue(name, value);
    }

    public override string ToString()
    {
        StringBuilder buf = CookieUtil.StringBuilder()
            .Append(Name())
            .Append('=')
            .Append(Value());

        if (Domain() != null)
        {
            buf.Append(", domain=")
               .Append(Domain());
        }

        if (Path() != null)
        {
            buf.Append(", path=")
               .Append(Path());
        }

        if (MaxAge() >= 0)
        {
            buf.Append(", maxAge=")
               .Append(MaxAge().ToString(CultureInfo.InvariantCulture))
               .Append('s');
        }

        if (IsSecure())
        {
            buf.Append(", secure");
        }

        if (IsHttpOnly())
        {
            buf.Append(", HTTPOnly");
        }

        if (SameSite() != null)
        {
            buf.Append(", SameSite=").Append(SameSite());
        }

        if (IsPartitioned())
        {
            buf.Append(", Partitioned");
        }

        return buf.ToString();
    }
}
