namespace Netty.NET.Common.Tests.Porting;

public class NetUtilInitializationContractTest
{
    [Fact]
    public void GraalImageSubstitutionScaffoldingIsAbsent()
    {
        var assembly = typeof(NetUtil).Assembly;
        string[] names =
        {
            "NetUtilSubstitutions",
            "NetUtilLocalhost4Accessor", "NetUtilLocalhost4LazyHolder",
            "NetUtilLocalhost6Accessor", "NetUtilLocalhost6LazyHolder",
            "NetUtilLocalhostAccessor", "NetUtilLocalhostLazyHolder",
            "NetUtilNetworkInterfacesAccessor", "NetUtilNetworkInterfacesLazyHolder"
        };

        foreach (string name in names)
        {
            Assert.Null(assembly.GetType("Netty.NET.Common." + name));
        }
    }
}
