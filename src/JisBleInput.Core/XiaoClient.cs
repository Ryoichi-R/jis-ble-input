using System.Collections.Concurrent;

namespace JisBleInput.Core;

public interface IXiaoChannel : IDisposable
{
    Task<int> ReadAsync(byte[] buffer, CancellationToken cancellation);
    Task WriteAsync(byte[] buffer, CancellationToken cancellation);
}

public sealed class XiaoRequestTimeoutException(bool requestStarted, Exception innerException)
    : TimeoutException(requestStarted ? "XIAOの応答が途絶えました。" : "XIAOへの送信待ちが時間切れになりました。", innerException)
{
}

public sealed record XiaoStatus(byte Flags, byte Mode, byte[] BoardId, byte[] PeerId, byte[] DescriptorHash, string Firmware)
{
    public const int Length = 57;
    // Firmware versions this app has been verified against. Test-XiaoContract.ps1 requires the current firmware here.
    public static readonly IReadOnlyList<string> SupportedFirmware = ["0.1.0", "0.1.1", "0.1.2"];
    public bool FirmwareSupported => SupportedFirmware.Contains(Firmware);
    public bool Ready => (Flags & 15) == 7;
    public bool? CapsLock => (Flags & 16) == 0 ? null : (Flags & 32) != 0;
    public bool Bonded => (Flags & 64) != 0;
    public string Board => Convert.ToHexString(BoardId);
    public string Peer => Convert.ToHexString(PeerId);
    public static XiaoStatus Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != Length || !data[..4].SequenceEqual("JBI1"u8) || data[5] > 1)
            throw new InvalidDataException("XIAOの製品識別・状態形式が一致しません。");
        return new(data[4], data[5], data.Slice(6, 8).ToArray(), data.Slice(14, 8).ToArray(), data.Slice(22, 32).ToArray(),
            $"{data[54]}.{data[55]}.{data[56]}");
    }
}

// One reader, one ordered writer, independent command completions (including PING).
public sealed class XiaoClient : IDisposable
{
    private sealed record Snapshot(uint Generation, XiaoStatus Status);
    private sealed record Pending(byte Command, TaskCompletionSource<XiaoFrame> Completion);
    private readonly IXiaoChannel channel;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly ConcurrentDictionary<uint, Pending> pending = new();
    private readonly Guid session = Guid.NewGuid();
    private readonly TimeSpan timeout;
    private uint sequence;
    private readonly Task reader;
    private Task? heartbeat;
    private int disposed;
    private Snapshot? snapshot;
    public uint Generation => Volatile.Read(ref snapshot)?.Generation ?? 0;
    public XiaoStatus? Current => Volatile.Read(ref snapshot)?.Status;
    public event Action? Changed;
    public event Action? Faulted;

    public XiaoClient(IXiaoChannel channel, TimeSpan? timeout = null)
    {
        this.channel = channel;
        this.timeout = timeout ?? TimeSpan.FromSeconds(1);
        reader = ReadAsync();
    }

    public async Task StartAsync(CancellationToken cancellation)
    {
        await RequestAsync(XiaoCommand.Hello, [], 0, cancellation).ConfigureAwait(false);
        if (Current is null) throw new InvalidDataException("XIAOが状態を返しませんでした。");
        heartbeat = HeartbeatAsync();
    }

