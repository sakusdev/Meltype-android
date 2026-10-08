#!/usr/bin/env python3
from pathlib import Path

src = Path('src/Meltype.Android/MeltypeInputMethodService.cs')
text = src.read_text(encoding='utf-8')

def r(old: str, new: str):
    global text
    c = text.count(old)
    if c != 1:
        raise SystemExit(f'expected one match, got {c}: {old[:100]!r}')
    text = text.replace(old, new, 1)

# Preview lifecycle fields.
r('''    private MaterialButton? _backspaceButton;\n    private ActionRunnable? _backspaceRepeatRunnable;''',
  '''    private MaterialButton? _backspaceButton;\n    private PopupWindow? _keyPreviewPopup;\n    private TextView? _keyPreviewText;\n    private ActionRunnable? _backspaceRepeatRunnable;''')

r('''        _backspaceButton = null;\n        _backspaceRepeatRunnable?.Dispose();''',
  '''        _backspaceButton = null;\n        DismissKeyPreview();\n        _keyPreviewText = null;\n        _backspaceRepeatRunnable?.Dispose();''')

r('''    public override void OnFinishInputView(bool finishingInput)\n    {\n        StopBackspaceRepeat();''',
  '''    public override void OnFinishInputView(bool finishingInput)\n    {\n        StopBackspaceRepeat();\n        DismissKeyPreview();''')

# Dedicated key icons for non-character controls.
r('''        _shiftButton = CreateKey("⇧", ToggleShift, KeyKind.Special);''',
  '''        _shiftButton = CreateIconKey(Resource.Drawable.ic_key_shift, ToggleShift, KeyKind.Special, "Shift");''')

r('''        _backspaceButton = CreateKey(\n            "⌫",\n            () =>''',
  '''        _backspaceButton = CreateIconKey(\n            Resource.Drawable.ic_key_backspace,\n            () =>''')

r('''            },\n            KeyKind.Special);\n        AttachBackspaceRepeat(_backspaceButton);''',
  '''            },\n            KeyKind.Special,\n            "削除");\n        AttachBackspaceRepeat(_backspaceButton);''')

r('''        bottom.AddView(\n            CreateKey("☺︎", ShowEmojiBar, KeyKind.Special),\n            WeightedKeyParams(1.0f));''',
  '''        bottom.AddView(\n            CreateIconKey(Resource.Drawable.ic_toolbar_emoji, ShowEmojiBar, KeyKind.Special, "絵文字"),\n            WeightedKeyParams(1.0f));''')

r('''        bottom.AddView(\n            CreateKey("◀", () => MoveCursor(global::Android.Views.Keycode.DpadLeft), KeyKind.Special),\n            WeightedKeyParams(.96f));\n\n        bottom.AddView(\n            CreateKey("▶", () => MoveCursor(global::Android.Views.Keycode.DpadRight), KeyKind.Special),\n            WeightedKeyParams(.96f));\n\n        _enterButton = CreateKey("↵", HandleEnter, KeyKind.Accent);''',
  '''        bottom.AddView(\n            CreateIconKey(Resource.Drawable.ic_key_cursor_left, () => MoveCursor(global::Android.Views.Keycode.DpadLeft), KeyKind.Special, "カーソルを左へ"),\n            WeightedKeyParams(.96f));\n\n        bottom.AddView(\n            CreateIconKey(Resource.Drawable.ic_key_cursor_right, () => MoveCursor(global::Android.Views.Keycode.DpadRight), KeyKind.Special, "カーソルを右へ"),\n            WeightedKeyParams(.96f));\n\n        _enterButton = CreateIconKey(Resource.Drawable.ic_key_return, HandleEnter, KeyKind.Accent, "改行");''')

# Character key previews.
r('''        var button = CreateKey(character.ToString(), () => HandleLetter(character));\n        _letterButtons.Add((button, character));''',
  '''        var button = CreateKey(character.ToString(), () => HandleLetter(character));\n        _letterButtons.Add((button, character));\n        AttachKeyPreview(button);''')

