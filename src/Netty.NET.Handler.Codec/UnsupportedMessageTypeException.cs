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
using System.Text;

namespace Netty.NET.Handler.Codec;

/**
 * Thrown if an unsupported message is received by an codec.
 */
// CLR: serialVersionUID is JVM serialization metadata and has no counterpart.
public class UnsupportedMessageTypeException : CodecException
{
    // CLR: Class<?>... is params Type[]; Class.getName() is Type.FullName (nested types are spelled Outer+Inner).
    public UnsupportedMessageTypeException(
            object message, params Type[] expectedTypes)
        : base(Message(
                message == null? "null" : message.GetType().FullName, expectedTypes))
    {
    }

    public UnsupportedMessageTypeException() { }

    public UnsupportedMessageTypeException(string message, Exception cause) : base(message, cause)
    {
    }

    public UnsupportedMessageTypeException(string s) : base(s)
    {
    }

    public UnsupportedMessageTypeException(Exception cause) : base(cause)
    {
    }

    // CLR: the Java helper name hides the inherited Exception.Message property, hence 'new'.
    private static new string Message(
            string actualType, params Type[] expectedTypes)
    {
        StringBuilder buf = new StringBuilder(actualType);

        if (expectedTypes != null && expectedTypes.Length > 0)
        {
            buf.Append(" (expected: ").Append(expectedTypes[0].FullName);
            for (int i = 1; i < expectedTypes.Length; i ++)
            {
                Type t = expectedTypes[i];
                if (t == null)
                {
                    break;
                }
                buf.Append(", ").Append(t.FullName);
            }
            buf.Append(')');
        }

        return buf.ToString();
    }
}
