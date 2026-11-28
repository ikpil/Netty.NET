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

namespace Netty.NET.Handler.Codec.Stomp.Tests;

// CLR: Java passes the Strings "Foo" and foo.toString() as CharSequence names; C# string is not an ICharSequence,
// so they are the Common StringCharSequence.
public class StompHeadersTest
{
    [Fact]
    public void TestHeadersCaseSensitive()
    {
        DefaultStompHeaders headers = new DefaultStompHeaders();
        AsciiString foo = new AsciiString("foo");
        AsciiString value = new AsciiString("value");
        headers.Add(foo, value);
        Assert.Null(headers.Get(new StringCharSequence("Foo")));
        Assert.Equal(value, headers.Get(foo));
        Assert.Equal(value, headers.Get(new StringCharSequence(foo.ToString())));
    }
}
