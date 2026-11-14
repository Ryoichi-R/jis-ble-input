using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace JisBleInput.Core;

// Each trial transfer owns one timer. Task.Delay cannot reliably schedule 5 ms waits on Windows.
internal sealed class WindowsHighResolutionTimer : IDisposable
{
    private const uint HighResolution = 0x00000002;
    private const uint SynchronizeAndModify = 0x00100002;
    private readonly WaitHandle timer;

    public WindowsHighResolutionTimer()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("高速試験にはWindowsの高精度タイマーが必要です。");
        var handle = CreateWaitableTimerEx(IntPtr.Zero, IntPtr.Zero, HighResolution, SynchronizeAndModify);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new InvalidOperationException($"高速試験のタイマーを使用できません（Windowsエラー {error}）。「最速」で送信してください。");
        }
        timer = new TimerWaitHandle(handle);
    }

    public async Task DelayAsync(int milliseconds, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        // A negative due time is relative and expressed in 100 ns units.
        long dueTime = -milliseconds * TimeSpan.TicksPerMillisecond;
        if (!SetWaitableTimer(timer.SafeWaitHandle, in dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
            throw new InvalidOperationException($"高速試験のタイマーを開始できません（Windowsエラー {Marshal.GetLastPInvokeError()}）。");

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ThreadPool.RegisterWaitForSingleObject(timer,
            static (state, _) => ((TaskCompletionSource)state!).TrySetResult(),
            completed, Timeout.Infinite, executeOnlyOnce: true);
        try { await completed.Task.WaitAsync(cancellation).ConfigureAwait(false); }
        finally { registration.Unregister(null); }
    }

    public void Dispose() => timer.Dispose();

    private sealed class TimerWaitHandle : WaitHandle
    {
        public TimerWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(IntPtr attributes, IntPtr name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle handle, in long dueTime, int period,
        IntPtr completionRoutine, IntPtr completionArg, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
