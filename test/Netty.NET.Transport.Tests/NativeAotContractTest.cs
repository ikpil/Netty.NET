namespace Netty.NET.Transport.Tests;

public class NativeAotContractTest
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void PipelineContractsSurviveMetadataReduction(int scenario)
    {
        NativeAotContractScenarios.Run(scenario);
    }

    // CLR: real loopback TCP and UDP I/O, also run by the NativeAOT executable (NativeAotNetworkScenarios).
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void NetworkIoSurvivesNativeCompilation(int scenario)
    {
        NativeAotNetworkScenarios.Run(scenario);
    }
}
