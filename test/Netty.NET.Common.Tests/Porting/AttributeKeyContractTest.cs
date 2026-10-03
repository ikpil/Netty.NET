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
        var textKey = AttributeKey.ValueOf<string>(name);
        var valueKey = AttributeKey.ValueOf<Value>(Guid.NewGuid().ToString());
        Assert.NotEqual(textKey.Id(), valueKey.Id());
        Assert.True(AttributeKey.Exists<Value>(name));
        Assert.Throws<ArgumentException>(() => AttributeKey.NewInstance<Value>(name));
        Assert.Throws<ArgumentException>(() => AttributeKey.ValueOf<Value>(name));

        var map = new DefaultAttributeMap();
        string text = "value";
        var value = new Value();
        map.Attr(textKey).Set(text);
        map.Attr(valueKey).Set(value);
        Assert.Same(text, map.Attr(textKey).Get());
        Assert.Same(value, map.Attr(valueKey).Get());
    }

    [Fact]
    public async Task ConcurrentValueOfReturnsOneKey()
    {
        string name = Guid.NewGuid().ToString();
        var keys = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => AttributeKey.ValueOf<Value>(name))));
        Assert.All(keys, key => Assert.Same(keys[0], key));
    }
}
