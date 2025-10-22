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
    public void testNullDefaultValue()
    {
        Assert.Throws<NullReferenceException>(() =>
        {
            new DomainWildcardMappingBuilder<string>(null);
        });
    }

    [Fact]
    public void testNullDomainNamePatternsAreForbidden()
    {
        Assert.Throws<NullReferenceException>(() =>
        {
            new DomainWildcardMappingBuilder<string>("NotFound").add(null, "Some value");
        });
    }

    [Fact]
    public void testNullValuesAreForbidden()
    {
        Assert.Throws<NullReferenceException>(() =>
        {
            new DomainWildcardMappingBuilder<string>("NotFound").add("Some key", null);
        });
    }

    [Fact]
    public void testDefaultValue()
    {
        IMapping<string, string> mapping = new DomainWildcardMappingBuilder<string>("NotFound")
            .add("*.netty.io", "Netty")
            .build();

        Assert.Equal("NotFound", mapping.map("not-existing"));
    }

    [Fact]
    public void testStrictEquality()
    {
        IMapping<string, string> mapping = new DomainWildcardMappingBuilder<string>("NotFound")
            .add("netty.io", "Netty")
            .add("downloads.netty.io", "Netty-Downloads")
            .build();

        Assert.Equal("Netty", mapping.map("netty.io"));
        Assert.Equal("Netty-Downloads", mapping.map("downloads.netty.io"));

        Assert.Equal("NotFound", mapping.map("x.y.z.netty.io"));
    }

    [Fact]
    public void testWildcardMatchesNotAnyPrefix()
    {
        IMapping<string, string> mapping = new DomainWildcardMappingBuilder<string>("NotFound")
            .add("*.netty.io", "Netty")
            .build();

        Assert.Equal("NotFound", mapping.map("netty.io"));
        Assert.Equal("Netty", mapping.map("downloads.netty.io"));
        Assert.Equal("NotFound", mapping.map("x.y.z.netty.io"));

        Assert.Equal("NotFound", mapping.map("netty.io.x"));
    }

    [Fact]
    public void testExactMatchWins()
    {
        Assert.Equal("Netty-Downloads",
            new DomainWildcardMappingBuilder<string>("NotFound")
                .add("*.netty.io", "Netty")
                .add("downloads.netty.io", "Netty-Downloads")
                .build()
                .map("downloads.netty.io"));

        Assert.Equal("Netty-Downloads",
            new DomainWildcardMappingBuilder<string>("NotFound")
                .add("downloads.netty.io", "Netty-Downloads")
                .add("*.netty.io", "Netty")
                .build()
                .map("downloads.netty.io"));
    }

    [Fact]
    public void testToString()
    {
        IMapping<string, string> mapping = new DomainWildcardMappingBuilder<string>("NotFound")
            .add("*.netty.io", "Netty")
            .add("downloads.netty.io", "Netty-Download")
            .build();

        Assert.Equal(
            "ImmutableDomainWildcardMapping(default: NotFound, map: " +
            "{*.netty.io=Netty, downloads.netty.io=Netty-Download})",
            mapping.ToString());
    }
}