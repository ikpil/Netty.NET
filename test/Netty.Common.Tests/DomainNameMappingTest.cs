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

using Netty.NET.Common;

namespace Netty.Common.Tests;

//@SuppressWarnings("deprecation")
public class DomainNameMappingTest
{
    // Deprecated API

    [Fact]
    public void testNullDefaultValueInDeprecatedApi()
    {
        Assert.Throws<NullReferenceException>(() =>
        {
            new DomainNameMapping<string>(null);
        });
    }

    [Fact]
    public void testNullDomainNamePatternsAreForbiddenInDeprecatedApi()
    {
        Assert.Throws<NullReferenceException>(() =>
        {
            new DomainNameMapping<string>("NotFound").add(null, "Some value");
        });
    }

    [Fact]
    public void testNullValuesAreForbiddenInDeprecatedApi()
    {
        Assert.Throws<NullReferenceException>(() =>
        {
            new DomainNameMapping<string>("NotFound").add("Some key", null);
        });
    }

    [Fact]
    public void testDefaultValueInDeprecatedApi()
    {
        DomainNameMapping<string> mapping = new DomainNameMapping<string>("NotFound");

        Assert.Equal("NotFound", mapping.map("not-existing"));

        mapping.add("*.netty.io", "Netty");

        Assert.Equal("NotFound", mapping.map("not-existing"));
    }

    [Fact]
    public void testStrictEqualityInDeprecatedApi()
    {
        DomainNameMapping<string> mapping = new DomainNameMapping<string>("NotFound")
            .add("netty.io", "Netty")
            .add("downloads.netty.io", "Netty-Downloads");

        Assert.Equal("Netty", mapping.map("netty.io"));
        Assert.Equal("Netty-Downloads", mapping.map("downloads.netty.io"));

        Assert.Equal("NotFound", mapping.map("x.y.z.netty.io"));
    }

    [Fact]
    public void testWildcardMatchesAnyPrefixInDeprecatedApi()
    {
        DomainNameMapping<string> mapping = new DomainNameMapping<string>("NotFound")
            .add("*.netty.io", "Netty");

        Assert.Equal("Netty", mapping.map("netty.io"));
        Assert.Equal("Netty", mapping.map("downloads.netty.io"));
        Assert.Equal("Netty", mapping.map("x.y.z.netty.io"));

        Assert.Equal("NotFound", mapping.map("netty.io.x"));
    }

    [Fact]
    public void testFirstMatchWinsInDeprecatedApi()
    {
        Assert.Equal("Netty",
            new DomainNameMapping<string>("NotFound")
                .add("*.netty.io", "Netty")
                .add("downloads.netty.io", "Netty-Downloads")
                .map("downloads.netty.io"));

        Assert.Equal("Netty-Downloads",
            new DomainNameMapping<string>("NotFound")
                .add("downloads.netty.io", "Netty-Downloads")
                .add("*.netty.io", "Netty")
                .map("downloads.netty.io"));
    }

    [Fact]
    public void testToStringInDeprecatedApi()
    {
        DomainNameMapping<string> mapping = new DomainNameMapping<string>("NotFound")
            .add("*.netty.io", "Netty")
            .add("downloads.netty.io", "Netty-Downloads");

        Assert.Equal(
            "DomainNameMapping(default: NotFound, map: {*.netty.io=Netty, downloads.netty.io=Netty-Downloads})",
            mapping.ToString());
    }

    // Immutable DomainNameMapping Builder API

    [Fact]
    public void testNullDefaultValue()
    {
        Assert.Throws<NullReferenceException>(() =>
        {
            new DomainNameMappingBuilder<string>(null);
        });
    }

    [Fact]
    public void testNullDomainNamePatternsAreForbidden()
    {
        Assert.Throws<NullReferenceException>(() =>
        {
            new DomainNameMappingBuilder<string>("NotFound").add(null, "Some value");
        });
    }

    [Fact]
    public void testNullValuesAreForbidden()
    {
        Assert.Throws<NullReferenceException>(() =>
        {
            new DomainNameMappingBuilder<string>("NotFound").add("Some key", null);
        });
    }

    [Fact]
    public void testDefaultValue()
    {
        DomainNameMapping<string> mapping = new DomainNameMappingBuilder<string>("NotFound")
            .add("*.netty.io", "Netty")
            .build();

        Assert.Equal("NotFound", mapping.map("not-existing"));
    }

    [Fact]
    public void testStrictEquality()
    {
        DomainNameMapping<string> mapping = new DomainNameMappingBuilder<string>("NotFound")
            .add("netty.io", "Netty")
            .add("downloads.netty.io", "Netty-Downloads")
            .build();

        Assert.Equal("Netty", mapping.map("netty.io"));
        Assert.Equal("Netty-Downloads", mapping.map("downloads.netty.io"));

        Assert.Equal("NotFound", mapping.map("x.y.z.netty.io"));
    }

    [Fact]
    public void testWildcardMatchesAnyPrefix()
    {
        DomainNameMapping<string> mapping = new DomainNameMappingBuilder<string>("NotFound")
            .add("*.netty.io", "Netty")
            .build();

        Assert.Equal("Netty", mapping.map("netty.io"));
        Assert.Equal("Netty", mapping.map("downloads.netty.io"));
        Assert.Equal("Netty", mapping.map("x.y.z.netty.io"));

        Assert.Equal("NotFound", mapping.map("netty.io.x"));
    }

    [Fact]
    public void testFirstMatchWins()
    {
        Assert.Equal("Netty",
            new DomainNameMappingBuilder<string>("NotFound")
                .add("*.netty.io", "Netty")
                .add("downloads.netty.io", "Netty-Downloads")
                .build()
                .map("downloads.netty.io"));

        Assert.Equal("Netty-Downloads",
            new DomainNameMappingBuilder<string>("NotFound")
                .add("downloads.netty.io", "Netty-Downloads")
                .add("*.netty.io", "Netty")
                .build()
                .map("downloads.netty.io"));
    }

    [Fact]
    public void testToString()
    {
        DomainNameMapping<string> mapping = new DomainNameMappingBuilder<string>("NotFound")
            .add("*.netty.io", "Netty")
            .add("downloads.netty.io", "Netty-Download")
            .build();

        Assert.Equal(
            "ImmutableDomainNameMapping(default: NotFound, map: {*.netty.io=Netty, downloads.netty.io=Netty-Download})",
            mapping.ToString());
    }

    [Fact]
    public void testAsMap()
    {
        DomainNameMapping<string> mapping = new DomainNameMapping<string>("NotFound")
            .add("netty.io", "Netty")
            .add("downloads.netty.io", "Netty-Downloads");

        IReadOnlyDictionary<string, string> entries = mapping.asMap();

        Assert.Equal(2, entries.Count);
        Assert.Equal("Netty", entries.GetValueOrDefault("netty.io"));
        Assert.Equal("Netty-Downloads", entries.GetValueOrDefault("downloads.netty.io"));
    }

    [Fact]
    public void testAsMapWithImmutableDomainNameMapping()
    {
        DomainNameMapping<string> mapping = new DomainNameMappingBuilder<string>("NotFound")
            .add("netty.io", "Netty")
            .add("downloads.netty.io", "Netty-Downloads")
            .build();

        IReadOnlyDictionary<string, string> entries = mapping.asMap();

        Assert.Equal(2, entries.Count);
        Assert.Equal("Netty", entries.GetValueOrDefault("netty.io"));
        Assert.Equal("Netty-Downloads", entries.GetValueOrDefault("downloads.netty.io"));
    }
}