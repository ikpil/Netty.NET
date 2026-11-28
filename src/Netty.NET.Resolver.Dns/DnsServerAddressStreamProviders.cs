/*
 * Copyright 2017 The Netty Project
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
using System.IO;
using System.Reflection;
using System.Threading;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Resolver.Dns;

/**
 * Utility methods related to {@link DnsServerAddressStreamProvider}.
 */
// CLR: the final utility class with a private constructor is a static class; the static initializer is a static
// constructor and the Holder class a nested static class, whose static fields are initialized on first use. The macOS
// provider lives in another module (resolver-dns-classes-macos, not ported); the reflective load is kept: the Java class
// name is the assembly-qualified CLR name that module would get under the naming rule, and Class.forName is
// Type.GetType(name, throwOnError: true), which throws TypeLoadException when the type is not found and
// FileNotFoundException when "the assembly or one of its dependencies was not found"
// (https://learn.microsoft.com/en-us/dotnet/api/system.type.gettype?view=net-10.0) - together ClassNotFoundException.
// getConstructor() is GetConstructor(Type.EmptyTypes), which returns null where Java throws NoSuchMethodException
// (https://learn.microsoft.com/en-us/dotnet/api/system.type.getconstructor?view=net-10.0), so a null constructor throws
// MissingMethodException; newInstance() is ConstructorInfo.Invoke, which reports an exception of the constructor as
// TargetInvocationException (https://learn.microsoft.com/en-us/dotnet/api/system.reflection.constructorinfo.invoke?view=net-10.0)
// where Java reports InvocationTargetException, and IllegalAccessException/InstantiationException are
// MemberAccessException. AccessController.doPrivileged is deprecated for removal and "only useful in conjunction with
// the Security Manager" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/security/AccessController.html);
// .NET has no security manager, so the PrivilegedAction (an anonymous class) is the method LoadProviderClass, run
// directly. The log text names the CLR module instead of the Maven artifact. Throwable is Exception and toString() is
// ThrowableUtil.ThrowableToString. System.nanoTime() is SystemTimer.NanoTime() (docs/handler-porting.md) and the
// AtomicLong a long field with Interlocked, because Common's AtomicLong has no long constructor.
public static class DnsServerAddressStreamProviders
{

    private static readonly IInternalLogger LOGGER =
            InternalLoggerFactory.GetInstance(typeof(DnsServerAddressStreamProviders));
    private static readonly ConstructorInfo STREAM_PROVIDER_CONSTRUCTOR;
    private const string MACOS_PROVIDER_CLASS_NAME =
            "Netty.NET.Resolver.Dns.MacOS.MacOSDnsServerAddressStreamProvider, Netty.NET.Resolver.Dns.MacOS";

    static DnsServerAddressStreamProviders()
    {
        ConstructorInfo constructor = null;

        if (PlatformDependent.IsOsx())
        {
            try
            {
                // As MacOSDnsServerAddressStreamProvider is contained in another jar which depends on this jar
                // we use reflection to use it if its on the classpath.
                object maybeProvider = LoadProviderClass();
                if (maybeProvider is Type)
                {
                    Type providerClass = (Type) maybeProvider;
                    constructor = providerClass.GetConstructor(Type.EmptyTypes);
                    if (constructor == null)
                    {
                        throw new MissingMethodException(providerClass.FullName, ".ctor");
                    }

                    constructor.Invoke(null);  // ctor ensures availability
                    LOGGER.Debug("{}: available", MACOS_PROVIDER_CLASS_NAME);
                }
                else
                {
                    throw (Exception) maybeProvider;
                }
            }
            catch (Exception cause) when (cause is TypeLoadException || cause is FileNotFoundException)
            {
                LOGGER.Warn("Can not find {} in the classpath, fallback to system defaults. This may result in "
                        + "incorrect DNS resolutions on MacOS. Check whether you have a dependency on "
                        + "'Netty.NET.Resolver.Dns.MacOS'", MACOS_PROVIDER_CLASS_NAME);
            }
            catch (Exception cause)
            {
                if (LOGGER.IsDebugEnabled())
                {
                    LOGGER.Error("Unable to load {}, fallback to system defaults. This may result in "
                            + "incorrect DNS resolutions on MacOS. Check whether you have a dependency on "
                            + "'Netty.NET.Resolver.Dns.MacOS'", MACOS_PROVIDER_CLASS_NAME, cause);
                }
                else
                {
                    LOGGER.Error("Unable to load {}, fallback to system defaults. This may result in "
                            + "incorrect DNS resolutions on MacOS. Check whether you have a dependency on "
                            + "'Netty.NET.Resolver.Dns.MacOS'. Use DEBUG level to see the full stack: {}",
                            MACOS_PROVIDER_CLASS_NAME,
                            cause.InnerException != null ? ThrowableUtil.ThrowableToString(cause.InnerException) :
                                    ThrowableUtil.ThrowableToString(cause));
                }

                constructor = null;
            }
        }

        STREAM_PROVIDER_CONSTRUCTOR = constructor;
    }

