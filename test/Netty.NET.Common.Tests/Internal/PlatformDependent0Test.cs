/*
 * Copyright 2017 The Netty Project
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
namespace Netty.NET.Common.Tests.Internal;


public class PlatformDependent0Test {

    @BeforeAll
    public static void assumeUnsafe() {
        assumeTrue(PlatformDependent0.hasUnsafe());
        assumeTrue(PlatformDependent0.hasDirectBufferNoCleanerConstructor());
    }

    [Fact]
    public void testNewDirectBufferNegativeMemoryAddress() {
        testNewDirectBufferMemoryAddress(-1);
    }

    [Fact]
    public void testNewDirectBufferNonNegativeMemoryAddress() {
        testNewDirectBufferMemoryAddress(10);
    }

    [Fact]
    public void testNewDirectBufferZeroMemoryAddress() {
        PlatformDependent0.newDirectBuffer(0, 10);
    }

    private static void testNewDirectBufferMemoryAddress(long address) {
        assumeTrue(PlatformDependent0.hasDirectBufferNoCleanerConstructor());

        int capacity = 10;
        ByteBuffer buffer = PlatformDependent0.newDirectBuffer(address, capacity);
        Assert.Equal(address, PlatformDependent0.directBufferAddress(buffer));
        Assert.Equal(capacity, buffer.capacity());
    }

    [Fact]
    public void testMajorVersionFromJavaSpecificationVersion() {
        final SecurityManager current = System.getSecurityManager();

        try {
            System.setSecurityManager(new SecurityManager() {
                @Override
                public void checkPropertyAccess(string key) {
                    if (key.equals("java.specification.version")) {
                        // deny
                        throw new SecurityException(key);
                    }
                }

                // so we can restore the security manager
                @Override
                public void checkPermission(Permission perm) {
                }
            });

            Assert.Equal(6, PlatformDependent0.majorVersionFromJavaSpecificationVersion());
        } finally {
            System.setSecurityManager(current);
        }
    }

    [Fact]
    public void testMajorVersion() {
        Assert.Equal(6, PlatformDependent0.majorVersion("1.6"));
        Assert.Equal(7, PlatformDependent0.majorVersion("1.7"));
        Assert.Equal(8, PlatformDependent0.majorVersion("1.8"));
        Assert.Equal(8, PlatformDependent0.majorVersion("8"));
        Assert.Equal(9, PlatformDependent0.majorVersion("1.9")); // early version of JDK 9 before Project Verona
        Assert.Equal(9, PlatformDependent0.majorVersion("9"));
    }
}
