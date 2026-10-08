// SPDX-License-Identifier: GPL-3.0-or-later

namespace Meltype.Android;

/// <summary>Distinguish asynchronous callbacks for our own edits from a user moving the cursor.</summary>
internal sealed class EditorSelectionTracker
{
    private readonly List<(int Start, int End)> _pending = [];
    private int _start = -1;
    private int _end = -1;
    private int _composingStart = -1;

    public void Reset(int start, int end)
    {
        _pending.Clear();
        _start = start;
        _end = end;
        _composingStart = -1;
    }

    public void Replace(int utf16Length, bool composing)
    {
        var start = _composingStart >= 0 ? _composingStart : Math.Min(_start, _end);
        _composingStart = composing ? start : -1;
        if (start < 0) return;
        _start = _end = start + utf16Length;
        ExpectSelection();
    }

    public void DeleteBefore(int utf16Length)
    {
        if (_start < 0 || _end < 0) return;
        var before = _composingStart >= 0 ? Math.Min(_start, _composingStart) : Math.Min(_start, _end);
        var count = Math.Min(before, utf16Length);
        _start -= count;
        _end -= count;
        if (_composingStart >= 0) _composingStart -= count;
        ExpectSelection();
    }

    public void FinishComposition() => _composingStart = -1;

    public bool Observe(int start, int end, int composingStart, int composingEnd)
    {
        // Editors may coalesce a whole batch into its last selection update.
        var match = _pending.FindLastIndex(p => p.Start == start && p.End == end);
        if (match >= 0)
        {
            _pending.RemoveRange(0, match + 1);
            if (_pending.Count == 0)
                SetObserved(start, end, composingStart);
            return false;
        }

        var moved = _start >= 0 && _end >= 0 && (start != _start || end != _end);
        var composingMoved = _composingStart >= 0 &&
                             (composingStart < 0 || start != composingEnd || end != composingEnd);
        _pending.Clear();
        SetObserved(start, end, composingStart);
        return moved || composingMoved;
    }

    private void SetObserved(int start, int end, int composingStart)
    {
        _start = start;
        _end = end;
        _composingStart = composingStart;
    }

    private void ExpectSelection()
    {
        // A disconnected/slow editor must not let the callback queue grow forever.
        if (_pending.Count == 64) _pending.RemoveAt(0);
        _pending.Add((_start, _end));
    }
}
