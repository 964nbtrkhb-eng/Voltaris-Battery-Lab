using System.Runtime.InteropServices;

namespace Voltaris.Services;

public static class PowerAwake
{
    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        Continuous = 0x80000000
    }

    [DllImport("kernel32.dll")]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState state);

    public static void KeepSystemAwake(bool enabled) =>
        SetThreadExecutionState(enabled ? ExecutionState.Continuous | ExecutionState.SystemRequired : ExecutionState.Continuous);
}
