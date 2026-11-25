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

using System.IO;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the test resources are deployed next to the test assembly under the Java package path
// io/netty/handler/codec/http2/testdata/ (Netty.NET.Handler.Codec.Http2.Tests.csproj), so TEST_DIR is that path: the
// Java expression getPackage().getName().replaceAll("\\.", "/") of io.netty.handler.codec.http2 written out,
// because the C# namespace of the test class is not the Java package. ResourcesUtil.getFile(Class, String) is the
// Common ResourcesUtil.GetFile(Type, string), which resolves the name against the assembly directory, so the leading
// '/' of the class-path-absolute name is dropped; File.listFiles() of that directory is DirectoryInfo.GetFiles() and
// getResourceAsStream(TEST_DIR + name) opens the deployed file (File.OpenRead). @ParameterizedTest
// @MethodSource("files") is a [Theory] with MemberData; the FileInfo rows are not serializable, so xUnit enumerates
// them when the theory runs (DisableDiscoveryEnumeration, as docs/codec-http-porting.md records for non-serializable
// rows). try-with-resources is a using statement; Stream is written System.IO.Stream, because the simple name binds
// to the namespace Netty.NET.Handler.Stream inside Netty.NET.Handler.*.
public class HpackTest
{

    private const string TEST_DIR = "/io/netty/handler/codec/http2/testdata/";

    public static TheoryData<FileInfo> Files()
    {
        FileInfo[] files = new DirectoryInfo(ResourcesUtil.GetFile(typeof(HpackTest), TEST_DIR.Substring(1)).FullName)
                .GetFiles();
        ObjectUtil.CheckNotNull(files, "files");
        TheoryData<FileInfo> data = new TheoryData<FileInfo>();
        foreach (FileInfo file in files)
        {
            data.Add(file);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Files), DisableDiscoveryEnumeration = true)]
    public void Test(FileInfo file)
    {
        using (System.IO.Stream @is = File.OpenRead(
                   ResourcesUtil.GetFile(typeof(HpackTest), TEST_DIR.Substring(1) + file.Name).FullName))
        {
            HpackTestCase hpackTestCase = HpackTestCase.Load(@is);
            hpackTestCase.TestCompress();
            hpackTestCase.TestDecompress();
        }
    }
}