    private static object LoadProviderClass()
    {
        try
        {
            return Type.GetType(MACOS_PROVIDER_CLASS_NAME, true);
        }
        catch (Exception cause)
        {
            return cause;
        }
    }

    /**
     * A {@link DnsServerAddressStreamProvider} which inherits the DNS servers from your local host's configuration.
     * <p>
     * Note that only macOS and Linux are currently supported.
     * @return A {@link DnsServerAddressStreamProvider} which inherits the DNS servers from your local host's
     * configuration.
     */
    public static IDnsServerAddressStreamProvider PlatformDefault()
    {
        if (STREAM_PROVIDER_CONSTRUCTOR != null)
        {
            try
            {
                return (IDnsServerAddressStreamProvider) STREAM_PROVIDER_CONSTRUCTOR.Invoke(null);
            }
            catch (Exception e) when (e is MemberAccessException || e is TargetInvocationException)
            {
                // ignore
            }
        }

        return UnixDefault();
    }

    public static IDnsServerAddressStreamProvider UnixDefault()
    {
        return DefaultProviderHolder.DEFAULT_DNS_SERVER_ADDRESS_STREAM_PROVIDER;
    }

    // We use a Holder class to only initialize DEFAULT_DNS_SERVER_ADDRESS_STREAM_PROVIDER if we really
    // need it.
    private static class DefaultProviderHolder
    {
        // We use 5 minutes which is the same as what OpenJDK is using in sun.net.dns.ResolverConfigurationImpl.
        private const long REFRESH_INTERVAL = 5L * 60 * 1_000_000_000;

        // TODO(scott): how is this done on Windows? This may require a JNI call to GetNetworkParams
        // https://msdn.microsoft.com/en-us/library/aa365968(VS.85).aspx.
        internal static readonly IDnsServerAddressStreamProvider DEFAULT_DNS_SERVER_ADDRESS_STREAM_PROVIDER =
                new RefreshingDnsServerAddressStreamProvider();

        // CLR: The volatile field's initializer calls the instance method provider(), so the constructor assigns
        // it.
        private sealed class RefreshingDnsServerAddressStreamProvider : IDnsServerAddressStreamProvider
        {
            private volatile IDnsServerAddressStreamProvider currentProvider;
            private long lastRefresh = SystemTimer.NanoTime();

            internal RefreshingDnsServerAddressStreamProvider()
            {
                currentProvider = Provider();
            }

            public IDnsServerAddressStream NameServerAddressStream(string hostname)
            {
                long last = Interlocked.Read(ref lastRefresh);
                IDnsServerAddressStreamProvider current = currentProvider;

                if (SystemTimer.NanoTime() - last > REFRESH_INTERVAL)
                {
                    // This is slightly racy which means it will be possible still use the old configuration
                    // for a small amount of time, but that's ok.
                    if (Interlocked.CompareExchange(ref lastRefresh, SystemTimer.NanoTime(), last) == last)
                    {
                        current = currentProvider = Provider();
                    }
                }

                return current.NameServerAddressStream(hostname);
            }

            private IDnsServerAddressStreamProvider Provider()
            {
                // If on windows just use the DefaultDnsServerAddressStreamProvider.INSTANCE as otherwise
                // we will log some error which may be confusing.
                return PlatformDependent.IsWindows() ? DefaultDnsServerAddressStreamProvider.INSTANCE :
                        UnixResolverDnsServerAddressStreamProvider.ParseSilently();
            }
        }
    }
}
