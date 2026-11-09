namespace JisBleInput.Core;

public static class AsciiKeyEncoder
{
    // Dedicated ImeOff, not a state-dependent half/full-width toggle.
    public static IReadOnlyList<KeyStep> EncodePreparation() =>
    [
        new(new KeyStroke(0, 0x91), 50),
        new(default, 250)
    ];

    public static void ValidateSnapshot(TextSnapshot snapshot)
    {
        foreach (var unit in snapshot.Units)
            if (!unit.IsLineBreak && unit.Character is < ' ' or > '~')
                throw new ArgumentException($"{unit.SourceOffset + 1}文字目は英数字・URLモードの対象外です。半角ASCII以外を含む文章は日本語モードで入力してください。");
    }

    public static IReadOnlyList<KeyStep> EncodeCharacter(char character, ReceiverKeyboardLayout layout,
        AsciiSpeed speed = AsciiSpeed.Fast)
    {
        var (press, release) = speed switch
        {
            AsciiSpeed.Relaxed => (60, 60),
            AsciiSpeed.Fast => (40, 40),
            AsciiSpeed.Fastest => (25, 25),
            AsciiSpeed.XiaoStandard => (5, 5),
            AsciiSpeed.XiaoFastest => (3, 2),
            _ => throw new ArgumentException("英数字の入力速度を確認してください。")
        };
        var stroke = ToStroke(character, layout);
        return [new(stroke, press), new(default, release)];
    }

    public static KeyStroke ToStroke(char character, ReceiverKeyboardLayout layout)
    {
        if (!Enum.IsDefined(layout)) throw new ArgumentException("受信側の配列を確認してください。");
        if (character is >= 'a' and <= 'z') return new(0, (byte)(0x04 + character - 'a'));
        if (character is >= 'A' and <= 'Z') return new(2, (byte)(0x04 + character - 'A'));
        if (character is >= '1' and <= '9') return new(0, (byte)(0x1E + character - '1'));
        if (character == '0') return new(0, 0x27);
        if (character == ' ') return new(0, 0x2C);

        // Printable ASCII only. JIS punctuation follows the receiver's 106/109
        // layout; sender-side keyboard labels do not determine these characters.
        const string shiftedUsDigits = "!@#$%^&*()";
        const string shiftedJisDigits = "!\"#$%&'()";
        var shiftedDigits = layout == ReceiverKeyboardLayout.JapaneseJis ? shiftedJisDigits : shiftedUsDigits;
        var digitIndex = shiftedDigits.IndexOf(character);
        if (digitIndex >= 0) return new(2, (byte)(0x1E + digitIndex));

        if (layout == ReceiverKeyboardLayout.JapaneseJis)
            return character switch
            {
                '-' => new(0, 0x2D),
                '=' => new(2, 0x2D),
                '^' => new(0, 0x2E),
                '~' => new(2, 0x2E),
                '@' => new(0, 0x2F),
                '`' => new(2, 0x2F),
                '[' => new(0, 0x30),
                '{' => new(2, 0x30),
                ']' => new(0, 0x32),
                '}' => new(2, 0x32),
                ';' => new(0, 0x33),
                '+' => new(2, 0x33),
                ':' => new(0, 0x34),
                '*' => new(2, 0x34),
                '\\' => new(0, 0x87),
                '|' => new(2, 0x89),
                '_' => new(2, 0x87),
                ',' => new(0, 0x36),
                '<' => new(2, 0x36),
                '.' => new(0, 0x37),
                '>' => new(2, 0x37),
                '/' => new(0, 0x38),
                '?' => new(2, 0x38),
                _ => throw new ArgumentException("英数字・URLモードは半角ASCIIだけを入力できます。")
            };
        return character switch
        {
            '-' => new(0, 0x2D),
            '_' => new(2, 0x2D),
            '=' => new(0, 0x2E),
            '+' => new(2, 0x2E),
            '[' => new(0, 0x2F),
            '{' => new(2, 0x2F),
            ']' => new(0, 0x30),
            '}' => new(2, 0x30),
            '\\' => new(0, 0x31),
            '|' => new(2, 0x31),
            ';' => new(0, 0x33),
            ':' => new(2, 0x33),
            '\'' => new(0, 0x34),
            '"' => new(2, 0x34),
            '`' => new(0, 0x35),
            '~' => new(2, 0x35),
            ',' => new(0, 0x36),
            '<' => new(2, 0x36),
            '.' => new(0, 0x37),
            '>' => new(2, 0x37),
            '/' => new(0, 0x38),
            '?' => new(2, 0x38),
            _ => throw new ArgumentException("英数字・URLモードは半角ASCIIだけを入力できます。")
        };
    }
}
