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

using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Netty.NET.Transport.Tests;

// CLR: io.netty.testsuite.util.TestUtils. isSctpSupported() and testMethodName(TestInfo) are ported. The heap and
// thread dumps (dump, dumpHeap, dumpThreads, timestamp and the HotSpotDiagnosticMXBean fields) and the xz compression
// of heap dumps (compressHeapDumps) use JVM-only APIs: HotSpotDiagnosticMXBean is the "Diagnostic management interface
// for the HotSpot Virtual Machine" and ThreadMXBean the JVM thread system's management interface, while a .NET process
// dump is collected from outside the process with dotnet-dump
// (https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump). They are recorded as JVM-only in the
// Transport manifest and SocketGatheringWriteSourceTest does not call them. The private constructor of the final class
// is the static class.
internal static class TestUtils
{
    /**
     * Return {@code true} if SCTP is supported by the running os.
     *
     */
    public static bool IsSctpSupported()
    {
        // CLR: System.getProperty("os.name") has no CLR property. The JDK reports "Linux" there, which
        // OperatingSystem.IsLinux() identifies; RuntimeInformation.OSDescription stands for every other platform.
        string os = (OperatingSystem.IsLinux() ? "Linux" : RuntimeInformation.OSDescription).ToLowerInvariant();
        if ("unix".Equals(os) || "linux".Equals(os) || "sun".Equals(os) || "solaris".Equals(os))
        {
            try
            {
                // Try to open a SCTP Channel, by using reflection to make it compile also on
                // operation systems that not support SCTP like OSX and Windows
                // CLR: SctpChannel.open() opens a one-to-one style SCTP socket, a SOCK_STREAM socket of protocol
                // IPPROTO_SCTP (RFC 6458 section 3, https://www.rfc-editor.org/rfc/rfc6458#section-3). ProtocolType
                // has no SCTP member, so the IANA protocol number 132 is passed. ConstructorInfo.Invoke wraps an
                // exception thrown by the constructor in TargetInvocationException
                // (https://learn.microsoft.com/en-us/dotnet/api/system.reflection.constructorinfo.invoke), as
                // Method.invoke wraps one in InvocationTargetException, so the catch clauses below see the same
                // exception kinds as the source's.
                Socket channel = (Socket) typeof(Socket)
                    .GetConstructor(new[] { typeof(SocketType), typeof(ProtocolType) })!
                    .Invoke(new object[] { SocketType.Stream, (ProtocolType) 132 });

                try
                {
                    channel.Close();
                }
                // CLR: java.io.IOException of a socket operation is SocketException.
                catch (SocketException)
                {
                    // ignore
                }
            }
            // CLR: UnsupportedOperationException is NotSupportedException.
            catch (NotSupportedException e)
            {
                // This exception may get thrown if the OS does not have
                // the shared libs installed.
                Console.Write("Not supported: " + e.Message);
                return false;
            }
            catch (Exception t)
            {
                if (!(t is SocketException))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    /**
     * Returns the method name of the current test.
     */
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
