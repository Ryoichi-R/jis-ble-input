using System.Runtime.InteropServices;
using System.Text.Json;

namespace JisBleInput.Core;

public enum ConnectionMethod { LocalBluetooth, XiaoUsb }

public static class ConnectionAvailability
{
    public static bool IsAvailable(Architecture architecture, ConnectionMethod method) => method switch
    {
        ConnectionMethod.LocalBluetooth => true,
        ConnectionMethod.XiaoUsb => architecture == Architecture.Arm64,
        _ => false
    };
}

public sealed record TransportPreferences(int Version = 1, ConnectionMethod Method = ConnectionMethod.LocalBluetooth, string? BoardHash = null, string? PeerHash = null)
{
    public void Validate()
    {
        if (Version != 1 || !Enum.IsDefined(Method) || !IsHash(BoardHash) || !IsHash(PeerHash))
            throw new InvalidDataException("送信方式の設定が不正です。");
    }
    private static bool IsHash(string? value) => value is null || value.Length == 64 && value.All(char.IsAsciiHexDigit);
}

public sealed class TransportPreferencesStore(string path)
{
    public TransportPreferences Load()
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("送信方式の設定が大きすぎます。");
        var result = JsonSerializer.Deserialize<TransportPreferences>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("送信方式の設定が空です。");
        result.Validate();
        return result;
    }
    public void Save(TransportPreferences preferences)
    {
        preferences.Validate();
        PreferenceFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(preferences));
    }
}
