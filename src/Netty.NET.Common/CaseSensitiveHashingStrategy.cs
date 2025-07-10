using System.Collections.Generic;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

public class CaseSensitiveHashingStrategy : IHashingStrategy<ICharSequence>
{
    int IEqualityComparer<ICharSequence>.GetHashCode(ICharSequence obj)
    {
        return hashCode(obj);
    }

    public int hashCode(ICharSequence o)
    {
        return AsciiString.hashCode(o);
    }

    public bool Equals(ICharSequence a, ICharSequence b)
    {
        return AsciiString.contentEquals(a, b);
    }
}