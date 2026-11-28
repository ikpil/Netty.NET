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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * A skeletal implementation of {@link DnsMessage}.
 */
// CLR: the non-final Java methods are virtual, and the subclasses narrow their return types with C# 9 covariant
// overrides: "an overriding method can declare a more derived return type the method it overrides"
// (https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance); the same covariant
// overrides narrow retain()/touch() of AbstractReferenceCounted. equals/hashCode are Equals/GetHashCode. A source IndexOutOfBoundsException is ArgumentOutOfRangeException with the
// source text (List<T> indexers throw ArgumentOutOfRangeException as well), IllegalArgumentException is
// ArgumentException and checkNotNull throws ArgumentNullException (docs/codec-base-porting.md, Shared CLR
// adaptations). Numbers in text use the invariant culture (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
public abstract class AbstractDnsMessage : AbstractReferenceCounted, IDnsMessage
{

    private static readonly ResourceLeakDetector<IDnsMessage> leakDetector =
            ResourceLeakDetectorFactory.Instance().NewResourceLeakDetector<IDnsMessage>(typeof(IDnsMessage));

    private static readonly int SECTION_QUESTION = (int) DnsSection.QUESTION;
    private const int SECTION_COUNT = 4;

    private readonly IResourceLeakTracker<IDnsMessage> leak;
    private short id;
    private DnsOpCode opCode;
    private bool recursionDesired;
    private byte z;

    // To reduce the memory footprint of a message,
    // each of the following fields is a single record or a list of records.
    private object questions;
    private object answers;
    private object authorities;
    private object additionals;

    /**
     * Creates a new instance with the specified {@code id} and {@link DnsOpCode#QUERY} opCode.
     */
    protected AbstractDnsMessage(int id) : this(id, DnsOpCode.QUERY)
    {
    }

    /**
     * Creates a new instance with the specified {@code id} and {@code opCode}.
     */
    // CLR: the Java field initializer leakDetector.track(this) runs before the constructor body; a C# instance field
    // initializer "cannot reference the instance being created"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#15563-instance-field-initialization),
    // so the tracker is created first in the constructor that every other constructor chains to.
    protected AbstractDnsMessage(int id, DnsOpCode opCode)
    {
        leak = leakDetector.Track(this);
        SetId(id);
        SetOpCode(opCode);
    }

    public virtual int Id()
    {
        return id & 0xFFFF;
    }

    // CLR: the (short) narrowing keeps the low 16 bits as Java does (unchecked, so a checked build does not throw).
    public virtual IDnsMessage SetId(int id)
    {
        this.id = unchecked((short) id);
        return this;
    }

    public virtual DnsOpCode OpCode()
    {
        return opCode;
    }

    public virtual IDnsMessage SetOpCode(DnsOpCode opCode)
    {
        this.opCode = CheckNotNull(opCode, "opCode");
        return this;
    }

    public virtual bool IsRecursionDesired()
    {
        return recursionDesired;
    }

    public virtual IDnsMessage SetRecursionDesired(bool recursionDesired)
    {
        this.recursionDesired = recursionDesired;
        return this;
    }

    public virtual int Z()
    {
        return z;
    }

    public virtual IDnsMessage SetZ(int z)
    {
        this.z = (byte) (z & 7);
        return this;
    }

    public virtual int Count(DnsSection section)
    {
        return Count(SectionOrdinal(section));
    }

    private int Count(int section)
    {
        object records = SectionAt(section);
        if (records == null)
        {
            return 0;
        }
        if (records is IDnsRecord)
        {
            return 1;
        }

        List<IDnsRecord> recordList = (List<IDnsRecord>) records;
        return recordList.Count;
    }

    public virtual int Count()
    {
        int count = 0;

        for (int i = 0; i < SECTION_COUNT; i ++)
        {
            count += Count(i);
        }

        return count;
    }

    public virtual T RecordAt<T>(DnsSection section) where T : IDnsRecord
    {
        return RecordAt<T>(SectionOrdinal(section));
    }

    private T RecordAt<T>(int section) where T : IDnsRecord
    {
        object records = SectionAt(section);
        if (records == null)
        {
            return default;
        }

        if (records is IDnsRecord)
        {
            return CastRecord<T>(records);
        }

        List<IDnsRecord> recordList = (List<IDnsRecord>) records;
        if (recordList.Count == 0)
        {
            return default;
        }

        return CastRecord<T>(recordList[0]);
    }

    public virtual T RecordAt<T>(DnsSection section, int index) where T : IDnsRecord
    {
        return RecordAt<T>(SectionOrdinal(section), index);
    }

