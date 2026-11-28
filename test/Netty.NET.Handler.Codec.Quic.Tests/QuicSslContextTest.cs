/*
 * Copyright 2021 The Netty Project
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

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: both methods of QuicSslContextTest (testSessionContextSettingsForClient,
// testSessionContextSettingsForServer) are CLR-inapplicable, so the class declares no test. They read the session
// cache size and timeout back from SslContext.sessionCacheSize()/sessionTimeout() and from the SSLSessionContext
// of sessionContext(), change them through SSLSessionContext.setSessionCacheSize/setSessionTimeout, and read the
// endpoint identification algorithm from the SSLParameters of context.newEngine(null). The handler port has no
// SSLEngine facade and no SSLSessionContext (SslContext.sessionCacheSize()/sessionTimeout() only read the
// SSLSessionContext and are not ported, docs/handler-ssl-design.md), IQuicSslSessionContext has no
// SSLSessionContext base (msquic keeps the TLS session cache), and QuicSslEngine carries no SSLParameters.
public class QuicSslContextTest
{
}
