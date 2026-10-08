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
}