    public async Task RequestAsync(XiaoCommand command, byte[] payload, uint generation, CancellationToken cancellation)
    {
        using var requestTimeout = new CancellationTokenSource(timeout);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellation, requestTimeout.Token);
        uint number = 0;
        var completion = new TaskCompletionSource<XiaoFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = false;
        var registered = false;
        try
        {
            await writer.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                if (sequence == uint.MaxValue) throw new IOException("USB sessionの更新が必要です。");
                number = sequence++;
                if (!pending.TryAdd(number, new Pending((byte)((byte)command | 0x80), completion))) throw new IOException("USB sequenceが重複しました。");
                registered = true;
                // Once Write begins, cancellation has an unknown remote result.
                sent = true;
                await channel.WriteAsync(XiaoProtocol.Encode(new((byte)command, session, number, generation, payload)), deadline.Token).ConfigureAwait(false);
            }
            finally { writer.Release(); }
            var response = await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (command == XiaoCommand.ClearBond && response.Payload[0] == (byte)XiaoResult.StorageFailed)
                throw new IOException("XIAOの登録ファイルを削除できませんでした。基板に再接続して登録状態を確認してください。");
            if (response.Payload[0] != (byte)XiaoResult.Success)
                throw new IOException($"XIAO操作失敗: {(XiaoResult)response.Payload[0]}。自動再送しません。");
        }
        catch (OperationCanceledException error) when (requestTimeout.IsCancellationRequested
            && !cancellation.IsCancellationRequested && !lifetime.IsCancellationRequested)
        {
            if (sent) Abort();
            throw new XiaoRequestTimeoutException(sent, error);
        }
        catch
        {
            if (sent) Abort();
            throw;
        }
        finally { if (registered) pending.TryRemove(number, out _); }
    }

    private async Task ReadAsync()
    {
        var frames = new XiaoFrameReader();
        var buffer = new byte[256];
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var count = await channel.ReadAsync(buffer, lifetime.Token).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("XIAOが切断されました。");
                for (var index = 0; index < count; index++)
                {
                    var frame = frames.Push(buffer[index]);
                    if (frame is null) continue;
                    if (frame.Session != session) throw new InvalidDataException("USB sessionが変わりました。");
                    if (frame.Command == 0xf0)
                    {
                        AcceptStatus(frame.Generation, frame.Payload);
                        continue;
                    }
                    if (!pending.TryGetValue(frame.Sequence, out var request) || request.Command != frame.Command)
                        throw new InvalidDataException("XIAOの応答順序が一致しません。");
                    if (frame.Payload.Length == 0) throw new InvalidDataException("XIAOの応答が空です。");
                    if (frame.Payload[0] == (byte)XiaoResult.Success)
                        AcceptStatus(frame.Generation, frame.Payload.AsSpan(1));
                    if (!request.Completion.TrySetResult(frame)) throw new InvalidDataException("XIAOの応答が重複しました。");
                }
            }
        }
        catch (Exception error)
        {
            foreach (var entry in pending.Values) entry.Completion.TrySetException(new IOException("XIAO通信が終了しました。自動再送しません。", error));
            Abort();
        }
    }

    private void AcceptStatus(uint generation, ReadOnlySpan<byte> payload)
    {
        var next = XiaoStatus.Parse(payload);
        if (Current is { } previous && (previous.Board != next.Board || previous.Firmware != next.Firmware
            || !previous.DescriptorHash.SequenceEqual(next.DescriptorHash)))
            throw new InvalidDataException("XIAOの識別・キー定義が接続中に変わりました。");
        if (generation < Generation) throw new InvalidDataException("XIAOの接続世代が巻き戻りました。");
        var changed = Generation != generation || Current is null || Current.Flags != next.Flags || Current.Mode != next.Mode
            || Current.Board != next.Board || Current.Peer != next.Peer;
        Volatile.Write(ref snapshot, new Snapshot(generation, next));
        if (changed) Changed?.Invoke();
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(250, lifetime.Token).ConfigureAwait(false);
                await RequestAsync(XiaoCommand.Ping, [], Generation, lifetime.Token).ConfigureAwait(false);
            }
        }
        catch { Abort(); }
    }

    public void Dispose() => Abort();
    private void Abort()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        channel.Dispose();
        lifetime.Cancel();
        foreach (var entry in pending.Values) entry.Completion.TrySetException(new IOException("XIAO通信を終了しました。"));
        Faulted?.Invoke();
    }
}
