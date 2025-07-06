using System;

namespace Netty.NET.Common.Internal.Logging
{
    internal class ProviderRegistration
    {
        public readonly IInternalLoggerProvider Provider;
        public readonly bool ShouldDispose;
        public readonly LogLevel MinLevel;
        public readonly Func<string, string, LogLevel, bool> Filter;

        public ProviderRegistration(IInternalLoggerProvider provider, bool shouldDispose, LogLevel minLevel, Func<string, string, LogLevel, bool> filter)
        {
            Provider = provider;
            ShouldDispose = shouldDispose;
            MinLevel = minLevel;
            Filter = filter;
        }
    }
}