using System.Runtime.CompilerServices;
using Netty.NET.Transport.Tests;

if (args.Contains("--require-native") && RuntimeFeature.IsDynamicCodeSupported)
    throw new InvalidOperationException("This validation requires an actual NativeAOT executable.");
for (int scenario = 0; scenario < NativeAotContractScenarios.Count; scenario++)
{
    NativeAotContractScenarios.Run(scenario);
    Console.WriteLine($"PASS native pipeline scenario {scenario}");
}
Console.WriteLine($"Passed {NativeAotContractScenarios.Count} scenarios; dynamic code supported: {RuntimeFeature.IsDynamicCodeSupported}");
