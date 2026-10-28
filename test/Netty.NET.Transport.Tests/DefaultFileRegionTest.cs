/*
 * Copyright 2019 The Netty Project
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
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

public class DefaultFileRegionTest
{

    private static readonly byte[] data = new byte[1048576 * 10];

    static DefaultFileRegionTest()
    {
        Random.Shared.NextBytes(data);
    }

    private static FileInfo NewFile()
    {
        var file = new FileInfo(Path.GetTempFileName());
        File.WriteAllBytes(file.FullName, data);
        return file;
    }

    [Fact]
    public void TestCreateFromFile()
    {
        FileInfo file = NewFile();
        try
        {
            TestFileRegion(new DefaultFileRegion(file, 0, data.Length));
        }
        finally
        {
            file.Delete();
        }
    }

    [Fact]
    public void TestCreateFromFileChannel()
    {
        FileInfo file = NewFile();
        try
        {
            using var randomAccessFile = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            TestFileRegion(new DefaultFileRegion(randomAccessFile, 0, data.Length));
        }
        finally
        {
            file.Delete();
        }
    }

    private static void TestFileRegion(IFileRegion region)
    {
        var outputStream = new MemoryStream();

        try
        {
            using Stream channel = outputStream;
            Assert.Equal(data.Length, region.Count());
            Assert.Equal(0, region.Transferred());
            Assert.Equal(data.Length, region.TransferTo(channel, 0));
            Assert.Equal(data.Length, region.Count());
            Assert.Equal(data.Length, region.Transferred());
            Assert.Equal(data, outputStream.ToArray());
        }
        finally { region.Release(); }
    }

    [Fact]
    public void TestTruncated()
    {
        FileInfo file = NewFile();
        var outputStream = new MemoryStream();

        try
        {
            using Stream channel = outputStream;
            using var randomAccessFile = new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            IFileRegion region = new DefaultFileRegion(randomAccessFile, 0, data.Length);

            randomAccessFile.SetLength(data.Length - 1024);

            Assert.Equal(data.Length, region.Count());
            Assert.Equal(0, region.Transferred());

            Assert.Equal(data.Length - 1024, region.TransferTo(channel, 0));
            Assert.Equal(data.Length, region.Count());
            Assert.Equal(data.Length - 1024, region.Transferred());
            try
            { Assert.Throws<IOException>(() => region.TransferTo(channel, data.Length - 1024)); }
            finally { region.Release(); }
        }
        finally
        {
            file.Delete();
        }
    }
}
