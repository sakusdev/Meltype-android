// SPDX-License-Identifier: GPL-3.0-or-later

using Meltype.Config;

namespace Meltype.Android;

internal enum KeyboardTheme { System, Light, Dark }
internal enum InitialInputMode { Remember, Japanese, English }

// Independent of Android bindings so privacy and persisted-value validation can
// be checked by the Core test runner without an Android SDK.
internal sealed record KeyboardOptions
{
    public KeyboardTheme Theme { get; init; }
    public int KeyHeightDp { get; init; } = 48;
    public InitialInputMode InitialMode { get; init; }
    public bool KeyVibration { get; init; } = true;
    public bool KeySound { get; init; }
    public bool KeyPreview { get; init; } = true;
    public bool SpaceSwipe { get; init; } = true;
    public bool BackspaceRepeat { get; init; } = true;
    public bool LiveConversion { get; init; } = true;
    public DetectionLevel Detection { get; init; } = DetectionLevel.Balanced;
    public PunctuationStyle Punctuation { get; init; } = PunctuationStyle.Japanese;
    public bool CorrectTypos { get; init; } = true;
    public bool AutoCorrectAfterCommit { get; init; } = true;
    public bool TranslationCandidates { get; init; } = true;
    public bool PersonalizedLearning { get; init; } = true;

    public KeyboardOptions Normalize() => this with
    {
        Theme = Enum.IsDefined(Theme) ? Theme : KeyboardTheme.System,
        KeyHeightDp = KeyHeightDp is 40 or 48 or 56 or 64 ? KeyHeightDp : 48,
        InitialMode = Enum.IsDefined(InitialMode) ? InitialMode : InitialInputMode.Remember,
        Detection = Enum.IsDefined(Detection) ? Detection : DetectionLevel.Balanced,
        Punctuation = Enum.IsDefined(Punctuation) ? Punctuation : PunctuationStyle.Japanese,
    };

    public bool CanLearn(EditorInputPolicy policy) => PersonalizedLearning && policy.AllowLearning;

    public bool StartInEnglish(EditorInputPolicy policy, bool previousEnglish) => policy.Direct || (InitialMode switch
    {
        InitialInputMode.Japanese => false,
        InitialInputMode.English => true,
        _ => previousEnglish,
    });

    public (string Comma, string Period) PunctuationCharacters => Punctuation switch
    {
        PunctuationStyle.FullWidthCommaPeriod => ("，", "．"),
        PunctuationStyle.FullWidthCommaKuten => ("，", "。"),
        PunctuationStyle.ToutenFullWidthPeriod => ("、", "．"),
        _ => ("、", "。"),
    };

    public Settings ToCoreSettings() => new()
    {
        LiveConversion = LiveConversion,
        DetectionLevel = Detection,
        Punctuation = Punctuation,
        CorrectTypos = CorrectTypos,
        AutoCorrectAfterCommit = AutoCorrectAfterCommit,
        TranslationCandidates = TranslationCandidates,
        LearningEnabled = PersonalizedLearning,
        FileLog = false,
        LogTypedText = false,
    };
}
