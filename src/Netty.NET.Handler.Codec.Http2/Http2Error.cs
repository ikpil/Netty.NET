/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

namespace Netty.NET.Handler.Codec.Http2;

/**
 * All error codes identified by the HTTP/2 spec.
 */
// CLR: a Java enum with a constructor, a field and methods is a sealed class with static readonly instances, as
// codec-base Base64Dialect and codec-http HttpStatusClass (docs/codec-base-porting.md, docs/codec-http-porting.md):
// "You can't define a method inside the definition of an enumeration type"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum). ToString() returns the
// constant name as Enum.toString() does; the implicit values() is the private VALUES array that the static
// initializer walks (no other caller), and valueOf(String) is not added (no caller). Instances are compared by
// reference, as Java compares enum constants.
public sealed class Http2Error
{
    public static readonly Http2Error NO_ERROR = new Http2Error(nameof(NO_ERROR), 0x0);
    public static readonly Http2Error PROTOCOL_ERROR = new Http2Error(nameof(PROTOCOL_ERROR), 0x1);
    public static readonly Http2Error INTERNAL_ERROR = new Http2Error(nameof(INTERNAL_ERROR), 0x2);
    public static readonly Http2Error FLOW_CONTROL_ERROR = new Http2Error(nameof(FLOW_CONTROL_ERROR), 0x3);
    public static readonly Http2Error SETTINGS_TIMEOUT = new Http2Error(nameof(SETTINGS_TIMEOUT), 0x4);
    public static readonly Http2Error STREAM_CLOSED = new Http2Error(nameof(STREAM_CLOSED), 0x5);
    public static readonly Http2Error FRAME_SIZE_ERROR = new Http2Error(nameof(FRAME_SIZE_ERROR), 0x6);
    public static readonly Http2Error REFUSED_STREAM = new Http2Error(nameof(REFUSED_STREAM), 0x7);
    public static readonly Http2Error CANCEL = new Http2Error(nameof(CANCEL), 0x8);
    public static readonly Http2Error COMPRESSION_ERROR = new Http2Error(nameof(COMPRESSION_ERROR), 0x9);
    public static readonly Http2Error CONNECT_ERROR = new Http2Error(nameof(CONNECT_ERROR), 0xA);
    public static readonly Http2Error ENHANCE_YOUR_CALM = new Http2Error(nameof(ENHANCE_YOUR_CALM), 0xB);
    public static readonly Http2Error INADEQUATE_SECURITY = new Http2Error(nameof(INADEQUATE_SECURITY), 0xC);
    public static readonly Http2Error HTTP_1_1_REQUIRED = new Http2Error(nameof(HTTP_1_1_REQUIRED), 0xD);

    private static readonly Http2Error[] VALUES =
    {
        NO_ERROR, PROTOCOL_ERROR, INTERNAL_ERROR, FLOW_CONTROL_ERROR, SETTINGS_TIMEOUT, STREAM_CLOSED,
        FRAME_SIZE_ERROR, REFUSED_STREAM, CANCEL, COMPRESSION_ERROR, CONNECT_ERROR, ENHANCE_YOUR_CALM,
        INADEQUATE_SECURITY, HTTP_1_1_REQUIRED
    };

    private readonly string name;
    private readonly long code;
    private static readonly Http2Error[] INT_TO_ENUM_MAP;
    static Http2Error()
    {
        Http2Error[] errors = VALUES;
        Http2Error[] map = new Http2Error[errors.Length];

        foreach (Http2Error error in errors)
        {
            map[(int) error.Code()] = error;
        }

        INT_TO_ENUM_MAP = map;
    }

    private Http2Error(string name, long code)
    {
        this.name = name;
        this.code = code;
    }

    /**
     * Gets the code for this error used on the wire.
     */
    public long Code()
    {
        return code;
    }

    public static Http2Error ValueOf(long value)
    {
        return value >= INT_TO_ENUM_MAP.Length || value < 0 ? null : INT_TO_ENUM_MAP[(int) value];
    }

    public override string ToString()
    {
        return name;
    }
}
