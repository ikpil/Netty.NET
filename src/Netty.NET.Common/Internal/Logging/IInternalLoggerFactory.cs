namespace Netty.NET.Common.Internal.Logging
{
    public interface IInternalLoggerFactory
    {
        IInternalLogger NewInstance(string categoryName);
    }
}