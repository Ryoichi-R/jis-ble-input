using System.Text.Json;
using System.Text.Json.Serialization;

namespace JisBleInput.Core;

public sealed record InputPreferences(int Version = 1,
    TextInputMode Mode = TextInputMode.Japanese,
    JapaneseSpeed JapaneseSpeed = JapaneseSpeed.Standard,
    AsciiSpeed AsciiSpeed = AsciiSpeed.Fast,
    ReceiverKeyboardLayout Layout = ReceiverKeyboardLayout.JapaneseJis)
{
    public void Validate()
    {
        if (Version != 1 || !Enum.IsDefined(Mode) || !Enum.IsDefined(JapaneseSpeed) || JapaneseSpeed > JapaneseSpeed.Faster
            || !Enum.IsDefined(AsciiSpeed) || !Enum.IsDefined(Layout))
            throw new InvalidDataException("入力設定の形式に対応していません。");
    }
}

// Only the input method, speeds and receiver layout are saved, never the text.
public sealed class InputPreferencesStore(string path)
{
    private readonly string filePath = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 4, Converters = { new JsonStringEnumConverter() } };

    public InputPreferences Load()
    {
        if (!File.Exists(filePath)) return new();
        using var stream = File.OpenRead(filePath);
        if (stream.Length > 4096) throw new InvalidDataException("入力設定が大きすぎます。");
        var preferences = JsonSerializer.Deserialize<InputPreferences>(stream, JsonOptions)
            ?? throw new InvalidDataException("入力設定が空です。");
        preferences.Validate();
        return preferences;
    }

    public void Save(InputPreferences preferences)
    {
        preferences.Validate();
        PreferenceFile.Write(filePath, JsonSerializer.SerializeToUtf8Bytes(preferences, JsonOptions));
    }
}
