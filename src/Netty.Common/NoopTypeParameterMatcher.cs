using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

public class NoopTypeParameterMatcher : TypeParameterMatcher
{
    public override bool match(object msg)
    {
        return true;
    }
}