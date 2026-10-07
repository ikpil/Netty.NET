using System;
using System.Collections.Generic;
using Netty.NET.Common;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class ConstantPoolNativeContractTest
{
    private sealed class Constant(int id, string name) : AbstractConstant<Constant>(id, name);
    private sealed class Pool : ConstantPool<Constant>
    {
        internal Func<int, string, Constant> Factory = (id, name) => new Constant(id, name);
        protected override Constant NewConstant(int id, string name) => Factory(id, name);
    }

    private sealed class First<T>;
    private sealed class Second<T>;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullFactoryResultDoesNotReserveNameAndRetryConsumesNextId(bool createOnly)
    {
        var pool = new Pool { Factory = (_, _) => null };
        Assert.Throws<InvalidOperationException>(() => createOnly ? pool.NewInstance("retry") : pool.ValueOf("retry"));
        Assert.False(pool.Exists("retry"));
        int retryId = 0;
        pool.Factory = (id, name) => { retryId = id; return new Constant(id, name); };
        Constant value = createOnly ? pool.NewInstance("retry") : pool.ValueOf("retry");
        Assert.Equal(2, retryId);
        Assert.Equal("retry", value.ToString());
        Assert.Same(value, pool.ValueOf("retry"));
        Assert.Throws<ArgumentException>(() => pool.NewInstance("retry"));
        Assert.Equal(3, pool.NextId());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FactoryExceptionIsPreservedAndPublishedLookupDoesNotReenterFactory(bool createOnly)
    {
        var failure = new InvalidOperationException("factory failure");
        var pool = new Pool { Factory = (_, _) => throw failure };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => createOnly ? pool.NewInstance("retry") : pool.ValueOf("retry")));
        Assert.False(pool.Exists("retry"));
        pool.Factory = (id, name) => new Constant(id, name);
        Constant value = pool.ValueOf("retry");
        pool.Factory = (_, _) => throw failure;
        Assert.Same(value, pool.ValueOf("retry"));
        Assert.Throws<ArgumentException>(() => pool.NewInstance("retry"));
        Assert.Equal(3, pool.NextId());
    }

    private static Type GetUnnamedType(int kind) => kind switch
    {
        0 => typeof(First<>).GetGenericArguments()[0],
        1 => typeof(Second<>).GetGenericArguments()[0],
        2 => typeof(Dictionary<,>).MakeGenericType(typeof(string), typeof(First<>).GetGenericArguments()[0]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [InlineData("First<T> parameter", 0)]
    [InlineData("Second<T> parameter", 1)]
    [InlineData("partially constructed dictionary", 2)]
    public void UnnamedTypesCannotCollapseIntoASharedPoolName(string description, int kind)
    {
        Type type = GetUnnamedType(kind);
        Assert.Null(type.FullName, description);
        var pool = new Pool();
        Assert.Equal("firstNameComponent", Assert.Throws<ArgumentException>(() => pool.ValueOf(type, "suffix")).ParamName);
        Assert.False(pool.Exists("#suffix"));
        Assert.Equal(1, pool.NextId());
    }

    [Theory]
    [InlineData("First<T> parameter", 0)]
    [InlineData("Second<T> parameter", 1)]
    [InlineData("partially constructed dictionary", 2)]
    public void UnnamedTypesCannotPublishAttributeKeysOrSignals(string description, int kind)
    {
        Type type = GetUnnamedType(kind);
        Assert.Null(type.FullName, description);
        string suffix = "unnamed-" + Guid.NewGuid();
        Assert.Equal("firstNameComponent", Assert.Throws<ArgumentException>(() => AttributeKey<string>.ValueOf(type, suffix)).ParamName);
        Assert.Equal("firstNameComponent", Assert.Throws<ArgumentException>(() => AttributeKey.ValueOf<string>(type, suffix)).ParamName);
        Assert.False(AttributeKey.Exists("#" + suffix));
        Assert.Equal("firstNameComponent", Assert.Throws<ArgumentException>(() => Signal.ValueOf(type, suffix)).ParamName);
    }

    [Theory]
    [InlineData(typeof(object))]
    [InlineData(typeof(List<>))]
    [InlineData(typeof(List<string>))]
    public void NamedTypesRetainFullNameCompositionIncludingOpenGenericDefinitions(Type type)
    {
        string suffix = "named-" + Guid.NewGuid();
        string name = type.FullName + '#' + suffix;
        var pool = new Pool();
        Assert.Same(pool.ValueOf(name), pool.ValueOf(type, suffix));
        Assert.Same(AttributeKey<string>.ValueOf(name), AttributeKey.ValueOf<string>(type, suffix));
        Assert.Same(Signal.ValueOf(name), Signal.ValueOf(type, suffix));
    }

    [Fact]
    public void NullArgumentsRemainExplicitAndDoNotCreateConstants()
    {
        var pool = new Pool();
        Assert.Equal("firstNameComponent", Assert.Throws<ArgumentNullException>(() => pool.ValueOf(null, null)).ParamName);
        Assert.Equal("secondNameComponent", Assert.Throws<ArgumentNullException>(() => pool.ValueOf(typeof(First<>).GetGenericArguments()[0], null)).ParamName);
        Assert.Equal("secondNameComponent", Assert.Throws<ArgumentNullException>(() => AttributeKey<string>.ValueOf(typeof(First<>).GetGenericArguments()[0], null)).ParamName);
        Assert.Equal(1, pool.NextId());
    }

    [Theory]
    [InlineData(typeof(IConstant<Signal>))]
    [InlineData(typeof(Signal))]
    [InlineData(typeof(AttributeKey<string>))]
    [InlineData(typeof(IAttributeKey))]
    public void ConstantMetadataUsesReadOnlyNativeProperties(Type type)
    {
        var id = type.GetProperty("Id");
        Assert.NotNull(id);
        Assert.Equal(typeof(int), id.PropertyType);
        Assert.NotNull(id.GetMethod);
        Assert.Null(id.SetMethod);
        Assert.Null(type.GetMethod("Id"));
        if (type == typeof(IAttributeKey)) return;
        var name = type.GetProperty("Name");
        Assert.NotNull(name);
        Assert.Equal(typeof(string), name.PropertyType);
        Assert.NotNull(name.GetMethod);
        Assert.Null(name.SetMethod);
        Assert.Null(type.GetMethod("Name"));
    }
}
