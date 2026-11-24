/*
 * Copyright 2024 The Netty Project
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
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.PkiTesting;

/**
 * A simple HTTP server that serves Certificate Revocation Lists.
 * <p>
 * Issuer certificates can be registered with the server, and revocations of their certificates and be published
 * and added to the revocation lists.
 * <p>
 * The server is only intended for testing usage, and runs entirely in a single thread.
 *
 * @implNote The CRLs will have the same very short life times, to minimize caching effects in tests.
 * This currently means the time in the "this update" and "next update" fields are set to the same value.
 */
// CLR: com.sun.net.httpserver.HttpServer is System.Net.HttpListener, "a simple, programmatically controlled HTTP
// protocol listener" (https://learn.microsoft.com/en-us/dotnet/api/system.net.httplistener?view=net-10.0), which serves
// URI prefixes ("composed of a scheme (http or https), a host, an optional port, and an optional path"; a listener on
// http://127.0.0.1:<port>/ and http://localhost:<port>/ starts without administrator rights on this host). The
// requests are handled one after the other by one dispatch loop, as the default executor of HttpServer "uses the
// thread which was created by the start() method"
// (https://docs.oracle.com/en/java/javase/17/docs/api/jdk.httpserver/com/sun/net/httpserver/HttpServer.html).
// nextUpdate differs from @implNote: the Windows chain engine treats a CRL whose nextUpdate equals thisUpdate as
// expired (X509Chain reported OfflineRevocation and RevocationStatusUnknown on this host), so nextUpdate is ten
// seconds after thisUpdate; with that, a revoked certificate is reported Revoked and a CRL that has expired is fetched
// again (measured with X509Chain and X509RevocationMode.Online on this host). Each issuer still has its own CRL path,
// so a test's CRL is fetched after its revocations. X509Certificate map keys compare encoded forms (Certificate.equals,
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/security/cert/Certificate.html), so the issuers
// map is keyed by the SHA-256 hash of the certificate's encoding; Provider is not ported (see Algorithms).
public sealed class RevocationServer
{
    private static volatile RevocationServer instance;
    // CLR: synchronized (RevocationServer.class) locks a private object, the C# convention for a type-wide lock.
    private static readonly object INSTANCE_LOCK = new object();

    private readonly HttpListener crlServer;
    private readonly string crlBaseAddress;
    // CLR: AtomicInteger is an int updated with Interlocked.Increment.
    private int issuerCounter;
    private readonly ConcurrentDictionary<string, CrlInfo> issuers;
    private readonly ConcurrentDictionary<string, CrlInfo> paths;
    private readonly Action<HttpListenerContext> handler;

    /**
     * Get the shared revocation server instance.
     * This will start the server, if it isn't already running, and bind it to a random port on the loopback address.
     * @return The revocation server instance.
     * @throws Exception If the server failed to start.
     */
    public static RevocationServer GetInstance()
    {
        if (instance != null)
        {
            return instance;
        }
        lock (INSTANCE_LOCK)
        {
            RevocationServer server = instance;
            if (server == null)
            {
                server = new RevocationServer();
                server.Start();
                instance = server;
            }
            return server;
        }
    }

    private RevocationServer()
    {
        // Use the JDK built-in HttpServer to avoid any circular dependencies with Netty itself.
        // CLR: HttpListener is the BCL HTTP server. Its prefixes need an explicit port, so the random port that
        // HttpServer.create(new InetSocketAddress(loopback, 0), 0) binds is taken from a loopback socket bound to port 0
        // and then registered; the listener starts here, so a port taken in between is replaced, as binding does in
        // HttpServer.create.
        crlServer = StartListener(out int port);
        crlBaseAddress = "http://localhost:" + port;
        issuerCounter = 0;
        issuers = new ConcurrentDictionary<string, CrlInfo>();
        paths = new ConcurrentDictionary<string, CrlInfo>();
        // CLR: sendResponseHeaders(rCode, 0) selects chunked transfer encoding ("If the response length parameter is
        // zero, then chunked transfer encoding is used",
        // https://docs.oracle.com/en/java/javase/17/docs/api/jdk.httpserver/com/sun/net/httpserver/HttpExchange.html)
        // and the 404/405 responses send no data; HttpListenerResponse.Close() sends them without a body.
        handler = exchange =>
        {
            if ("GET".Equals(exchange.Request.HttpMethod))
            {
                string path = exchange.Request.Url.AbsolutePath;
                if (!paths.TryGetValue(path, out CrlInfo info))
                {
                    exchange.Response.StatusCode = 404;
                    exchange.Response.Close();
                    return;
                }
                byte[] crl = GenerateCrl(info);
                exchange.Response.ContentType = "application/pkix-crl";
                exchange.Response.StatusCode = 200;
                exchange.Response.ContentLength64 = crl.Length;
                using (Stream @out = exchange.Response.OutputStream)
                {
                    @out.Write(crl);
                    @out.Flush();
                }
            }
            else
            {
                exchange.Response.StatusCode = 405;
            }
            exchange.Response.Close();
        };
    }

