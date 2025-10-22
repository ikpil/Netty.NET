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

using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Internal;


public class NativeLibraryLoaderTest {

    private static string OS_ARCH = SystemPropertyUtil.get("os.arch");
    private bool is_x86_64() {
        return "x86_64".Equals(OS_ARCH) || "amd64".Equals(OS_ARCH);
    }

    [Fact]
    void testFileNotFound() {
        try {
            NativeLibraryLoader.load(UUID.randomUUID().ToString(), NativeLibraryLoaderTest.class.getClassLoader());
            Assert.Fail();
        } catch (UnsatisfiedLinkError error) {
            Assert.True(error.getCause() instanceof FileNotFoundException);
            verifySuppressedException(error, UnsatisfiedLinkError.class);
        }
    }

    [Fact]
    void testFileNotFoundWithNullClassLoader() {
        try {
            NativeLibraryLoader.load(UUID.randomUUID().ToString(), null);
            Assert.Fail();
        } catch (UnsatisfiedLinkError error) {
            Assert.True(error.getCause() instanceof FileNotFoundException);
            verifySuppressedException(error, ClassNotFoundException.class);
        }
    }

    [Fact]
    @EnabledOnOs(LINUX)
    @EnabledIf("is_x86_64")
    void testMultipleResourcesWithSameContentInTheClassLoader() throws MalformedURLException {
        URL url1 = new File("src/test/data/NativeLibraryLoader/1").toURI().toURL();
        URL url2 = new File("src/test/data/NativeLibraryLoader/2").toURI().toURL();
        final URLClassLoader loader = new URLClassLoader(new URL[] {url1, url2});
        final string resourceName = "test3";

        NativeLibraryLoader.load(resourceName, loader);
        Assert.True(true);
    }

    [Fact]
    @EnabledOnOs(LINUX)
    @EnabledIf("is_x86_64")
    void testMultipleResourcesInTheClassLoader() throws MalformedURLException {
        URL url1 = new File("src/test/data/NativeLibraryLoader/1").toURI().toURL();
        URL url2 = new File("src/test/data/NativeLibraryLoader/2").toURI().toURL();
        final URLClassLoader loader = new URLClassLoader(new URL[] {url1, url2});
        final string resourceName = "test1";

        Exception ise = Assert.Throws<IllegalStateException>(new Executable() {
            @Override
            public void execute() {
                NativeLibraryLoader.load(resourceName, loader);
            }
        });
        Assert.True(ise.getMessage()
                    .contains("Multiple resources found for 'META-INF/native/lib" + resourceName + ".so'"));
    }

    [Fact]
    @EnabledOnOs(LINUX)
    @EnabledIf("is_x86_64")
    void testSingleResourceInTheClassLoader() throws MalformedURLException {
        URL url1 = new File("src/test/data/NativeLibraryLoader/1").toURI().toURL();
        URL url2 = new File("src/test/data/NativeLibraryLoader/2").toURI().toURL();
        URLClassLoader loader = new URLClassLoader(new URL[] {url1, url2});
        string resourceName = "test2";

        NativeLibraryLoader.load(resourceName, loader);
        Assert.True(true);
    }

    private static void verifySuppressedException(UnsatisfiedLinkError error,
            Class<?> expectedSuppressedExceptionClass) {
        try {
            Exception[] suppressed = error.getCause().getSuppressed();
            Assert.True(suppressed.length == 1);
            Assert.True(suppressed[0] instanceof UnsatisfiedLinkError);
            suppressed = (suppressed[0]).getSuppressed();
            Assert.True(suppressed.length == 1);
            Assert.True(expectedSuppressedExceptionClass.isInstance(suppressed[0]));
        } catch (Exception e) {
            throw new Exception(e);
        }
    }
}
