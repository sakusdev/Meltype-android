// SPDX-License-Identifier: GPL-3.0-or-later

namespace Meltype.Android;

// Keep the policy independent of Android bindings so its bit masks can be tested
// with the same values that EditorInfo supplies on a device.
internal readonly record struct EditorInputPolicy(bool Sensitive, bool Numeric, bool Direct, bool AllowLearning)
{
    public static EditorInputPolicy From(int inputType, int imeOptions)
    {
        var inputClass = inputType & 0x0f;
        var variation = inputType & 0x0ff0;
        var sensitive = (inputClass == 1 && variation is 0x80 or 0x90 or 0xe0) ||
                        (inputClass == 2 && variation == 0x10);
        var numeric = inputClass is 2 or 3 or 4;
        var direct = sensitive || numeric ||
                     (inputClass == 1 && variation is 0x10 or 0x20 or 0xd0);
        // EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING (API 26).
        var allowLearning = !sensitive && (imeOptions & 0x01000000) == 0;
        return new(sensitive, numeric, direct, allowLearning);
    }
}
