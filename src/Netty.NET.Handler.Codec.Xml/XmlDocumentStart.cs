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
 * Beginning of the XML document ... i.e. XML header
 */
// CLR: the accessors are virtual: a Java method that is not final can be overridden ("A method may be declared final
// (§8.4.3.3), in which case it cannot be hidden or overridden", https://docs.oracle.com/javase/specs/jls/se17/html/jls-8.html),
// while in C# "By default, methods are non-virtual. You can't override a non-virtual method"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual). equals/hashCode/toString are
// the overrides Equals(object)/GetHashCode()/ToString(). Java string conversion writes a null reference as "null" and a
// boolean as "true"/"false" (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11), while in C#
// "the string representation of null is an empty string"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/addition-operator) and
// Boolean.ToString() returns "True" or "False" (https://learn.microsoft.com/en-us/dotnet/api/system.boolean.tostring?view=net-10.0),
// so ToString() writes (field ?? "null") and (standalone ? "true" : "false"). String.hashCode() is
// s[0]*31^(n-1) + ... + s[n-1] (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html),
// while for string.GetHashCode() "The hash code itself is not guaranteed to be stable"
// (https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0): the hash values differ from
// the JVM's, and equal document starts still hash equally, which is the contract Equals relies on.
public class XmlDocumentStart
{

    private readonly string encoding;
    private readonly string version;
    private readonly bool standalone;
    private readonly string encodingScheme;

    public XmlDocumentStart(string encoding, string version, bool standalone, string encodingScheme)
    {
        this.encoding = encoding;
        this.version = version;
        this.standalone = standalone;
        this.encodingScheme = encodingScheme;
    }

    /** Return defined or guessed XML encoding **/
    public virtual string Encoding()
    {
        return encoding;
    }

    /** Return defined XML version or null **/
    public virtual string Version()
    {
        return version;
    }

    /** Return standalonity of the document **/
    public virtual bool Standalone()
    {
        return standalone;
    }

    /** Return defined encoding or null **/
    public virtual string EncodingScheme()
    {
        return encodingScheme;
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

        XmlDocumentStart that = (XmlDocumentStart) o;

        if (standalone != that.standalone)
        {
            return false;
        }
        if (encoding != null ? !encoding.Equals(that.encoding) : that.encoding != null)
        {
            return false;
        }
        if (encodingScheme != null ? !encodingScheme.Equals(that.encodingScheme) : that.encodingScheme != null)
        {
            return false;
        }
        if (version != null ? !version.Equals(that.version) : that.version != null)
        {
            return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        int result = encoding != null ? encoding.GetHashCode() : 0;
        result = 31 * result + (version != null ? version.GetHashCode() : 0);
        result = 31 * result + (standalone ? 1 : 0);
        result = 31 * result + (encodingScheme != null ? encodingScheme.GetHashCode() : 0);
        return result;
    }

    public override string ToString()
    {
        return "XmlDocumentStart{" +
                "encoding='" + (encoding ?? "null") + '\'' +
                ", version='" + (version ?? "null") + '\'' +
                ", standalone=" + (standalone ? "true" : "false") +
                ", encodingScheme='" + (encodingScheme ?? "null") + '\'' +
                '}';
    }
}
