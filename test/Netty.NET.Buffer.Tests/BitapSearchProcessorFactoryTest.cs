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
using Netty.NET.Buffer.Search;

namespace Netty.NET.Buffer.Tests;

public class BitapSearchProcessorFactoryTest
{
    [Fact]
    public void AcceptMaximumLengthNeedle()
    {
        var processor = AbstractSearchProcessorFactory.NewBitapSearchProcessorFactory(new byte[64]).NewSearchProcessor();
        for (int i = 0; i < 63; i++) Assert.True(processor.Process(0));
        Assert.False(processor.Process(0)); Assert.False(processor.Process(0));
        processor.Reset(); Assert.True(processor.Process(0));
    }

    [Fact]
    public void RejectTooLongNeedle()
        => Assert.Throws<ArgumentException>(() => AbstractSearchProcessorFactory.NewBitapSearchProcessorFactory(new byte[65]));
}
