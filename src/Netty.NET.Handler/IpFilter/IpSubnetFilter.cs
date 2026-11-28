/*
 * Copyright 2020 The Netty Project
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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.IpFilter;

/**
 * <p>
 * This class allows one to filter new {@link Channel}s based on the
 * {@link IpSubnetFilter}s passed to its constructor. If no rules are provided, all connections
 * will be accepted since {@code acceptIfNotFound} is {@code true} by default.
 * </p>
 *
 * <p>
 * If you would like to explicitly take action on rejected {@link Channel}s, you should override
 * {@link AbstractRemoteAddressFilter#channelRejected(ChannelHandlerContext, SocketAddress)}.
 * </p>
 *
 * <p>
 *     Few Points to keep in mind:
 *     <ol>
 *         <li> Since {@link IpSubnetFilter} uses Binary search algorithm, it's a good
 *         idea to insert IP addresses in incremental order. </li>
 *         <li> Remove any over-lapping CIDR.  </li>
 *     </ol>
 * </p>
 *
 */
// CLR: InetSocketAddress is IPEndPoint and instanceof Inet4Address/Inet6Address is the AddressFamily test (see
// IpSubnetFilterRule.cs). The null "no common rule type" state of the enum fields is IpFilterRuleType? (see
// IpFilterRuleType.cs).
[Sharable]
public class IpSubnetFilter : AbstractRemoteAddressFilter<IPEndPoint>
{
    private readonly bool acceptIfNotFound;
    private readonly IpSubnetFilterRule[] ipv4Rules;
    private readonly IpSubnetFilterRule[] ipv6Rules;
    private readonly IpFilterRuleType? ipFilterRuleTypeIPv4;
    private readonly IpFilterRuleType? ipFilterRuleTypeIPv6;

    /**
     * <p> Create new {@link IpSubnetFilter} Instance with specified {@link IpSubnetFilterRule} as array. </p>
     * <p> {@code acceptIfNotFound} is set to {@code true}. </p>
     *
     * @param rules {@link IpSubnetFilterRule} as an array
     */
    public IpSubnetFilter(params IpSubnetFilterRule[] rules)
        : this(true, (IList<IpSubnetFilterRule>) ObjectUtil.CheckNotNull(rules, "rules"))
    {
    }

    /**
     * <p> Create new {@link IpSubnetFilter} Instance with specified {@link IpSubnetFilterRule} as array
     * and specify if we'll accept a connection if we don't find it in the rule(s). </p>
     *
     * @param acceptIfNotFound {@code true} if we'll accept connection if not found in rule(s).
     * @param rules            {@link IpSubnetFilterRule} as an array
     */
    public IpSubnetFilter(bool acceptIfNotFound, params IpSubnetFilterRule[] rules)
        : this(acceptIfNotFound, (IList<IpSubnetFilterRule>) ObjectUtil.CheckNotNull(rules, "rules"))
    {
    }

    /**
     * <p> Create new {@link IpSubnetFilter} Instance with specified {@link IpSubnetFilterRule} as {@link List}. </p>
     * <p> {@code acceptIfNotFound} is set to {@code true}. </p>
     *
     * @param rules {@link IpSubnetFilterRule} as a {@link List}
     */
    public IpSubnetFilter(IList<IpSubnetFilterRule> rules) : this(true, rules)
    {
    }

