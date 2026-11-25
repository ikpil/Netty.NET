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

/*
 * Copyright 2014 Twitter, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Collections.Generic;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the package-private final class is internal sealed; List is IList. add(CharSequence, CharSequence) narrows the
// return type with a covariant override of DefaultHeaders.Add (C# 9 covariant returns,
// https://github.com/dotnet/csharplang/blob/main/proposals/csharp-9.0/covariant-returns.md, recorded in
// docs/codec-http-porting.md).
internal sealed class TestHeaderListener : DefaultHttp2Headers
{

    private readonly IList<HpackHeaderField> headers;

    internal TestHeaderListener(IList<HpackHeaderField> headers)
    {
        this.headers = headers;
    }

    public override TestHeaderListener Add(ICharSequence name, ICharSequence value)
    {
        headers.Add(new HpackHeaderField(name, value));
        return this;
    }
}
