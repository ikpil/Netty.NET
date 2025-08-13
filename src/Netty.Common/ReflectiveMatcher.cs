using System;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

public class ReflectiveMatcher : TypeParameterMatcher
{
    private readonly Type _type;

    public ReflectiveMatcher(Type type)
    {
        _type = type;
    }

    public override bool match(object msg)
    {
        return _type.IsInstanceOfType(msg);
    }
}