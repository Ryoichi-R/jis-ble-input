namespace JisBleInput.Core;

public interface IReportTransport
{
    bool IsReady(SendTarget target);
    Task WriteAsync(byte[] report, SendTarget target, CancellationToken cancellation);
}

public enum SendState { Idle, PreparingIme, Ready, Sending, Pausing, Paused, KeysSent, StoppedPartial, Faulted }

public sealed record TransferProgress(int ProcessedCharacters, int TotalCharacters,
    TimeSpan ActiveElapsed, TimeSpan? EstimatedRemaining);

public sealed class StagedSender(IReportTransport transport, Func<int, CancellationToken, Task>? delay = null,
    TimeProvider? timeProvider = null)
{
    private readonly object sync = new();
    private readonly Func<int, CancellationToken, Task> wait = delay ?? ((ms, token) => Task.Delay(ms, token));
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private CancellationTokenSource? stop;
    private SendTarget? activeTarget;
    private TaskCompletionSource? resumeSignal;
    private bool pauseRequested;
    private bool busy;
    private TimeSpan accumulatedActive;
    private long? activeSince;
    public SendState State { get; private set; } = SendState.Idle;
    public int ProcessedCharacters { get; private set; }
    public int TotalCharacters { get; private set; }
    public bool? ReleaseReportSent { get; private set; }
    public event Action<int, int>? Progress;
    public event Action<SendState>? StateChanged;

    public TransferProgress ProgressInfo
    {
        get
        {
            lock (sync)
            {
                var elapsed = accumulatedActive + (activeSince is long since ? clock.GetElapsedTime(since) : TimeSpan.Zero);
                TimeSpan? remaining = ProcessedCharacters == 0 ? null : TimeSpan.FromTicks((long)Math.Clamp(
                    elapsed.Ticks * (double)(TotalCharacters - ProcessedCharacters) / ProcessedCharacters, 0, TimeSpan.MaxValue.Ticks));
                return new TransferProgress(ProcessedCharacters, TotalCharacters, elapsed, remaining);
            }
        }
    }

    public Task PrepareImeAsync(SendTarget destination, SendOptions? options = null, CancellationToken cancellation = default)
    {
        var selected = options ?? new SendOptions();
        selected.Validate();
        return Start(null, destination, selected, cancellation);
    }

    public Task SendAsync(string text, SendTarget destination, bool allowLineBreaks = false, SendOptions? options = null)
    {
        // Validate the WHOLE text before sending even a mode-switch key.
        var selected = options ?? new SendOptions();
        selected.Validate();
        var snapshot = TextSnapshot.Create(text, allowLineBreaks);
        if (selected.Mode == TextInputMode.Ascii) AsciiKeyEncoder.ValidateSnapshot(snapshot);
        return Start(snapshot, destination, selected, CancellationToken.None);
    }

    public Task SendSnapshotAsync(TextSnapshot snapshot, SendTarget destination, SendOptions options, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options.Validate();
        if (options.Mode == TextInputMode.Ascii) AsciiKeyEncoder.ValidateSnapshot(snapshot);
        return Start(snapshot, destination, options, cancellation);
    }

    private Task Start(TextSnapshot? snapshot, SendTarget destination, SendOptions options, CancellationToken externalCancellation)
    {
        externalCancellation.ThrowIfCancellationRequested();
        CancellationToken cancellation;
        SendState state;
        lock (sync)
        {
            if (busy) throw new InvalidOperationException("送信処理中です。");
            if (!transport.IsReady(destination)) throw new InvalidOperationException("接続先を確認してください。");
            stop?.Dispose();
            stop = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
            cancellation = stop.Token;
            activeTarget = destination;
            resumeSignal = null;
            pauseRequested = false;
            ProcessedCharacters = 0;
            TotalCharacters = snapshot?.UnitCount ?? 0;
            ReleaseReportSent = null;
            accumulatedActive = TimeSpan.Zero;
            activeSince = clock.GetTimestamp();
            state = State = snapshot is null ? SendState.PreparingIme : SendState.Sending;
            busy = true;
        }
        StateChanged?.Invoke(state);
        // Exactly one worker owns the snapshot, destination generation and queue.
        return RunAsync(snapshot, destination, options, cancellation);
    }

    public bool RequestPause()
    {
        lock (sync)
        {
            if (!busy || State != SendState.Sending) return false;
            pauseRequested = true;
            State = SendState.Pausing;
        }
        StateChanged?.Invoke(SendState.Pausing);
        return true;
    }

    public void Resume()
    {
        TaskCompletionSource signal;
        lock (sync)
        {
            if (!busy || State != SendState.Paused || resumeSignal is null || activeTarget is null)
                throw new InvalidOperationException("続行できる一時停止中の送信はありません。");
            if (!transport.IsReady(activeTarget))
            {
                stop!.Cancel();
                throw new InvalidOperationException("接続先または接続状態が変わったため続行できません。受信側の途中入力を確認してください。");
            }
            signal = resumeSignal;
            resumeSignal = null;
            pauseRequested = false;
            activeSince = clock.GetTimestamp();
            ReleaseReportSent = null;
            State = SendState.Sending;
        }
        StateChanged?.Invoke(SendState.Sending);
        signal.TrySetResult();
    }

    public void Cancel()
    {
        var changed = false;
        lock (sync)
        {
            stop?.Cancel();
            if (busy)
            {
                State = SendState.StoppedPartial;
                PauseClock();
                changed = true;
            }
        }
        if (changed) StateChanged?.Invoke(SendState.StoppedPartial);
    }

    private void PauseClock()
    {
        if (activeSince is long since) accumulatedActive += clock.GetElapsedTime(since);
        activeSince = null;
    }

    private async Task<bool> WaitIfPausedAsync(SendTarget destination, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        lock (sync) { if (!pauseRequested) return false; }
        // A pause starts only between complete character sequences, never between
        // code entry and IME commit. Do not leave a modifier held while waiting.
        await transport.WriteAsync(new byte[8], destination, cancellation);
        TaskCompletionSource signal;
        lock (sync)
        {
            cancellation.ThrowIfCancellationRequested();
            PauseClock();
            ReleaseReportSent = true;
            resumeSignal = signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            State = SendState.Paused;
        }
        StateChanged?.Invoke(SendState.Paused);
        await signal.Task.WaitAsync(cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (!transport.IsReady(destination)) throw new InvalidOperationException("一時停止後に接続が変わりました。");
        return true;
    }

    private async Task WriteStepsAsync(IReadOnlyList<KeyStep> steps, SendTarget destination, CancellationToken cancellation)
    {
        foreach (var step in steps)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!transport.IsReady(destination)) throw new InvalidOperationException("接続先またはCapsLockの状態が変わりました。");
            await transport.WriteAsync(step.Stroke.ToReport(), destination, cancellation);
            await wait(step.DelayMilliseconds, cancellation);
        }
    }

    private async Task RunAsync(TextSnapshot? snapshot, SendTarget destination, SendOptions options, CancellationToken cancellation)
    {
        try
        {
            if (snapshot is null)
            {
                // Optional setup changes IME mode only: no text, F5 or Enter.
                await WriteStepsAsync(options.Mode == TextInputMode.Ascii ? AsciiKeyEncoder.EncodePreparation()
                    : ImeKeyEncoder.EncodePreparation(options.Speed), destination, cancellation);
            }
            else
            {
                for (var index = 0; index < snapshot.UnitCount; index++)
                {
                    var resumed = await WaitIfPausedAsync(destination, cancellation);
                    if (options.Mode == TextInputMode.Ascii && (index == 0 || resumed))
                        await WriteStepsAsync(AsciiKeyEncoder.EncodePreparation(), destination, cancellation);
                    var unit = snapshot.Units[index];
                    await WriteStepsAsync(unit.IsLineBreak ? ImeKeyEncoder.EncodeLineBreak()
                        : options.Mode == TextInputMode.Ascii ? AsciiKeyEncoder.EncodeCharacter(unit.Character, options.Layout, options.DirectSpeed)
                        : ImeKeyEncoder.EncodeCharacter(unit.Character, options.Speed), destination, cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    lock (sync) ProcessedCharacters = index + 1;
                    Progress?.Invoke(ProcessedCharacters, snapshot.UnitCount);
                }
            }
            lock (sync)
            {
                cancellation.ThrowIfCancellationRequested();
                State = snapshot is null ? SendState.Ready : SendState.KeysSent;
            }
        }
        catch (OperationCanceledException)
        {
            lock (sync) State = SendState.StoppedPartial;
        }
        catch
        {
            lock (sync) State = SendState.Faulted;
            throw;
        }
        finally
        {
            lock (sync) PauseClock();
            ReleaseReportSent = false;
            using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await transport.WriteAsync(new byte[8], destination, releaseTimeout.Token);
                ReleaseReportSent = true;
            }
            catch { /* A local send cannot prove release on the remote PC. */ }
            lock (sync)
            {
                if (ReleaseReportSent == false && State is SendState.KeysSent or SendState.Ready)
                    State = SendState.StoppedPartial;
                resumeSignal = null;
                pauseRequested = false;
                activeTarget = null;
                busy = false;
            }
            StateChanged?.Invoke(State);
        }
    }
}