# Icon-key helper after CreateKey.
needle = '''        button.Click += (_, _) => SafeRun("KeyAction", action);\n        return button;\n    }\n\n    private void ApplyKeyAppearance'''
replacement = '''        button.Click += (_, _) => SafeRun("KeyAction", action);\n        return button;\n    }\n\n    private MaterialButton CreateIconKey(\n        int iconResource,\n        Action action,\n        KeyKind kind,\n        string description)\n    {\n        var button = CreateKey(string.Empty, action, kind);\n        button.ContentDescription = description;\n        SetKeyIcon(\n            button,\n            iconResource,\n            kind is KeyKind.Accent && !IsDarkTheme ? OnPrimary : KeyForeground);\n        return button;\n    }\n\n    private void SetKeyIcon(MaterialButton button, int iconResource, Color tint)\n    {\n        try\n        {\n            var icon = UiContext.GetDrawable(iconResource)?.Mutate();\n            if (icon is null)\n                return;\n\n            icon.SetTint(tint);\n            var size = Dp(24);\n            icon.SetBounds(0, 0, size, size);\n            button.SetCompoundDrawables(icon, null, null, null);\n            button.CompoundDrawablePadding = 0;\n            button.SetPadding(0, 0, 0, 0);\n        }\n        catch (Exception ex)\n        {\n            Warn("SetKeyIcon", ex);\n        }\n    }\n\n    private void ApplyKeyAppearance'''
r(needle, replacement)

# Shift needs icon tint refresh instead of relying on text color alone.
r('''        _shiftButton.BackgroundTintList = ColorStateList.ValueOf(\n            _shift ? PrimaryContainer : SpecialKeyBackground);\n        _shiftButton.SetTextColor(_shift ? OnPrimaryContainer : KeyForeground);''',
  '''        _shiftButton.BackgroundTintList = ColorStateList.ValueOf(\n            _shift ? PrimaryContainer : SpecialKeyBackground);\n        _shiftButton.SetTextColor(_shift ? OnPrimaryContainer : KeyForeground);\n        SetKeyIcon(\n            _shiftButton,\n            Resource.Drawable.ic_key_shift,\n            _shift ? OnPrimaryContainer : KeyForeground);''')

# Editor action now changes the vector icon too.
r('''        var action = editor is null ? 0 : ((int)editor.ImeOptions & ImeActionMask);\n        var (label, description) = action switch\n        {\n            2 => ("→", "移動"),\n            3 => ("⌕", "検索"),\n            4 => ("➤", "送信"),\n            5 => ("⇥", "次へ"),\n            6 => ("✓", "完了"),\n            7 => ("⇤", "前へ"),\n            _ => ("↵", "改行")\n        };\n\n        _enterButton.Text = label;\n        _enterButton.ContentDescription = description;''',
  '''        var action = editor is null ? 0 : ((int)editor.ImeOptions & ImeActionMask);\n        var (icon, description) = action switch\n        {\n            2 => (Resource.Drawable.ic_key_arrow_forward, "移動"),\n            3 => (Resource.Drawable.ic_key_search, "検索"),\n            4 => (Resource.Drawable.ic_key_send, "送信"),\n            5 => (Resource.Drawable.ic_key_arrow_forward, "次へ"),\n            6 => (Resource.Drawable.ic_key_done, "完了"),\n            7 => (Resource.Drawable.ic_key_cursor_left, "前へ"),\n            _ => (Resource.Drawable.ic_key_return, "改行")\n        };\n\n        _enterButton.Text = string.Empty;\n        _enterButton.ContentDescription = description;\n        SetKeyIcon(\n            _enterButton,\n            icon,\n            IsDarkTheme ? KeyForeground : OnPrimary);''')

# Add popup preview implementation before press visual helper.
marker = '''    private void AttachLetterPressVisual(MaterialButton button)\n    {'''
preview = '''    private void AttachKeyPreview(MaterialButton button)\n    {\n        button.Touch += (_, e) =>\n        {\n            try\n            {\n                switch (e.Event?.Action)\n                {\n                    case MotionEventActions.Down:\n                        ShowKeyPreview(button);\n                        break;\n                    case MotionEventActions.Up:\n                    case MotionEventActions.Cancel:\n                        DismissKeyPreview();\n                        break;\n                }\n            }\n            catch (Exception ex)\n            {\n                Warn("KeyPreviewTouch", ex);\n                DismissKeyPreview();\n            }\n\n            e.Handled = false;\n        };\n    }\n\n    private void ShowKeyPreview(MaterialButton button)\n    {\n        DismissKeyPreview();\n\n        var label = new TextView(UiContext)\n        {\n            Text = button.Text,\n            TextSize = 27,\n            Gravity = GravityFlags.Center,\n            Focusable = false,\n            FocusableInTouchMode = false\n        };\n        label.Typeface = global::Android.Graphics.Typeface.Create(\n            "sans-serif-medium",\n            global::Android.Graphics.TypefaceStyle.Normal);\n        label.SetTextColor(KeyForeground);\n\n        var background = new global::Android.Graphics.Drawables.GradientDrawable();\n        background.SetColor(SpecialKeyBackground);\n        background.SetCornerRadius(Dp(9));\n        label.Background = background;\n\n        var width = Math.Max(button.Width, Dp(46));\n        var height = Dp(58);\n        var popup = new PopupWindow(label, width, height, false)\n        {\n            Focusable = false,\n            OutsideTouchable = false\n        };\n\n        _keyPreviewText = label;\n        _keyPreviewPopup = popup;\n        popup.ShowAsDropDown(button, 0, -(button.Height + height + Dp(5)));\n    }\n\n    private void DismissKeyPreview()\n    {\n        try\n        {\n            _keyPreviewPopup?.Dismiss();\n        }\n        catch (Exception ex)\n        {\n            Warn("DismissKeyPreview", ex);\n        }\n        finally\n        {\n            _keyPreviewPopup = null;\n            _keyPreviewText = null;\n        }\n    }\n\n    private void AttachLetterPressVisual(MaterialButton button)\n    {'''
r(marker, preview)

