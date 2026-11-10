using System.Buffers.Binary;

namespace JisBleInput.Core;

public enum XiaoCommand : byte { Hello = 1, Status = 2, Arm = 3, Report = 4, Release = 5, Disarm = 6, Ping = 7, PairingOpen = 8, ClearBond = 9 }
public enum XiaoResult : byte { Success, InvalidFrame, WrongSession, WrongSequence, WrongGeneration, NotReady, NotArmed, Busy, NotificationFailed, Rejected, StorageFailed }
public sealed record XiaoFrame(byte Command, Guid Session, uint Sequence, uint Generation, byte[] Payload);

public static class XiaoProtocol
{
    public const byte Version = 1;
    public const int MaximumFrame = 128;
    public const int HeaderLength = 28;

    public static ushort Crc(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xffff;
        foreach (var value in data)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }

    public static byte[] Encode(XiaoFrame frame)
    {
        if (frame.Payload.Length > MaximumFrame - HeaderLength - 2) throw new ArgumentException("USBフレームが大きすぎます。");
        var raw = new byte[HeaderLength + frame.Payload.Length + 2];
        raw[0] = Version;
        raw[1] = frame.Command;
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(2), (ushort)frame.Payload.Length);
        frame.Session.TryWriteBytes(raw.AsSpan(4, 16));
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(20), frame.Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(24), frame.Generation);
        frame.Payload.CopyTo(raw, HeaderLength);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(raw.Length - 2), Crc(raw.AsSpan(0, raw.Length - 2)));
        var encoded = new List<byte>(raw.Length + 2) { 0 };
        var codeIndex = 0;
        byte code = 1;
        foreach (var value in raw)
        {
            if (value == 0) { encoded[codeIndex] = code; codeIndex = encoded.Count; encoded.Add(0); code = 1; }
            else { encoded.Add(value); code++; }
        }
        encoded[codeIndex] = code;
        encoded.Add(0);
        return encoded.ToArray();
    }

    // Argument excludes the zero terminator. The maximum is below a COBS 0xff block.
    public static XiaoFrame Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length is 0 or > MaximumFrame + 1) throw new InvalidDataException("USBフレーム長が不正です。");
        var raw = new List<byte>(MaximumFrame);
        for (var index = 0; index < encoded.Length;)
        {
            var code = encoded[index++];
            if (code == 0 || index + code - 1 > encoded.Length) throw new InvalidDataException("USBフレームの符号が不正です。");
            for (var count = 1; count < code; count++)
            {
                if (encoded[index] == 0) throw new InvalidDataException("USBフレーム内に終端があります。");
                raw.Add(encoded[index++]);
            }
            if (code != 255 && index < encoded.Length) raw.Add(0);
        }
        var bytes = raw.ToArray();
        if (bytes.Length is < HeaderLength + 2 or > MaximumFrame || bytes[0] != Version
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)) != bytes.Length - HeaderLength - 2
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(bytes.Length - 2)) != Crc(bytes.AsSpan(0, bytes.Length - 2)))
            throw new InvalidDataException("USBフレームの版・長さ・CRCが一致しません。");
        return new XiaoFrame(bytes[1], new Guid(bytes.AsSpan(4, 16)), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20)),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24)), bytes.AsSpan(HeaderLength, bytes.Length - HeaderLength - 2).ToArray());
    }
}

public sealed class XiaoFrameReader
{
    private readonly List<byte> buffer = [];
    private bool discarding;
    private long lastByte;
    public void Reset() { buffer.Clear(); discarding = false; }
    public XiaoFrame? Push(byte value) => Push(value, Environment.TickCount64);
    public XiaoFrame? Push(byte value, long now)
    {
        if (buffer.Count > 0 && now - lastByte >= 250) { buffer.Clear(); discarding = true; }
        lastByte = now;
        if (value != 0)
        {
            if (!discarding) buffer.Add(value);
            if (buffer.Count > XiaoProtocol.MaximumFrame + 1) { buffer.Clear(); discarding = true; }
            return null;
        }
        try { return discarding || buffer.Count == 0 ? null : XiaoProtocol.Decode(buffer.ToArray()); }
        finally { Reset(); }
    }
}
