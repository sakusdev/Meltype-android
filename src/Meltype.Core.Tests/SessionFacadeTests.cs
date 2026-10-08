// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>Mac 版・Linux 版から使う入力の本体 (MeltypeSession) のテスト。OS がキーを 1 つずつ渡し、使ったかをその場で返す。</summary>
internal static class SessionFacadeTests
{
    private static MeltypeSession Create() => new(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions(), () => new Settings());

    /// <summary>文字を 1 つずつ打つ (英字は大文字なら Shift 付き)。</summary>
    private static List<SessionResult> Type(MeltypeSession session, string text, string? before = null)
    {
        var results = new List<SessionResult>();
        foreach (var c in text)
        {
            var vk = c switch
            {
                ' ' => VirtualKeys.Space,
                '\n' => VirtualKeys.Return,
                '\b' => VirtualKeys.Back,
                ',' => VirtualKeys.OemComma,
                '.' => VirtualKeys.OemPeriod,
                '-' => VirtualKeys.OemMinus,
                _ when char.IsAsciiLetter(c) => char.ToUpperInvariant(c),
                _ => c,
            };
            char? ch = c is ' ' or '\n' or '\b' ? null : c;
            results.Add(session.HandleKey(vk, ch, char.IsAsciiLetterUpper(c), false, false, false, before));
        }
        return results;
    }

    [Test]
    public static void Romaji_ComposesAndEnterCommits()
    {
        var session = Create();
        var results = Type(session, "kyouha");
        Assert.True(results.All(r => r.Consumed), "打った英字はアプリに渡さない");
        Assert.Equal("きょうは", results[^1].View?.Text);
        var enter = Type(session, "\n")[0];
        Assert.True(enter.Consumed, "Enter は確定に使う");
        Assert.Equal("きょうは", enter.Commits.Single().Text);
        Assert.True(enter.View is null, "確定したら変換ボックスを閉じる");
    }

    [Test]
    public static void EnglishWord_SpaceCommitsWithSpace()
    {
        var session = Create();
        var results = Type(session, "google ");
        Assert.Equal("google ", results[^1].Commits.Single().Text);
    }

    [Test]
    public static void KeysOutsideComposition_GoToTheApp()
    {
        var session = Create();
        Assert.True(!session.HandleKey(VirtualKeys.Left, null, false, false, false, false).Consumed, "変換ボックスが空なら矢印はアプリへ");
        Assert.True(!session.HandleKey('C', 'c', false, false, false, true).Consumed, "Command + C はアプリの操作");
        Assert.True(!Type(session, " ")[0].Consumed, "空白はアプリへ");
        session.Direct = true;
        Assert.True(!Type(session, "a")[0].Consumed, "英数 (直接入力) ならすべてアプリへ");
    }

    [Test]
    public static void ShortcutWhileComposing_CommitsThenPassesTheKey()
    {
        var session = Create();
        Type(session, "abc");
        var result = session.HandleKey('S', 's', false, false, false, true);
        Assert.True(!result.Consumed, "Command + S はアプリへ");
        Assert.True(result.Commits.Count == 1, "その前に変換ボックスの内容を確定する");
    }

    [Test]
    public static void ArrowWhileComposing_SelectsClauses()
    {
        var session = Create();
        Type(session, "kyouha");
        var result = session.HandleKey(VirtualKeys.Right, null, false, false, false, false);
        Assert.True(result.Consumed && result.View is { Converting: true }, "変換前の矢印は文節の選択に使う");
    }

    [Test]
    public static void Candidates_CanBeSelectedByIndex()
    {
        var session = Create();
        Type(session, "api ");
        var view = session.SelectCandidate(2).View!;
        Assert.Equal(2, view.SelectedIndex);
        var commit = Type(session, "\n")[0];
        Assert.Equal(view.Candidates[2], commit.Commits.Single().Text);
    }

    [Test]
    public static void DirectInsertionBoundary_CommitsPendingTextBeforeTheNextComposition()
    {
        var session = Create();
        Type(session, "nihongo");
        var pending = session.CommitPending();
        Assert.Equal("にほんご", pending.Commits.Single().Text);
        Assert.True(!session.IsComposing, "punctuation must not leave a stale reading in Core");
        session.ResetInputContext();
        Assert.Equal("あ", Type(session, "a")[0].View?.Text, "next character starts a fresh composition after punctuation");
    }

