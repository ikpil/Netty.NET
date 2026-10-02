using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Netty.NET.Common;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class ConstantIdentityContractTest
{
    private sealed class First(int id, string name) : AbstractConstant<First>(id, name);
    private sealed class Second(int id, string name) : AbstractConstant<Second>(id, name);
    private readonly struct ValueConstant : IConstant<ValueConstant>
    {
        public int id() => 1;
        public string name() => "value";
        public int CompareTo(ValueConstant other) => 0;
    }

    [Fact]
    public void PoolsRequireReferenceConstantsForSingletonIdentity()
    {
        Assert.Throws<ArgumentException>(() => typeof(ConstantPool<>).MakeGenericType(typeof(ValueConstant)));
    }

    [Fact]
    public void IdentityOverridesAreFinalAsInTheOriginal()
    {
        Type type = typeof(AbstractConstant<First>);
        Assert.True(type.GetMethod(nameof(object.Equals), new[] { typeof(object) }).IsFinal);
        Assert.True(type.GetMethod(nameof(object.GetHashCode)).IsFinal);
        Assert.True(type.GetMethod(nameof(object.ToString)).IsFinal);
    }

    [Fact]
    public void GenericConstantTypesShareTheOriginalUniquifierSequence()
    {
        // Java has one static generator despite T; CLR closed generic statics
        // would each restart it. Inspect the private collision discriminator
        // because the type-safe public comparer cannot compare unrelated T types.
        var first = new First(1, "same");
        var second = new Second(1, "same");
        long firstSequence = (long)typeof(AbstractConstant<First>).GetField("_uniquifier", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(first);
        long secondSequence = (long)typeof(AbstractConstant<Second>).GetField("_uniquifier", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(second);
        Assert.True(secondSequence > firstSequence);
    }

    [Fact]
    public void SameNameAndIdDoNotCreateValueEquality()
    {
        var first = new First(1, "same");
        var second = new First(1, "same");
        Assert.True(first.Equals(first));
        Assert.False(first.Equals(second));
        Assert.False(first.Equals(null));
        Assert.Equal(RuntimeHelpers.GetHashCode(first), first.GetHashCode());
        Assert.Equal(0, first.CompareTo(first));
        Assert.NotEqual(0, first.CompareTo(second));
        Assert.Equal(-Math.Sign(first.CompareTo(second)), Math.Sign(second.CompareTo(first)));
        Assert.Equal(2, new SortedSet<First> { first, second }.Count);
    }

    [Fact]
    public void ConstantMetadataAndDescriptionRemainStable()
    {
        var value = new First(19, "name");
        Assert.Equal(19, value.id());
        Assert.Equal("name", value.name());
        Assert.Equal("name", value.ToString());
    }

    [Fact]
    public void NullComparisonIsAnExplicitArgumentError()
    {
        var value = new First(1, "name");
        Assert.Throws<ArgumentNullException>(() => value.CompareTo(null));
    }
}
