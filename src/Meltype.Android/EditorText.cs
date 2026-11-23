// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;

namespace Meltype.Android;

internal static class EditorText
{
    public static int BackspaceLength(string textBeforeCursor)
    {
        var starts = StringInfo.ParseCombiningCharacters(textBeforeCursor);
        return starts.Length == 0 ? 0 : textBeforeCursor.Length - starts[^1];
    }

    public static (int Start, int End)? SelectedClauseRange(
        string text,
        IReadOnlyList<string>? clauses,
        int selectedClause)
    {
        if (string.IsNullOrEmpty(text) || clauses is null ||
            selectedClause < 0 || selectedClause >= clauses.Count)
            return null;

        var start = 0;
        for (var i = 0; i < selectedClause; i++)
            start += clauses[i].Length;

        var clause = clauses[selectedClause];
        if (clause.Length == 0 || start + clause.Length > text.Length)
            return null;

        if (!text.AsSpan(start, clause.Length).SequenceEqual(clause.AsSpan()))
            return null;

        return (start, start + clause.Length);
    }
}
