using System;
using System.Linq;
using System.Threading.Tasks;

namespace Netty.NET.Common.Tests.Porting;

public class AttributeKeyContractTest
{
    private sealed class Value { }

    [Fact]
    public void DifferentValueTypesShareUniqueIdsAndGlobalNames()
    {
        string name = Guid.NewGuid().ToString();
        var textKey = AttributeKey.valueOf<string>(name);
        var valueKey = AttributeKey.valueOf<Value>(Guid.NewGuid().ToString());
        Assert.NotEqual(textKey.id(), valueKey.id());
        Assert.True(AttributeKey.exists<Value>(name));
        Assert.Throws<ArgumentException>(() => AttributeKey.newInstance<Value>(name));
        Assert.Throws<ArgumentException>(() => AttributeKey.valueOf<Value>(name));

        var map = new DefaultAttributeMap();
        string text = "value";
        var value = new Value();
        map.attr(textKey).set(text);
        map.attr(valueKey).set(value);
        Assert.Same(text, map.attr(textKey).get());
        Assert.Same(value, map.attr(valueKey).get());
    }

    [Fact]
    public async Task ConcurrentValueOfReturnsOneKey()
    {
        string name = Guid.NewGuid().ToString();
        var keys = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => AttributeKey.valueOf<Value>(name))));
        Assert.All(keys, key => Assert.Same(keys[0], key));
    }
}