src.write_text(text, encoding='utf-8')

# Vector drawables for Gboard-like control keys.
drawables = {
'ic_key_shift.xml': '''<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="24dp" android:height="24dp" android:viewportWidth="24" android:viewportHeight="24">\n  <path android:fillColor="#FFFFFFFF" android:pathData="M4,12l1.41,1.41L11,7.83V20h2V7.83l5.59,5.58L20,12l-8,-8z"/>\n</vector>\n''',
'ic_key_backspace.xml': '''<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="24dp" android:height="24dp" android:viewportWidth="24" android:viewportHeight="24">\n  <path android:fillColor="#FFFFFFFF" android:pathData="M22,3H7c-0.69,0-1.23,0.35-1.59,0.88L0,12l5.41,8.12c0.36,0.53 0.9,0.88 1.59,0.88h15c1.1,0 2,-0.9 2,-2V5c0,-1.1-0.9,-2-2,-2zM19,15.59L17.59,17 14,13.41 10.41,17 9,15.59 12.59,12 9,8.41 10.41,7 14,10.59 17.59,7 19,8.41 15.41,12z"/>\n</vector>\n''',
'ic_key_cursor_left.xml': '''<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="24dp" android:height="24dp" android:viewportWidth="24" android:viewportHeight="24">\n  <path android:fillColor="#FFFFFFFF" android:pathData="M15.5,5l-7,7 7,7z"/>\n</vector>\n''',
'ic_key_cursor_right.xml': '''<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="24dp" android:height="24dp" android:viewportWidth="24" android:viewportHeight="24">\n  <path android:fillColor="#FFFFFFFF" android:pathData="M8.5,5l7,7 -7,7z"/>\n</vector>\n''',
'ic_key_return.xml': '''<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="24dp" android:height="24dp" android:viewportWidth="24" android:viewportHeight="24">\n  <path android:fillColor="#FFFFFFFF" android:pathData="M19,7v4H5.83l3.58,-3.59L8,6l-6,6 6,6 1.41,-1.41L5.83,13H21V7z"/>\n</vector>\n''',
'ic_key_search.xml': '''<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="24dp" android:height="24dp" android:viewportWidth="24" android:viewportHeight="24">\n  <path android:fillColor="#FFFFFFFF" android:pathData="M9.5,3a6.5,6.5 0,1 0,4.04,11.59L19.95,21 21,19.95l-6.41,-6.41A6.5,6.5 0,0 0,9.5,3zm0,2A4.5,4.5 0,1 1,5,9.5 4.5,4.5 0,0 1,9.5,5z"/>\n</vector>\n''',
'ic_key_send.xml': '''<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="24dp" android:height="24dp" android:viewportWidth="24" android:viewportHeight="24">\n  <path android:fillColor="#FFFFFFFF" android:pathData="M2.01,21L23,12 2.01,3 2,10l15,2 -15,2z"/>\n</vector>\n''',
'ic_key_done.xml': '''<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="24dp" android:height="24dp" android:viewportWidth="24" android:viewportHeight="24">\n  <path android:fillColor="#FFFFFFFF" android:pathData="M9,16.17L4.83,12l-1.42,1.41L9,19 21,7l-1.41,-1.41z"/>\n</vector>\n''',
'ic_key_arrow_forward.xml': '''<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="24dp" android:height="24dp" android:viewportWidth="24" android:viewportHeight="24">\n  <path android:fillColor="#FFFFFFFF" android:pathData="M12,4l-1.41,1.41L16.17,11H4v2h12.17l-5.58,5.59L12,20l8,-8z"/>\n</vector>\n''',
}
base = Path('src/Meltype.Android/Resources/drawable')
base.mkdir(parents=True, exist_ok=True)
for name, data in drawables.items():
    (base / name).write_text(data, encoding='utf-8')

print('patched key UX and wrote', len(drawables), 'drawables')
