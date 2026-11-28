/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Handler.Codec.Http.Multipart;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

// CLR: AssertJ assertThatThrownBy(...).isInstanceOf(IllegalArgumentException.class).hasMessageContaining(text) is
// Assert.ThrowsAny<ArgumentException> (isInstanceOf admits subclasses) with Assert.Contains on the message. The
// test methods with package access (no modifier in JUnit 5) are public, as xUnit requires.
public class MemoryFileUploadTest
{

    [Fact]
    public void TestMemoryFileUploadEquals()
    {
        MemoryFileUpload f1 =
                new MemoryFileUpload("m1", "m1", "application/json", null, null, 100);
        Assert.Equal(f1, f1);
    }

    [Theory]
    [InlineData((byte) 0x00)]
    [InlineData(HttpConstants.CR)]
    [InlineData(HttpConstants.LF)]
    [InlineData((byte) 0x19)]
    [InlineData(HttpConstants.DEL)]
    [InlineData(HttpConstants.DOUBLE_QUOTE)]
    [InlineData(HttpConstants.BACKSLASH)]
    public void FilenameCannotContainIllegalCharacters(byte illegal)
    {
        AssertIllegalFilename(((char) illegal) + "f");
        AssertIllegalFilename("f" + ((char) illegal) + "f");
        AssertIllegalFilename("f" + ((char) illegal));
    }

    private static void AssertIllegalFilename(string filename)
    {
        ArgumentException e = Assert.ThrowsAny<ArgumentException>(
                () => new MemoryFileUpload("f", filename, "plain/text", null, null, 0));
        Assert.Contains("Illegal filename character", e.Message);
    }

    [Theory]
    [InlineData((byte) 0x00)]
    [InlineData(HttpConstants.CR)]
    [InlineData(HttpConstants.LF)]
    [InlineData((byte) 0x19)]
    [InlineData(HttpConstants.DEL)]
    public void ContentTypeCannotContainIllegalCharacters(byte illegal)
    {
        AssertIllegalContentType(((char) illegal) + "text/plain");
        AssertIllegalContentType("text/plain" + ((char) illegal) + " charset=\"us-ascii\"");
        AssertIllegalContentType("text/plain" + ((char) illegal));
    }

    [Fact]
    public void ContentTypeCannotStartWithSpaceOrTabCharacter()
    {
        AssertIllegalContentType(" text/plain");
        AssertIllegalContentType("\ttext/plain");
    }

    private static void AssertIllegalContentType(string contentType)
    {
        ArgumentException e = Assert.ThrowsAny<ArgumentException>(
                () => new MemoryFileUpload("f", "f.txt", contentType, null, null, 0));
        Assert.Contains("Illegal Content-Type character", e.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("text")]
    [InlineData("text;plain")]
    [InlineData("text;plain/octet-stream")]
    [InlineData("text; charset=\"utf/8\"; charset=us-ascii")]
    [InlineData("text/")]
    [InlineData("text/;")]
    [InlineData("/plain")]
    public void ContentTypeCannotHaveMalformedGrammar(string contentType)
    {
        AssertMalformedContentType(contentType);
    }

    private static void AssertMalformedContentType(string contentType)
    {
        ArgumentException e = Assert.ThrowsAny<ArgumentException>(
                () => new MemoryFileUpload("f", "f.txt", contentType, null, null, 0));
        Assert.Contains("Malformed content type value", e.Message);
    }

    [Fact]
    public void ContentTypeAllowsWhitespaceQuotesAndObsText()
    {
        AssertValidContentType("text/plain; charset=\"us-ascii\"");
        AssertValidContentType("text/plain;\tcharset=us-ascii");
        AssertValidContentType("text/plain; name=\"é\"");
        AssertValidContentType("t/p;");
        AssertValidContentType("t/p; ");
        AssertValidContentType("t/p");
    }

    private static void AssertValidContentType(string contentType)
    {
        Assert.Equal(contentType, new MemoryFileUpload("f", "f.txt", contentType, null, null, 0).GetContentType());
    }
}
