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

using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * The superclass which contains core information concerning a {@link DnsQuery} and a {@link DnsResponse}.
 */
// CLR: interface with the I prefix. The generic record accessors keep their Java type parameter
// <T extends DnsRecord> as T : IDnsRecord. Java infers T from the assignment target; C# infers a type argument only
// from the method arguments, "it cannot infer the type parameters only from a constraint or return value"
// (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/generic-methods), so callers state it
// (RecordAt<IDnsQuestion>(section)). The Java re-declarations of touch(), touch(Object), retain() and retain(int)
// only narrow the return type and are inherited unchanged from IReferenceCounted, as for the Transport interfaces:
// C# 9 covariant returns cover class overrides only (docs/transport-porting.md, "Java type names and hierarchy");
// AbstractDnsMessage and its subclasses keep the narrowed return types.
public interface IDnsMessage : IReferenceCounted
{

    /**
     * Returns the {@code ID} of this DNS message.
     */
    int Id();

    /**
     * Sets the {@code ID} of this DNS message.
     */
    IDnsMessage SetId(int id);

    /**
     * Returns the {@code opCode} of this DNS message.
     */
    DnsOpCode OpCode();

    /**
     * Sets the {@code opCode} of this DNS message.
     */
    IDnsMessage SetOpCode(DnsOpCode opCode);

    /**
     * Returns the {@code RD} (recursion desired} field of this DNS message.
     */
    bool IsRecursionDesired();

    /**
     * Sets the {@code RD} (recursion desired} field of this DNS message.
     */
    IDnsMessage SetRecursionDesired(bool recursionDesired);

    /**
     * Returns the {@code Z} (reserved for future use) field of this DNS message.
     */
    int Z();

    /**
     * Sets the {@code Z} (reserved for future use) field of this DNS message.
     */
    IDnsMessage SetZ(int z);

    /**
     * Returns the number of records in the specified {@code section} of this DNS message.
     */
    int Count(DnsSection section);

    /**
     * Returns the number of records in this DNS message.
     */
    int Count();

    /**
     * Returns the first record in the specified {@code section} of this DNS message.
     * When the specified {@code section} is {@link DnsSection#QUESTION}, the type of the returned record is
     * always {@link DnsQuestion}.
     *
     * @return {@code null} if this message doesn't have any records in the specified {@code section}
     */
    T RecordAt<T>(DnsSection section) where T : IDnsRecord;

    /**
     * Returns the record at the specified {@code index} of the specified {@code section} of this DNS message.
     * When the specified {@code section} is {@link DnsSection#QUESTION}, the type of the returned record is
     * always {@link DnsQuestion}.
     *
     * @throws IndexOutOfBoundsException if the specified {@code index} is out of bounds
     */
    T RecordAt<T>(DnsSection section, int index) where T : IDnsRecord;

    /**
     * Sets the specified {@code section} of this DNS message to the specified {@code record},
     * making it a single-record section. When the specified {@code section} is {@link DnsSection#QUESTION},
     * the specified {@code record} must be a {@link DnsQuestion}.
     */
    IDnsMessage SetRecord(DnsSection section, IDnsRecord record);

    /**
     * Sets the specified {@code record} at the specified {@code index} of the specified {@code section}
     * of this DNS message. When the specified {@code section} is {@link DnsSection#QUESTION},
     * the specified {@code record} must be a {@link DnsQuestion}.
     *
     * @return the old record
     * @throws IndexOutOfBoundsException if the specified {@code index} is out of bounds
     */
    T SetRecord<T>(DnsSection section, int index, IDnsRecord record) where T : IDnsRecord;

    /**
     * Adds the specified {@code record} at the end of the specified {@code section} of this DNS message.
     * When the specified {@code section} is {@link DnsSection#QUESTION}, the specified {@code record}
     * must be a {@link DnsQuestion}.
     */
    IDnsMessage AddRecord(DnsSection section, IDnsRecord record);

    /**
     * Adds the specified {@code record} at the specified {@code index} of the specified {@code section}
     * of this DNS message. When the specified {@code section} is {@link DnsSection#QUESTION}, the specified
     * {@code record} must be a {@link DnsQuestion}.
     *
     * @throws IndexOutOfBoundsException if the specified {@code index} is out of bounds
     */
    IDnsMessage AddRecord(DnsSection section, int index, IDnsRecord record);

    /**
     * Removes the record at the specified {@code index} of the specified {@code section} from this DNS message.
     * When the specified {@code section} is {@link DnsSection#QUESTION}, the type of the returned record is
     * always {@link DnsQuestion}.
     *
     * @return the removed record
     */
    T RemoveRecord<T>(DnsSection section, int index) where T : IDnsRecord;

    /**
     * Removes all the records in the specified {@code section} of this DNS message.
     */
    IDnsMessage Clear(DnsSection section);

    /**
     * Removes all the records in this DNS message.
     */
    IDnsMessage Clear();
}
