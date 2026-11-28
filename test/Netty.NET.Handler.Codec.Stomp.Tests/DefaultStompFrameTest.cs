/*
 * Copyright 2018 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Stomp.Tests;

// CLR: Java passes the String "localhost" as a CharSequence; C# string is not an ICharSequence, so the value is
// the Common StringCharSequence, whose Equals matches String.equals. xUnit2013 is suppressed because
// assertEquals(n, size()) keeps the source assertions, as in codec-base.
#pragma warning disable xUnit2013 // assertEquals(n, size()) keeps the source assertions.
public class DefaultStompFrameTest
{

    [Fact]
    public void TestStompFrameCopy()
    {
        IStompFrame sourceFrame = new DefaultStompFrame(StompCommand.CONNECT);

        Assert.True(sourceFrame.Headers().IsEmpty());

        sourceFrame.Headers().Set(IStompHeaders.HOST, new StringCharSequence("localhost"));

        // CLR: IStompFrame inherits copy() from IByteBufHolder instead of narrowing it, so the interface-typed
        // result is cast to IStompFrame.
        IStompFrame copyFrame = (IStompFrame) sourceFrame.Copy();

        Assert.Equal(sourceFrame.Headers(), copyFrame.Headers());
        Assert.Equal(sourceFrame.Content, copyFrame.Content);

        AsciiString copyHeaderName = new AsciiString("foo");
        AsciiString copyHeaderValue = new AsciiString("bar");
        copyFrame.Headers().Set(copyHeaderName, copyHeaderValue);

        Assert.False(sourceFrame.Headers().Contains(copyHeaderName, copyHeaderValue));
        Assert.True(copyFrame.Headers().Contains(copyHeaderName, copyHeaderValue));

        Assert.Equal(1, sourceFrame.Headers().Size());
        Assert.Equal(2, copyFrame.Headers().Size());
        Assert.NotEqual(sourceFrame.Headers(), copyFrame.Headers());

        Assert.True(sourceFrame.Release());
        Assert.True(copyFrame.Release());
    }
}
