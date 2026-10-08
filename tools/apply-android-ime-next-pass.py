#!/usr/bin/env python3
from pathlib import Path

path = Path("src/Meltype.Android/MeltypeInputMethodService.cs")
text = path.read_text(encoding="utf-8")


def replace_once(old: str, new: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"expected exactly one match, got {count}: {old[:100]!r}")
    text = text.replace(old, new, 1)


replace_once(
'''    private MaterialButton? _spaceButton;
    private MaterialButton? _enterButton;
    private bool _direct;
    private bool _shift;
    private bool _hasComposingText;
''',
'''    private MaterialButton? _spaceButton;
    private MaterialButton? _enterButton;
    private MaterialButton? _backspaceButton;
    private ActionRunnable? _backspaceRepeatRunnable;
    private bool _backspaceRepeating;
    private bool _backspaceRepeated;
    private float _spaceLastX;
    private bool _spaceWasSwiped;
    private bool _direct;
    private bool _shift;
    private bool _hasComposingText;
''')

replace_once(
'''        _spaceButton = null;
        _enterButton = null;
        _uiContext = null;
''',
'''        StopBackspaceRepeat();
        _spaceButton = null;
        _enterButton = null;
        _backspaceButton = null;
        _backspaceRepeatRunnable?.Dispose();
        _backspaceRepeatRunnable = null;
        _uiContext = null;
''')

replace_once(
'''    public override void OnFinishInput()
    {
''',
'''    public override void OnStartInputView(EditorInfo? info, bool restarting)
    {
        base.OnStartInputView(info, restarting);
        Log.Info(LogTag,
            $"input-view start restart={restarting} package={info?.PackageName ?? "?"} inputType={(int)(info?.InputType ?? 0)} imeOptions={(int)(info?.ImeOptions ?? 0)}");
    }

    public override void OnFinishInputView(bool finishingInput)
    {
        StopBackspaceRepeat();
        Log.Info(LogTag, $"input-view finish finishingInput={finishingInput}");
        base.OnFinishInputView(finishingInput);
    }

    public override void OnFinishInput()
    {
''')

replace_once(
'''        _shiftButton = CreateKey("⇧", ToggleShift, KeyKind.Special);
        third.AddView(_shiftButton, WeightedKeyParams(1.34f));
        foreach (var c in "zxcvbnm")
            third.AddView(CreateLetterKey(c), WeightedKeyParams());
        third.AddView(CreateKey("⌫", HandleBackspace, KeyKind.Special), WeightedKeyParams(1.34f));
        root.AddView(third);
''',
'''        _shiftButton = CreateKey("⇧", ToggleShift, KeyKind.Special);
        third.AddView(_shiftButton, WeightedKeyParams(1.34f));
        foreach (var c in "zxcvbnm")
            third.AddView(CreateLetterKey(c), WeightedKeyParams());
        _backspaceButton = CreateKey(
            "⌫",
            () =>
            {
                if (_backspaceRepeated)
                {
                    _backspaceRepeated = false;
                    return;
                }

                HandleBackspace();
            },
            KeyKind.Special);
        AttachBackspaceRepeat(_backspaceButton);
        third.AddView(_backspaceButton, WeightedKeyParams(1.34f));
        root.AddView(third);
''')

replace_once(
'''        _spaceButton = CreateKey("日本語", HandleSpace, KeyKind.Normal);
        bottom.AddView(_spaceButton, WeightedKeyParams(2.15f));
''',
'''        _spaceButton = CreateKey("日本語", HandleSpace, KeyKind.Normal);
        AttachSpaceSwipe(_spaceButton);
        bottom.AddView(_spaceButton, WeightedKeyParams(2.15f));
''')

replace_once(
'''        var button = CreateKey(character.ToString(), () => HandleLetter(character));
        _letterButtons.Add((button, character));

        if (hint is null)
            return button;
''',
'''        var button = CreateKey(character.ToString(), () => HandleLetter(character));
        _letterButtons.Add((button, character));
        AttachLetterPressVisual(button);

        if (hint is not null)
        {
            button.LongClickable = true;
            button.LongClick += (_, e) =>
            {
                e.Handled = true;
                SafeRun("LongPressHint", () => HandleCharacter(hint[0]));
                SafeHaptic(button);
            };
        }

        if (hint is null)
            return button;
''')

replace_once(
'''    private LinearLayout.LayoutParams WeightedKeyParams(float weight = 1f)
    {
        var p = new LinearLayout.LayoutParams(0, Dp(50), weight);
        p.SetMargins(Dp(2), Dp(3), Dp(2), Dp(3));
        return p;
    }
''',
'''    private LinearLayout.LayoutParams WeightedKeyParams(float weight = 1f)
    {
        var p = new LinearLayout.LayoutParams(0, Dp(48), weight);
        p.SetMargins(Dp(2), Dp(2), Dp(2), Dp(2));
        return p;
    }
''')