    /**
     * <p> Create new {@link IpSubnetFilter} Instance with specified {@link IpSubnetFilterRule} as {@link List}
     * and specify if we'll accept a connection if we don't find it in the rule(s). </p>
     *
     * @param acceptIfNotFound {@code true} if we'll accept connection if not found in rule(s).
     * @param rules            {@link IpSubnetFilterRule} as a {@link List}
     */
    public IpSubnetFilter(bool acceptIfNotFound, IList<IpSubnetFilterRule> rules)
    {
        ObjectUtil.CheckNotNull(rules, "rules");

        this.acceptIfNotFound = acceptIfNotFound;

        int numAcceptIPv4 = 0;
        int numRejectIPv4 = 0;
        int numAcceptIPv6 = 0;
        int numRejectIPv6 = 0;

        List<IpSubnetFilterRule> unsortedIPv4Rules = new List<IpSubnetFilterRule>();
        List<IpSubnetFilterRule> unsortedIPv6Rules = new List<IpSubnetFilterRule>();

        // Iterate over rules and check for `null` rule.
        foreach (IpSubnetFilterRule ipSubnetFilterRule in rules)
        {
            ObjectUtil.CheckNotNull(ipSubnetFilterRule, "rule");

            if (ipSubnetFilterRule.GetFilterRule() is IpSubnetFilterRule.Ip4SubnetFilterRule)
            {
                unsortedIPv4Rules.Add(ipSubnetFilterRule);

                if (ipSubnetFilterRule.RuleType() == IpFilterRuleType.ACCEPT)
                {
                    numAcceptIPv4++;
                }
                else
                {
                    numRejectIPv4++;
                }
            }
            else
            {
                unsortedIPv6Rules.Add(ipSubnetFilterRule);

                if (ipSubnetFilterRule.RuleType() == IpFilterRuleType.ACCEPT)
                {
                    numAcceptIPv6++;
                }
                else
                {
                    numRejectIPv6++;
                }
            }
        }

        /*
         * If Number of ACCEPT rule is 0 and number of REJECT rules is more than 0,
         * then all rules are of "REJECT" type.
         *
         * In this case, we'll set `ipFilterRuleTypeIPv4` to `IpFilterRuleType.REJECT`.
         *
         * If Number of ACCEPT rules are more than 0 and number of REJECT rules is 0,
         * then all rules are of "ACCEPT" type.
         *
         * In this case, we'll set `ipFilterRuleTypeIPv4` to `IpFilterRuleType.ACCEPT`.
         */
        if (numAcceptIPv4 == 0 && numRejectIPv4 > 0)
        {
            ipFilterRuleTypeIPv4 = IpFilterRuleType.REJECT;
        }
        else if (numAcceptIPv4 > 0 && numRejectIPv4 == 0)
        {
            ipFilterRuleTypeIPv4 = IpFilterRuleType.ACCEPT;
        }
        else
        {
            ipFilterRuleTypeIPv4 = null;
        }

        if (numAcceptIPv6 == 0 && numRejectIPv6 > 0)
        {
            ipFilterRuleTypeIPv6 = IpFilterRuleType.REJECT;
        }
        else if (numAcceptIPv6 > 0 && numRejectIPv6 == 0)
        {
            ipFilterRuleTypeIPv6 = IpFilterRuleType.ACCEPT;
        }
        else
        {
            ipFilterRuleTypeIPv6 = null;
        }

        this.ipv4Rules = unsortedIPv4Rules.Count == 0 ? null : SortAndFilter(unsortedIPv4Rules);
        this.ipv6Rules = unsortedIPv6Rules.Count == 0 ? null : SortAndFilter(unsortedIPv6Rules);
    }

    protected override bool Accept(IChannelHandlerContext ctx, IPEndPoint remoteAddress)
    {
        if (ipv4Rules != null && remoteAddress.Address.AddressFamily == AddressFamily.InterNetwork)
        {
            int indexOf = Array.BinarySearch<object>(ipv4Rules, remoteAddress, IpSubnetFilterRuleComparator.INSTANCE);
            if (indexOf >= 0)
            {
                if (ipFilterRuleTypeIPv4 == null)
                {
                    return ipv4Rules[indexOf].RuleType() == IpFilterRuleType.ACCEPT;
                }
                else
                {
                    return ipFilterRuleTypeIPv4 == IpFilterRuleType.ACCEPT;
                }
            }
        }
        else if (ipv6Rules != null && remoteAddress.Address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            int indexOf = Array.BinarySearch<object>(ipv6Rules, remoteAddress, IpSubnetFilterRuleComparator.INSTANCE);
            if (indexOf >= 0)
            {
                if (ipFilterRuleTypeIPv6 == null)
                {
                    return ipv6Rules[indexOf].RuleType() == IpFilterRuleType.ACCEPT;
                }
                else
                {
                    return ipFilterRuleTypeIPv6 == IpFilterRuleType.ACCEPT;
                }
            }
        }

        return acceptIfNotFound;
    }

