namespace Netty.NET.Common.Internal.Logging
{
    public interface IInternalLoggerFactory
    {
        IInternalLogger newInstance(string categoryName);
    }
}