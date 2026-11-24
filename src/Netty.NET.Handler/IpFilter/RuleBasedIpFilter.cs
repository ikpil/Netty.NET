/*
 * Copyright 2014 The Netty Project
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

using System.Collections.Generic;
using System.Net;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.IpFilter;

/**
 * <p>
 * This class allows one to filter new {@link Channel}s based on the
 * {@link IpFilterRule}s passed to its constructor. If no rules are provided, all connections
 * will be accepted.
 * </p>
 *
 * <p>
 * If you would like to explicitly take action on rejected {@link Channel}s, you should override
 * {@link AbstractRemoteAddressFilter#channelRejected(ChannelHandlerContext, SocketAddress)}.
 * </p>
 *
 * <p> Consider using {@link IpSubnetFilter} for better performance while not as
 * general purpose as this filter. </p>
 */
// CLR: the @Deprecated RuleBasedIpFilter(IpFilterRule...) names its replacement
// RuleBasedIpFilter(boolean, IpFilterRule...), so it is not ported (owner decision 2026-11-16); its Javadoc is
// recorded in the manifest. ArrayList is List and InetSocketAddress is IPEndPoint.
[Sharable]
public class RuleBasedIpFilter : AbstractRemoteAddressFilter<IPEndPoint>
{
    private readonly bool acceptIfNotFound;
    private readonly List<IIpFilterRule> rules;

    /**
     * Create new Instance of {@link RuleBasedIpFilter} and filter incoming connections
     * based on their IP address and {@code rules} applied.
     *
     * @param acceptIfNotFound If {@code true} then accept connection from IP Address if it
     *                         doesn't match any rule.
     * @param rules            An array of {@link IpFilterRule} containing all rules.
     */
    public RuleBasedIpFilter(bool acceptIfNotFound, params IIpFilterRule[] rules)
    {
        ObjectUtil.CheckNotNull(rules, "rules");

        this.acceptIfNotFound = acceptIfNotFound;
        this.rules = new List<IIpFilterRule>(rules.Length);

        foreach (IIpFilterRule rule in rules)
        {
            if (rule != null)
            {
                this.rules.Add(rule);
            }
        }
    }

    protected override bool Accept(IChannelHandlerContext ctx, IPEndPoint remoteAddress)
    {
        foreach (IIpFilterRule rule in rules)
        {
            if (rule.Matches(remoteAddress))
            {
                return rule.RuleType() == IpFilterRuleType.ACCEPT;
            }
        }

        return acceptIfNotFound;
    }
}
