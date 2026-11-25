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
using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Interface for all Objects that could be encoded/decoded using HttpPostRequestEncoder/Decoder
 */
// CLR: the Java interface InterfaceHttpData takes the I prefix (owner rule). Comparable<InterfaceHttpData> is
// IComparable<IInterfaceHttpData> (compareTo is the BCL CompareTo). The nested enum HttpDataType stays nested, so the
// implementing classes, which do not inherit an interface's nested types by simple name, write
// IInterfaceHttpData.HttpDataType. retain(), retain(int), touch() and touch(Object) are re-declared only to narrow the
// return type, so they are inherited from IReferenceCounted, not repeated (owner decision 2026-11-19,
// docs/transport-porting.md, Decisions); the classes narrow them with covariant override returns.
public interface IInterfaceHttpData : IComparable<IInterfaceHttpData>, IReferenceCounted
{
    enum HttpDataType
    {
        Attribute, FileUpload, InternalAttribute
    }

    /**
     * Returns the name of this InterfaceHttpData.
     */
    string GetName();

    /**
     *
     * @return The HttpDataType
     */
    HttpDataType GetHttpDataType();
}
