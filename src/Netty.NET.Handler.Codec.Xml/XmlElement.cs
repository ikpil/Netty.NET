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

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Netty.NET.Handler.Codec.Xml;

/**
 * Generic XML element in document, {@link XmlElementStart} represents open element and provides access to attributes,
 * {@link XmlElementEnd} represents closing element.
 */
// CLR: the accessors are virtual: a Java method that is not final can be overridden ("A method may be declared
// final (§8.4.3.3), in which case it cannot be hidden or overridden",
// https://docs.oracle.com/javase/specs/jls/se17/html/jls-8.html), while in C# "By default, methods are
// non-virtual. You can't override a non-virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual). equals/hashCode/toString
// are the overrides Equals(object)/GetHashCode()/ToString(). The field namespace is @namespace, because "You can't
// use them [keywords] as identifiers in your program unless you prefix them with @"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/). The mutable ArrayList is a
// List<XmlNamespace> returned as IList<XmlNamespace> (the List<Object> out mapping of docs/codec-base-porting.md),
// so callers still add namespaces to it. Java string conversion writes a null reference as "null"
// (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11), while in C# "the string
// representation of null is an empty string"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/addition-operator), so ToString()
// writes (field ?? "null"). String.hashCode() is s[0]*31^(n-1) +... + s[n-1]
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html), while for
// string.GetHashCode() "The hash code itself is not guaranteed to be stable"
// (https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0): the hash values differ
// from the JVM's, and equal elements still hash equally, which is the contract Equals relies on.
public abstract class XmlElement
{

    private readonly string name;
    private readonly string @namespace;
    private readonly string prefix;

    private readonly List<XmlNamespace> namespaces = new List<XmlNamespace>();

    protected XmlElement(string name, string @namespace, string prefix)
    {
        this.name = name;
        this.@namespace = @namespace;
        this.prefix = prefix;
    }

    public virtual string Name()
    {
        return name;
    }

    public virtual string Namespace()
    {
        return @namespace;
    }

    public virtual string Prefix()
    {
        return prefix;
    }

    public virtual IList<XmlNamespace> Namespaces()
    {
        return namespaces;
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

        XmlElement that = (XmlElement) o;

        if (!name.Equals(that.name))
        {
            return false;
        }
        if (@namespace != null ? !@namespace.Equals(that.@namespace) : that.@namespace != null)
        {
            return false;
        }
        if (!ListEquals(namespaces, that.namespaces))
        {
            return false;
        }
        if (prefix != null ? !prefix.Equals(that.prefix) : that.prefix != null)
        {
            return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        int result = name.GetHashCode();
        result = 31 * result + (@namespace != null ? @namespace.GetHashCode() : 0);
        result = 31 * result + (prefix != null ? prefix.GetHashCode() : 0);
        result = 31 * result + ListHashCode(namespaces);
        return result;
    }

    public override string ToString()
    {
        return ", name='" + (name ?? "null") + '\'' +
                ", namespace='" + (@namespace ?? "null") + '\'' +
                ", prefix='" + (prefix ?? "null") + '\'' +
                ", namespaces=" + ListToString(namespaces);
    }

    // CLR: the source relies on java.util.List equality, hashing and text for namespaces (and XmlElementStart for
    // attributes): lists are equal if "both lists have the same size, and all corresponding pairs of elements in the two
    // lists are equal", the hash is "int hashCode = 1; for (E e : list) hashCode = 31*hashCode + (e==null ? 0 :
    // e.hashCode());" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/List.html), and toString()
    // lists the elements "enclosed in square brackets ("[]")" separated by ", ", each converted "as by
    // String.valueOf(Object)" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/AbstractCollection.html).
    // List<T> inherits Object.Equals/GetHashCode/ToString, so these helpers give the List behaviour. They are private
    // protected: "Types that derive from the class and are declared in the containing assembly can access a private
    // protected member" (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/private-protected),
    // so the public surface stays that of the source. SequenceEqual is true "if the two source sequences are of equal
    // length and their corresponding elements are equal according to the default equality comparer for their type"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.sequenceequal?view=net-10.0), which calls the
    // element's Equals(object) as Objects.equals does.
    private protected static bool ListEquals<T>(List<T> a, List<T> b)
    {
        return a.SequenceEqual(b);
    }

    private protected static int ListHashCode<T>(List<T> list)
    {
        int hashCode = 1;

        foreach (T e in list)
        {
            hashCode = 31 * hashCode + (e == null ? 0 : e.GetHashCode());
        }

        return hashCode;
    }

    private protected static string ListToString<T>(List<T> list)
    {
        StringBuilder buf = new StringBuilder("[");

        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                buf.Append(", ");
            }

            buf.Append(list[i]?.ToString() ?? "null");
        }

        return buf.Append(']').ToString();
    }

}
