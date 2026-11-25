/*
 * Copyright 2019 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the "License");
 * you may not use this file except in compliance with the License. You may obtain a copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License is
 * distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and limitations under the License.
 */

using System;
using System.Collections.Generic;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using static Netty.NET.Handler.Codec.Http.WebSocketx.WebSocketCloseStatus;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: the static import of WebSocketCloseStatus.* is using static. AssertJ's assertThat(a).isGreaterThan(b) and
// isLessThan(b) compare through Comparable, so they are Assert.True(a.CompareTo(b) > 0) and < 0; the anonymous
// ThrowingCallable is a lambda for Assert.ThrowsAny<ArgumentException> (AssertJ accepts subtypes too).
// SortedSet<Integer>/TreeSet is SortedSet<int> (first()/last() are Min/Max, retainAll is IntersectWith), Arrays.asList
// is a List<T>, and assertEquals(invalidCodes, Collections.emptySet()) is Assert.Empty. Short.MIN_VALUE/MAX_VALUE are
// short.MinValue/MaxValue. assertSame/assertNotSame are Assert.Same/NotSame.
public class WebSocketCloseStatusTest
{

    private readonly List<WebSocketCloseStatus> validCodes = new List<WebSocketCloseStatus>
    {
        NORMAL_CLOSURE,
        ENDPOINT_UNAVAILABLE,
        PROTOCOL_ERROR,
        INVALID_MESSAGE_TYPE,
        INVALID_PAYLOAD_DATA,
        POLICY_VIOLATION,
        MESSAGE_TOO_BIG,
        MANDATORY_EXTENSION,
        INTERNAL_SERVER_ERROR,
        SERVICE_RESTART,
        TRY_AGAIN_LATER,
        BAD_GATEWAY
    };

    [Fact]
    public void TestToString()
    {
        Assert.Equal("1000 Bye", NORMAL_CLOSURE.ToString());
    }

    [Fact]
    public void TestKnownStatuses()
    {
        Assert.Same(NORMAL_CLOSURE, ValueOf(1000));
        Assert.Same(ENDPOINT_UNAVAILABLE, ValueOf(1001));
        Assert.Same(PROTOCOL_ERROR, ValueOf(1002));
        Assert.Same(INVALID_MESSAGE_TYPE, ValueOf(1003));
        Assert.Same(EMPTY, ValueOf(1005));
        Assert.Same(ABNORMAL_CLOSURE, ValueOf(1006));
        Assert.Same(INVALID_PAYLOAD_DATA, ValueOf(1007));
        Assert.Same(POLICY_VIOLATION, ValueOf(1008));
        Assert.Same(MESSAGE_TOO_BIG, ValueOf(1009));
        Assert.Same(MANDATORY_EXTENSION, ValueOf(1010));
        Assert.Same(INTERNAL_SERVER_ERROR, ValueOf(1011));
        Assert.Same(SERVICE_RESTART, ValueOf(1012));
        Assert.Same(TRY_AGAIN_LATER, ValueOf(1013));
        Assert.Same(BAD_GATEWAY, ValueOf(1014));
        Assert.Same(TLS_HANDSHAKE_FAILED, ValueOf(1015));
    }

    [Fact]
    public void TestNaturalOrder()
    {
        Assert.True(PROTOCOL_ERROR.CompareTo(NORMAL_CLOSURE) > 0);
        Assert.True(PROTOCOL_ERROR.CompareTo(ValueOf(1001)) > 0);
        Assert.Equal(PROTOCOL_ERROR, ValueOf(1002));
        Assert.True(PROTOCOL_ERROR.CompareTo(INVALID_MESSAGE_TYPE) < 0);
        Assert.True(PROTOCOL_ERROR.CompareTo(ValueOf(1007)) < 0);
    }

    [Fact]
    public void TestUserDefinedStatuses()
    {
        // Given, when
        WebSocketCloseStatus feedTimeot = new WebSocketCloseStatus(6033, "Feed timed out");
        WebSocketCloseStatus untradablePrice = new WebSocketCloseStatus(6034, "Untradable price");

        // Then
        Assert.NotSame(feedTimeot, ValueOf(6033));
        Assert.Equal(6033, feedTimeot.Code());
        Assert.Equal("Feed timed out", feedTimeot.ReasonText());

        Assert.NotSame(untradablePrice, ValueOf(6034));
        Assert.Equal(6034, untradablePrice.Code());
        Assert.Equal("Untradable price", untradablePrice.ReasonText());
    }

    [Fact]
    public void TestRfc6455CodeValidation()
    {
        // Given
        List<int> knownCodes = new List<int>
        {
            NORMAL_CLOSURE.Code(),
            ENDPOINT_UNAVAILABLE.Code(),
            PROTOCOL_ERROR.Code(),
            INVALID_MESSAGE_TYPE.Code(),
            INVALID_PAYLOAD_DATA.Code(),
            POLICY_VIOLATION.Code(),
            MESSAGE_TOO_BIG.Code(),
            MANDATORY_EXTENSION.Code(),
            INTERNAL_SERVER_ERROR.Code(),
            SERVICE_RESTART.Code(),
            TRY_AGAIN_LATER.Code(),
            BAD_GATEWAY.Code()
        };

        SortedSet<int> invalidCodes = new SortedSet<int>();

        // When
        for (int statusCode = short.MinValue; statusCode < short.MaxValue; statusCode++)
        {
            if (!IsValidStatusCode(statusCode))
            {
                invalidCodes.Add(statusCode);
            }
        }

        // Then
        Assert.Equal(0, invalidCodes.Min);
        Assert.Equal(2999, invalidCodes.Max);
        Assert.Equal(3000 - validCodes.Count, invalidCodes.Count);

        invalidCodes.IntersectWith(knownCodes);
        Assert.Empty(invalidCodes);
    }

    [Fact]
    public void TestValidationEnabled()
    {
        Assert.ThrowsAny<ArgumentException>(() => new WebSocketCloseStatus(1006, "validation disabled"));
    }

    [Fact]
    public void TestValidationDisabled()
    {
        WebSocketCloseStatus status = new WebSocketCloseStatus(1006, "validation disabled", false);
        Assert.Equal(1006, status.Code());
        Assert.Equal("validation disabled", status.ReasonText());
    }
}