    [Test]
    public static void ExternalCursorMove_DropsCompositionWithoutWritingToTheNewPosition()
    {
        var session = Create();
        Type(session, "nihongo");
        session.ResetInputContext();
        Assert.True(!session.IsComposing, "moving in the editor ends the old Core buffer");
        var result = Type(session, "a")[0];
        Assert.Equal(0, result.Commits.Count, "old text must not be committed at the new cursor");
        Assert.Equal("あ", result.View?.Text);
    }

    [Test]
    public static void ExternalCursorMove_DropsCorrectionOfPreviouslyCommittedWords()
    {
        var session = Create();
        Type(session, "i \n");
        session.ResetInputContext();
        var result = Type(session, "want ")[^1];
        Assert.True(result.Commits.All(c => c.DeleteBefore == 0), "must not delete unrelated text at the new cursor");
    }

    [Test]
    public static void ConversionArrow_CanSelectTheSecondClause()
    {
        var session = Create();
        var first = Type(session, "tanniwotoru ")[^1].View!;
        Assert.True(first.Clauses is { Count: > 1 }, "test needs a multi-clause conversion");
        var next = session.HandleKey(VirtualKeys.Right, null, false, false, false, false);
        Assert.True(next.Consumed, "clause movement must not reach the editor");
        Assert.Equal(1, next.View?.SelectedClause);
        Assert.True(next.View!.Candidates.Contains("取る"), "show candidates for the second clause");
    }

    [Test]
    public static void PrivateComposition_DoesNotUpdateLanguageOrCandidateHistory()
    {
        var languages = new LanguageMemory(null);
        var history = new ConversionHistory(null);
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { PersonalizedLearning = false, Languages = languages, History = history },
            () => new Settings());
        var candidates = Type(session, "api ")[^1].View!.Candidates;
        var rawIndex = candidates.ToList().IndexOf("api");
        Assert.True(rawIndex >= 0, "raw candidate is available in private fields");
        session.SelectCandidate(rawIndex);
        Type(session, "\n");
        Assert.Equal(0, languages.Count, "chosen Latin input must not become a learned word");

        candidates = Type(session, "nihongo ")[^1].View!.Candidates;
        session.SelectCandidate(candidates.ToList().IndexOf("日本語"));
        Type(session, "\n");
        Assert.Equal(0, history.Count, "candidate choices must not enter personalized history");
    }

    private sealed class RecordingLearningConverter : IKanjiConverter, ILearningConverter, IDisposable
    {
        public ManualResetEventSlim Learned { get; } = new();
        public string? Convert(string hiragana) => "日本語";
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            [new(hiragana, "日本語")];
        public void Learn(string? context, IReadOnlyList<ConversionClause> clauses) => Learned.Set();
        public void Dispose() => Learned.Dispose();
    }

    [Test]
    public static void PrivateComposition_DoesNotSendLearningToTheNativeBackend()
    {
        using var normal = new RecordingLearningConverter();
        using var privateConverter = new RecordingLearningConverter();
        foreach (var (converter, learning) in new[] { (normal, true), (privateConverter, false) })
        {
            var session = new MeltypeSession(CompositionTests.Detector, converter,
                new CompositionOptions { PersonalizedLearning = learning }, () => new Settings());
            Type(session, "nihongo \n");
        }
        Assert.True(normal.Learned.Wait(TimeSpan.FromSeconds(5)), "normal input still learns asynchronously");
        Assert.True(!privateConverter.Learned.Wait(TimeSpan.FromMilliseconds(200)),
            "private input must not queue learning against the shared native profile");
    }

    [Test]
    public static void Json_IsEscaped()
    {
        var result = new SessionResult(true, [new TextEdit(2, "a\"b\\c\n")], new CompositionView("x", ["y"], 0, true, "h", ["x"], 0));
        const string expected = """{"consumed":true,"commits":[{"deleteBefore":2,"text":"a\"b\\c\n"}],"view":{"text":"x","converting":true,"selectedIndex":0,"selectedClause":0,"hint":"h","candidates":["y"],"clauses":["x"],"suggestion":null,"meaning":null}}""";
        Assert.Equal(expected, result.ToJson());
    }
}
