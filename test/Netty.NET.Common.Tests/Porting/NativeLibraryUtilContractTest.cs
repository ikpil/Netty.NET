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

    private static void VerifyExport(IntPtr handle)
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
        IntPtr handle = NativeLibraryUtil.LoadLibrary(name, absolute);
        try { VerifyExport(handle); }
        finally { NativeLibrary.Free(handle); }
    }

    [Fact]
    public void MissingLibraryAndInvalidAbsolutePathUseClrExceptions()
    {
        string missing = "netty-missing-" + Guid.NewGuid().ToString("N");
        Assert.Throws<DllNotFoundException>(() => NativeLibraryUtil.LoadLibrary(missing, false));
        Assert.Throws<DllNotFoundException>(() => NativeLibraryUtil.LoadLibrary(Path.Combine(Path.GetTempPath(), missing), true));
        Assert.Throws<ArgumentException>(() => NativeLibraryUtil.LoadLibrary(library, true));
        Assert.Throws<ArgumentNullException>(() => NativeLibraryUtil.LoadLibrary(null, false));
        Assert.Throws<ArgumentNullException>(() => NativeLibraryUtil.LoadLibrary(null, true));
    }

    [Fact]
    public void SeparateLoadsReturnHandlesThatCanBeFreedIndependently()
    {
        IntPtr first = NativeLibraryUtil.LoadLibrary(library, false);
        IntPtr second;
        try { second = NativeLibraryUtil.LoadLibrary(library, false); }
        finally { NativeLibrary.Free(first); }
        try { VerifyExport(second); }
        finally { NativeLibrary.Free(second); }
    }
}
