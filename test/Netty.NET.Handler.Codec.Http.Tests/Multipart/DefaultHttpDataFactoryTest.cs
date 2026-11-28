/*
 * Copyright 2017 The Netty Project
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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.Multipart;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

// CLR: @AfterEach cleanupFactory() is IDisposable.Dispose(); the lifecycle methods are private, because xUnit
// flags public non-test methods of a test class (xUnit1013, https://xunit.net/xunit.analyzers/rules/xUnit1013).
// HttpRequest is IHttpRequest, UTF_8 Encoding.UTF8, and DiskAttribute.class.cast(x) a C# cast;
// getBaseDirectory()/deleteOnExit() are protected internal in the port (see AbstractDiskHttpData).
// HttpHeaderValues.IDENTITY and HttpPostBodyUtil.DEFAULT_TEXT_CONTENT_TYPE are written in full.
// hashCode()/equals() are GetHashCode()/Equals() and refCnt() RefCnt.
public class DefaultHttpDataFactoryTest : IDisposable
{
    // req1 equals req2
    private static readonly IHttpRequest req1 = new DefaultHttpRequest(HTTP_1_1, POST, "/form");
    private static readonly IHttpRequest req2 = new DefaultHttpRequest(HTTP_1_1, POST, "/form");

    private DefaultHttpDataFactory factory;

    public DefaultHttpDataFactoryTest()
    {
        AssertReq1EqualsReq2();
        SetupFactory();
    }

    private static void AssertReq1EqualsReq2()
    {
        // Before doing anything, assert that the requests are equal
        Assert.Equal(req1.GetHashCode(), req2.GetHashCode());
        Assert.True(req1.Equals(req2));
    }

    private void SetupFactory()
    {
        factory = new DefaultHttpDataFactory();
    }

    public void Dispose()
    {
        CleanupFactory();
    }

    private void CleanupFactory()
    {
        factory.CleanAllHttpData();
    }

    [Fact]
    public void CustomBaseDirAndDeleteOnExit()
    {
        DefaultHttpDataFactory defaultHttpDataFactory = new DefaultHttpDataFactory(true);
        string dir = "target/DefaultHttpDataFactoryTest/customBaseDirAndDeleteOnExit";
        defaultHttpDataFactory.SetBaseDir(dir);
        defaultHttpDataFactory.SetDeleteOnExit(true);
        IAttribute attr = defaultHttpDataFactory.CreateAttribute(req1, "attribute1");
        IFileUpload fu = defaultHttpDataFactory.CreateFileUpload(
                req1, "attribute1", "f.txt", "text/plain", null, null, 0);
        Assert.Equal(dir, ((DiskAttribute) attr).GetBaseDirectory());
        Assert.Equal(dir, ((DiskFileUpload) fu).GetBaseDirectory());
        Assert.True(((DiskAttribute) attr).DeleteOnExit());
        Assert.True(((DiskFileUpload) fu).DeleteOnExit());
    }

    [Fact]
    public void CleanRequestHttpDataShouldIdentifiesRequestsByTheirIdentities()
    {
        // Create some data belonging to req1 and req2
        IAttribute attribute1 = factory.CreateAttribute(req1, "attribute1", "value1");
        IAttribute attribute2 = factory.CreateAttribute(req2, "attribute2", "value2");
        IFileUpload file1 = factory.CreateFileUpload(
                req1, "file1", "file1.txt",
                HttpPostBodyUtil.DEFAULT_TEXT_CONTENT_TYPE, HttpHeaderValues.IDENTITY.ToString(), Encoding.UTF8, 123
        );
        IFileUpload file2 = factory.CreateFileUpload(
                req2, "file2", "file2.txt",
                HttpPostBodyUtil.DEFAULT_TEXT_CONTENT_TYPE, HttpHeaderValues.IDENTITY.ToString(), Encoding.UTF8, 123
        );
        file1.SetContent(Unpooled.CopiedBuffer("file1 content", Encoding.UTF8));
        file2.SetContent(Unpooled.CopiedBuffer("file2 content", Encoding.UTF8));

        // Assert that they are not deleted
        Assert.NotNull(attribute1.GetByteBuf());
        Assert.NotNull(attribute2.GetByteBuf());
        Assert.NotNull(file1.GetByteBuf());
        Assert.NotNull(file2.GetByteBuf());
        Assert.Equal(1, attribute1.RefCnt);
        Assert.Equal(1, attribute2.RefCnt);
        Assert.Equal(1, file1.RefCnt);
        Assert.Equal(1, file2.RefCnt);

        // Clean up by req1
        factory.CleanRequestHttpData(req1);

        // Assert that data belonging to req1 has been cleaned up
        Assert.Null(attribute1.GetByteBuf());
        Assert.Null(file1.GetByteBuf());
        Assert.Equal(0, attribute1.RefCnt);
        Assert.Equal(0, file1.RefCnt);

        // But not req2
        Assert.NotNull(attribute2.GetByteBuf());
        Assert.NotNull(file2.GetByteBuf());
        Assert.Equal(1, attribute2.RefCnt);
        Assert.Equal(1, file2.RefCnt);
    }

    [Fact]
    public void RemoveHttpDataFromCleanShouldIdentifiesDataByTheirIdentities()
    {
        // Create some equal data items belonging to the same request
        IAttribute attribute1 = factory.CreateAttribute(req1, "attribute", "value");
        IAttribute attribute2 = factory.CreateAttribute(req1, "attribute", "value");
        IFileUpload file1 = factory.CreateFileUpload(
                req1, "file", "file.txt",
                HttpPostBodyUtil.DEFAULT_TEXT_CONTENT_TYPE, HttpHeaderValues.IDENTITY.ToString(), Encoding.UTF8, 123
        );
        IFileUpload file2 = factory.CreateFileUpload(
                req1, "file", "file.txt",
                HttpPostBodyUtil.DEFAULT_TEXT_CONTENT_TYPE, HttpHeaderValues.IDENTITY.ToString(), Encoding.UTF8, 123
        );
        file1.SetContent(Unpooled.CopiedBuffer("file content", Encoding.UTF8));
        file2.SetContent(Unpooled.CopiedBuffer("file content", Encoding.UTF8));

        // Before doing anything, assert that the data items are equal
        Assert.Equal(attribute1.GetHashCode(), attribute2.GetHashCode());
        Assert.True(attribute1.Equals(attribute2));
        Assert.Equal(file1.GetHashCode(), file2.GetHashCode());
        Assert.True(file1.Equals(file2));

        // Remove attribute2 and file2 from being cleaned up by factory
        factory.RemoveHttpDataFromClean(req1, attribute2);
        factory.RemoveHttpDataFromClean(req1, file2);

        // Clean up by req1
        factory.CleanRequestHttpData(req1);

        // Assert that attribute1 and file1 have been cleaned up
        Assert.Null(attribute1.GetByteBuf());
        Assert.Null(file1.GetByteBuf());
        Assert.Equal(0, attribute1.RefCnt);
        Assert.Equal(0, file1.RefCnt);

        // But not attribute2 and file2
        Assert.NotNull(attribute2.GetByteBuf());
        Assert.NotNull(file2.GetByteBuf());
        Assert.Equal(1, attribute2.RefCnt);
        Assert.Equal(1, file2.RefCnt);

        // Cleanup attribute2 and file2 manually to avoid memory leak, not via factory
        attribute2.Release();
        file2.Release();
        Assert.Equal(0, attribute2.RefCnt);
        Assert.Equal(0, file2.RefCnt);
    }
}
