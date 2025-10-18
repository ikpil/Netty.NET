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

namespace Netty.Common.Tests;public class DomainWildcardMappingBuilderTest {

    [Fact]
    public void testNullDefaultValue() {
        assertThrows(NullReferenceException.class, new Executable() {
            @Override
            public void execute() {
                new DomainWildcardMappingBuilder<String>(null);
            }
        });
    }

    [Fact]
    public void testNullDomainNamePatternsAreForbidden() {
        assertThrows(NullReferenceException.class, new Executable() {
            @Override
            public void execute() {
                new DomainWildcardMappingBuilder<String>("NotFound").add(null, "Some value");
            }
        });
    }

    [Fact]
    public void testNullValuesAreForbidden() {
        assertThrows(NullReferenceException.class, new Executable() {
            @Override
            public void execute() {
                new DomainWildcardMappingBuilder<String>("NotFound").add("Some key", null);
            }
        });
    }

    [Fact]
    public void testDefaultValue() {
        IMapping<String, String> mapping = new DomainWildcardMappingBuilder<String>("NotFound")
            .add("*.netty.io", "Netty")
            .build();

        assertEquals("NotFound", mapping.map("not-existing"));
    }

    [Fact]
    public void testStrictEquality() {
        IMapping<String, String> mapping = new DomainWildcardMappingBuilder<String>("NotFound")
            .add("netty.io", "Netty")
            .add("downloads.netty.io", "Netty-Downloads")
            .build();

        assertEquals("Netty", mapping.map("netty.io"));
        assertEquals("Netty-Downloads", mapping.map("downloads.netty.io"));

        assertEquals("NotFound", mapping.map("x.y.z.netty.io"));
    }

    [Fact]
    public void testWildcardMatchesNotAnyPrefix() {
        IMapping<String, String> mapping = new DomainWildcardMappingBuilder<String>("NotFound")
            .add("*.netty.io", "Netty")
            .build();

        assertEquals("NotFound", mapping.map("netty.io"));
        assertEquals("Netty", mapping.map("downloads.netty.io"));
        assertEquals("NotFound", mapping.map("x.y.z.netty.io"));

        assertEquals("NotFound", mapping.map("netty.io.x"));
    }

    [Fact]
    public void testExactMatchWins() {
        assertEquals("Netty-Downloads",
            new DomainWildcardMappingBuilder<String>("NotFound")
                .add("*.netty.io", "Netty")
                .add("downloads.netty.io", "Netty-Downloads")
                .build()
                .map("downloads.netty.io"));

        assertEquals("Netty-Downloads",
            new DomainWildcardMappingBuilder<String>("NotFound")
                .add("downloads.netty.io", "Netty-Downloads")
                .add("*.netty.io", "Netty")
                .build()
                .map("downloads.netty.io"));
    }

    [Fact]
    public void testToString() {
        IMapping<String, String> mapping = new DomainWildcardMappingBuilder<String>("NotFound")
            .add("*.netty.io", "Netty")
            .add("downloads.netty.io", "Netty-Download")
            .build();

        assertEquals(
            "ImmutableDomainWildcardMapping(default: NotFound, map: " +
                    "{*.netty.io=Netty, downloads.netty.io=Netty-Download})",
            mapping.toString());
    }
}