    // CLR: the tests call the protected accept(...) through Java package access; C# protected access is limited to
    // derived classes and an override cannot change its accessibility, so this internal forwarder serves the
    // friend test assembly, like MessageToMessageDecoder.InvokeDecode in codec-base.
    internal bool InvokeAccept(IChannelHandlerContext ctx, IPEndPoint remoteAddress)
    {
        return Accept(ctx, remoteAddress);
    }

    /**
     * <ol>
     *     <li> Sort the list </li>
     *     <li> Remove over-lapping subnet </li>
     *     <li> Sort the list again </li>
     * </ol>
     */
    // CLR: Collections.sort "is guaranteed to be stable: equal elements will not be reordered as a result of the sort"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Collections.html#sort(java.util.List,java.util.Comparator)),
    // and Enumerable.OrderBy "performs a stable sort"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.orderby?view=net-10.0), so the rules are
    // iterated in the order Java sorts the local list in place, which decides the parent rule named in the overlap
    // message. The anonymous Comparator is a Comparison delegate wrapped by Comparer<T>.Create, and the Iterator's
    // hasNext()/next() are MoveNext()/Current.
    private static IpSubnetFilterRule[] SortAndFilter(List<IpSubnetFilterRule> rules)
    {
        // Equal network addresses: broader subnet first, so it becomes the parent of the narrower one.
        IComparer<IpSubnetFilterRule> comparator = Comparer<IpSubnetFilterRule>.Create((a, b) =>
        {
            int cmp = a.CompareTo(b);
            if (cmp != 0)
            {
                return cmp;
            }

            return a.Contains(b) ? (b.Contains(a) ? 0 : -1) : 1;
        });
        using IEnumerator<IpSubnetFilterRule> iterator = rules.OrderBy(rule => rule, comparator).GetEnumerator();
        List<IpSubnetFilterRule> toKeep = new List<IpSubnetFilterRule>();

        IpSubnetFilterRule parentRule = iterator.MoveNext() ? iterator.Current : null;
        if (parentRule != null)
        {
            toKeep.Add(parentRule);
        }

        while (iterator.MoveNext())
        {

            // Grab a potential child rule.
            IpSubnetFilterRule childRule = iterator.Current;

            // If parentRule matches childRule, then there's no need to keep the child rule.
            // Otherwise, the rules are distinct and we need both.
            if (parentRule.Contains(childRule))
            {
                // The child rule is a subset of the parent rule. If they don't share the same rule type
                // we can't safely drop the child rule, as doing so would silently change the outcome for
                // addresses covered by the child but not otherwise expressible with the parent's ruleType.
                // This binary-search based design can't represent longest-prefix-match semantics, so reject
                // the unsupported configuration instead of failing open.
                if (childRule.RuleType() != parentRule.RuleType())
                {
                    throw new ArgumentException(
                            "IpSubnetFilter does not support overlapping rules with different ruleTypes: "
                                    + parentRule + " (" + parentRule.RuleType() + ") overlaps "
                                    + childRule + " (" + childRule.RuleType() + "). "
                                    + "Use RuleBasedIpFilter for mixed ACCEPT/REJECT carve-outs.");
                }

                continue;
            }

            toKeep.Add(childRule);
            // Then we'll keep the child rule around as the parent for the next round.
            parentRule = childRule;
        }

        return toKeep.ToArray();
    }
}
