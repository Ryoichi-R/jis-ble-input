using System.Runtime.InteropServices;

namespace JisBleInput.App;

// Keeps the sending PC and its display awake while key reports are scheduled.
// The request belongs to the calling thread, so both calls stay on the UI thread.
// Closing the lid or pressing the power button still suspends the PC.
internal static class TransferKeepAwake
{
    internal const uint Continuous = 0x80000000;
    // ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
    internal const uint KeepAwake = Continuous | 0x00000001 | 0x00000002;
    internal static Func<uint, uint> Apply { get; set; } = SetThreadExecutionState;

    public static void Begin() => Apply(KeepAwake);
    public static void End() => Apply(Continuous);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
