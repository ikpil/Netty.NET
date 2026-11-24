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

namespace Netty.NET.Handler.Codec.Xml;

/**
 * Specific {@link XmlElement} representing beginning  of element.
 */
// CLR: attributes() is virtual: a Java method that is not final can be overridden ("A method may be declared final
// (§8.4.3.3), in which case it cannot be hidden or overridden", https://docs.oracle.com/javase/specs/jls/se17/html/jls-8.html),
// while in C# "By default, methods are non-virtual. You can't override a non-virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual). The mutable ArrayList is a
// List<XmlAttribute> returned as IList<XmlAttribute> (the List<Object> out mapping of docs/codec-base-porting.md), so
// callers still add attributes to it. attributes.equals/hashCode/toString are the java.util.List equality, hash and
// AbstractCollection text, which List<T> does not override; the private protected helpers
// XmlElement.ListEquals/ListHashCode/ListToString give them (the evidence is at those helpers).
public class XmlElementStart : XmlElement
{

    private readonly List<XmlAttribute> attributes = new List<XmlAttribute>();

    public XmlElementStart(string name, string @namespace, string prefix) : base(name, @namespace, prefix)
    {
    }

    public virtual IList<XmlAttribute> Attributes()
    {
        return attributes;
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
        if (!base.Equals(o))
        {
            return false;
        }

        XmlElementStart that = (XmlElementStart) o;

        return ListEquals(attributes, that.attributes);
    }

    public override int GetHashCode()
    {
        int result = base.GetHashCode();
        result = 31 * result + ListHashCode(attributes);
        return result;
    }

    public override string ToString()
    {
        return "XmlElementStart{" +
                "attributes=" + ListToString(attributes) +
                base.ToString() +
                "} ";
    }

}
