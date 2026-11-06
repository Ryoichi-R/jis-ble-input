namespace JisBleInput.Core;

public interface IBluetoothRadioSwitch
{
    // Returns true only when this call turned the radio off; TurnOnAsync then restores it.
    Task<bool> TryTurnOffAsync(CancellationToken cancellation);
    Task TurnOnAsync();
}

public enum ExitDisconnectResult { NoHost, DisconnectedFirst, RadioUnavailable, DisconnectUnconfirmed, RadioRestoreFailed, ServiceReleaseFailed }

// A Windows GATT peripheral cannot drop a central's LE link itself. Removing the
// HID service while the receiver is still connected makes the receiver drop its
// HID device and never reconnect, so the radio is briefly turned off first.
public static class ExitDisconnect
{
    public static async Task<ExitDisconnectResult> ReleaseAsync(bool hostConnected, IBluetoothRadioSwitch radio,
        Func<CancellationToken, Task> waitForDisconnect, Action releaseService, TimeSpan timeout)
    {
        if (!hostConnected)
        {
            try { releaseService(); }
            catch (Exception) { return ExitDisconnectResult.ServiceReleaseFailed; }
            return ExitDisconnectResult.NoHost;
        }
        var turnedOff = false;
        var disconnected = false;
        var restored = true;
        var released = true;
        try
        {
            using var cancellation = new CancellationTokenSource(timeout);
            try
            {
                turnedOff = await radio.TryTurnOffAsync(cancellation.Token);
                if (turnedOff)
                {
                    await waitForDisconnect(cancellation.Token);
                    disconnected = true;
                }
            }
            // Exit must continue even when the radio or link state cannot be confirmed.
            catch (Exception) { }
            try { releaseService(); }
            catch (Exception) { released = false; }
        }
        finally
        {
            // The switch restores the radio only if it turned it off, including a
            // turn-off request that was interrupted by the timeout.
            try { await radio.TurnOnAsync(); } catch (Exception) { restored = false; }
        }
        if (!restored) return ExitDisconnectResult.RadioRestoreFailed;
        if (!released) return ExitDisconnectResult.ServiceReleaseFailed;
        if (!turnedOff) return ExitDisconnectResult.RadioUnavailable;
        return disconnected ? ExitDisconnectResult.DisconnectedFirst : ExitDisconnectResult.DisconnectUnconfirmed;
    }
}
