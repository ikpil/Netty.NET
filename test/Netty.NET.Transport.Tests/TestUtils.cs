/*
 * Copyright 2012 The Netty Project
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

namespace Netty.NET.Transport.Tests;

// CLR: only io.netty.testsuite.util.TestUtils.testMethodName(TestInfo) is ported, because the ported testsuite
// harness (AbstractTestsuiteTest/AbstractComboTestsuiteTest.run) calls it. The rest of the source class (heap and
// thread dumps, compression helpers) belongs to testsuite sources that are not selected for this port.
internal static class TestUtils
{
    // CLR: JUnit's TestInfo has no xUnit counterpart. xUnit v3 exposes the running test through the static
    // TestContext.Current snapshot ("You can access it from anywhere via the static TestContext.Current",
    // https://xunit.net/docs/getting-started/v3/whats-new). ITestContext.TestMethod is null outside a test method,
    // which takes the place of the source's empty Optional<Method>.
    public static string TestMethodName(ITestContext testInfo)
    {
        string testMethodName = testInfo.TestMethod?.MethodName ?? "[unknown method]";
        if (testMethodName.Contains('['))
        {
            testMethodName = testMethodName.Substring(0, testMethodName.IndexOf('['));
        }
        return testMethodName;
    }
}
