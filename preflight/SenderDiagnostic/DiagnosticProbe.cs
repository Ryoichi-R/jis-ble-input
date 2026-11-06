namespace JisBleInput.Preflight;

internal sealed record AdapterInfo(bool IsLowEnergySupported, bool IsPeripheralRoleSupported);

internal sealed record DiagnosticResult(
    string ProbeStatus,
    bool? IsLowEnergySupported = null,
    bool? IsPeripheralRoleSupported = null,
    string? ErrorType = null,
    string? ErrorCode = null)
{
    public int SchemaVersion => 1;
    public string Scope => "SenderOnlyReadOnlyAdapterQuery";
    public string HogpStatus => "NotTested";
    public string ReceiverResult => "NotTested";
    public bool CanStartProductImplementation => false;
    public int ExitCode => ProbeStatus == "NoAdapter" ? 2
        : ProbeStatus != "CapabilityReported" ? 3
        : IsLowEnergySupported == true && IsPeripheralRoleSupported == true ? 0 : 2;
}

internal static class DiagnosticProbe
{
    public static async Task<DiagnosticResult> RunAsync(
        bool isWindows,
        Func<CancellationToken, Task<AdapterInfo?>> query,
        TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        if (!isWindows)
        {
            return new DiagnosticResult("NotWindows");
        }
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            var adapter = await query(cancellation.Token).WaitAsync(cancellation.Token);
            return adapter is null
                ? new DiagnosticResult("NoAdapter")
                : new DiagnosticResult("CapabilityReported", adapter.IsLowEnergySupported, adapter.IsPeripheralRoleSupported);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return new DiagnosticResult("ProbeTimedOut", ErrorType: "OperationCanceledException");
        }
        catch (Exception error)
        {
            // Error messages can contain machine-specific paths or identifiers.
            return new DiagnosticResult("ProbeFailed", ErrorType: error.GetType().Name, ErrorCode: $"0x{error.HResult:X8}");
        }
    }
}
