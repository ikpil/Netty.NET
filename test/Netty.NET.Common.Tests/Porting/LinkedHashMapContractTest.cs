using System;
using System.Collections.Generic;
using System.Linq;
using Netty.NET.Common.Collections;

namespace Netty.NET.Common.Tests.Porting;

public class LinkedHashMapContractTest
{
    [Fact]
    public void UpdatesPreserveInsertionOrderAndCopiesOwnTheirNodes()
    {
        var map = new LinkedHashMap<string, string>();
        map["first"] = "one";
        map["second"] = "two";
        map["first"] = "updated";
        Assert.Equal(new[] { "first", "second" }, map.Keys);
        var copy = new LinkedHashMap<string, string>(map);
        copy["first"] = "copy";
        Assert.Equal("updated", map["first"]);
        Assert.Equal("copy", copy["first"]);
        Assert.Equal("{first=updated, second=two}", map.ToString());
    }

    [Fact]
    public void AccessOrderPairOperationsAndCopyToUseTheirOwnContracts()
    {
        var map = new LinkedHashMap<string, string>(accessOrder: true);
        map["a"] = null;
        map["b"] = "value";
        Assert.True(map.Contains(new("a", null)));
        Assert.False(map.Remove(new KeyValuePair<string, string>("b", "other")));
        Assert.Null(map["a"]);
        Assert.Equal(new[] { "b", "a" }, map.Keys);
        var array = new KeyValuePair<string, string>[3];
        map.CopyTo(array, 1);
        Assert.Equal(map.ToArray(), array.Skip(1));
        Assert.Throws<ArgumentException>(() => map.CopyTo(array, 2));
    }
}
