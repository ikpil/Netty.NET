/*
 * Copyright 2026 The Netty Project
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
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Ssl;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR supplemental (no Java counterpart): SslContext.BuildKeyManagerFactory keeps the entries of its PKCS #12 load alive
// with the SslStreamCertificateContext. rsaValidations-server-keystore.p12 holds its key entry's certificate twice, and
// both loaded entries share one key container; before the entries were kept, finalizing the entry that is not the
// context's target deleted the key, and the SslStream server failed with SEC_E_UNKNOWN_CREDENTIALS (0x8009030D). That
// made JdkSslEngineTest.TestRSASSAPSS fail intermittently in full test runs, and every time with a forced GC after the
// key stores were loaded (measured 2026-11-26, docs/handler-porting.md). The case forces that GC and then completes a
// TLS handshake with the context.
public class SslContextKeyStoreLifetimeTest
{
    [Fact(Timeout = 30000)]
    public async Task ServerKeySurvivesFinalizationOfDuplicateKeyStoreEntries()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] keyStore = File.ReadAllBytes(
                ResourcesUtil.GetFile(GetType(), "rsaValidations-server-keystore.p12").FullName);
        SslStreamCertificateContext context =
                SslContext.BuildKeyManagerFactory(keyStore, null, "password".ToCharArray(), null);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint) listener.LocalEndpoint).Port;
        Task<TcpClient> accepted = listener.AcceptTcpClientAsync(token).AsTask();
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, token);
        using TcpClient serverSocket = await accepted;
        using SslStream server = new SslStream(serverSocket.GetStream(), false);
        using SslStream clientStream = new SslStream(client.GetStream(), false);

        Task serverHandshake = server.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions { ServerCertificateContext = context }, token);
        Task clientHandshake = clientStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
        }, token);
        await Task.WhenAll(serverHandshake, clientHandshake);

        Assert.True(server.IsAuthenticated);
        Assert.True(clientStream.IsAuthenticated);
        GC.KeepAlive(context);
    }
}
