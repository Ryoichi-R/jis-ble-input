namespace JisBleInput.Core;

public interface IRadioPort
{
    bool IsOn { get; }
    Task<bool> SetOnAsync(bool on);
}

// This state machine is shared by the Windows adapter and hardware-free tests.
public sealed class RadioStateSwitch(Func<CancellationToken, Task<IRadioPort?>> discover,
    TimeSpan? restoreTimeout = null) : IBluetoothRadioSwitch
{
    private IRadioPort? changed;
    private Task<bool>? pendingOff;

    public async Task<bool> TryTurnOffAsync(CancellationToken cancellation)
    {
        var radio = await discover(cancellation);
        if (radio is null || !radio.IsOn) return false;
        changed = radio;
        pendingOff = radio.SetOnAsync(false);
        if (!await pendingOff.WaitAsync(cancellation))
        {
            pendingOff = null;
            changed = null;
            return false;
        }
        await WaitForStateAsync(radio, false, cancellation);
        return true;
    }

    public async Task TurnOnAsync()
    {
        if (changed is null) return;
        using var timeout = new CancellationTokenSource(restoreTimeout ?? TimeSpan.FromSeconds(5));
        if (pendingOff is not null)
        {
            try { await pendingOff.WaitAsync(timeout.Token); }
            // A completed fault cannot turn the radio off later. An unfinished
            // request must remain tracked so restoration cannot overtake it.
            catch (Exception) when (pendingOff.IsCompleted) { }
            pendingOff = null;
        }
        if (!await changed.SetOnAsync(true).WaitAsync(timeout.Token))
            throw new InvalidOperationException("Bluetoothをオンに戻す要求が許可されませんでした。");
        await WaitForStateAsync(changed, true, timeout.Token);
        changed = null;
    }

    private static async Task WaitForStateAsync(IRadioPort radio, bool on, CancellationToken cancellation)
    {
        while (radio.IsOn != on) await Task.Delay(10, cancellation);
    }
}