replace_once(
'''    private void HandleSpace() =>
        HandleVirtualKey(VkSpace, ' ', () => CurrentInputConnection?.CommitText(" ", 1));

    private void HandleBackspace() =>
''',
'''    private void HandleSpace()
    {
        if (_spaceWasSwiped)
        {
            _spaceWasSwiped = false;
            return;
        }

        HandleVirtualKey(VkSpace, ' ', () => CurrentInputConnection?.CommitText(" ", 1));
    }

    private void HandleBackspace() =>
''')

replace_once(
'''    private void MoveCursor(global::Android.Views.Keycode keycode)
    {
        var connection = CurrentInputConnection;
        if (connection is null)
            return;

        connection.SendKeyEvent(new KeyEvent(KeyEventActions.Down, keycode));
        connection.SendKeyEvent(new KeyEvent(KeyEventActions.Up, keycode));
    }

    private void HandleVirtualKey(int vk, char? ch, Action fallback)
''',
'''    private void MoveCursor(global::Android.Views.Keycode keycode)
    {
        var connection = CurrentInputConnection;
        if (connection is null)
            return;

        connection.SendKeyEvent(new KeyEvent(KeyEventActions.Down, keycode));
        connection.SendKeyEvent(new KeyEvent(KeyEventActions.Up, keycode));
    }

    private void AttachSpaceSwipe(MaterialButton button)
    {
        button.Touch += (_, e) =>
        {
            var motion = e.Event;
            if (motion is null)
                return;

            try
            {
                switch (motion.Action)
                {
                    case MotionEventActions.Down:
                        _spaceLastX = motion.RawX;
                        _spaceWasSwiped = false;
                        break;

                    case MotionEventActions.Move:
                    {
                        var threshold = Math.Max(1, Dp(14));
                        var delta = motion.RawX - _spaceLastX;
                        var steps = (int)(delta / threshold);
                        if (steps == 0)
                            break;

                        _spaceWasSwiped = true;
                        var keycode = steps < 0
                            ? global::Android.Views.Keycode.DpadLeft
                            : global::Android.Views.Keycode.DpadRight;

                        for (var i = 0; i < Math.Min(8, Math.Abs(steps)); i++)
                            MoveCursor(keycode);

                        _spaceLastX += steps * threshold;
                        SafeHaptic(button);
                        break;
                    }

                    case MotionEventActions.Cancel:
                        _spaceWasSwiped = false;
                        break;
                }
            }
            catch (Exception ex)
            {
                Warn("SpaceSwipe", ex);
            }
        };
    }

    private void AttachBackspaceRepeat(MaterialButton button)
    {
        _backspaceRepeatRunnable ??= new ActionRunnable(RepeatBackspace);

        button.Touch += (_, e) =>
        {
            var action = e.Event?.Action;
            if (action == MotionEventActions.Down)
            {
                _backspaceRepeated = false;
                _backspaceRepeating = true;
                button.RemoveCallbacks(_backspaceRepeatRunnable);
                button.PostDelayed(_backspaceRepeatRunnable, 380);
            }
            else if (action is MotionEventActions.Up or MotionEventActions.Cancel)
            {
                StopBackspaceRepeat();
            }
        };
    }

    private void RepeatBackspace()
    {
        if (!_backspaceRepeating || _backspaceButton is null || _backspaceRepeatRunnable is null)
            return;

        _backspaceRepeated = true;
        SafeRun("BackspaceRepeat", HandleBackspace);
        SafeHaptic(_backspaceButton);
        _backspaceButton.PostDelayed(_backspaceRepeatRunnable, 58);
    }

    private void StopBackspaceRepeat()
    {
        _backspaceRepeating = false;
        if (_backspaceButton is not null && _backspaceRepeatRunnable is not null)
            _backspaceButton.RemoveCallbacks(_backspaceRepeatRunnable);
    }

    private void AttachLetterPressVisual(MaterialButton button)
    {
        button.Touch += (_, e) =>
        {
            try
            {
                switch (e.Event?.Action)
                {
                    case MotionEventActions.Down:
                        button.ScaleX = 1.045f;
                        button.ScaleY = 1.045f;
                        button.TranslationY = -Dp(1);
                        break;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        button.ScaleX = 1f;
                        button.ScaleY = 1f;
                        button.TranslationY = 0f;
                        break;
                }
            }
            catch (Exception ex)
            {
                Warn("LetterPressVisual", ex);
            }
        };
    }

    private void SafeHaptic(View view)
    {
        try
        {
            view.PerformHapticFeedback(FeedbackConstants.VirtualKey);
        }
        catch (Exception ex)
        {
            Warn("Haptic", ex);
        }
    }

    private void HandleVirtualKey(int vk, char? ch, Action fallback)
''')

replace_once(
'''    private static void Warn(string operation, Exception ex)
    {
        Log.Warn(LogTag, $"{operation}: {ex}");
    }

    private int Dp(int value) =>
''',
'''    private static void Warn(string operation, Exception ex)
    {
        Log.Warn(LogTag, $"{operation}: {ex}");
    }

    private sealed class ActionRunnable : Java.Lang.Object, Java.Lang.IRunnable
    {
        private readonly Action _action;

        public ActionRunnable(Action action)
        {
            _action = action;
        }

        public void Run() => _action();
    }

    private int Dp(int value) =>
''')

path.write_text(text, encoding="utf-8")
print(f"patched {path}")
