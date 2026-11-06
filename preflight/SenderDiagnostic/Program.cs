using System.Text;
using System.Text.Json;
using Windows.Devices.Bluetooth;
using JisBleInput.Preflight;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("Surface sender Bluetooth capability diagnostic");
Console.WriteLine("Read-only: no advertising, pairing, GATT service or keyboard input.");
Console.WriteLine();

var result = await DiagnosticProbe.RunAsync(
    OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041),
    async cancellation =>
    {
        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellation);
        return adapter is null ? null : new AdapterInfo(adapter.IsLowEnergySupported, adapter.IsPeripheralRoleSupported);
    },
    TimeSpan.FromSeconds(5));

Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine();
Console.WriteLine("This checks adapter capability only. Bluetooth text input is NOT tested.");

if (!Console.IsInputRedirected && !args.Contains("--no-pause", StringComparer.Ordinal))
{
    Console.WriteLine("Press Enter to close this window.");
    Console.ReadLine();
}
return result.ExitCode;
