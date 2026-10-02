using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Netty.NET.Common.Internal;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class NativeLibraryUtilContractTest
{
    private static string library => OperatingSystem.IsWindows() ? "kernel32.dll" :
        OperatingSystem.IsMacOS() ? "libSystem.B.dylib" : "libc.so.6";

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ProcessId();

    private static void verifyExport(IntPtr handle)
    {
        Assert.NotEqual(IntPtr.Zero, handle);
        IntPtr export = NativeLibrary.GetExport(handle, OperatingSystem.IsWindows() ? "GetCurrentProcessId" : "getpid");
        Assert.Equal((uint)Environment.ProcessId, Marshal.GetDelegateForFunctionPointer<ProcessId>(export)());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadsNativeLibraryByNameOrAbsolutePathAndInvokesItsExport(bool absolute)
    {
        string name = library;
        if (absolute)
        {
            using var process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
                if (string.Equals(Path.GetFileName(module.FileName), library, StringComparison.OrdinalIgnoreCase))
                {
                    name = module.FileName;
                    break;
                }
            Assert.True(Path.IsPathFullyQualified(name));
        }
        IntPtr handle = NativeLibraryUtil.loadLibrary(name, absolute);
        try { verifyExport(handle); }
        finally { NativeLibrary.Free(handle); }
    }

    [Fact]
    public void MissingLibraryAndInvalidAbsolutePathUseClrExceptions()
    {
        string missing = "netty-missing-" + Guid.NewGuid().ToString("N");
        Assert.Throws<DllNotFoundException>(() => NativeLibraryUtil.loadLibrary(missing, false));
        Assert.Throws<DllNotFoundException>(() => NativeLibraryUtil.loadLibrary(Path.Combine(Path.GetTempPath(), missing), true));
        Assert.Throws<ArgumentException>(() => NativeLibraryUtil.loadLibrary(library, true));
        Assert.Throws<ArgumentNullException>(() => NativeLibraryUtil.loadLibrary(null, false));
        Assert.Throws<ArgumentNullException>(() => NativeLibraryUtil.loadLibrary(null, true));
    }

    [Fact]
    public void SeparateLoadsReturnHandlesThatCanBeFreedIndependently()
    {
        IntPtr first = NativeLibraryUtil.loadLibrary(library, false);
        IntPtr second;
        try { second = NativeLibraryUtil.loadLibrary(library, false); }
        finally { NativeLibrary.Free(first); }
        try { verifyExport(second); }
        finally { NativeLibrary.Free(second); }
    }
}
