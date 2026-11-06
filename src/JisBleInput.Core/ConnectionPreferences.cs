using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JisBleInput.Core;

public sealed record ConnectionPreferences(int Version = 1, bool AutoReconnect = true, string? PeerHash = null)
{
    [JsonIgnore]
    public bool ShouldAutoStart => AutoReconnect;

    public void Validate()
    {
        if (Version != 1 || PeerHash is not null && (PeerHash.Length != 64 || !PeerHash.All(char.IsAsciiHexDigit)))
            throw new InvalidDataException("接続設定の形式に対応していません。");
    }

    public static string HashPeer(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    }

    public string? FindRememberedPeer(IEnumerable<string> connectedPairedSubscribers)
    {
        Validate();
        if (!AutoReconnect || PeerHash is null) return null;
        return connectedPairedSubscribers.FirstOrDefault(id => string.Equals(HashPeer(id), PeerHash, StringComparison.OrdinalIgnoreCase));
    }
}

// Only this app's connection preference file is read. No device name, raw device
// ID, text, or pairing credentials are saved. Pairing remains owned by Windows.
public sealed class ConnectionPreferencesStore(string path)
{
    private readonly string filePath = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 4 };

    public ConnectionPreferences Load()
    {
        if (!File.Exists(filePath)) return new();
        using var stream = File.OpenRead(filePath);
        if (stream.Length > 4096) throw new InvalidDataException("接続設定が大きすぎます。");
        var preferences = JsonSerializer.Deserialize<ConnectionPreferences>(stream, JsonOptions)
            ?? throw new InvalidDataException("接続設定が空です。");
        preferences.Validate();
        return preferences;
    }

    public void Save(ConnectionPreferences preferences)
    {
        preferences.Validate();
        PreferenceFile.Write(filePath, JsonSerializer.SerializeToUtf8Bytes(preferences, JsonOptions));
    }
}

internal static class PreferenceFile
{
    public static void Write(string filePath, byte[] data)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var pending = Path.Combine(directory, $".preferences-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            File.Move(pending, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(pending)) File.Delete(pending);
        }
    }
}
