// SPDX-License-Identifier: GPL-3.0-or-later

namespace Meltype.Android;

internal enum ShiftMode
{
    Off,
    Once,
    Locked
}

internal sealed class ShiftState
{
    public const long DoubleTapWindowMs = 350;

    private long _lastTapMs = -1;

    public ShiftMode Mode { get; private set; } = ShiftMode.Off;
    public bool IsActive => Mode != ShiftMode.Off;
    public bool IsLocked => Mode == ShiftMode.Locked;

    public void Tap(long nowMs)
    {
        switch (Mode)
        {
            case ShiftMode.Off:
                Mode = ShiftMode.Once;
                _lastTapMs = nowMs;
                break;
            case ShiftMode.Once:
                Mode = _lastTapMs >= 0 && nowMs >= _lastTapMs && nowMs - _lastTapMs <= DoubleTapWindowMs
                    ? ShiftMode.Locked
                    : ShiftMode.Off;
                _lastTapMs = -1;
                break;
            case ShiftMode.Locked:
                Mode = ShiftMode.Off;
                _lastTapMs = -1;
                break;
        }
    }

    public bool ConsumeLetter()
    {
        if (Mode != ShiftMode.Once)
            return false;
        Mode = ShiftMode.Off;
        _lastTapMs = -1;
        return true;
    }

    public void Reset()
    {
        Mode = ShiftMode.Off;
        _lastTapMs = -1;
    }
}
