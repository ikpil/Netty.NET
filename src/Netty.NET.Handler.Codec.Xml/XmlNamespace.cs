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

namespace Netty.NET.Handler.Codec.Xml;

/**
 * XML namespace is part of XML element.
 */
// CLR: the accessors are virtual: a Java method that is not final can be overridden ("A method may be declared final
// (§8.4.3.3), in which case it cannot be hidden or overridden", https://docs.oracle.com/javase/specs/jls/se17/html/jls-8.html),
// while in C# "By default, methods are non-virtual. You can't override a non-virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual). equals/hashCode/toString are
// the overrides Equals(object)/GetHashCode()/ToString(). Java string conversion writes a null reference as "null"
// (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11), while in C# "the string representation
// of null is an empty string"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/addition-operator), so ToString()
// writes (field ?? "null"). String.hashCode() is s[0]*31^(n-1) + ... + s[n-1]
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html), while for string.GetHashCode()
// "The hash code itself is not guaranteed to be stable"
// (https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0): the hash values differ from
// the JVM's, and equal namespaces still hash equally, which is the contract Equals relies on.
public class XmlNamespace
{

    private readonly string prefix;
    private readonly string uri;

    public XmlNamespace(string prefix, string uri)
    {
        this.prefix = prefix;
        this.uri = uri;
    }

    public virtual string Prefix()
    {
        return prefix;
    }

    public virtual string Uri()
    {
        return uri;
    }

    public override bool Equals(object o)
    {
        if (this == o)
        {
            return true;
        }
        if (o == null || GetType() != o.GetType())
        {
            return false;
        }

        XmlNamespace that = (XmlNamespace) o;

        if (prefix != null ? !prefix.Equals(that.prefix) : that.prefix != null)
        {
            return false;
        }
        if (uri != null ? !uri.Equals(that.uri) : that.uri != null)
        {
            return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        int result = prefix != null ? prefix.GetHashCode() : 0;
        result = 31 * result + (uri != null ? uri.GetHashCode() : 0);
        return result;
    }

    public override string ToString()
    {
        return "XmlNamespace{" +
                "prefix='" + (prefix ?? "null") + '\'' +
                ", uri='" + (uri ?? "null") + '\'' +
                '}';
    }

}
