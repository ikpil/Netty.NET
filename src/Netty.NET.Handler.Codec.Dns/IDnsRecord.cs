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

namespace Netty.NET.Handler.Codec.Dns;

/**
 * A DNS resource record.
 */
// CLR: interface with the I prefix. The Java interface constants are C# interface constants; C# classes do not
// inherit an interface's static members, so implementers and callers name them through the interface
// (IDnsRecord.CLASS_IN), as static interface members are called on the interface type
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface).
public interface IDnsRecord
{
    /**
     * DNS resource record class: {@code IN}
     */
    const int CLASS_IN = 0x0001;

    /**
     * DNS resource record class: {@code CSNET}
     */
    const int CLASS_CSNET = 0x0002;

    /**
     * DNS resource record class: {@code CHAOS}
     */
    const int CLASS_CHAOS = 0x0003;

    /**
     * DNS resource record class: {@code HESIOD}
     */
    const int CLASS_HESIOD = 0x0004;

    /**
     * DNS resource record class: {@code NONE}
     */
    const int CLASS_NONE = 0x00fe;

    /**
     * DNS resource record class: {@code ANY}
     */
    const int CLASS_ANY = 0x00ff;

    /**
     * Returns the name of this resource record.
     */
    string Name();

    /**
     * Returns the type of this resource record.
     */
    DnsRecordType Type();

    /**
     * Returns the class of this resource record.
     *
     * @return the class value, usually one of the following:
     *         <ul>
     *             <li>{@link #CLASS_IN}</li>
     *             <li>{@link #CLASS_CSNET}</li>
     *             <li>{@link #CLASS_CHAOS}</li>
     *             <li>{@link #CLASS_HESIOD}</li>
     *             <li>{@link #CLASS_NONE}</li>
     *             <li>{@link #CLASS_ANY}</li>
     *         </ul>
     */
    int DnsClass();

    /**
     * Returns the time to live after reading for this resource record.
     */
    long TimeToLive();
}
