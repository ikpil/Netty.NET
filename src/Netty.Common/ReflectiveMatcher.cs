using System;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

public class ReflectiveMatcher : TypeParameterMatcher
{
    private readonly Type type;

    public ReflectiveMatcher(Type type)
    {
        this.type = type;
    }

    public override bool match(object msg)
    {
        return type.IsInstanceOfType(msg);
    }
}