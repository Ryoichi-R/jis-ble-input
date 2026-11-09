using System.Globalization;

namespace JisBleInput.Core;

public readonly record struct KeyStroke(byte Modifier, byte Usage)
{
    public byte[] ToReport() => [Modifier, 0, Usage, 0, 0, 0, 0, 0];
}

public readonly record struct KeyStep(KeyStroke Stroke, int DelayMilliseconds);
public sealed record SendTarget(string Id, long Generation);

public readonly record struct TextUnit(int SourceOffset, int SourceLength, char Character)
{
    public bool IsLineBreak => Character is '\r' or '\n';
}

public sealed class TextSnapshot
{
    public string Text { get; }
    public IReadOnlyList<TextUnit> Units { get; }
    public int UnitCount => Units.Count;
    private TextSnapshot(string text, List<TextUnit> units)
    {
        Text = text;
        Units = units.AsReadOnly();
    }

    public static int CountInputUnits(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var count = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
            count++;
        }
        return count;
    }

    public static TextSnapshot Create(string text, bool allowLineBreaks = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length is 0 or > 4000 || CountInputUnits(text) > 2000)
        {
            throw new ArgumentException("実験版の送信文字数は1〜2,000文字です。改行（CRLFを含む）は1文字として数えます。");
        }
        var units = new List<TextUnit>();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is '\r' or '\n')
            {
                if (!allowLineBreaks)
                    throw new ArgumentException("改行を送るには、Enterで改行できる複数行の入力欄を確認してください。");
                var length = text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                units.Add(new TextUnit(index, length, text[index]));
                index += length - 1;
                continue;
            }
            var category = char.GetUnicodeCategory(text[index]);
            if (char.IsControl(text[index]) || category is UnicodeCategory.Surrogate
                or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
            {
                throw new ArgumentException($"{index + 1}文字目（U+{(int)text[index]:X4}）は実験版の対象外です。Tab・補助平面・結合文字等は送れません。");
            }
            units.Add(new TextUnit(index, 1, text[index]));
        }
        return new TextSnapshot(text, units);
    }
}

public static class ImeKeyEncoder
{
    public static IReadOnlyList<KeyStep> EncodePreparation(JapaneseSpeed speed) => WithSpeed(EncodePreparation(), speed);
    public static IReadOnlyList<KeyStep> EncodeCharacter(char character, JapaneseSpeed speed) => WithSpeed(EncodeCharacter(character), speed);

    private static IReadOnlyList<KeyStep> WithSpeed(IReadOnlyList<KeyStep> steps, JapaneseSpeed speed)
    {
        if (speed == JapaneseSpeed.Standard) return steps;
        // SlightlyFaster keeps every key-down and digit delay and only reduces
        // mode/conversion/commit settling waits. Faster also shortens key holds;
        // the standard sequence remains available when a receiver drops input.
        if (speed == JapaneseSpeed.SlightlyFaster)
        {
            int[] slowerDelays = [50, 70, 50, 130, 25, 25, 25, 25, 25, 25, 25, 25, 50, 200, 50, 100];
            return steps.Select((step, index) => step with { DelayMilliseconds = slowerDelays[index] }).ToArray();
        }
        // XIAO presets use shorter key holds and IME settling waits.
        var (keyDelay, digitDelay, imeOn, hiragana, convert, commit) = speed switch
        {
            JapaneseSpeed.Faster => (40, 20, 60, 100, 150, 70),
            JapaneseSpeed.XiaoSlow => (10, 5, 1, 1, 15, 3),
            JapaneseSpeed.XiaoStandard => (4, 2, 1, 1, 15, 1),
            JapaneseSpeed.XiaoFastest => (2, 1, 1, 1, 6, 1),
            _ => throw new ArgumentException("日本語入力の速度を確認してください。")
        };
        int[] delays = [keyDelay, imeOn, keyDelay, hiragana,
            digitDelay, digitDelay, digitDelay, digitDelay, digitDelay, digitDelay, digitDelay, digitDelay,
            keyDelay, convert, keyDelay, commit];
        return steps.Select((step, index) => step with { DelayMilliseconds = delays[index] }).ToArray();
    }

    // Only for a confirmed ordinary multiline field. The previous text unit has
    // already committed its IME composition; a line break has no F5 conversion.
    public static IReadOnlyList<KeyStep> EncodeLineBreak() =>
    [
        new(new KeyStroke(0, 0x28), 50),
        new(default, 300)
    ];

    public static IReadOnlyList<KeyStep> EncodePreparation() =>
    [
        // Both operations select Hiragana, rather than toggling the current state.
        // Ctrl+CapsLock also covers layouts/hosts that ignore the LANG1 usage.
        // The commit wait and these settle waits form the gap between visible
        // characters; the F5 conversion wait is kept unchanged.
        new(new KeyStroke(0, 0x90), 50),
        new(default, 100),
        new(new KeyStroke(0x01, 0x39), 50),
        new(default, 150)
    ];

    public static IReadOnlyList<KeyStep> EncodeCharacter(char character)
    {
        _ = TextSnapshot.Create(character.ToString());
        // A prior commit can leave the IME in alphanumeric mode. Re-establish
        // the input mode before EACH code, not just at the start of a paragraph.
        var result = new List<KeyStep>(16);
        result.AddRange(EncodePreparation());
        foreach (var digit in ((int)character).ToString("X4", CultureInfo.InvariantCulture))
        {
            var stroke = digit is >= 'A' and <= 'F'
                ? new KeyStroke(0x02, (byte)(0x04 + digit - 'A'))
                : new KeyStroke(0, digit == '0' ? (byte)0x27 : (byte)(0x1E + digit - '1'));
            result.Add(new KeyStep(stroke, 25));
            result.Add(new KeyStep(default, 25));
        }
        result.Add(new KeyStep(new KeyStroke(0, 0x3E), 50));
        result.Add(new KeyStep(default, 300));
        result.Add(new KeyStep(new KeyStroke(0, 0x28), 50));
        result.Add(new KeyStep(default, 150));
        return result;
    }
}
