using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class StringMetadataContractTest
{
    public class Holder<T> { }

    [Theory]
    [InlineData(typeof(List<int>[]), "List[]")]
    [InlineData(typeof(Dictionary<string, List<int>>[,]), "Dictionary[,]")]
    [InlineData(typeof(Holder<int>[]), "StringMetadataContractTest+Holder[]")]
    [InlineData(typeof(object[][]), "Object[][]")]
    public void ArrayDiagnosticsUseNativeElementAndModifierMetadata(Type type, string expected)
    {
        Assert.Equal(expected, StringUtil.SimpleClassName(type));
    }

    [Fact]
    public void PointerByRefAndNonVectorArrayModifiersKeepTheirNativeSpelling()
    {
        Type element = typeof(List<int>);
        Assert.Equal("List*", StringUtil.SimpleClassName(element.MakePointerType()));
        Assert.Equal("List&", StringUtil.SimpleClassName(element.MakeByRefType()));
        Assert.Equal("List[*]", StringUtil.SimpleClassName(element.MakeArrayType(1)));
        Assert.Equal("StringMetadataContractTest+Holder[]", StringUtil.SimpleClassName(typeof(Holder<>).MakeArrayType()));
        Assert.Equal("T", StringUtil.SimpleClassName(typeof(Holder<>).GetGenericArguments()[0]));
    }

    [Fact]
    public unsafe void FunctionPointerMetadataProvidesItsSignatureInsteadOfAnEmptyLabel()
    {
        string name = StringUtil.SimpleClassName(typeof(delegate*<int, int>));
        Assert.Contains("Int32", name);
        Assert.Contains("(", name);
        Assert.Contains(")", name);
        Assert.DoesNotContain("Version=", name);
    }

    [Fact]
    public void NonGenericMetadataNamesDoNotLoseLiteralBackticksAndDigits()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LiteralName" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        Type type = assembly.DefineDynamicModule("metadata").DefineType("NativeNames.Literal`12", TypeAttributes.Public).CreateType();
        Assert.False(type.IsGenericType);
        Assert.Equal("Literal`12", StringUtil.SimpleClassName(type));
    }

    [Fact]
    public void ClassDiagnosticsRetainNativeRuntimeIdentityAndNestedNames()
    {
        var value = new Holder<int>();
        Assert.Equal(value.GetType().FullName, StringUtil.ClassName(value));
        Assert.Equal("StringMetadataContractTest+Holder", StringUtil.SimpleClassName(value));
        Assert.Equal("StringMetadataContractTest+Holder", StringUtil.SimpleClassName<Holder<int>>());
        Assert.Equal("null_object", StringUtil.ClassName(null));
        Assert.Equal("null_object", StringUtil.SimpleClassName((object)null));
        Assert.Equal("t", Assert.Throws<ArgumentNullException>(() => StringUtil.SimpleClassName((Type)null)).ParamName);
    }

    [Fact]
    public void DefaultNewlineMatchesTheNativePlatformUnlessExplicitlyOverridden()
    {
        Assert.Equal(Environment.GetEnvironmentVariable("line.separator") ?? Environment.NewLine, StringUtil.NEWLINE);
    }

    [Fact]
    public void StandardJoiningOwnsNullAndSingleEnumerationSemantics()
    {
        int enumerations = 0;
        int disposals = 0;
        IEnumerable<string> Values()
        {
            enumerations++;
            if (enumerations != 1) throw new InvalidOperationException("The input can only be enumerated once.");
            try { yield return "gzip"; yield return "br"; yield return "identity"; }
            finally { disposals++; }
        }
        Assert.Equal("gzip,br,identity", string.Join(",", Values()));
        Assert.Equal(1, enumerations);
        Assert.Equal(1, disposals);
        Assert.Equal("", string.Join(",", new string[] { null }));
        Assert.Equal(",br,", string.Join(",", new string[] { null, "br", null }));
        Assert.Equal("gzipbr", string.Join(null, new[] { "gzip", "br" }));
    }
}
