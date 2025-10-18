/*
 * Copyright 2016 The Netty Project
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
namespace Netty.Common.Tests.Internal;


public class MacAddressUtilTest {
    [Fact]
    public void testCompareAddresses() {
        // should not prefer empty address when candidate is not globally unique
        Assert.Equal(
                0,
                MacAddressUtil.compareAddresses(
                        EMPTY_BYTES,
                        new byte[]{(byte) 0x52, (byte) 0x54, (byte) 0x00, (byte) 0xf9, (byte) 0x32, (byte) 0xbd}));

        // only candidate is globally unique
        Assert.Equal(
                -1,
                MacAddressUtil.compareAddresses(
                        EMPTY_BYTES,
                        new byte[]{(byte) 0x50, (byte) 0x54, (byte) 0x00, (byte) 0xf9, (byte) 0x32, (byte) 0xbd}));

        // only candidate is globally unique
        Assert.Equal(
                -1,
                MacAddressUtil.compareAddresses(
                        new byte[]{(byte) 0x52, (byte) 0x54, (byte) 0x00, (byte) 0xf9, (byte) 0x32, (byte) 0xbd},
                        new byte[]{(byte) 0x50, (byte) 0x54, (byte) 0x00, (byte) 0xf9, (byte) 0x32, (byte) 0xbd}));

        // only current is globally unique
        Assert.Equal(
                1,
                MacAddressUtil.compareAddresses(
                        new byte[]{(byte) 0x52, (byte) 0x54, (byte) 0x00, (byte) 0xf9, (byte) 0x32, (byte) 0xbd},
                        EMPTY_BYTES));

        // only current is globally unique
        Assert.Equal(
                1,
                MacAddressUtil.compareAddresses(
                        new byte[]{(byte) 0x50, (byte) 0x54, (byte) 0x00, (byte) 0xf9, (byte) 0x32, (byte) 0xbd},
                        new byte[]{(byte) 0x52, (byte) 0x54, (byte) 0x00, (byte) 0xf9, (byte) 0x32, (byte) 0xbd}));

        // both are globally unique
        Assert.Equal(
                0,
                MacAddressUtil.compareAddresses(
                        new byte[]{(byte) 0x50, (byte) 0x54, (byte) 0x00, (byte) 0xf9, (byte) 0x32, (byte) 0xbd},
                        new byte[]{(byte) 0x50, (byte) 0x55, (byte) 0x01, (byte) 0xfa, (byte) 0x33, (byte) 0xbe}));
    }

    [Fact]
    public void testParseMacEUI48() {
        Assert.Equal(new byte[]{0, (byte) 0xaa, 0x11, (byte) 0xbb, 0x22, (byte) 0xcc},
                parseMAC("00-AA-11-BB-22-CC"));
        Assert.Equal(new byte[]{0, (byte) 0xaa, 0x11, (byte) 0xbb, 0x22, (byte) 0xcc},
                parseMAC("00:AA:11:BB:22:CC"));
    }

    [Fact]
    public void testParseMacMAC48ToEUI64() {
        // MAC-48 into an EUI-64
        Assert.Equal(new byte[]{0, (byte) 0xaa, 0x11, (byte) 0xff, (byte) 0xff, (byte) 0xbb, 0x22, (byte) 0xcc},
                parseMAC("00-AA-11-FF-FF-BB-22-CC"));
        Assert.Equal(new byte[]{0, (byte) 0xaa, 0x11, (byte) 0xff, (byte) 0xff, (byte) 0xbb, 0x22, (byte) 0xcc},
                parseMAC("00:AA:11:FF:FF:BB:22:CC"));
    }

    [Fact]
    public void testParseMacEUI48ToEUI64() {
        // EUI-48 into an EUI-64
        Assert.Equal(new byte[]{0, (byte) 0xaa, 0x11, (byte) 0xff, (byte) 0xfe, (byte) 0xbb, 0x22, (byte) 0xcc},
                parseMAC("00-AA-11-FF-FE-BB-22-CC"));
        Assert.Equal(new byte[]{0, (byte) 0xaa, 0x11, (byte) 0xff, (byte) 0xfe, (byte) 0xbb, 0x22, (byte) 0xcc},
                parseMAC("00:AA:11:FF:FE:BB:22:CC"));
    }

    [Fact]
    public void testParseMacInvalid7HexGroupsA() {
        assertThrows(ArgumentException.class, new Executable() {
            @Override
            public void execute() {
                parseMAC("00-AA-11-BB-22-CC-FF");
            }
        });
    }

    [Fact]
    public void testParseMacInvalid7HexGroupsB() {
        assertThrows(ArgumentException.class, new Executable() {
            @Override
            public void execute() {
                parseMAC("00:AA:11:BB:22:CC:FF");
            }
        });
    }

    [Fact]
    public void testParseMacInvalidEUI48MixedSeparatorA() {
        assertThrows(ArgumentException.class, new Executable() {
            @Override
            public void execute() {
                parseMAC("00-AA:11-BB-22-CC");
            }
        });
    }

    [Fact]
    public void testParseMacInvalidEUI48MixedSeparatorB() {
        assertThrows(ArgumentException.class, new Executable() {
            @Override
            public void execute() {
                parseMAC("00:AA-11:BB:22:CC");
            }
        });
    }

    [Fact]
    public void testParseMacInvalidEUI64MixedSeparatorA() {
        assertThrows(ArgumentException.class, new Executable() {
            @Override
            public void execute() {
                parseMAC("00-AA-11-FF-FE-BB-22:CC");
            }
        });
    }

    [Fact]
    public void testParseMacInvalidEUI64MixedSeparatorB() {
        assertThrows(ArgumentException.class, new Executable() {
            @Override
            public void execute() {
                parseMAC("00:AA:11:FF:FE:BB:22-CC");
            }
        });
    }

    [Fact]
    public void testParseMacInvalidEUI48TrailingSeparatorA() {
        assertThrows(ArgumentException.class, new Executable() {
            @Override
            public void execute() {
                parseMAC("00-AA-11-BB-22-CC-");
            }
        });
    }

    [Fact]
    public void testParseMacInvalidEUI48TrailingSeparatorB() {
        assertThrows(ArgumentException.class, new Executable() {
            @Override
            public void execute() {
                parseMAC("00:AA:11:BB:22:CC:");
            }
        });
    }

    [Fact]
    public void testParseMacInvalidEUI64TrailingSeparatorA() {
        assertThrows(ArgumentException.class, new Executable() {
            @Override
            public void execute() {
                parseMAC("00-AA-11-FF-FE-BB-22-CC-");
            }
        });
    }

    [Fact]
    public void testParseMacInvalidEUI64TrailingSeparatorB() {
        assertThrows(ArgumentException.class, new Executable() {
            @Override
            public void execute() {
                parseMAC("00:AA:11:FF:FE:BB:22:CC:");
            }
        });
    }
}