    private T RecordAt<T>(int section, int index) where T : IDnsRecord
    {
        object records = SectionAt(section);
        if (records == null)
        {
            throw new ArgumentOutOfRangeException(null, "index: " + index.ToString(CultureInfo.InvariantCulture) +
                                                        " (expected: none)");
        }

        if (records is IDnsRecord)
        {
            if (index == 0)
            {
                return CastRecord<T>(records);
            }
            else
            {
                throw new ArgumentOutOfRangeException(null, "index: " + index.ToString(CultureInfo.InvariantCulture) +
                                                            "' (expected: 0)");
            }
        }

        List<IDnsRecord> recordList = (List<IDnsRecord>) records;
        return CastRecord<T>(recordList[index]);
    }

    public virtual IDnsMessage SetRecord(DnsSection section, IDnsRecord record)
    {
        SetRecord(SectionOrdinal(section), record);
        return this;
    }

    private void SetRecord(int section, IDnsRecord record)
    {
        Clear(section);
        SetSection(section, CheckQuestion(section, record));
    }

    public virtual T SetRecord<T>(DnsSection section, int index, IDnsRecord record) where T : IDnsRecord
    {
        return SetRecord<T>(SectionOrdinal(section), index, record);
    }

    // CLR: List.set(index, element) returns the replaced element; the List<T> indexer does not, so the old element is
    // read first.
    private T SetRecord<T>(int section, int index, IDnsRecord record) where T : IDnsRecord
    {
        CheckQuestion(section, record);

        object records = SectionAt(section);
        if (records == null)
        {
            throw new ArgumentOutOfRangeException(null, "index: " + index.ToString(CultureInfo.InvariantCulture) +
                                                        " (expected: none)");
        }

        if (records is IDnsRecord)
        {
            if (index == 0)
            {
                SetSection(section, record);

                return CastRecord<T>(records);
            }
            else
            {
                throw new ArgumentOutOfRangeException(null, "index: " + index.ToString(CultureInfo.InvariantCulture) +
                                                            " (expected: 0)");
            }
        }

        List<IDnsRecord> recordList = (List<IDnsRecord>) records;
        IDnsRecord old = recordList[index];
        recordList[index] = record;
        return CastRecord<T>(old);
    }

    public virtual IDnsMessage AddRecord(DnsSection section, IDnsRecord record)
    {
        AddRecord(SectionOrdinal(section), record);
        return this;
    }

    private void AddRecord(int section, IDnsRecord record)
    {
        CheckQuestion(section, record);

        object records = SectionAt(section);
        if (records == null)
        {
            SetSection(section, record);
            return;
        }

        if (records is IDnsRecord)
        {
            List<IDnsRecord> recordList = NewRecordList();
            recordList.Add(CastRecord<IDnsRecord>(records));
            recordList.Add(record);
            SetSection(section, recordList);
            return;
        }

        List<IDnsRecord> recordList0 = (List<IDnsRecord>) records;
        recordList0.Add(record);
    }

    public virtual IDnsMessage AddRecord(DnsSection section, int index, IDnsRecord record)
    {
        AddRecord(SectionOrdinal(section), index, record);
        return this;
    }

    private void AddRecord(int section, int index, IDnsRecord record)
    {
        CheckQuestion(section, record);

        object records = SectionAt(section);
        if (records == null)
        {
            if (index != 0)
            {
                throw new ArgumentOutOfRangeException(null, "index: " + index.ToString(CultureInfo.InvariantCulture) +
                                                            " (expected: 0)");
            }

            SetSection(section, record);
            return;
        }

        if (records is IDnsRecord)
        {
            List<IDnsRecord> recordList;

            if (index == 0)
            {
                recordList = NewRecordList();
                recordList.Add(record);
                recordList.Add(CastRecord<IDnsRecord>(records));
            }
            else if (index == 1)
            {
                recordList = NewRecordList();
                recordList.Add(CastRecord<IDnsRecord>(records));
                recordList.Add(record);
            }
            else
            {
                throw new ArgumentOutOfRangeException(null, "index: " + index.ToString(CultureInfo.InvariantCulture) +
                                                            " (expected: 0 or 1)");
            }

            SetSection(section, recordList);
            return;
        }

        List<IDnsRecord> recordList0 = (List<IDnsRecord>) records;
        recordList0.Insert(index, record);
    }

    public virtual T RemoveRecord<T>(DnsSection section, int index) where T : IDnsRecord
    {
        return RemoveRecord<T>(SectionOrdinal(section), index);
    }

