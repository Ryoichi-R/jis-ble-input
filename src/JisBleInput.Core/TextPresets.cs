using System.Text.Json;
using System.Text.Json.Serialization;

namespace JisBleInput.Core;

public sealed record TextPreset(string Name, string Text, TextInputMode Mode, int? BluetoothSpeed, int? XiaoSpeed)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name != Name.Trim() || Name.Any(char.IsControl))
            throw new ArgumentException("表示名は前後の空白を除いた1～80文字で入力してください。");
        if (!Enum.IsDefined(Mode)
            || (BluetoothSpeed is int local && (local < 0 || local >= InputSettings.SpeedLabels(Mode).Count))
            || (XiaoSpeed is int xiao && (xiao < 0 || xiao >= InputSettings.SpeedLabels(Mode, useXiao: true).Count)))
            throw new ArgumentException("入力方法と速度を確認してください。");
        if (Text is null) throw new ArgumentException("本文を入力してください。");
        var snapshot = TextSnapshot.Create(Text, allowLineBreaks: true);
        if (Mode == TextInputMode.Ascii) AsciiKeyEncoder.ValidateSnapshot(snapshot);
    }
}

public sealed record TextPresetLoad(TextPreset[] Presets, int Skipped = 0, bool InvalidFile = false)
{
    public bool NeedsBackup => Skipped > 0 || InvalidFile;
}

public sealed class TextPresetStore(string path)
{
    public const int MaxPresets = 100;
    private readonly string filePath = Path.GetFullPath(path);
    // Version 1 indices are in slow-to-fast order. Version 2 IDs stay fixed
    // independently of the displayed labels and measured times.
    private static readonly string[] SpeedIds = ["slow", "standard", "fastest"];
    private static readonly JsonSerializerOptions Options = new()
    {
        MaxDepth = 6,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false, namingPolicy: null) }
    };

    public TextPresetLoad Load()
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length > 3 * 1024 * 1024) return new([], InvalidFile: true);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 6 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Version", out var versionNode) || versionNode.ValueKind != JsonValueKind.Number
                || !versionNode.TryGetInt32(out var version) || version is not (1 or 2)
                || !root.TryGetProperty("Presets", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return new([], InvalidFile: true);
            var presets = new List<TextPreset>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries.EnumerateArray())
            {
                if (presets.Count == MaxPresets) break;
                try
                {
                    if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("Mode", out _)) continue;
                    var preset = version == 1 ? entry.Deserialize<TextPreset>(Options)
                        : entry.Deserialize<StoredPreset>(Options)?.ToPreset();
                    if (preset is null) continue;
                    preset.Validate();
                    if (names.Add(preset.Name)) presets.Add(preset);
                }
                catch (Exception error) when (error is ArgumentException or JsonException) { }
            }
            return new(presets.ToArray(), entries.GetArrayLength() - presets.Count);
        }
        catch (FileNotFoundException) { return new([]); }
        catch (DirectoryNotFoundException) { return new([]); }
        catch (JsonException) { return new([], InvalidFile: true); }
    }

    public string? Save(TextPreset[] presets, bool backupOriginal = false)
    {
        Validate(presets);
        var entries = presets.Select(preset => new StoredPreset(preset.Name, preset.Text, preset.Mode,
            GetId(preset.BluetoothSpeed), GetId(preset.XiaoSpeed))).ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new PresetFile(2, entries), Options);
        string? backup = null;
        if (backupOriginal)
        {
            backup = Path.Combine(Path.GetDirectoryName(filePath)!, $"text-presets-recovery-{Guid.NewGuid():N}.json");
            File.Copy(filePath, backup);
        }
        PreferenceFile.Write(filePath, bytes);
        return backup;
    }

    private static void Validate(TextPreset[] presets)
    {
        if (presets.Length > MaxPresets) throw new ArgumentException($"定型文は最大{MaxPresets}件です。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in presets)
        {
            if (preset is null) throw new ArgumentException("定型文の内容を確認してください。");
            preset.Validate();
            if (!names.Add(preset.Name)) throw new ArgumentException("同じ表示名の定型文がすでにあります。");
        }
    }

    private static string? GetId(int? speed) => speed is int index ? SpeedIds[index] : null;
    private static int? GetSpeed(string? id) => id is null ? null : Array.IndexOf(SpeedIds, id);

    private sealed record StoredPreset(string Name, string Text, TextInputMode Mode, string? BluetoothSpeed, string? XiaoSpeed)
    {
        public TextPreset ToPreset() => new(Name, Text, Mode, GetSpeed(BluetoothSpeed), GetSpeed(XiaoSpeed));
    }

    private sealed record PresetFile(int Version, StoredPreset[] Presets);
}
