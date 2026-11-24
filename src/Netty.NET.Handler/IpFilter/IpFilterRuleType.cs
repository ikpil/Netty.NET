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

namespace Netty.NET.Handler.IpFilter;

/**
 * Used in {@link IpFilterRule} to decide if a matching IP Address should be allowed or denied to connect.
 */
// CLR: a simple Java enum is a C# enum with the Java constant names (owner rule). Being "a value type"
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum), an
// IpFilterRuleType cannot be null, so IpSubnetFilterRule's checkNotNull(ruleType, "ruleType") has no counterpart and
// IpSubnetFilter keeps its "no common rule type" null state in IpFilterRuleType?.
public enum IpFilterRuleType
{
    ACCEPT,
    REJECT
}
