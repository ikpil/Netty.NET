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

namespace Netty.NET.Common.Tests;

public class DomainWildcardMappingBuilderTest
{
    [Fact]
    public void TestNullDefaultValue()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            new DomainWildcardMappingBuilder<string>(null);
        });
    }

    [Fact]
    public void TestNullDomainNamePatternsAreForbidden()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            new DomainWildcardMappingBuilder<string>("NotFound").Add(null, "Some value");
        });
    }

    [Fact]
    public void TestNullValuesAreForbidden()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            new DomainWildcardMappingBuilder<string>("NotFound").Add("Some key", null);
        });
    }

    [Fact]
    public void TestDefaultValue()
    {
        Func<string, string> mapping = new DomainWildcardMappingBuilder<string>("NotFound")
            .Add("*.netty.io", "Netty")
            .Build();

        Assert.Equal("NotFound", mapping("not-existing"));
    }

    [Fact]
    public void TestStrictEquality()
    {
        Func<string, string> mapping = new DomainWildcardMappingBuilder<string>("NotFound")
            .Add("netty.io", "Netty")
            .Add("downloads.netty.io", "Netty-Downloads")
            .Build();

        Assert.Equal("Netty", mapping("netty.io"));
        Assert.Equal("Netty-Downloads", mapping("downloads.netty.io"));

        Assert.Equal("NotFound", mapping("x.y.z.netty.io"));
    }

    [Fact]
    public void TestWildcardMatchesNotAnyPrefix()
    {
        Func<string, string> mapping = new DomainWildcardMappingBuilder<string>("NotFound")
            .Add("*.netty.io", "Netty")
            .Build();

        Assert.Equal("NotFound", mapping("netty.io"));
        Assert.Equal("Netty", mapping("downloads.netty.io"));
        Assert.Equal("NotFound", mapping("x.y.z.netty.io"));

        Assert.Equal("NotFound", mapping("netty.io.x"));
    }

    [Fact]
    public void TestExactMatchWins()
    {
        Assert.Equal("Netty-Downloads",
            new DomainWildcardMappingBuilder<string>("NotFound")
                .Add("*.netty.io", "Netty")
                .Add("downloads.netty.io", "Netty-Downloads")
                .Build()("downloads.netty.io"));

        Assert.Equal("Netty-Downloads",
            new DomainWildcardMappingBuilder<string>("NotFound")
                .Add("downloads.netty.io", "Netty-Downloads")
                .Add("*.netty.io", "Netty")
                .Build()("downloads.netty.io"));
    }

    [Fact]
    public void TestToString()
    {
        Func<string, string> mapping = new DomainWildcardMappingBuilder<string>("NotFound")
            .Add("*.netty.io", "Netty")
            .Add("downloads.netty.io", "Netty-Download")
            .Build();

        Assert.Equal(
            "ImmutableDomainWildcardMapping(default: NotFound, map: " +
            "{*.netty.io=Netty, downloads.netty.io=Netty-Download})",
            mapping.Target.ToString());
    }
}
