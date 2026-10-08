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
'''    private Color KeyboardBackground => Color.ParseColor(IsDarkTheme ? "#171815" : "#F2F2F2");
    private Color KeyBackground => Color.ParseColor(IsDarkTheme ? "#2B2D29" : "#FFFFFF");
    private Color SpecialKeyBackground => Color.ParseColor(IsDarkTheme ? "#3A3B38" : "#D9D9D9");
    private Color Primary => Color.ParseColor(IsDarkTheme ? "#BBA1FF" : "#6750A4");
    private Color OnPrimary => Color.ParseColor(IsDarkTheme ? "#2E2143" : "#FFFFFF");
    private Color PrimaryContainer => Color.ParseColor(IsDarkTheme ? "#514371" : "#EADDFF");
    private Color OnPrimaryContainer => Color.ParseColor(IsDarkTheme ? "#F1E9FF" : "#21005D");
    private Color KeyForeground => Color.ParseColor(IsDarkTheme ? "#F1F1F1" : "#202124");
    private Color SecondaryForeground => Color.ParseColor(IsDarkTheme ? "#B6B7B3" : "#5F6368");
    private Color CandidateBackground => Color.ParseColor(IsDarkTheme ? "#232420" : "#ECECEC");''',
'''    private Color KeyboardBackground => Color.ParseColor(IsDarkTheme ? "#171814" : "#F3F3F3");
    private Color KeyBackground => Color.ParseColor(IsDarkTheme ? "#2A2B28" : "#FFFFFF");
    private Color SpecialKeyBackground => Color.ParseColor(IsDarkTheme ? "#3B3C39" : "#DADCE0");
    private Color Primary => Color.ParseColor(IsDarkTheme ? "#C8AEFF" : "#7656A8");
    private Color OnPrimary => Color.ParseColor(IsDarkTheme ? "#2B2430" : "#FFFFFF");
    private Color PrimaryContainer => Color.ParseColor(IsDarkTheme ? "#47404F" : "#EADDFF");
    private Color OnPrimaryContainer => Color.ParseColor(IsDarkTheme ? "#F3ECFA" : "#21005D");
    private Color KeyForeground => Color.ParseColor(IsDarkTheme ? "#F2F2F2" : "#202124");
    private Color SecondaryForeground => Color.ParseColor(IsDarkTheme ? "#B8BAB5" : "#5F6368");
    private Color CandidateBackground => Color.ParseColor(IsDarkTheme ? "#242522" : "#EEF0F1");''')

replace_once(
'''        root.SetBackgroundColor(KeyboardBackground);
        root.SetPadding(Dp(4), Dp(1), Dp(4), Dp(18));''',
'''        root.SetBackgroundColor(KeyboardBackground);
        root.SetPadding(Dp(5), 0, Dp(5), Dp(14));''')

replace_once(
'''        _candidateStrip.SetGravity(GravityFlags.CenterVertical);
        _candidateStrip.SetPadding(Dp(2), Dp(1), Dp(2), Dp(1));''',
'''        _candidateStrip.SetGravity(GravityFlags.CenterVertical);
        _candidateStrip.SetPadding(Dp(2), 0, Dp(2), 0);''')

replace_once(
'''        root.AddView(candidateScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(44)));''',
'''        root.AddView(candidateScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(46)));''')

replace_once(
'''        var button = CreateKey(character.ToString(), () => HandleLetter(character));
        _letterButtons.Add((button, character));
        AttachLetterPressVisual(button);''',
'''        var button = CreateKey(character.ToString(), () => HandleLetter(character));
        _letterButtons.Add((button, character));''')

replace_once(
'''            TextSize = kind switch
            {
                KeyKind.Candidate or KeyKind.CandidateSelected => 15,
                KeyKind.Toolbar => 20,
                _ => 18
            },''',
'''            TextSize = kind switch
            {
                KeyKind.Candidate or KeyKind.CandidateSelected => 16,
                KeyKind.Toolbar => 20,
                _ when text.Length >= 3 => 15,
                _ => 20
            },''')

replace_once(
'''        button.SetMinWidth(0);
        button.SetMinHeight(0);
        button.SetPadding(''',
'''        button.SetMinWidth(0);
        button.SetMinHeight(0);
        button.Typeface = global::Android.Graphics.Typeface.Create(
            "sans-serif-medium",
            global::Android.Graphics.TypefaceStyle.Normal);
        button.SetPadding(''')

replace_once(
'''        button.Click += (_, _) => SafeRun("KeyAction", action);
        return button;''',
'''        if (kind is KeyKind.Normal or KeyKind.Special or KeyKind.PillSpecial or KeyKind.Accent)
            AttachLetterPressVisual(button);

        button.Click += (_, _) => SafeRun("KeyAction", action);
        return button;''')

