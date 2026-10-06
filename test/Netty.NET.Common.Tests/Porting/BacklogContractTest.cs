using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Netty.NET.Common.Tests.Porting;

public class BacklogContractTest
{
    [Fact]
    public void SysctlImplementationIsNotAnApplicationApi()
    {
        Assert.Null(typeof(NetUtil).GetMethod("SysctlGetInt", BindingFlags.Public | BindingFlags.Static));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("+32", 32)]
    [InlineData("4096", 4096)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData(" 32 ", 200)]
    [InlineData("2147483648", 200)]
    [InlineData("", 200)]
    public void KernelFilePrecedesOptionalSysctlAndOwnsItsStream(string line, int expected)
    {
        var input = new OwnedStream(line);
        int result = SoMaxConnAction.Run(200, _ => true, _ => input,
            () => throw new InvalidOperationException("configuration must not be read"),
            _ => throw new InvalidOperationException("sysctl must not run"));
        Assert.Equal(expected, result);
        Assert.Equal(1, input.DisposeCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void AValidFirstSysctlValueStopsFallback(int value)
    {
        int calls = 0;
        Assert.Equal(value, SoMaxConnAction.Run(200, _ => false, _ => throw new InvalidOperationException(), () => true,
            key => { Assert.Equal("kern.ipc.somaxconn", key); Assert.Equal(0, calls++); return value; }));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(null, 128)]
    [InlineData(0, 0)]
    [InlineData(32, 32)]
    public void OnlyAnUnavailableFirstKeyQueriesTheSecond(int? value, int expected)
    {
        int calls = 0;
        Assert.Equal(expected, SoMaxConnAction.Run(128, _ => false, _ => throw new InvalidOperationException(), () => true,
            key => { Assert.Equal(calls++ == 0 ? "kern.ipc.somaxconn" : "kern.ipc.soacceptqueue", key); return calls == 1 ? null : value; }));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(128)]
    [InlineData(4096)]
    public void DisabledSysctlKeepsThePlatformDefault(int value)
    {
        Assert.Equal(value, SoMaxConnAction.Run(value, _ => false, _ => throw new InvalidOperationException(), () => false,
            _ => throw new InvalidOperationException("sysctl must not run")));
    }

    [Fact]
    public void ProviderErrorsRecoverButMemoryFailureEscapes()
    {
        Assert.Equal(200, SoMaxConnAction.Run(200, _ => throw new IOException(), _ => throw new InvalidOperationException(),
            () => false, _ => throw new InvalidOperationException()));
        Assert.Equal(200, SoMaxConnAction.Run(200, _ => true, _ => throw new UnauthorizedAccessException(),
            () => false, _ => throw new InvalidOperationException()));
        Assert.Equal(200, SoMaxConnAction.Run(200, _ => false, _ => throw new InvalidOperationException(),
            () => true, _ => throw new Win32Exception()));
        var error = new OutOfMemoryException();
        Assert.Same(error, Assert.Throws<OutOfMemoryException>(() => SoMaxConnAction.Run(200, _ => false,
            _ => throw new InvalidOperationException(), () => true, _ => throw error)));
    }

    [Fact]
    public void KernelNumbersIgnoreCallerCultureAndOversizedInputIsClosed()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.PositiveSign = "!";
            CultureInfo.CurrentCulture = culture;
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("+32"));
            Assert.Equal(32, SoMaxConnAction.Run(200, _ => true, _ => input, () => false, _ => null));
            var oversized = new OwnedStream(new string('1', 9000));
            Assert.Equal(200, SoMaxConnAction.Run(200, _ => true, _ => oversized, () => false, _ => null));
            Assert.Equal(1, oversized.DisposeCount);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("kern.ipc.somaxconn: 0", 0)]
    [InlineData("kern.ipc.somaxconn = 4096", 4096)]
    [InlineData("kern.ipc.somaxconn: 2147483647", int.MaxValue)]
    [InlineData("unrelated: 32", null)]
    [InlineData("kern.ipc.somaxconn", null)]
    public void SysctlReadsOneBoundedLineAndClosesTheStartedChild(string line, int? expected)
    {
        Process observer = null;
        SafeProcessHandle handle = null;
        try
        {
            Assert.Equal(expected, NetUtil.SysctlGetInt("kern.ipc.somaxconn", info => StartFixture(info, line, out observer, out handle)));
            Assert.True(handle.IsClosed);
            Assert.True(observer.WaitForExit(5000));
        }
        finally { StopObserver(observer); }
    }

    [Fact]
    public void SysctlFailuresCloseTheChildAndStartupExceptionsKeepTheirIdentity()
    {
        Process observer = null;
        SafeProcessHandle handle = null;
        try
        {
            Assert.Throws<OverflowException>(() => NetUtil.SysctlGetInt("kern.ipc.somaxconn",
                info => StartFixture(info, "kern.ipc.somaxconn: 2147483648", out observer, out handle)));
            Assert.True(handle.IsClosed);
            Assert.True(observer.WaitForExit(5000));
        }
        finally { StopObserver(observer); }
        var error = new Win32Exception(2);
        Assert.Same(error, Assert.Throws<Win32Exception>(() => NetUtil.SysctlGetInt("kern.ipc.somaxconn", _ => throw error)));
        Assert.Throws<Win32Exception>(() => NetUtil.SysctlGetInt("kern.ipc.somaxconn",
            info => { info.FileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); return Process.Start(info); }));
    }

    [Fact]
    public void AKeyIsOneNativeArgumentAndUnavailableOutputIsNull()
    {
        Assert.Null(NetUtil.SysctlGetInt("key with spaces and \"quotes\"", info =>
        {
            Assert.Equal("sysctl", info.FileName);
            Assert.Equal("key with spaces and \"quotes\"", Assert.Single(info.ArgumentList));
            Assert.Empty(info.Arguments);
            Assert.True(info.RedirectStandardOutput);
            Assert.False(info.UseShellExecute);
            Assert.True(info.CreateNoWindow);
            var child = FixtureInfo();
            child.ArgumentList.Add(OperatingSystem.IsWindows() ? "exit /b 0" : "exit 0");
            return Process.Start(child);
        }));
    }

    private static Process StartFixture(ProcessStartInfo supplied, string line, out Process observer, out SafeProcessHandle handle)
    {
        Assert.Equal("kern.ipc.somaxconn", Assert.Single(supplied.ArgumentList));
        var info = FixtureInfo();
        info.ArgumentList.Add(OperatingSystem.IsWindows() ? "echo " + line + "&set /p waiting=" : "printf '%s\\n' '" + line + "'; read waiting");
        Process process = Process.Start(info);
        handle = process.SafeHandle;
        observer = Process.GetProcessById(process.Id);
        _ = observer.SafeHandle;
        return process;
    }

    private static ProcessStartInfo FixtureInfo()
    {
        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardInput = true
        };
        info.ArgumentList.Add(OperatingSystem.IsWindows() ? "/d" : "-c");
        if (OperatingSystem.IsWindows()) info.ArgumentList.Add("/c");
        return info;
    }

    private static void StopObserver(Process observer)
    {
        if (observer == null) return;
        try { if (!observer.HasExited) { observer.Kill(); observer.WaitForExit(5000); } }
        finally { observer.Dispose(); }
    }

    private sealed class OwnedStream : MemoryStream
    {
        internal int DisposeCount;
        internal OwnedStream(string line) : base(Encoding.UTF8.GetBytes(line)) { }
        protected override void Dispose(bool disposing) { if (disposing) DisposeCount++; base.Dispose(disposing); }
    }
}
