using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Tests.Porting;

// CLR regressions for the 2026-11-16 Common closure review (docs/common-porting.md#common-closure-review-2026-11-16).
public class CommonClosureContractTest
{
    [Fact]
    public void OsReleaseReadingStopsAtTheSourceBoundedInputStreamLimit()
    {
        // PlatformDependent.processOsReleaseFile reads through BoundedInputStream (8 KiB). A larger file
        // fails with IOException, which is logged; the file still counts as present. Only the classifiers
        // allowed by isAllowedClassifier (fedora, suse, arch) are recorded.
        string path = Path.GetTempFileName();

        try
        {
            var text = new StringBuilder("ID=fedora\n");
            text.Append('#', 9000).Append('\n').Append("ID_LIKE=suse\n");
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            var classifiers = new HashSet<string>();
            Assert.True(PlatformDependent.ProcessOsReleaseFile(path, classifiers));
            Assert.Contains("fedora", classifiers);
            Assert.DoesNotContain("suse", classifiers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OsReleaseWithinTheLimitIsReadCompletely()
    {
        string path = Path.GetTempFileName();

        try
        {
            File.WriteAllText(path, "ID=\"fedora\"\nID_LIKE=\"suse arch\"\n", new UTF8Encoding(false));
            var classifiers = new HashSet<string>();
            Assert.True(PlatformDependent.ProcessOsReleaseFile(path, classifiers));
            Assert.Equal(new HashSet<string> { "fedora", "suse", "arch" }, classifiers);
            Assert.False(PlatformDependent.ProcessOsReleaseFile(path + ".missing", classifiers));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResourcesUtilResolvesNextToTheAssemblyWithoutUrlDecoding()
    {
        // Java URL-decodes Class.getResource(...).getFile(); a CLR path is not URL-encoded, so '+' and
        // "%20" must survive unchanged.
        FileInfo file = ResourcesUtil.GetFile(typeof(CommonClosureContractTest), "a+b%20c.txt");
        string directory = Path.GetDirectoryName(typeof(CommonClosureContractTest).Assembly.Location);
        Assert.Equal(Path.Combine(directory, "a+b%20c.txt"), file.FullName);
    }

    [Fact]
    public void UnstableApiHasSourceRetentionOnly()
    {
        // @Retention(RetentionPolicy.SOURCE): the [Conditional] attribute class is not emitted to metadata.
        Assert.Empty(typeof(NonStickyEventExecutorGroup).GetCustomAttributes(typeof(UnstableApiAttribute), false));
        Assert.NotEmpty(typeof(UnstableApiAttribute).GetCustomAttributes(typeof(System.Diagnostics.ConditionalAttribute), false));
    }

    [Fact]
    public void MessageFormatterFindsAnchorsOrdinally()
    {
        // String.indexOf is ordinal: a soft hyphen between the braces is not an anchor.
        Assert.Equal("x{­}y", MessageFormatter.Format("x{­}y", 1).GetMessage());
        Assert.Equal("x1y{­}2", MessageFormatter.Format("x{}y{­}{}", 1, 2).GetMessage());
    }

    [Fact]
    public void AsciiStringSplitCharFollowsTheSource()
    {
        // AsciiString.split(char): empty middle parts are kept, trailing empty parts are truncated,
        // and a value without the delimiter returns itself.
        var value = new AsciiString("a,,b,,");
        Assert.Equal(new[] { "a", "", "b" }, Array.ConvertAll(value.Split(','), s => s.ToString()));
        var single = new AsciiString("abc");
        Assert.Same(single, Assert.Single(single.Split(',')));
        Assert.Empty(new AsciiString(",,").Split(','));
        Assert.Equal(new[] { "", "x" }, Array.ConvertAll(new AsciiString(",x").Split(','), s => s.ToString()));
    }

    [Fact]
    public void AsciiStringInstanceSearchesFollowTheSource()
    {
        var value = new AsciiString("abcabc");
        Assert.True(value.Contains(new StringCharSequence("cab")));
        Assert.False(value.Contains(new StringCharSequence("cba")));
        Assert.Equal(2, value.IndexOf(new StringCharSequence("ca")));
        Assert.Equal(3, value.LastIndexOf(new StringCharSequence("abc")));
        Assert.Equal(6, value.LastIndexOf(new StringCharSequence("")));
        Assert.Equal(6, value.IndexOf(new StringCharSequence(""), 9));
        Assert.Equal(-1, value.IndexOf(new StringCharSequence("Ā")));
        Assert.Throws<ArgumentNullException>(() => value.IndexOf((ICharSequence)null, 0));
        Assert.Equal(0, value.CompareTo((ICharSequence)value));
        Assert.True(value.CompareTo((object)null) > 0);
        Assert.Throws<ArgumentException>(() => value.CompareTo(new object()));
        Assert.IsType<AsciiString>(value.SubSequence(1, 3));
    }

    [Fact]
    public void EmptyArraysUseTheMappedClrElementTypes()
    {
        Assert.Empty(EmptyArrays.EMPTY_MAP_ENTRY);
        Assert.IsType<KeyValuePair<object, object>[]>(EmptyArrays.EMPTY_MAP_ENTRY);
        Assert.IsType<System.Security.Cryptography.X509Certificates.X509Certificate2[]>(EmptyArrays.EMPTY_X509_CERTIFICATES);
        Assert.Same(EmptyArrays.EMPTY_BYTES, Array.Empty<byte>());
    }
}
