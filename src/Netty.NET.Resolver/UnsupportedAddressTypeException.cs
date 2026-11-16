using System;

namespace Netty.NET.Resolver;

/// <summary>
/// CLR counterpart of <c>java.nio.channels.UnsupportedAddressTypeException</c>, which the resolver throws.
/// </summary>
// CLR: the JDK type is an IllegalArgumentException (ArgumentException here) with only a no-argument constructor
// and no message. The BCL has no such type, so this one keeps the same base type and shape.
public class UnsupportedAddressTypeException : ArgumentException
{
    public UnsupportedAddressTypeException() : base((string)null) { }
}
