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
using System.Collections.Generic;

namespace Netty.NET.Common.Tests;

//@SuppressWarnings("deprecation")
public class DomainNameMappingTest
{
    // Deprecated API

    [Fact]
    public void TestNullDefaultValueInDeprecatedApi()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            new DomainNameMapping<string>(null);
        });
    }

    [Fact]
    public void TestNullDomainNamePatternsAreForbiddenInDeprecatedApi()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            new DomainNameMapping<string>("NotFound").Add(null, "Some value");
        });
    }

    [Fact]
    public void TestNullValuesAreForbiddenInDeprecatedApi()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            new DomainNameMapping<string>("NotFound").Add("Some key", null);
        });
    }

    [Fact]
    public void TestDefaultValueInDeprecatedApi()
    {
        DomainNameMapping<string> mapping = new DomainNameMapping<string>("NotFound");

        Assert.Equal("NotFound", mapping.Map("not-existing"));

        mapping.Add("*.netty.io", "Netty");

        Assert.Equal("NotFound", mapping.Map("not-existing"));
    }

    [Fact]
    public void TestStrictEqualityInDeprecatedApi()
    {
        DomainNameMapping<string> mapping = new DomainNameMapping<string>("NotFound")
            .Add("netty.io", "Netty")
            .Add("downloads.netty.io", "Netty-Downloads");

        Assert.Equal("Netty", mapping.Map("netty.io"));
        Assert.Equal("Netty-Downloads", mapping.Map("downloads.netty.io"));

        Assert.Equal("NotFound", mapping.Map("x.y.z.netty.io"));
    }

    [Fact]
    public void TestWildcardMatchesAnyPrefixInDeprecatedApi()
    {
        DomainNameMapping<string> mapping = new DomainNameMapping<string>("NotFound")
            .Add("*.netty.io", "Netty");

        Assert.Equal("Netty", mapping.Map("netty.io"));
        Assert.Equal("Netty", mapping.Map("downloads.netty.io"));
        Assert.Equal("Netty", mapping.Map("x.y.z.netty.io"));

        Assert.Equal("NotFound", mapping.Map("netty.io.x"));
    }

    [Fact]
    public void TestFirstMatchWinsInDeprecatedApi()
    {
        Assert.Equal("Netty",
            new DomainNameMapping<string>("NotFound")
                .Add("*.netty.io", "Netty")
                .Add("downloads.netty.io", "Netty-Downloads")
                .Map("downloads.netty.io"));

        Assert.Equal("Netty-Downloads",
            new DomainNameMapping<string>("NotFound")
                .Add("downloads.netty.io", "Netty-Downloads")
                .Add("*.netty.io", "Netty")
                .Map("downloads.netty.io"));
    }

    [Fact]
    public void TestToStringInDeprecatedApi()
    {
        DomainNameMapping<string> mapping = new DomainNameMapping<string>("NotFound")
            .Add("*.netty.io", "Netty")
            .Add("downloads.netty.io", "Netty-Downloads");

        Assert.Equal(
            "DomainNameMapping(default: NotFound, map: {*.netty.io=Netty, downloads.netty.io=Netty-Downloads})",
            mapping.ToString());
    }

    // Immutable DomainNameMapping Builder API

    [Fact]
    public void TestNullDefaultValue()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            new DomainNameMappingBuilder<string>(null);
        });
    }

    [Fact]
    public void TestNullDomainNamePatternsAreForbidden()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            new DomainNameMappingBuilder<string>("NotFound").Add(null, "Some value");
        });
    }

    [Fact]
    public void TestNullValuesAreForbidden()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            new DomainNameMappingBuilder<string>("NotFound").Add("Some key", null);
        });
    }

    [Fact]
    public void TestDefaultValue()
    {
        DomainNameMapping<string> mapping = new DomainNameMappingBuilder<string>("NotFound")
            .Add("*.netty.io", "Netty")
            .Build();

        Assert.Equal("NotFound", mapping.Map("not-existing"));
    }

    [Fact]
    public void TestStrictEquality()
    {
        DomainNameMapping<string> mapping = new DomainNameMappingBuilder<string>("NotFound")
            .Add("netty.io", "Netty")
            .Add("downloads.netty.io", "Netty-Downloads")
            .Build();

        Assert.Equal("Netty", mapping.Map("netty.io"));
        Assert.Equal("Netty-Downloads", mapping.Map("downloads.netty.io"));

        Assert.Equal("NotFound", mapping.Map("x.y.z.netty.io"));
    }

    [Fact]
    public void TestWildcardMatchesAnyPrefix()
    {
        DomainNameMapping<string> mapping = new DomainNameMappingBuilder<string>("NotFound")
            .Add("*.netty.io", "Netty")
            .Build();

        Assert.Equal("Netty", mapping.Map("netty.io"));
        Assert.Equal("Netty", mapping.Map("downloads.netty.io"));
        Assert.Equal("Netty", mapping.Map("x.y.z.netty.io"));

        Assert.Equal("NotFound", mapping.Map("netty.io.x"));
    }

    [Fact]
    public void TestFirstMatchWins()
    {
        Assert.Equal("Netty",
            new DomainNameMappingBuilder<string>("NotFound")
                .Add("*.netty.io", "Netty")
                .Add("downloads.netty.io", "Netty-Downloads")
                .Build()
                .Map("downloads.netty.io"));

        Assert.Equal("Netty-Downloads",
            new DomainNameMappingBuilder<string>("NotFound")
                .Add("downloads.netty.io", "Netty-Downloads")
                .Add("*.netty.io", "Netty")
                .Build()
                .Map("downloads.netty.io"));
    }

    [Fact]
    public void TestToString()
    {
        DomainNameMapping<string> mapping = new DomainNameMappingBuilder<string>("NotFound")
            .Add("*.netty.io", "Netty")
            .Add("downloads.netty.io", "Netty-Download")
            .Build();

        Assert.Equal(
            "ImmutableDomainNameMapping(default: NotFound, map: {*.netty.io=Netty, downloads.netty.io=Netty-Download})",
            mapping.ToString());
    }

    [Fact]
    public void TestAsMap()
    {
        DomainNameMapping<string> mapping = new DomainNameMapping<string>("NotFound")
            .Add("netty.io", "Netty")
            .Add("downloads.netty.io", "Netty-Downloads");

        IReadOnlyDictionary<string, string> entries = mapping.AsMap();

        Assert.Equal(2, entries.Count);
        Assert.Equal("Netty", entries.GetValueOrDefault("netty.io"));
        Assert.Equal("Netty-Downloads", entries.GetValueOrDefault("downloads.netty.io"));
    }

    [Fact]
    public void TestAsMapWithImmutableDomainNameMapping()
    {
        DomainNameMapping<string> mapping = new DomainNameMappingBuilder<string>("NotFound")
            .Add("netty.io", "Netty")
            .Add("downloads.netty.io", "Netty-Downloads")
            .Build();

        IReadOnlyDictionary<string, string> entries = mapping.AsMap();

        Assert.Equal(2, entries.Count);
        Assert.Equal("Netty", entries.GetValueOrDefault("netty.io"));
        Assert.Equal("Netty-Downloads", entries.GetValueOrDefault("downloads.netty.io"));
    }
}
