namespace Netty.NET.Transport.Tests;

public class ChannelIdContractTest
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void NativeIdentityContracts(int scenario) => ChannelIdContractScenarios.Run(scenario);
}
