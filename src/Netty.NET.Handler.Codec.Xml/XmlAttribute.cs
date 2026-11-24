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
 * XML attributes, it is part of {@link XmlElement}
 */
// CLR: the accessors are virtual: a Java method that is not final can be overridden ("A method may be declared final
// (§8.4.3.3), in which case it cannot be hidden or overridden", https://docs.oracle.com/javase/specs/jls/se17/html/jls-8.html),
// while in C# "By default, methods are non-virtual. You can't override a non-virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual). equals/hashCode/toString are
// the overrides Equals(object)/GetHashCode()/ToString(). The field namespace is @namespace, because "You can't use
// them [keywords] as identifiers in your program unless you prefix them with @"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/).
// Java string conversion writes a null reference as "null"
// (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11), while in C# "the string representation
// of null is an empty string"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/addition-operator), so ToString()
// writes (field ?? "null"). String.hashCode() is s[0]*31^(n-1) + ... + s[n-1]
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html), while for string.GetHashCode()
// "The hash code itself is not guaranteed to be stable"
// (https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0): the hash values differ from
// the JVM's, and equal attributes still hash equally, which is the contract Equals relies on.
public class XmlAttribute
{

    private readonly string type;
    private readonly string name;
    private readonly string prefix;
    private readonly string @namespace;
    private readonly string value;

    public XmlAttribute(string type, string name, string prefix, string @namespace, string value)
    {
        this.type = type;
        this.name = name;
        this.prefix = prefix;
        this.@namespace = @namespace;
        this.value = value;
    }

    public virtual string Type()
    {
        return type;
    }

    public virtual string Name()
    {
        return name;
    }

    public virtual string Prefix()
    {
        return prefix;
    }

    public virtual string Namespace()
    {
        return @namespace;
    }

    public virtual string Value()
    {
        return value;
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

        XmlAttribute that = (XmlAttribute) o;

        if (!name.Equals(that.name))
        {
            return false;
        }
        if (@namespace != null ? !@namespace.Equals(that.@namespace) : that.@namespace != null)
        {
            return false;
        }
        if (prefix != null ? !prefix.Equals(that.prefix) : that.prefix != null)
        {
            return false;
        }
        if (type != null ? !type.Equals(that.type) : that.type != null)
        {
            return false;
        }
        if (value != null ? !value.Equals(that.value) : that.value != null)
        {
            return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        int result = type != null ? type.GetHashCode() : 0;
        result = 31 * result + name.GetHashCode();
        result = 31 * result + (prefix != null ? prefix.GetHashCode() : 0);
        result = 31 * result + (@namespace != null ? @namespace.GetHashCode() : 0);
        result = 31 * result + (value != null ? value.GetHashCode() : 0);
        return result;
    }

    public override string ToString()
    {
        return "XmlAttribute{" +
                "type='" + (type ?? "null") + '\'' +
                ", name='" + (name ?? "null") + '\'' +
                ", prefix='" + (prefix ?? "null") + '\'' +
                ", namespace='" + (@namespace ?? "null") + '\'' +
                ", value='" + (value ?? "null") + '\'' +
                '}';
    }
}
