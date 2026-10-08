// SPDX-License-Identifier: GPL-3.0-or-later

using Meltype.Composition;

namespace Meltype.Android;

/// <summary>
/// Temporary Android conversion backend used until the native Mozc bridge is wired.
/// It deliberately keeps the reading as-is, while allowing all of Meltype's
/// language detection, romaji handling, history and composition flow to run.
/// </summary>
internal sealed class AndroidFallbackConverter : IKanjiConverter
{
    public string? Convert(string hiragana) =>
        string.IsNullOrEmpty(hiragana) ? null : hiragana;

    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
    {
        if (string.IsNullOrEmpty(hiragana))
            return null;

        return [new ConversionClause(hiragana, hiragana)];
    }
}
