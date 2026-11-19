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

using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Tests;

#pragma warning disable xUnit2013 // assertEquals(n, size()) keeps the source assertions.

public class CodecOutputListTest
{
    [Fact]
    public void TestCodecOutputListAdd()
    {
        CodecOutputList codecOutputList = CodecOutputList.NewInstance();
        try
        {
            Assert.Equal(0, codecOutputList.Count);
            Assert.True(codecOutputList.Count == 0);

            codecOutputList.Add(1);
            Assert.Equal(1, codecOutputList.Count);
            Assert.False(codecOutputList.Count == 0);
            Assert.Equal(1, codecOutputList[0]);

            codecOutputList.Insert(0, 0);
            Assert.Equal(2, codecOutputList.Count);
            Assert.False(codecOutputList.Count == 0);
            Assert.Equal(0, codecOutputList[0]);
            Assert.Equal(1, codecOutputList[1]);

            codecOutputList.Insert(1, 2);
            Assert.Equal(3, codecOutputList.Count);
            Assert.False(codecOutputList.Count == 0);
            Assert.Equal(0, codecOutputList[0]);
            Assert.Equal(2, codecOutputList[1]);
            Assert.Equal(1, codecOutputList[2]);
        }
        finally
        {
            codecOutputList.Recycle();
        }
    }
}
