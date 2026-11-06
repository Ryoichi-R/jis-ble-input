using System.Text.Json;
using JisBleInput.Preflight;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAILED: {name}");
    }
    passed++;
    Console.WriteLine($"PASS: {name}");
}

var timeout = TimeSpan.FromSeconds(1);
var calls = 0;
var notWindows = await DiagnosticProbe.RunAsync(false, _ =>
{
    calls++;
    return Task.FromResult<AdapterInfo?>(new(true, true));
}, timeout);
Check(notWindows.ProbeStatus == "NotWindows" && notWindows.ExitCode == 3 && calls == 0, "non-Windows never queries an adapter");

foreach (var le in new[] { false, true })
{
    foreach (var peripheral in new[] { false, true })
    {
        var result = await DiagnosticProbe.RunAsync(true, _ => Task.FromResult<AdapterInfo?>(new(le, peripheral)), timeout);
        Check(result.ProbeStatus == "CapabilityReported" && result.ExitCode == (le && peripheral ? 0 : 2), $"capability flags {le}/{peripheral}");
        Check(result.HogpStatus == "NotTested" && result.ReceiverResult == "NotTested" && !result.CanStartProductImplementation, "capability does not approve text transmission");
    }
}

var absent = await DiagnosticProbe.RunAsync(true, _ => Task.FromResult<AdapterInfo?>(null), timeout);
Check(absent.ProbeStatus == "NoAdapter" && absent.ExitCode == 2 && absent.IsPeripheralRoleSupported is null, "missing adapter differs from negative capability");

var failed = await DiagnosticProbe.RunAsync(true, _ => throw new InvalidOperationException("private-message-fixture"), timeout);
Check(failed.ProbeStatus == "ProbeFailed" && failed.ExitCode == 3, "query errors are not hardware failure");
Check(!JsonSerializer.Serialize(failed).Contains("private-message-fixture", StringComparison.Ordinal), "reports do not expose exception messages");

var timedOut = await DiagnosticProbe.RunAsync(true, async token =>
{
    await Task.Delay(Timeout.Infinite, token);
    return null;
}, TimeSpan.FromMilliseconds(20));
Check(timedOut.ProbeStatus == "ProbeTimedOut" && timedOut.IsLowEnergySupported is null, "bounded query timeout stays unknown");

var spontaneousCancellation = await DiagnosticProbe.RunAsync(true, _ => throw new OperationCanceledException(), timeout);
Check(spontaneousCancellation.ProbeStatus == "ProbeFailed", "unrequested cancellation is a query error");

foreach (var invalid in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(31) })
{
    try
    {
        await DiagnosticProbe.RunAsync(false, _ => Task.FromResult<AdapterInfo?>(null), invalid);
        Check(false, "invalid timeout rejected");
    }
    catch (ArgumentOutOfRangeException)
    {
        Check(true, "invalid timeout rejected");
    }
}

Console.WriteLine($"Passed: {passed}");
