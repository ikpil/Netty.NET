/*
 * Copyright 2025 The Netty Project
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

namespace Netty.NET.Common.Test;

/**
 * Disable a test when slow leak detection is enabled.
 */
// CLR: a JUnit annotation meta-annotated with @DisabledIf("io.netty.util.test.Helper#slowLeakDetection"). An xUnit
// attribute cannot carry a condition of its own; FactAttribute and TheoryAttribute skip on a static property named by
// SkipWhen on SkipType, with the Skip text as the reason, so a test or every test of a class (the annotation's TYPE
// target) is marked with
//
//     [Fact(Skip = DisabledForSlowLeakDetection.Reason, SkipType = typeof(DisabledForSlowLeakDetection),
//         SkipWhen = nameof(DisabledForSlowLeakDetection.SlowLeakDetection))]
//
// The reason is the one JUnit reports for a @DisabledIf condition without a disabledReason.
public static class DisabledForSlowLeakDetection
{
    public const string Reason = "@DisabledIf(\"io.netty.util.test.Helper#slowLeakDetection\") evaluated to true";

    public static bool SlowLeakDetection => Helper.SlowLeakDetection();
}

internal sealed class Helper
{
    private static readonly ResourceLeakDetector<Helper> DETECTOR =
        ResourceLeakDetectorFactory.Instance().NewResourceLeakDetector<Helper>(typeof(Helper));

    private Helper()
    {
    }

    internal static bool SlowLeakDetection()
    {
        return DETECTOR.IsRecordEnabled();
    }
}
