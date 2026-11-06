using JisBleInput.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;

namespace JisBleInput.App;

internal sealed class BluetoothRadioSwitch : IBluetoothRadioSwitch
{
    private readonly RadioStateSwitch stateSwitch = new(FindRadioAsync);

    public Task<bool> TryTurnOffAsync(CancellationToken cancellation) => stateSwitch.TryTurnOffAsync(cancellation);
    public Task TurnOnAsync() => stateSwitch.TurnOnAsync();

    private static async Task<IRadioPort?> FindRadioAsync(CancellationToken cancellation)
    {
        if (await Radio.RequestAccessAsync().AsTask(cancellation) != RadioAccessStatus.Allowed) return null;
        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellation);
        var radio = adapter is null ? null : await adapter.GetRadioAsync().AsTask(cancellation);
        return radio is null ? null : new WindowsRadioPort(radio);
    }

    private sealed class WindowsRadioPort(Radio radio) : IRadioPort
    {
        public bool IsOn => radio.State == RadioState.On;
        public async Task<bool> SetOnAsync(bool on) =>
            await radio.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
    }
}
