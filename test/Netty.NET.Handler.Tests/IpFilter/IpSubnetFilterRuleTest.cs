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
using Netty.NET.Handler.IpFilter;

namespace Netty.NET.Handler.Tests.IpFilter;

// CLR: IllegalArgumentException is ArgumentException; the anonymous Executables are lambdas. assertDoesNotThrow is a
// plain call (an exception fails the test), with Record.Exception and Assert.Null keeping the explicit check. The AssertJ
// assertThat(rule.getIpAddress()).isEqualTo("10.10.0.0") is Assert.Equal on the internal GetIpAddress().
public class IpSubnetFilterRuleTest
{

    [Fact]
    public void CreateInvalidIpCidrRule()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            new IpSubnetFilterRule("192.168.0.0", IpFilterRuleType.ACCEPT);
        });
    }

    [Fact]
    public void CreateValidIpCidrRule()
    {
        Assert.Null(Record.Exception(() =>
        {
            new IpSubnetFilterRule("192.168.0.0/24", IpFilterRuleType.ACCEPT);
        }));
    }

    [Fact]
    public void ValidateIpAddressFromIpCidrString()
    {
        IpSubnetFilterRule rule = new IpSubnetFilterRule("10.10.0.0/16", IpFilterRuleType.ACCEPT);
        Assert.Equal("10.10.0.0", rule.GetIpAddress());
    }
}
