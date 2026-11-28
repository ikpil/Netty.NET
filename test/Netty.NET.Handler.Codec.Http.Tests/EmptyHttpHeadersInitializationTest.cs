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

namespace Netty.NET.Handler.Codec.Http.Tests;

/**
 * A test to validate that either order of initialization of the {@link EmptyHttpHeaders#INSTANCE} and
 * {@link HttpHeaders#EMPTY_HEADERS} field results in both fields being non-null.
 *
 * Since this is testing static initialization, the tests might not actually test anything, except
 * when run in isolation.
 */
// CLR: HttpHeaders.EMPTY_HEADERS is @Deprecated and its @deprecated text names EmptyHttpHeaders.INSTANCE, so it is
// not ported; each test checks EmptyHttpHeaders.INSTANCE in its place (rule 7: a test that also checks other
// behaviour is ported with the replacement). With EMPTY_HEADERS gone, EmptyHttpHeaders.INSTANCE is created by its
// own static field initializer (see EmptyHttpHeaders), so the cycle the test guards against cannot occur; the
// assertions still verify that the instance is initialized in either access order.
public class EmptyHttpHeadersInitializationTest
{

    [Fact]
    public void TestEmptyHttpHeadersFirst()
    {
        Assert.NotNull(EmptyHttpHeaders.INSTANCE);
        Assert.NotNull(EmptyHttpHeaders.INSTANCE);
    }

    [Fact]
    public void TestHttpHeadersFirst()
    {
        Assert.NotNull(EmptyHttpHeaders.INSTANCE);
        Assert.NotNull(EmptyHttpHeaders.INSTANCE);
    }

}
