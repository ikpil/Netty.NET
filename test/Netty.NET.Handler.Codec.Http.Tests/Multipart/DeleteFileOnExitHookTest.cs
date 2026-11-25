/*
 * Copyright 2020 The Netty Project
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
using System.IO;
using System.Linq;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Http.Multipart;
using static Netty.NET.Handler.Codec.Http.HttpMethod;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Codec.Http.Tests.Multipart;

// CLR: @Isolated keeps other test classes from running while this one runs, because DeleteFileOnExitHook.runHook()
// deletes every file registered by the DiskAttribute/DiskFileUpload instances of other tests; the class is in an xUnit
// collection "with parallelism turned off" (https://xunit.net/docs/running-tests-in-parallel), as the Buffer tests
// with process-wide state are (test/Netty.NET.Buffer.Tests/AllocatorLeakDetectionTest.cs).
[CollectionDefinition(nameof(DeleteFileOnExitHookCollection), DisableParallelization = true)]
public class DeleteFileOnExitHookCollection
{
}

/**
 * Test DeleteFileOnExitHook
 */
// CLR: @BeforeEach setUp() runs from the constructor, since xUnit "creates a new instance of the test class for every
// test that is run" (https://xunit.net/docs/shared-context); it is private, because xUnit flags public non-test methods
// of a test class (xUnit1013, https://xunit.net/xunit.analyzers/rules/xUnit1013). File is FileInfo (getPath() is
// FullName, as the port stores FullName in the hook) and new File(dir).mkdirs() is DirectoryInfo.Create().
// File.exists() is AbstractDiskHttpData.FileExists (FileInfo caches its state until Refresh()).
// listFiles(FilenameFilter) with the anonymous filter is DirectoryInfo.GetFiles() filtered by the same prefix test, and
// assertEquals(0, files.length) is Assert.Empty, the form the xUnit analyzer requires for a size check (xUnit2013,
// https://xunit.net/xunit.analyzers/rules/xUnit2013). UUID.randomUUID() is Guid.NewGuid().
[Collection(nameof(DeleteFileOnExitHookCollection))]
public class DeleteFileOnExitHookTest
{
    private static readonly IHttpRequest REQUEST = new DefaultHttpRequest(HTTP_1_1, POST, "/form");
    private static readonly string HOOK_TEST_TMP = "target/DeleteFileOnExitHookTest-" + Guid.NewGuid()  + "/tmp";
    private IFileUpload fu;

    public DeleteFileOnExitHookTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        DefaultHttpDataFactory defaultHttpDataFactory = new DefaultHttpDataFactory(true);
        defaultHttpDataFactory.SetBaseDir(HOOK_TEST_TMP);
        defaultHttpDataFactory.SetDeleteOnExit(true);

        DirectoryInfo baseDir = new DirectoryInfo(HOOK_TEST_TMP);
        baseDir.Create();  // we don't need to clean it since it is in volatile files anyway

        fu = defaultHttpDataFactory.CreateFileUpload(
                REQUEST, "attribute1", "tmp_f.txt", "text/plain", null, null, 0);
        fu.SetContent(Unpooled.WrappedBuffer(new byte[]{1, 2, 3, 4}));

        Assert.True(AbstractDiskHttpData.FileExists(fu.GetFile()));
    }

    [Fact]
    public void TestSimulateTriggerDeleteFileOnExitHook()
    {

        // simulate app exit
        DeleteFileOnExitHook.RunHook();

        FileInfo[] files = new DirectoryInfo(HOOK_TEST_TMP).GetFiles()
                .Where(file => file.Name.StartsWith(DiskFileUpload.prefix, StringComparison.Ordinal)).ToArray();

        Assert.Empty(files);
    }

    [Fact]
    public void TestAfterHttpDataReleaseCheckFileExist()
    {

        string filePath = fu.GetFile().FullName;
        Assert.True(DeleteFileOnExitHook.CheckFileExist(filePath));

        fu.Release();
        Assert.False(DeleteFileOnExitHook.CheckFileExist(filePath));
    }
}
