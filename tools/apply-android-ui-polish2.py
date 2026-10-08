#!/usr/bin/env python3
from pathlib import Path

path = Path('src/Meltype.Android/MeltypeInputMethodService.cs')
text = path.read_text(encoding='utf-8')

def r(old, new):
    global text
    c = text.count(old)
    if c != 1:
        raise SystemExit(f'expected 1 match, got {c}: {old[:120]!r}')
    text = text.replace(old, new, 1)

r('root.SetPadding(Dp(5), 0, Dp(5), Dp(14));',
  'root.SetPadding(Dp(5), 0, Dp(5), Dp(38));')

r('''        root.AddView(candidateScroll, new LinearLayout.LayoutParams(\n            ViewGroup.LayoutParams.MatchParent,\n            Dp(46)));''',
  '''        root.AddView(candidateScroll, new LinearLayout.LayoutParams(\n            ViewGroup.LayoutParams.MatchParent,\n            Dp(52)));''')

r('CreateKey("☺", ShowEmojiBar, KeyKind.Special)',
  'CreateKey("☺︎", ShowEmojiBar, KeyKind.Special)')

r('''            KeyKind.Special or KeyKind.PillSpecial => SpecialKeyBackground,\n            KeyKind.Accent => Primary,''',
  '''            KeyKind.Special or KeyKind.PillSpecial => SpecialKeyBackground,\n            KeyKind.Accent => IsDarkTheme ? SpecialKeyBackground : Primary,''')

r('''            KeyKind.Accent => OnPrimary,\n            KeyKind.CandidateSelected => KeyForeground,''',
  '''            KeyKind.Accent => IsDarkTheme ? KeyForeground : OnPrimary,\n            KeyKind.CandidateSelected => KeyForeground,''')

r('''            AddToolbarIcon(Resource.Drawable.ic_toolbar_apps, ShowInputMethodPicker, "入力方法を切り替える");\n            AddToolbarIcon(Resource.Drawable.ic_toolbar_emoji, ShowEmojiBar, "絵文字");\n            AddToolbarIcon(Resource.Drawable.ic_toolbar_translate, ToggleDirectMode, "入力モードを切り替える");\n            AddToolbarIcon(Resource.Drawable.ic_toolbar_clipboard, PasteClipboard, "クリップボードから貼り付ける");\n            AddToolbarIcon(Resource.Drawable.ic_toolbar_settings, OpenSettings, "Meltype 設定");''',
  '''            AddToolbarIcon(Resource.Drawable.ic_toolbar_apps, ShowInputMethodPicker, "入力方法を切り替える");\n            AddToolbarSpacer();\n            AddToolbarIcon(Resource.Drawable.ic_toolbar_emoji, ShowEmojiBar, "絵文字");\n            AddToolbarSpacer();\n            AddToolbarIcon(Resource.Drawable.ic_toolbar_translate, ToggleDirectMode, "入力モードを切り替える");\n            AddToolbarSpacer();\n            AddToolbarIcon(Resource.Drawable.ic_toolbar_clipboard, PasteClipboard, "クリップボードから貼り付ける");\n            AddToolbarSpacer();\n            AddToolbarIcon(Resource.Drawable.ic_toolbar_settings, OpenSettings, "Meltype 設定");''')

r('''        var parameters = new LinearLayout.LayoutParams(\n            0,\n            Dp(44),\n            1f);\n        parameters.SetMargins(Dp(4), 0, Dp(4), 0);\n        strip.AddView(button, parameters);\n    }\n\n    private void AddCandidateHint''',
  '''        var parameters = new LinearLayout.LayoutParams(\n            Dp(44),\n            Dp(44));\n        strip.AddView(button, parameters);\n    }\n\n    private void AddToolbarSpacer()\n    {\n        var strip = _candidateStrip;\n        if (strip is null)\n            return;\n\n        strip.AddView(new View(UiContext), new LinearLayout.LayoutParams(\n            0,\n            Dp(1),\n            1f));\n    }\n\n    private void AddCandidateHint''')

path.write_text(text, encoding='utf-8')
print('patched', path)