    // CLR: List.remove(int) returns the removed element; List<T>.RemoveAt does not, so it is read first.
    private T RemoveRecord<T>(int section, int index) where T : IDnsRecord
    {
        object records = SectionAt(section);
        if (records == null)
        {
            throw new ArgumentOutOfRangeException(null, "index: " + index.ToString(CultureInfo.InvariantCulture) +
                                                        " (expected: none)");
        }

        if (records is IDnsRecord)
        {
            if (index != 0)
            {
                throw new ArgumentOutOfRangeException(null, "index: " + index.ToString(CultureInfo.InvariantCulture) +
                                                            " (expected: 0)");
            }

            T record = CastRecord<T>(records);
            SetSection(section, null);
            return record;
        }

        List<IDnsRecord> recordList = (List<IDnsRecord>) records;
        IDnsRecord removed = recordList[index];
        recordList.RemoveAt(index);

        return CastRecord<T>(removed);
    }

    public virtual IDnsMessage Clear(DnsSection section)
    {
        Clear(SectionOrdinal(section));
        return this;
    }

    public virtual IDnsMessage Clear()
    {
        for (int i = 0; i < SECTION_COUNT; i ++)
        {
            Clear(i);
        }

        return this;
    }

    private void Clear(int section)
    {
        object recordOrList = SectionAt(section);
        SetSection(section, null);

        if (recordOrList is IReferenceCounted)
        {
            ((IReferenceCounted) recordOrList).Release();
        }
        else if (recordOrList is List<IDnsRecord>)
        {
            List<IDnsRecord> list = (List<IDnsRecord>) recordOrList;
            if (list.Count != 0)
            {
                foreach (object r in list)
                {
                    ReferenceCountUtil.Release(r);
                }
            }
        }
    }

    public override IDnsMessage Touch()
    {
        return (IDnsMessage) base.Touch();
    }

    public override IDnsMessage Touch(object hint)
    {
        if (leak != null)
        {
            leak.Record(hint);
        }

        return this;
    }

    public override IDnsMessage Retain()
    {
        return (IDnsMessage) base.Retain();
    }

    public override IDnsMessage Retain(int increment)
    {
        return (IDnsMessage) base.Retain(increment);
    }

    // CLR: the Java assert is Debug.Assert, evaluated in Debug builds as Java asserts are with -ea.
    protected override void Deallocate()
    {
        Clear();

        IResourceLeakTracker<IDnsMessage> leak = this.leak;
        if (leak != null)
        {
            bool closed = leak.Close(this);
            Debug.Assert(closed);
        }
    }

    public override bool Equals(object obj)
    {
        if (this == obj)
        {
            return true;
        }

        if (!(obj is IDnsMessage))
        {
            return false;
        }

        IDnsMessage that = (IDnsMessage) obj;
        if (Id() != that.Id())
        {
            return false;
        }

        if (this is IDnsQuery)
        {
            if (!(that is IDnsQuery))
            {
                return false;
            }
        }
        else if (that is IDnsQuery)
        {
            return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        return Id() * 31 + (this is IDnsQuery? 0 : 1);
    }

    // CLR: java.lang.Error for an impossible section number is UnreachableException, "thrown when the program executes
    // an instruction that was thought to be unreachable"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.unreachableexception?view=net-10.0), as in
    // codec-base's LengthFieldPrepender.
    private object SectionAt(int section)
    {
        switch (section)
        {
        case 0:
            return questions;
        case 1:
            return answers;
        case 2:
            return authorities;
        case 3:
            return additionals;
        default:
            throw new UnreachableException("Unexpected section number: " + section.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void SetSection(int section, object value)
    {
        switch (section)
        {
        case 0:
            questions = value;
            return;
        case 1:
            answers = value;
            return;
        case 2:
            authorities = value;
            return;
        case 3:
            additionals = value;
            return;
        default:
            throw new UnreachableException("Unexpected section number: " + section.ToString(CultureInfo.InvariantCulture));
        }
    }

    // CLR: A C# enum value cannot be null, so the checkNotNull of the source has nothing to check.
    private static int SectionOrdinal(DnsSection section)
    {
        return (int) section;
    }

    private static IDnsRecord CheckQuestion(int section, IDnsRecord record)
    {
        if (section == SECTION_QUESTION && !(CheckNotNull(record, "record") is IDnsQuestion))
        {
            throw new ArgumentException(
                    "record: " + record + " (expected: " + StringUtil.SimpleClassName(typeof(IDnsQuestion)) + ')');
        }

        return record;
    }

    // CLR: the unchecked Java cast is erased and fails only where the caller uses the result; a C# cast to the type
    // argument is checked here and throws InvalidCastException for a mismatched T.
    private static T CastRecord<T>(object record)
    {
        return (T) record;
    }

    private static List<IDnsRecord> NewRecordList()
    {
        return new List<IDnsRecord>(2);
    }
}
