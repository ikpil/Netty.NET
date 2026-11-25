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
using System.Collections.ObjectModel;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;

/**
 * A WebSocket Extension data from the <tt>Sec-WebSocket-Extensions</tt> header.
 *
 * See <tt>io.netty.handler.codec.http.HttpHeaders.Names.SEC_WEBSOCKET_EXTENSIONS</tt>.
 */
// CLR: Map<String, String> is IDictionary<string, string>. Collections.unmodifiableMap is ReadOnlyDictionary, "a
// wrapper around the specified dictionary" whose IDictionary mutators "Throw a NotSupportedException exception in all
// cases"
// (https://learn.microsoft.com/en-us/dotnet/api/system.collections.objectmodel.readonlydictionary-2?view=net-10.0), the
// counterpart of UnsupportedOperationException (docs/codec-base-porting.md); like the Java view it enumerates in the
// order of the wrapped map. Map.get(key) returns null for a missing key, where the IDictionary indexer throws
// KeyNotFoundException and TryGetValue returns false. checkNotNull is the Common ObjectUtil.CheckNotNull
// (ArgumentNullException).
public sealed class WebSocketExtensionData
{

    private readonly string name;
    private readonly IDictionary<string, string> parameters;

    public WebSocketExtensionData(string name, IDictionary<string, string> parameters)
    {
        this.name = ObjectUtil.CheckNotNull(name, "name");
        this.parameters = new ReadOnlyDictionary<string, string>(
                ObjectUtil.CheckNotNull(parameters, "parameters"));
    }

    /**
     * @return the extension name.
     */
    public string Name()
    {
        return name;
    }

    /**
     * @return the extension optional parameters.
     */
    public IDictionary<string, string> Parameters()
    {
        return parameters;
    }
}
