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

using System;
using System.IO;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

public class ChannelOptionTest
{

    [Fact]
    public void TestExists()
    {
        string name = "test";
        Assert.False(ChannelOption.Exists(name));
        ChannelOption<string> option = ChannelOption.ValueOf<string>(name);

        Assert.True(ChannelOption.Exists(name));
        Assert.NotNull(option);
    }

    [Fact]
    public void TestValueOf()
    {
        string name = "test1";
        Assert.False(ChannelOption.Exists(name));
        ChannelOption<string> option = ChannelOption.ValueOf<string>(name);
        ChannelOption<string> option2 = ChannelOption.ValueOf<string>(name);

        Assert.Same(option, option2);
    }

    // Original test covers the deprecated create-or-fail contract.
#pragma warning disable CS0618
    [Fact]
    public void TestCreateOrFail()
    {
        string name = "test2";
        Assert.False(ChannelOption.Exists(name));
        ChannelOption<string> option = ChannelOption.NewInstance<string>(name);
        Assert.True(ChannelOption.Exists(name));
        Assert.NotNull(option);

        Assert.ThrowsAny<ArgumentException>(() => ChannelOption.NewInstance<string>(name));
    }
}
#pragma warning restore CS0618
