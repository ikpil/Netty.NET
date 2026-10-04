using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class TempFileContractTest
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]
    public void NullSuffixDefaultsToTmpWithoutAJavaFilePrefixLengthRule(string prefix)
    {
        FileInfo file = PlatformDependent.CreateTempFile(prefix, null, null);
        try
        {
            Assert.True(file.Exists);
            Assert.Equal(0, file.Length);
            Assert.StartsWith(prefix ?? "", file.Name);
            Assert.EndsWith(".tmp", file.Name);
            // Creation returns ownership of a path, with no retained file handle.
            using var stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.WriteByte(0xa5);
        }
        finally
        {
            file.Delete();
        }
    }

    [Theory]
    [InlineData(true, "../")]
    [InlineData(true, "child/")]
    [InlineData(false, "/child")]
    [InlineData(true, "bad\0name")]
    [InlineData(false, "bad\0name")]
    public void InvalidNamePartsAreRejectedBeforeCreatingAFile(bool prefix, string value)
    {
        FileInfo created = null;
        try
        {
            var failure = Assert.Throws<ArgumentException>(() =>
                created = PlatformDependent.CreateTempFile(prefix ? value : "netty-", prefix ? ".tmp" : value, null));
            Assert.Equal(prefix ? "prefix" : "suffix", failure.ParamName);
        }
        finally
        {
            created?.Delete();
        }
    }

    [Theory]
    [InlineData("netty-", ".key")]
    [InlineData("x", "")]
    [InlineData(null, null)]
    public void CallerDirectoryAndNamePartsArePreservedAndTheCallerCanWriteAndDelete(string prefix, string suffix)
    {
        WithDirectory(directory =>
        {
            FileInfo file = PlatformDependent.CreateTempFile(prefix, suffix, directory);
            Assert.Equal(directory.FullName, file.DirectoryName);
            Assert.StartsWith(prefix ?? "", file.Name);
            Assert.EndsWith(suffix ?? ".tmp", file.Name);
            Assert.Equal(0, file.Length);
            // Match library extraction, multipart data and certificate consumers: reopen, write, close, delete.
            byte[] payload = { 0, 0x80, 0xff, 0x42 };
            File.WriteAllBytes(file.FullName, payload);
            Assert.Equal(payload, File.ReadAllBytes(file.FullName));
            file.Delete();
            Assert.Empty(directory.GetFileSystemInfos());
        });
    }

    [Fact]
    public void DefaultDirectoryUsesTheClrTemporaryPath()
    {
        FileInfo file = PlatformDependent.CreateTempFile("netty-", ".tmp", null);
        try
        {
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), file.DirectoryName);
        }
        finally
        {
            file.Delete();
        }
    }

    [Fact]
    public void MissingDirectoryIsNotCreatedAndDoesNotFallBackElsewhere()
    {
        WithDirectory(directory =>
        {
            var missing = new DirectoryInfo(Path.Combine(directory.FullName, "missing"));
            Assert.Throws<DirectoryNotFoundException>(() => PlatformDependent.CreateTempFile("netty-", ".tmp", missing));
            Assert.False(missing.Exists);
            Assert.Empty(directory.GetFileSystemInfos());
        });
    }

    [Fact]
    public void AFileCannotBeUsedAsTheDirectoryAndItsContentsArePreserved()
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory.FullName, "ordinary-file");
            File.WriteAllText(path, "keep me");
            Assert.ThrowsAny<IOException>(() => PlatformDependent.CreateTempFile("netty-", ".tmp", new DirectoryInfo(path)));
            Assert.Equal("keep me", File.ReadAllText(path));
            Assert.Single(directory.GetFileSystemInfos());
        });
    }

    [Fact]
    public void InvalidPartsCannotEscapeAnExplicitDirectory()
    {
        WithDirectory(directory =>
        {
            Assert.Throws<ArgumentException>(() => PlatformDependent.CreateTempFile(directory.FullName + Path.DirectorySeparatorChar, ".tmp", directory));
            Assert.Throws<ArgumentException>(() => PlatformDependent.CreateTempFile("../", ".tmp", directory));
            Assert.Throws<ArgumentException>(() => PlatformDependent.CreateTempFile("netty-", "/child", directory));
            Assert.Empty(directory.GetFileSystemInfos());
        });
    }

    [Fact]
    public void ConcurrentCallersOwnDistinctReopenableFilesWithoutChangingExistingContents()
    {
        WithDirectory(directory =>
        {
            string sentinel = Path.Combine(directory.FullName, "netty-existing.tmp");
            File.WriteAllText(sentinel, "keep me");
            var files = new ConcurrentBag<FileInfo>();
            Parallel.For(0, 256, value =>
            {
                FileInfo file = PlatformDependent.CreateTempFile("netty-", ".tmp", directory);
                using (var stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    stream.WriteByte((byte)value);
                files.Add(file);
            });
            Assert.Equal(256, files.Count);
            Assert.Equal(256, files.Select(file => file.FullName).Distinct().Count());
            Assert.Equal(257, directory.GetFiles().Length);
            foreach (FileInfo file in files)
            {
                Assert.Equal(directory.FullName, file.DirectoryName);
                Assert.Single(File.ReadAllBytes(file.FullName));
                file.Delete();
            }
            Assert.Equal("keep me", File.ReadAllText(sentinel));
        });
    }

    [Fact]
    public void UnixPrivatePermissionsAreAppliedAtCreation()
    {
        if (OperatingSystem.IsWindows())
            return;
        WithDirectory(directory =>
        {
            FileInfo file = PlatformDependent.CreateTempFile("netty-", ".key", directory);
            UnixFileMode mode = File.GetUnixFileMode(file.FullName);
            // umask may further restrict access, but group/other access must never be granted.
            Assert.Equal((UnixFileMode)0, mode & ~(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        });
    }

    private static void WithDirectory(Action<DirectoryInfo> assertion)
    {
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "netty-temp-test-" + Guid.NewGuid().ToString("N")));
        directory.Create();
        try
        {
            assertion(directory);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
