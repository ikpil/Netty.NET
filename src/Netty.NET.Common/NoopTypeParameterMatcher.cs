using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

public class NoopTypeParameterMatcher : TypeParameterMatcher
{
    public override bool Match(object msg)
    {
        return true;
    }
}