replace_once(
'''            KeyKind.Accent => Primary,
            KeyKind.Candidate => CandidateBackground,
            KeyKind.CandidateSelected => PrimaryContainer,
            KeyKind.Toolbar => KeyboardBackground,''',
'''            KeyKind.Accent => Primary,
            KeyKind.Candidate => KeyboardBackground,
            KeyKind.CandidateSelected => CandidateBackground,
            KeyKind.Toolbar => KeyboardBackground,''')

replace_once(
'''            KeyKind.Accent => OnPrimary,
            KeyKind.CandidateSelected => OnPrimaryContainer,
            _ => KeyForeground''',
'''            KeyKind.Accent => OnPrimary,
            KeyKind.CandidateSelected => KeyForeground,
            _ => KeyForeground''')

replace_once(
'''            KeyKind.Candidate or KeyKind.CandidateSelected => 18,
            KeyKind.PillSpecial or KeyKind.Accent => 22,
            KeyKind.Toolbar => 20,
            _ => 7''',
'''            KeyKind.Candidate or KeyKind.CandidateSelected => 7,
            KeyKind.PillSpecial or KeyKind.Accent => 22,
            KeyKind.Toolbar => 20,
            _ => 6''')

replace_once(
'''                    case MotionEventActions.Down:
                        button.ScaleX = 1.045f;
                        button.ScaleY = 1.045f;
                        button.TranslationY = -Dp(1);
                        break;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        button.ScaleX = 1f;
                        button.ScaleY = 1f;
                        button.TranslationY = 0f;
                        break;''',
'''                    case MotionEventActions.Down:
                        button.Alpha = 0.78f;
                        button.ScaleX = 1f;
                        button.ScaleY = 1f;
                        button.TranslationY = Dp(1);
                        break;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        button.Alpha = 1f;
                        button.ScaleX = 1f;
                        button.ScaleY = 1f;
                        button.TranslationY = 0f;
                        break;''')

replace_once(
'''            AddToolbarKey("▦", ShowInputMethodPicker, "入力方法を切り替える");
            AddToolbarKey("☺", ShowEmojiBar, "絵文字");
            AddToolbarKey("あ", ToggleDirectMode, "入力モードを切り替える");
            AddToolbarKey("▣", PasteClipboard, "クリップボードから貼り付ける");
            AddToolbarKey("⚙", OpenSettings, "Meltype 設定");''',
'''            AddToolbarIcon(Resource.Drawable.ic_toolbar_apps, ShowInputMethodPicker, "入力方法を切り替える");
            AddToolbarIcon(Resource.Drawable.ic_toolbar_emoji, ShowEmojiBar, "絵文字");
            AddToolbarIcon(Resource.Drawable.ic_toolbar_translate, ToggleDirectMode, "入力モードを切り替える");
            AddToolbarIcon(Resource.Drawable.ic_toolbar_clipboard, PasteClipboard, "クリップボードから貼り付ける");
            AddToolbarIcon(Resource.Drawable.ic_toolbar_settings, OpenSettings, "Meltype 設定");''')

replace_once(
'''    private void AddToolbarKey(string label, Action action, string description)
    {
        var strip = _candidateStrip;
        if (strip is null)
            return;

        var button = CreateKey(label, action, KeyKind.Toolbar);
        button.ContentDescription = description;

        var parameters = new LinearLayout.LayoutParams(
            0,
            Dp(40),
            1f);
        parameters.SetMargins(Dp(2), 0, Dp(2), 0);
        strip.AddView(button, parameters);
    }''',
'''    private void AddToolbarIcon(int iconResource, Action action, string description)
    {
        var strip = _candidateStrip;
        if (strip is null)
            return;

        var button = new ImageButton(UiContext)
        {
            ContentDescription = description,
            Focusable = false,
            FocusableInTouchMode = false,
            Clickable = true,
            HapticFeedbackEnabled = true
        };
        button.SetImageResource(iconResource);
        button.SetColorFilter(KeyForeground);
        button.SetBackgroundColor(Color.Transparent);
        button.SetPadding(Dp(13), Dp(10), Dp(13), Dp(10));

        button.Touch += (_, e) =>
        {
            try
            {
                switch (e.Event?.Action)
                {
                    case MotionEventActions.Down:
                        button.Alpha = 0.62f;
                        button.PerformHapticFeedback(FeedbackConstants.VirtualKey);
                        break;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        button.Alpha = 1f;
                        break;
                }
            }
            catch (Exception ex)
            {
                Warn("ToolbarTouch", ex);
            }

            e.Handled = false;
        };
        button.Click += (_, _) => SafeRun("ToolbarAction", action);

        var parameters = new LinearLayout.LayoutParams(
            0,
            Dp(44),
            1f);
        parameters.SetMargins(Dp(4), 0, Dp(4), 0);
        strip.AddView(button, parameters);
    }''')

path.write_text(text, encoding="utf-8")
print("patched", path)