    private static HttpListener StartListener(out int port)
    {
        for (int attempt = 0; ; attempt++)
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            port = ((IPEndPoint) probe.LocalEndpoint).Port;
            probe.Stop();
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            listener.Prefixes.Add("http://localhost:" + port + "/");
            try
            {
                listener.Start();
                return listener;
            }
            catch (HttpListenerException) when (attempt < 10)
            {
                listener.Close();
            }
        }
    }

    private void Start()
    {
        // CLR: HttpServer.start() starts its dispatcher thread, which is a daemon thread only when the starting thread is
        // one ("is a daemon thread if and only if the creating thread is a daemon",
        // https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Thread.html), hence the ForkJoin pool. A
        // .NET thread does not inherit that state: "Threads that belong to the managed thread pool ... are background
        // threads" and "a background thread does not keep the managed execution environment running"
        // (https://learn.microsoft.com/en-us/dotnet/standard/threading/foreground-and-background-threads), so the dispatch
        // loop always runs on the thread pool and the isDaemon() branch has no counterpart.
        // It's important the CRL server creates a daemon thread,
        // because it's a singleton and won't be stopped except by terminating the JVM.
        // Threads in the ForkJoin common pool are always daemon, and JUnit 5 initializes
        // it anyway, so we can let it call start() for us.
        Task.Run(DispatchAsync);
    }

    // CLR: HttpServer's dispatcher calls the handler for each exchange and closes the exchange when the handler throws;
    // the loop does the same with HttpListenerResponse.Abort(), and ends when the listener is closed.
    private async Task DispatchAsync()
    {
        while (true)
        {
            HttpListenerContext exchange;
            try
            {
                exchange = await crlServer.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (!crlServer.IsListening)
            {
                return;
            }
            try
            {
                handler(exchange);
            }
            catch (Exception)
            {
                exchange.Response.Abort();
            }
        }
    }

    /**
     * Register an issuer with the revocation server.
     * This must be done before CRLs can be served for that issuer, and before any of its certificates can be revoked.
     * @param issuer The issuer to register.
     */
    // CLR: computeIfAbsent runs the mapping function once for an absent key, while ConcurrentDictionary.GetOrAdd's
    // "valueFactory may be called multiple times"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2.getoradd?view=net-10.0),
    // which would take a second counter value and path, so the lookup and insertion run under a lock.
    public void Register(X509Bundle issuer)
    {
        string key = Key(issuer.GetCertificate());
        lock (issuers)
        {
            if (issuers.ContainsKey(key))
            {
                return;
            }
            string path = "/crl/" + Interlocked.Increment(ref issuerCounter) + ".crl";
            Uri uri = new Uri(crlBaseAddress + path);
            CrlInfo info = new CrlInfo(issuer, uri);
            paths[path] = info;
            issuers[key] = info;
        }
    }

    /**
     * Revoke the given certificate with the given revocation time.
     * <p>
     * The issuer of the given certificate must be {@linkplain #register(X509Bundle) registered} before its certifiactes
     * can be revoked.
     * @param cert The certificate to revoke.
     * @param time The time of revocation.
     */
    public void Revoke(X509Bundle cert, DateTimeOffset time)
    {
        X509Certificate2[] certPath = cert.GetCertificatePathWithRoot();
        X509Certificate2 issuer = certPath.Length == 1 ? certPath[0] : certPath[1];
        if (issuers.TryGetValue(Key(issuer), out CrlInfo info))
        {
            info.revokedCerts[Algorithms.GetSerialNumber(cert.GetCertificate())] = time;
        }
        else
        {
            throw new ArgumentException("Not a registered issuer: " + issuer.SubjectName.Name);
        }
    }

    /**
     * Get the URI of the Certificate Revocation List for the given issuer.
     * @param issuer The issuer to get the CRL for.
     * @return The URI to the CRL for the given issuer,
     * or {@code null} if the issuer is not {@linkplain #register(X509Bundle) registered}.
     */
    public Uri GetCrlUri(X509Bundle issuer)
    {
        if (issuers.TryGetValue(Key(issuer.GetCertificate()), out CrlInfo info))
        {
            return info.uri;
        }
        return null;
    }

    // CLR: IllegalStateException is InvalidOperationException; Signed is the CRL builder's signature generator (see
    // CertificateList).
    private static byte[] GenerateCrl(CrlInfo info)
    {
        X509Bundle issuer = info.issuer;
        ConcurrentDictionary<BigInteger, DateTimeOffset> certs = info.revokedCerts;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CertificateList list = new CertificateList(issuer, now, now.AddSeconds(10), certs);
        try
        {
            Signed signed = new Signed(issuer);
            return list.GetEncoded(signed);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Failed to sign CRL", e);
        }
    }

    private static string Key(X509Certificate2 certificate)
    {
        return certificate.GetCertHashString(HashAlgorithmName.SHA256);
    }

    // CLR: the fields are internal because RevocationServer reads them; a C# private member is limited to its containing
    // type (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/accessibility-levels). The
    // Provider field is not ported (see Algorithms).
    private sealed class CrlInfo
    {
        internal readonly X509Bundle issuer;
        internal readonly Uri uri;
        internal readonly ConcurrentDictionary<BigInteger, DateTimeOffset> revokedCerts;

        internal CrlInfo(X509Bundle issuer, Uri uri)
        {
            this.issuer = issuer;
            this.uri = uri;
            revokedCerts = new ConcurrentDictionary<BigInteger, DateTimeOffset>();
        }
    }
}
