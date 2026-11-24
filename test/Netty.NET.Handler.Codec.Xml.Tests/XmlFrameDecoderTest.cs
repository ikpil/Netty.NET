/*
 * Copyright 2013 The Netty Project
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
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Collections;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Xml;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Xml.Tests;

// CLR: CharsetUtil.UTF_8 is Encoding.UTF8, each anonymous Executable passed to assertThrows is a lambda and
// IllegalArgumentException is ArgumentException (docs/codec-base-porting.md). Arrays.asList is Collectives.AsList and
// Collections.singletonList is Collectives.SingletonList (src/Netty.NET.Common/Collections/Collectives.cs, as codec-base
// EmptyHeadersTest uses them), so the List<String> parameter of the helper is IReadOnlyList<string>, the type both
// return; the helper only iterates it. Foo.class is typeof(Foo) and cause.getClass() is cause.GetType().
public class XmlFrameDecoderTest
{

    private readonly IReadOnlyList<string> xmlSamples;

    public XmlFrameDecoderTest()
    {
        xmlSamples = Collectives.AsList(
                Sample("01"), Sample("02"), Sample("03"),
                Sample("04"), Sample("05"), Sample("06")
        );
    }

    [Fact]
    public void TestConstructorWithIllegalArgs01()
    {
        Assert.Throws<ArgumentException>(() => new XmlFrameDecoder(0));
    }

    [Fact]
    public void TestConstructorWithIllegalArgs02()
    {
        Assert.Throws<ArgumentException>(() => new XmlFrameDecoder(-23));
    }

    [Fact]
    public void TestDecodeWithFrameExceedingMaxLength()
    {
        XmlFrameDecoder decoder = new XmlFrameDecoder(3);
        EmbeddedChannel ch = new EmbeddedChannel(decoder);
        Assert.Throws<TooLongFrameException>(
                () => ch.WriteInbound(Unpooled.CopiedBuffer("<v/>", Encoding.UTF8)));
    }

    [Fact]
    public void TestDecodeWithInvalidInput()
    {
        XmlFrameDecoder decoder = new XmlFrameDecoder(1048576);
        EmbeddedChannel ch = new EmbeddedChannel(decoder);
        Assert.Throws<CorruptedFrameException>(
                () => ch.WriteInbound(Unpooled.CopiedBuffer("invalid XML", Encoding.UTF8)));
    }

    [Fact]
    public void TestDecodeWithInvalidContentBeforeXml()
    {
        XmlFrameDecoder decoder = new XmlFrameDecoder(1048576);
        EmbeddedChannel ch = new EmbeddedChannel(decoder);
        Assert.Throws<CorruptedFrameException>(
                () => ch.WriteInbound(Unpooled.CopiedBuffer("invalid XML<foo/>", Encoding.UTF8)));
    }

    [Fact]
    public void TestDecodeShortValidXml()
    {
        TestDecodeWithXml("<xxx/>", "<xxx/>");
    }

    [Fact]
    public void TestDecodeShortValidXmlWithLeadingWhitespace01()
    {
        TestDecodeWithXml("   <xxx/>", "<xxx/>");
    }

    [Fact]
    public void TestDecodeShortValidXmlWithLeadingWhitespace02()
    {
        TestDecodeWithXml("  \n\r \t<xxx/>\t", "<xxx/>");
    }

    [Fact]
    public void TestDecodeShortValidXmlWithLeadingWhitespace02AndTrailingGarbage()
    {
        TestDecodeWithXml("  \n\r \t<xxx/>\ttrash", "<xxx/>", typeof(CorruptedFrameException));
    }

    [Fact]
    public void TestDecodeInvalidXml()
    {
        TestDecodeWithXml("<a></", new object[0]);
        TestDecodeWithXml("<a></a", new object[0]);
    }

    [Fact]
    public void TestDecodeInvalidNestedClosingTag()
    {
        XmlFrameDecoder decoder = new XmlFrameDecoder(1048576);
        EmbeddedChannel ch = new EmbeddedChannel(decoder);
        Assert.Throws<CorruptedFrameException>(
                () => ch.WriteInbound(Unpooled.CopiedBuffer("<a></</a>", Encoding.UTF8)));
        ch.FinishAndReleaseAll();
    }

    [Fact]
    public void TestDecodeInvalidRepeatedClosingTags()
    {
        XmlFrameDecoder decoder = new XmlFrameDecoder(1048576);
        EmbeddedChannel ch = new EmbeddedChannel(decoder);
        ch.WriteInbound(Unpooled.CopiedBuffer("</", Encoding.UTF8));
        Assert.Throws<CorruptedFrameException>(
                () => ch.WriteInbound(Unpooled.CopiedBuffer("</", Encoding.UTF8)));
        ch.FinishAndReleaseAll();
    }

    [Fact]
    public void TestDecodeWithCDATABlock()
    {
        string xml = "<book>" +
                "<![CDATA[K&R, a.k.a. Kernighan & Ritchie]]>" +
                "</book>";
        TestDecodeWithXml(xml, xml);
    }

    [Fact]
    public void TestDecodeWithCDATABlockContainingNestedUnbalancedXml()
    {
        // <br> isn't closed, also <a> should have been </a>
        string xml = "<info>" +
                "<![CDATA[Copyright 2012-2013,<br><a href=\"http://www.acme.com\">ACME Inc.<a>]]>" +
                "</info>";
        TestDecodeWithXml(xml, xml);
    }

    [Fact]
    public void TestDecodeWithCDATABlockContainingClosingTagThenOpeningBracket()
    {
        string xml = "<root>" +
                "<![CDATA[close </a then open <b]]>" +
                "</root>";
        TestDecodeWithXml(xml, xml);
    }

    [Fact]
    public void TestDecodeWithCommentContainingClosingTagThenOpeningBracket()
    {
        string xml = "<root>" +
                "<!-- close </a then open <b -->" +
                "</root>";
        TestDecodeWithXml(xml, xml);
    }

    [Fact]
    public void TestDecodeWithCommentContainingClosingTag()
    {
        string xml = "<root>" +
                "<!-- close </a -->" +
                "</root>";
        TestDecodeWithXml(xml, xml);
    }

    [Fact]
    public void TestDecodeWithProcessingInstructionContainingClosingTagThenOpeningBracket()
    {
        string xml = "<root>" +
                "<?pi close </a then open <b ?>" +
                "</root>";
        TestDecodeWithXml(xml, xml);
    }

    [Fact]
    public void TestDecodeWithProcessingInstructionContainingClosingTag()
    {
        string xml = "<root>" +
                "<?pi close </a ?>" +
                "</root>";
        TestDecodeWithXml(xml, xml);
    }

    [Fact]
    public void TestDecodeWithMultipleMessages()
    {
        string input = "<root xmlns=\"http://www.acme.com/acme\" status=\"loginok\" " +
                "timestamp=\"1362410583776\"/>\n\n" +
                "<root xmlns=\"http://www.acme.com/acme\" status=\"start\" time=\"0\" " +
                "timestamp=\"1362410584794\">\n<child active=\"1\" status=\"started\" id=\"935449\" " +
                "msgnr=\"2\"/>\n</root>" +
                "<root xmlns=\"http://www.acme.com/acme\" status=\"logout\" timestamp=\"1362410584795\"/>";
        string frame1 = "<root xmlns=\"http://www.acme.com/acme\" status=\"loginok\" " +
                "timestamp=\"1362410583776\"/>";
        string frame2 = "<root xmlns=\"http://www.acme.com/acme\" status=\"start\" time=\"0\" " +
                "timestamp=\"1362410584794\">\n<child active=\"1\" status=\"started\" id=\"935449\" " +
                "msgnr=\"2\"/>\n</root>";
        string frame3 = "<root xmlns=\"http://www.acme.com/acme\" status=\"logout\" " +
                "timestamp=\"1362410584795\"/>";
        TestDecodeWithXml(input, frame1, frame2, frame3);
    }

    [Fact]
    public void TestFraming()
    {
        TestDecodeWithXml(Collectives.AsList("<abc", ">123</a", "bc>"), "<abc>123</abc>");
    }

    [Fact]
    public void TestFramingWithSplitClosingTag()
    {
        TestDecodeWithXml(Collectives.AsList("<abc>", "123</", "abc>"), "<abc>123</abc>");
    }

    [Fact]
    public void TestFramingWithCommentContainingClosingTagThenOpeningBracket()
    {
        string frame = "<root><!-- close </a then open <b --></root>";
        TestDecodeWithXml(Collectives.AsList("<root><!-- close </", "a then open <b --></root>"), frame);
    }

    [Fact]
    public void TestFramingWithProcessingInstructionContainingClosingTagThenOpeningBracket()
    {
        string frame = "<root><?pi close </a then open <b ?></root>";
        TestDecodeWithXml(Collectives.AsList("<root><?pi close </", "a then open <b ?></root>"), frame);
    }

    [Fact]
    public void TestDecodeWithSampleXml()
    {
        foreach (string xmlSample in xmlSamples)
        {
            TestDecodeWithXml(xmlSample, xmlSample);
        }
    }

    [Fact]
    public void TestFramingWithCommentStartSplitAcrossChunks()
    {
        string frame = "<root><!-- comment --></root>";
        // split right in the middle of the "<!--" start marker, so the decoder cannot
        // tell yet (with only "<!" available) whether this is a comment or CDATA block.
        TestDecodeWithXml(Collectives.AsList("<root><!", "-- comment --></root>"), frame);
    }

    [Fact]
    public void TestFramingWithCDATAStartSplitAcrossChunks()
    {
        string frame = "<root><![CDATA[hello]]></root>";
        // split in the middle of the "<![CDATA[" start marker.
        TestDecodeWithXml(Collectives.AsList("<root><![CDA", "TA[hello]]></root>"), frame);
    }

    [Fact]
    public void TestFramingWithOpeningBracketAsLastByteOfChunk()
    {
        // '<' arrives with nothing after it yet, so the decoder cannot peek ahead to
        // classify it until the next chunk arrives.
        TestDecodeWithXml(Collectives.AsList("<", "abc/>"), "<abc/>");
    }

    [Fact]
    public void TestFramingWithSelfClosingSlashAsLastByteOfChunk()
    {
        // '/' arrives with nothing after it yet, so the decoder cannot peek ahead to see
        // whether it is immediately followed by '>' until the next chunk arrives.
        TestDecodeWithXml(Collectives.AsList("<abc", "/", ">"), "<abc/>");
    }

    // CLR: assertTimeoutPreemptively executes the executable "in a different thread than that of the calling code"
    // (https://docs.junit.org/5.10.0/api/org.junit.jupiter.api/org/junit/jupiter/api/Assertions.html) and fails when the
    // timeout passes. The executable runs in Task.Run and the test awaits it with WaitAsync(TimeSpan, CancellationToken),
    // whose task "should be faulted with a TimeoutException if it hasn't otherwise completed"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync?view=net-10.0), so a timeout
    // fails the test with that TimeoutException; the CLR cannot abort the thread, so the test method is async and does
    // not block a thread on it (as codec-compression's Bzip2DecoderTest does, docs/codec-compression-porting.md).
    [Fact]
    public async Task TestDecodeDoesNotHangOnTrickledUnbalancedElement()
    {
        // Regression test: a peer that opens an element ("<a>") and then trickles
        // non-markup content one byte at a time, without ever closing the element, used
        // to force XmlFrameDecoder to rescan the whole accumulated buffer from the start
        // on every single decode() call, making the total work quadratic in the number
        // of bytes received. If the fix regresses, this test will time out instead of
        // failing an assertion.
        int contentBytes = 200_000;
        XmlFrameDecoder decoder = new XmlFrameDecoder(contentBytes + 1024);
        EmbeddedChannel ch = new EmbeddedChannel(decoder);
        try
        {
            Task execution = Task.Run(() =>
            {
                ch.WriteInbound(Unpooled.CopiedBuffer("<a>", Encoding.UTF8));
                for (int i = 0; i < contentBytes; i++)
                {
                    ch.WriteInbound(Unpooled.CopiedBuffer("x", Encoding.UTF8));
                }
            }, TestContext.Current.CancellationToken);
            await execution.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }
    }

    [Fact]
    public void LookAheadBufferForUnknownMarkupDeclarationsMustNotIgnoreFollowingTags()
    {
        TestDecodeWithXml("<a><!x></a>", "<a><!x></a>");
        TestDecodeWithXml("<aa><!x></aa>", "<aa><!x></aa>");
    }

    // CLR: String.substring(beginIndex, endIndex) "begins at the specified beginIndex and extends to the character at
    // index endIndex - 1" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html), while
    // Substring(startIndex, length) takes "The number of characters in the substring"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.string.substring?view=net-10.0); with beginIndex 0 both
    // arguments are 59. substring(59) is Substring(59).
    [Fact]
    public void MustDecodeRemainingDataAfterCumulationCompaction()
    {
        // Regression test for the retained *absolute* 'length' field. The chunk sizes are
        // chosen so that ByteToMessageDecoder's cumulation buffer is re-based in between:
        //  - chunk 1 (59 bytes) becomes the cumulation buffer itself, capacity == 59;
        //  - chunk 2 (5 bytes) is appended, growing the capacity to exactly 64. The
        //    60-byte element is emitted and the stray "</b>" is left unread, which drives
        //    openBracketsCount to -1 while 'length' keeps the absolute value 64;
        //  - channelReadComplete() then calls discardSomeReadBytes(): readerIndex (60) is
        //    >= capacity/2 (32), so the 4 unread bytes move down to offset 0 and
        //    readerIndex becomes 0 -- 'length' is now stale by 60 bytes;
        //  - chunk 3 ("<c") brings openBracketsCount back to 0 via the '<' + start-char
        //    increment, without any '>' being scanned, so the stale 'length' decides the
        //    frame boundary: "</b><c" is emitted instead of "</b>", swallowing the start
        //    of the next element, and chunk 4 ("/>") then looks like leading garbage.
        StringBuilder content = new StringBuilder();
        for (int i = 0; i < 53; i++)
        {
            content.Append('x');
        }
        string frame = "<a>" + content + "</a>";
        Assert.Equal(60, frame.Length);

        TestDecodeWithXml(Collectives.AsList(
                frame.Substring(0, 59),       // the element minus its final '>'
                frame.Substring(59) + "</b>", // completes it, plus a stray closing tag
                "<c",
                "/>"),
            frame, "</b>", "<c/>");
    }

    private static void TestDecodeWithXml(IReadOnlyList<string> xmlFrames, params object[] expected)
    {
        EmbeddedChannel ch = new EmbeddedChannel(new XmlFrameDecoder(1048576));
        Exception cause = null;
        try
        {
            foreach (string xmlFrame in xmlFrames)
            {
                ch.WriteInbound(Unpooled.CopiedBuffer(xmlFrame, Encoding.UTF8));
            }
        }
        catch (Exception e)
        {
            cause = e;
        }
        List<object> actual = new List<object>();
        for (;;)
        {
            ByteBuf buf = ch.ReadInbound<ByteBuf>();
            if (buf == null)
            {
                break;
            }
            actual.Add(buf.ToString(Encoding.UTF8));
            buf.Release();
        }

        if (cause != null)
        {
            actual.Add(cause.GetType());
        }

        try
        {
            List<object> expectedList = new List<object>();
            expectedList.AddRange(expected);
            Assert.Equal(expectedList, actual);
        }
        finally
        {
            ch.Finish();
        }
    }

    private static void TestDecodeWithXml(string xml, params object[] expected)
    {
        TestDecodeWithXml(Collectives.SingletonList(xml), expected);
    }

    // CLR: getClass().getClassLoader().getResource(path) is the manifest resource of the test assembly with the same
    // logical name (the csproj embeds codec-xml's sample-0N.xml as io/netty/handler/codec/xml/sample-0N.xml), read with
    // Assembly.GetManifestResourceStream, which returns "The manifest resource; or null if no resources were specified
    // during compilation or if the resource is not visible to the caller"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.getmanifestresourcestream?view=net-10.0),
    // so null still throws the IllegalArgumentException (ArgumentException). Files.readAllBytes is Stream.CopyTo a
    // MemoryStream ("Reads the bytes from the current stream and writes them to another stream",
    // https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.copyto?view=net-10.0). StandardCharsets.UTF_8.decode
    // "always replaces malformed-input and unmappable-character sequences"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/Charset.html) and Encoding.UTF8
    // "uses replacement fallback" (https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.utf8?view=net-10.0),
    // so the bytes decode with Encoding.UTF8.GetString. The URISyntaxException/IOException declarations have no C#
    // counterpart.
    private string Sample(string number)
    {
        string path = "io/netty/handler/codec/xml/sample-" + number + ".xml";
        using Stream url = typeof(XmlFrameDecoderTest).Assembly.GetManifestResourceStream(path);
        if (url == null)
        {
            throw new ArgumentException("file not found: " + path);
        }
        using MemoryStream buf = new MemoryStream();
        url.CopyTo(buf);
        return Encoding.UTF8.GetString(buf.ToArray());
    }
}
