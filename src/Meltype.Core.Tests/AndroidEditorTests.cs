// SPDX-License-Identifier: GPL-3.0-or-later

using Meltype.Android;

namespace Meltype.Tests;

internal static class AndroidEditorTests
{
    [Test]
    public static void PasswordVariations_DisableLearningAndForceDirectInput()
    {
        foreach (var type in new[] { 0x81, 0x91, 0xe1, 0x12, 0x80081 })
        {
            var policy = EditorInputPolicy.From(type, 0);
            Assert.True(policy.Sensitive && policy.Direct && !policy.AllowLearning, $"password inputType={type:X}");
        }
        Assert.True(EditorInputPolicy.From(0x12, 0).Numeric, "numeric password keeps a numeric layout");
    }

    [Test]
    public static void PrivateFields_KeepJapaneseConversionWithoutLearning()
    {
        var policy = EditorInputPolicy.From(1, 0x01000006);
        Assert.True(!policy.Sensitive && !policy.Direct && !policy.AllowLearning,
            "NO_PERSONALIZED_LEARNING must not force Japanese users into Latin input");
        Assert.True(EditorInputPolicy.From(1, 6).AllowLearning, "normal field may learn");
    }

    [Test]
    public static void NumericAndAddressFields_SelectAppropriateInput()
    {
        foreach (var type in new[] { 2, 3, 4, 0x2002 })
            Assert.True(EditorInputPolicy.From(type, 0).Numeric, "numbers, phone and date fields");
        foreach (var type in new[] { 0x11, 0x21, 0xd1 })
            Assert.True(EditorInputPolicy.From(type, 0).Direct, "URL and email fields start in Latin input");
    }

    [Test]
    public static void Backspace_DeletesWholeUnicodeTextElements()
    {
        Assert.Equal(0, EditorText.BackspaceLength(""));
        Assert.Equal(1, EditorText.BackspaceLength("日本語"));
        Assert.Equal(2, EditorText.BackspaceLength("a😀"));
        Assert.Equal(2, EditorText.BackspaceLength("aか\u3099"));
        Assert.Equal(4, EditorText.BackspaceLength("a🇯🇵"));
        Assert.Equal(5, EditorText.BackspaceLength("a🧑‍💻"));
        Assert.Equal(2, EditorText.BackspaceLength("a❤️"));
    }

    [Test]
    public static void SelectionCallbacks_CanCoalesceOurCompositionEdits()
    {
        var tracker = new EditorSelectionTracker();
        tracker.Reset(10, 10);
        tracker.Replace(1, composing: true);
        tracker.Replace(4, composing: true);
        Assert.True(!tracker.Observe(14, 14, 10, 14), "composing text replaces the original span");
        tracker.Replace(3, composing: false);
        Assert.True(!tracker.Observe(13, 13, -1, -1), "our commit preserves Core correction context");
        Assert.True(tracker.Observe(5, 5, -1, -1), "an external cursor move clears Core context");
    }

    [Test]
    public static void SelectionCallbacks_CanArriveBeforeOurLatestEdit()
    {
        var tracker = new EditorSelectionTracker();
        tracker.Reset(0, 0);
        tracker.Replace(1, composing: true);
        tracker.Replace(2, composing: true);
        Assert.True(!tracker.Observe(1, 1, 0, 1), "old self callback");
        tracker.Replace(3, composing: true);
        Assert.True(!tracker.Observe(3, 3, 0, 3), "old callback must not overwrite the latest composing origin");
    }

    [Test]
    public static void SelectionCallbacks_HandleSelectionReplacementAndDeletion()
    {
        var tracker = new EditorSelectionTracker();
        tracker.Reset(7, 3);
        tracker.Replace(2, composing: false);
        Assert.True(!tracker.Observe(5, 5, -1, -1), "reversed selection is replaced at its lower bound");
        tracker.DeleteBefore(2);
        Assert.True(!tracker.Observe(3, 3, -1, -1), "deleting an emoji moves by two UTF-16 units");
    }

    [Test]
    public static void ExternalSelectionAndRemovedComposition_InvalidateTheSession()
    {
        var tracker = new EditorSelectionTracker();
        tracker.Reset(0, 0);
        tracker.Replace(3, composing: true);
        Assert.True(!tracker.Observe(3, 3, 0, 3), "self edit");
        Assert.True(tracker.Observe(1, 1, 0, 3), "cursor moved inside composing text");
        Assert.True(tracker.Observe(1, 1, -1, -1), "editor removed the composing span");
    }
}
