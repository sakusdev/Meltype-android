// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 sakusdev

using Meltype.Android;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

internal static class AndroidSettingsTests
{
    [Test]
    public static void InvalidSavedValues_RecoverToUsableDefaults()
    {
        var options = new KeyboardOptions
        {
            Theme = (KeyboardTheme)99,
            InitialMode = (InitialInputMode)(-1),
            KeyHeightDp = int.MaxValue,
            Detection = (DetectionLevel)100,
            Punctuation = (PunctuationStyle)100,
            PersonalizedLearning = false,
        }.Normalize();
        Assert.Equal(KeyboardTheme.System, options.Theme);
        Assert.Equal(InitialInputMode.Remember, options.InitialMode);
        Assert.Equal(48, options.KeyHeightDp);
        Assert.Equal(DetectionLevel.Balanced, options.Detection);
        Assert.Equal(PunctuationStyle.Japanese, options.Punctuation);
        Assert.True(!options.PersonalizedLearning, "recovering appearance values must not enable learning");
        Assert.Equal(48, (options with { KeyHeightDp = 52 }).Normalize().KeyHeightDp);
        Assert.Equal(64, (options with { KeyHeightDp = 64 }).Normalize().KeyHeightDp);
    }

    [Test]
    public static void LearningSetting_CannotOverridePrivateEditorPolicy()
    {
        var options = new KeyboardOptions();
        Assert.True(options.CanLearn(EditorInputPolicy.From(1, 0)), "normal editor may learn");
        Assert.True(!options.CanLearn(EditorInputPolicy.From(0x81, 0)), "password never learns");
        Assert.True(!options.CanLearn(EditorInputPolicy.From(1, 0x01000000)), "editor's learning prohibition wins");
        Assert.True(!(options with { PersonalizedLearning = false }).CanLearn(EditorInputPolicy.From(1, 0)),
            "user's global learning prohibition wins");
    }

    [Test]
    public static void InitialMode_RespectsAddressPasswordAndRememberedMode()
    {
        var japanese = new KeyboardOptions { InitialMode = InitialInputMode.Japanese };
        Assert.True(!japanese.StartInEnglish(EditorInputPolicy.From(1, 0), true), "explicit Japanese ignores last English mode");
        foreach (var type in new[] { 0x81, 0x21, 0x11, 2 })
            Assert.True(japanese.StartInEnglish(EditorInputPolicy.From(type, 0), false), "specialized fields stay direct");
        var remembered = new KeyboardOptions();
        Assert.True(remembered.StartInEnglish(EditorInputPolicy.From(1, 0), true), "remember English");
        Assert.True(!remembered.StartInEnglish(EditorInputPolicy.From(1, 0), false), "remember Japanese");
        Assert.True((remembered with { InitialMode = InitialInputMode.English }).StartInEnglish(EditorInputPolicy.From(1, 0), false),
            "explicit English ignores last Japanese mode");
    }

    [Test]
    public static void PlatformSettings_ReachTheDefaultSessionFactory()
    {
        var session = MeltypeSession.CreateDefault(new CompositionTests.FakeConverter(), null, null,
            allowPersonalizedLearning: false, settingsOverride: new Settings { Enabled = false });
        Assert.True(!session.HandleKey('K', 'k', false, false, false, false).Consumed,
            "supplied settings must replace the platform's config.json");
    }

    [Test]
    public static void SessionFactory_AutomaticSpacingWorksWithLearningDisabled()
    {
        foreach (var spacing in new[] { false, true })
        {
            var session = MeltypeSession.CreateDefault(new CompositionTests.FakeConverter(), null, null,
                allowPersonalizedLearning: false, settingsOverride: new Settings { SpaceAroundEnglish = false },
                autoSpacing: spacing);
            foreach (var character in "seeyouagain")
                session.HandleKey(char.ToUpperInvariant(character), character, false, false, false, false);
            var committed = session.HandleKey(VirtualKeys.Return, null, false, false, false, false);
            Assert.Equal(spacing ? "see you again" : "seeyouagain", string.Concat(committed.Commits.Select(edit => edit.Text)),
                "the upstream spacing option must remain independent of Android's learning permission");
        }
    }

    private sealed class LearningConverter : IKanjiConverter, ILearningConverter, IDisposable
    {
        public ManualResetEventSlim Learned { get; } = new();
        public string? Convert(string hiragana) => "日本語";
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            [new(hiragana, "日本語")];
        public void Learn(string? context, IReadOnlyList<ConversionClause> clauses) => Learned.Set();
        public void Dispose() => Learned.Dispose();
    }

    [Test]
    public static void SessionFactory_AutomaticSpacingCannotEnableNativeLearning()
    {
        using var converter = new LearningConverter();
        var session = MeltypeSession.CreateDefault(converter, null, null, allowPersonalizedLearning: false,
            settingsOverride: new KeyboardOptions { PersonalizedLearning = false }.ToCoreSettings(), autoSpacing: true);
        foreach (var character in "nihongo")
            session.HandleKey(char.ToUpperInvariant(character), character, false, false, false, false);
        session.HandleKey(VirtualKeys.Space, null, false, false, false, false);
        var committed = session.HandleKey(VirtualKeys.Return, null, false, false, false, false);
        Assert.True(committed.Commits.Any(edit => edit.Text.Contains("日本語")), "Japanese conversion must still work without learning");
        Assert.True(!converter.Learned.Wait(TimeSpan.FromMilliseconds(200)), "spacing must not re-enable Mozc learning");
    }

    [Test]
    public static void LiveConversionOff_KeepsReadingUntilExplicitConversion()
    {
        var options = new KeyboardOptions { LiveConversion = false, Detection = DetectionLevel.Manual };
        var session = MeltypeSession.CreateDefault(new CompositionTests.FakeConverter(), null, null,
            allowPersonalizedLearning: false, settingsOverride: options.ToCoreSettings());
        SessionResult? last = null;
        foreach (var character in "kyouha")
            last = session.HandleKey(char.ToUpperInvariant(character), character, false, false, false, false);
        Assert.Equal("きょうは", last?.View?.Text, "typing must not apply live kanji conversion");
        var converted = session.HandleKey(VirtualKeys.Space, null, false, false, false, false);
        Assert.True(converted.View?.Converting == true, "Space still explicitly converts");
    }

    [Test]
    public static void PunctuationSetting_ChangesKeyboardLabelsAndCommittedText()
    {
        var styles = new[] { PunctuationStyle.Japanese, PunctuationStyle.FullWidthCommaPeriod,
            PunctuationStyle.FullWidthCommaKuten, PunctuationStyle.ToutenFullWidthPeriod };
        var labels = new[] { ("、", "。"), ("，", "．"), ("，", "。"), ("、", "．") };
        for (var index = 0; index < styles.Length; index++)
        {
            var options = new KeyboardOptions { Punctuation = styles[index], LiveConversion = false };
            Assert.Equal(labels[index], options.PunctuationCharacters);
            var session = MeltypeSession.CreateDefault(new CompositionTests.FakeConverter(), null, null,
                allowPersonalizedLearning: false, settingsOverride: options.ToCoreSettings());
            foreach (var character in "kyouha")
                session.HandleKey(char.ToUpperInvariant(character), character, false, false, false, false);
            var result = session.HandleKey(VirtualKeys.OemComma, ',', false, false, false, false);
            var committed = session.CommitPending();
            Assert.True(result.Commits.Concat(committed.Commits).Any(edit => edit.Text.EndsWith(labels[index].Item1)),
                "hardware punctuation must use the same style as the on-screen key");
        }
    }
}
