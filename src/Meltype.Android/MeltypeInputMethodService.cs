// SPDX-License-Identifier: GPL-3.0-or-later

using Android.App;
using Android.Content;
using Android.InputMethodServices;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Google.Android.Material.Button;
using Meltype.Composition;
using Color = Android.Graphics.Color;
using ColorStateList = Android.Content.Res.ColorStateList;

namespace Meltype.Android;

[Service(
    Label = "@string/ime_name",
    Permission = "android.permission.BIND_INPUT_METHOD",
    Exported = true)]
[IntentFilter(new[] { "android.view.InputMethod" })]
[MetaData("android.view.im", Resource = "@xml/method")]
public sealed class MeltypeInputMethodService : InputMethodService
{
    private const int VkBack = 0x08;
    private const int VkReturn = 0x0D;
    private const int VkSpace = 0x20;
    private const int ImeActionMask = 0x000000ff;

    private static readonly string[] EmojiShortlist =
    [
        "😀", "😂", "🥹", "😍", "🤔", "👍", "🙏", "🔥", "✨", "❤️", "😭", "😎"
    ];

    private readonly List<(MaterialButton Button, char Character)> _letterButtons = [];

    private MeltypeSession? _session;
    private IKanjiConverter? _converter;
    private MozcNativeConverter? _nativeMozc;
    private Context? _uiContext;
    private LinearLayout? _candidateStrip;
    private MaterialButton? _modeButton;
    private MaterialButton? _shiftButton;
    private MaterialButton? _spaceButton;
    private MaterialButton? _enterButton;
    private bool _direct;
    private bool _shift;
    private bool _hasComposingText;

    private Context UiContext =>
        _uiContext ??= new ContextThemeWrapper(this, Resource.Style.MeltypeTheme);

    private bool IsDarkTheme
    {
        get
        {
            var mode = (int)(Resources?.Configuration?.UiMode ?? 0);
            return (mode & 0x30) == 0x20;
        }
    }

    private Color KeyboardBackground => Color.ParseColor(IsDarkTheme ? "#1B1A1D" : "#F3F0F4");
    private Color KeyBackground => Color.ParseColor(IsDarkTheme ? "#343237" : "#FFFFFF");
    private Color SpecialKeyBackground => Color.ParseColor(IsDarkTheme ? "#464349" : "#E2DDE4");
    private Color Primary => Color.ParseColor(IsDarkTheme ? "#CBB8F8" : "#6750A4");
    private Color OnPrimary => Color.ParseColor(IsDarkTheme ? "#35205E" : "#FFFFFF");
    private Color PrimaryContainer => Color.ParseColor(IsDarkTheme ? "#4D3A72" : "#EADDFF");
    private Color OnPrimaryContainer => Color.ParseColor(IsDarkTheme ? "#F0E5FF" : "#21005D");
    private Color KeyForeground => Color.ParseColor(IsDarkTheme ? "#F1EDF3" : "#1D1B20");
    private Color SecondaryForeground => Color.ParseColor(IsDarkTheme ? "#BBB5BF" : "#5F5964");
    private Color CandidateBackground => Color.ParseColor(IsDarkTheme ? "#29272C" : "#ECE7EE");

    public override void OnCreate()
    {
        base.OnCreate();
        var baseDirectory = FilesDir?.AbsolutePath ?? CacheDir?.AbsolutePath ?? ".";
        var profileDirectory = Path.Combine(baseDirectory, "mozc-profile");
        var dataFile = ExtractMozcData(baseDirectory);
        _nativeMozc = dataFile is null
            ? null
            : MozcNativeConverter.TryCreate(profileDirectory, dataFile);
        _converter = (IKanjiConverter?)_nativeMozc ?? new AndroidFallbackConverter();
    }

    public override void OnDestroy()
    {
        _nativeMozc?.Dispose();
        _nativeMozc = null;
        _converter = null;
        _uiContext = null;
        base.OnDestroy();
    }

    public override void OnStartInput(EditorInfo? attribute, bool restarting)
    {
        base.OnStartInput(attribute, restarting);

        var converter = _converter ?? new AndroidFallbackConverter();
        Func<string, IReadOnlyList<string>>? moreCandidates =
            _nativeMozc is null ? null : _nativeMozc.Candidates;

        _session = MeltypeSession.CreateDefault(
            converter,
            moreCandidates,
            wordChecker: null);
        _session.Direct = _direct;
        _hasComposingText = false;

        UpdateEnterKey(attribute);
        ShowIdleTopBar();
    }

    public override void OnFinishInput()
    {
        if (_session is { IsComposing: true })
            Apply(_session.CommitPending());

        _session = null;
        _hasComposingText = false;
        ShowIdleTopBar();
        base.OnFinishInput();
    }

    public override View? OnCreateInputView()
    {
        var context = UiContext;
        _letterButtons.Clear();

        var root = new LinearLayout(context)
        {
            Orientation = Orientation.Vertical
        };
        root.SetBackgroundColor(KeyboardBackground);
        root.SetPadding(Dp(5), Dp(2), Dp(7), Dp(20));

        var candidateScroll = new HorizontalScrollView(context)
        {
            HorizontalScrollBarEnabled = false,
            FillViewport = true
        };
        _candidateStrip = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal
        };
        _candidateStrip.SetGravity(GravityFlags.CenterVertical);
        _candidateStrip.SetPadding(Dp(4), Dp(2), Dp(4), Dp(2));
        candidateScroll.AddView(_candidateStrip, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.MatchParent));
        root.AddView(candidateScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(46)));

        root.AddView(CreateCharacterRow("qwertyuiop", "1234567890"));

        var second = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        second.SetPadding(Dp(7), 0, Dp(7), 0);
        foreach (var c in "asdfghjkl")
            second.AddView(CreateLetterKey(c), WeightedKeyParams());
        second.AddView(CreateKey("ー", () => HandleCharacter('ー'), KeyKind.Normal), WeightedKeyParams());
        root.AddView(second);

        var third = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        _shiftButton = CreateKey("⇧", ToggleShift, KeyKind.Special);
        third.AddView(_shiftButton, WeightedKeyParams(1.32f));
        foreach (var c in "zxcvbnm")
            third.AddView(CreateLetterKey(c), WeightedKeyParams());
        third.AddView(CreateKey("⌫", HandleBackspace, KeyKind.Special), WeightedKeyParams(1.32f));
        root.AddView(third);

        var bottom = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        _modeButton = CreateKey("あa1", ToggleDirectMode, KeyKind.Special);
        bottom.AddView(_modeButton, WeightedKeyParams(1.28f));
        bottom.AddView(CreateKey("、", () => HandleCharacter('、'), KeyKind.Special), WeightedKeyParams(.82f));
        bottom.AddView(CreateKey("☺", ShowEmojiBar, KeyKind.Special), WeightedKeyParams(.9f));
        _spaceButton = CreateKey("日本語", HandleSpace, KeyKind.Normal);
        bottom.AddView(_spaceButton, WeightedKeyParams(2.45f));
        bottom.AddView(CreateKey("。", () => HandleCharacter('。'), KeyKind.Special), WeightedKeyParams(.82f));
        bottom.AddView(CreateKey("◀", () => MoveCursor(global::Android.Views.Keycode.DpadLeft), KeyKind.Special), WeightedKeyParams(.82f));
        bottom.AddView(CreateKey("▶", () => MoveCursor(global::Android.Views.Keycode.DpadRight), KeyKind.Special), WeightedKeyParams(.82f));
        _enterButton = CreateKey("↵", HandleEnter, KeyKind.Accent);
        bottom.AddView(_enterButton, WeightedKeyParams(1.18f));
        root.AddView(bottom);

        UpdateModeLabel();
        RefreshShiftVisual();
        UpdateEnterKey(CurrentInputEditorInfo);
        ShowIdleTopBar();
        return root;
    }

    private string? ExtractMozcData(string baseDirectory)
    {
        try
        {
            var path = Path.Combine(baseDirectory, "mozc.data");
            using var input = Assets?.Open("mozc.data");
            if (input is null)
                return null;
            using var output = File.Create(path);
            input.CopyTo(output);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private LinearLayout CreateCharacterRow(string keys, string? hints = null)
    {
        var row = new LinearLayout(UiContext) { Orientation = Orientation.Horizontal };
        for (var i = 0; i < keys.Length; i++)
        {
            var hint = hints is not null && i < hints.Length ? hints[i].ToString() : null;
            row.AddView(CreateLetterKey(keys[i], hint), WeightedKeyParams());
        }
        return row;
    }

    private View CreateLetterKey(char character, string? hint = null)
    {
        var button = CreateKey(character.ToString(), () => HandleLetter(character));
        _letterButtons.Add((button, character));

        if (hint is null)
            return button;

        var frame = new FrameLayout(UiContext);
        frame.AddView(button, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        var hintView = new TextView(UiContext)
        {
            Text = hint,
            TextSize = 9,
            Gravity = GravityFlags.Center,
            Clickable = false,
            Focusable = false
        };
        hintView.SetTextColor(SecondaryForeground);
        var hintParams = new FrameLayout.LayoutParams(Dp(18), Dp(18))
        {
            Gravity = GravityFlags.Top | GravityFlags.Right
        };
        hintParams.SetMargins(0, Dp(1), Dp(2), 0);
        frame.AddView(hintView, hintParams);
        return frame;
    }

    private enum KeyKind
    {
        Normal,
        Special,
        Accent,
        Candidate,
        CandidateSelected,
        Toolbar
    }

    private MaterialButton CreateKey(string text, Action action, KeyKind kind = KeyKind.Normal)
    {
        var button = new MaterialButton(UiContext)
        {
            Text = text,
            TextSize = kind switch
            {
                KeyKind.Candidate or KeyKind.CandidateSelected => 15,
                KeyKind.Toolbar => 20,
                _ => 17
            },
            Gravity = GravityFlags.Center,
            Elevation = 0,
            HapticFeedbackEnabled = true
        };
        button.SetAllCaps(false);
        button.SetPadding(
            Dp(kind is KeyKind.Candidate or KeyKind.CandidateSelected ? 12 : 3),
            0,
            Dp(kind is KeyKind.Candidate or KeyKind.CandidateSelected ? 12 : 3),
            0);
        ApplyKeyAppearance(button, kind);

        button.Touch += (_, e) =>
        {
            if (e.Event?.Action == MotionEventActions.Down)
                button.PerformHapticFeedback(FeedbackConstants.KeyboardPress);
            e.Handled = false;
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void ApplyKeyAppearance(MaterialButton button, KeyKind kind)
    {
        var background = kind switch
        {
            KeyKind.Special => SpecialKeyBackground,
            KeyKind.Accent => Primary,
            KeyKind.Candidate => CandidateBackground,
            KeyKind.CandidateSelected => PrimaryContainer,
            KeyKind.Toolbar => KeyboardBackground,
            _ => KeyBackground
        };
        var foreground = kind switch
        {
            KeyKind.Accent => OnPrimary,
            KeyKind.CandidateSelected => OnPrimaryContainer,
            _ => KeyForeground
        };

        button.BackgroundTintList = ColorStateList.ValueOf(background);
        button.SetTextColor(foreground);
        button.CornerRadius = Dp(kind switch
        {
            KeyKind.Candidate or KeyKind.CandidateSelected => 20,
            KeyKind.Toolbar => 22,
            _ => 10
        });
        button.InsetTop = 0;
        button.InsetBottom = 0;
    }

    private LinearLayout.LayoutParams WeightedKeyParams(float weight = 1f)
    {
        var p = new LinearLayout.LayoutParams(0, Dp(54), weight);
        p.SetMargins(Dp(2), Dp(3), Dp(2), Dp(3));
        return p;
    }

    private void HandleLetter(char raw)
    {
        var c = _shift ? char.ToUpperInvariant(raw) : raw;
        HandleCharacter(c);
        if (_shift)
        {
            _shift = false;
            RefreshShiftVisual();
        }
    }

    private void HandleCharacter(char c)
    {
        var connection = CurrentInputConnection;
        if (connection is null)
            return;

        var session = _session;
        if (session is null)
        {
            connection.CommitText(c.ToString(), 1);
            return;
        }

        var vk = char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : c <= 0x7f ? c : 0;
        var (before, after) = SurroundingText();
        var result = session.HandleKey(vk, c, _shift, false, false, false, before, after);
        Apply(result);

        if (!result.Consumed)
            connection.CommitText(c.ToString(), 1);
    }

    private void HandleSpace() =>
        HandleVirtualKey(VkSpace, ' ', () => CurrentInputConnection?.CommitText(" ", 1));

    private void HandleBackspace() =>
        HandleVirtualKey(VkBack, null, () => CurrentInputConnection?.DeleteSurroundingText(1, 0));

    private void HandleEnter() =>
        HandleVirtualKey(VkReturn, null, PerformEditorEnter);

    private void PerformEditorEnter()
    {
        var connection = CurrentInputConnection;
        if (connection is null)
            return;

        var editor = CurrentInputEditorInfo;

        if (editor is not null && editor.ActionId > 0 && editor.ActionLabel is not null)
        {
            if (connection.PerformEditorAction((ImeAction)editor.ActionId))
                return;
        }

        if (SendDefaultEditorAction(true))
            return;

        SendKeyChar('\n');
    }

    private void MoveCursor(global::Android.Views.Keycode keycode)
    {
        var connection = CurrentInputConnection;
        if (connection is null)
            return;

        connection.SendKeyEvent(new KeyEvent(KeyEventActions.Down, keycode));
        connection.SendKeyEvent(new KeyEvent(KeyEventActions.Up, keycode));
    }

    private void HandleVirtualKey(int vk, char? ch, Action fallback)
    {
        var session = _session;
        if (session is null)
        {
            fallback();
            return;
        }

        var (before, after) = SurroundingText();
        var result = session.HandleKey(vk, ch, false, false, false, false, before, after);
        Apply(result);
        if (!result.Consumed)
            fallback();
    }

    private void ToggleShift()
    {
        _shift = !_shift;
        RefreshShiftVisual();
        if (!_hasComposingText)
            ShowIdleTopBar();
    }

    private void RefreshShiftVisual()
    {
        foreach (var (button, character) in _letterButtons)
            button.Text = (_shift ? char.ToUpperInvariant(character) : character).ToString();

        if (_shiftButton is null)
            return;

        _shiftButton.BackgroundTintList = ColorStateList.ValueOf(
            _shift ? PrimaryContainer : SpecialKeyBackground);
        _shiftButton.SetTextColor(_shift ? OnPrimaryContainer : KeyForeground);
    }

    private void ToggleDirectMode()
    {
        if (_session is { IsComposing: true } session)
            Apply(session.CommitPending());

        _direct = !_direct;
        if (_session is not null)
            _session.Direct = _direct;

        UpdateModeLabel();
        ShowIdleTopBar();
    }

    private void UpdateModeLabel()
    {
        if (_modeButton is not null)
        {
            _modeButton.Text = _direct ? "ABC" : "あa1";
            _modeButton.ContentDescription = _direct ? "英字直接入力" : "Meltype 日本語入力";
        }

        if (_spaceButton is not null)
            _spaceButton.Text = _direct ? "English" : "日本語";
    }

    private void UpdateEnterKey(EditorInfo? editor)
    {
        if (_enterButton is null)
            return;

        var action = editor is null ? 0 : ((int)editor.ImeOptions & ImeActionMask);
        var (label, description) = action switch
        {
            2 => ("→", "移動"),
            3 => ("⌕", "検索"),
            4 => ("➤", "送信"),
            5 => ("⇥", "次へ"),
            6 => ("✓", "完了"),
            7 => ("⇤", "前へ"),
            _ => ("↵", "改行")
        };
        _enterButton.Text = label;
        _enterButton.ContentDescription = description;
    }

    private void ShowInputMethodPicker()
    {
        var manager = GetSystemService(InputMethodService) as InputMethodManager;
        manager?.ShowInputMethodPicker();
    }

    private void OpenSettings()
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.NewTask);
        StartActivity(intent);
    }

    private void PasteClipboard()
    {
        var clipboard = GetSystemService(ClipboardService) as ClipboardManager;
        var clip = clipboard?.PrimaryClip;
        if (clip is null || clip.ItemCount == 0)
            return;

        var text = clip.GetItemAt(0)?.CoerceToText(this)?.ToString();
        if (!string.IsNullOrEmpty(text))
            CurrentInputConnection?.CommitText(text, 1);
    }

    private void ShowEmojiBar()
    {
        if (_candidateStrip is null)
            return;

        _candidateStrip.RemoveAllViews();
        foreach (var emoji in EmojiShortlist)
        {
            var captured = emoji;
            var button = CreateKey(captured, () =>
            {
                CurrentInputConnection?.CommitText(captured, 1);
                ShowIdleTopBar();
            }, KeyKind.Candidate);
            var parameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                Dp(40));
            parameters.SetMargins(Dp(2), Dp(1), Dp(2), Dp(1));
            _candidateStrip.AddView(button, parameters);
        }
    }

    private (string? Before, string? After) SurroundingText()
    {
        if (_hasComposingText)
            return (null, null);

        var connection = CurrentInputConnection;
        if (connection is null)
            return (null, null);

        try
        {
            return (
                connection.GetTextBeforeCursor(20, (GetTextFlags)0),
                connection.GetTextAfterCursor(20, (GetTextFlags)0));
        }
        catch
        {
            return (null, null);
        }
    }

    private void Apply(SessionResult? result)
    {
        if (result is null)
            return;

        var connection = CurrentInputConnection;
        if (connection is null)
            return;

        foreach (var edit in result.Commits)
        {
            if (edit.DeleteBefore > 0)
                connection.DeleteSurroundingText(edit.DeleteBefore, 0);
            if (!string.IsNullOrEmpty(edit.Text))
                connection.CommitText(edit.Text, 1);
            _hasComposingText = false;
        }

        if (result.View is { } view)
        {
            connection.SetComposingText(view.Text, 1);
            _hasComposingText = view.Text.Length > 0;
            ShowCandidates(view);
        }
        else
        {
            connection.FinishComposingText();
            _hasComposingText = false;
            ShowIdleTopBar();
        }
    }

    private void ShowCandidates(CompositionView view)
    {
        if (_candidateStrip is null)
            return;

        _candidateStrip.RemoveAllViews();
        if (view.Converting && view.Candidates.Count > 0)
        {
            for (var i = 0; i < view.Candidates.Count; i++)
            {
                var index = i;
                var kind = i == view.SelectedIndex ? KeyKind.CandidateSelected : KeyKind.Candidate;
                var button = CreateKey(view.Candidates[i], () =>
                {
                    if (_session is not null)
                        Apply(_session.SelectCandidate(index));
                }, kind);

                var parameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent,
                    Dp(40));
                parameters.SetMargins(Dp(2), Dp(1), Dp(2), Dp(1));
                _candidateStrip.AddView(button, parameters);
            }
            return;
        }

        AddCandidateHint(!string.IsNullOrWhiteSpace(view.Hint) ? view.Hint : view.Text);
    }

    private void ShowIdleTopBar()
    {
        if (_candidateStrip is null)
            return;

        _candidateStrip.RemoveAllViews();
        AddToolbarKey("▦", ShowInputMethodPicker, "入力方法を切り替える");
        AddToolbarKey("☺", ShowEmojiBar, "絵文字");
        AddToolbarKey("▣", PasteClipboard, "クリップボードから貼り付ける");
        AddToolbarKey("⚙", OpenSettings, "Meltype 設定");
    }

    private void AddToolbarKey(string label, Action action, string description)
    {
        if (_candidateStrip is null)
            return;

        var button = CreateKey(label, action, KeyKind.Toolbar);
        button.ContentDescription = description;
        var parameters = new LinearLayout.LayoutParams(Dp(54), Dp(40));
        parameters.SetMargins(Dp(3), Dp(1), Dp(3), Dp(1));
        _candidateStrip.AddView(button, parameters);
    }

    private void AddCandidateHint(string? text)
    {
        if (_candidateStrip is null || string.IsNullOrWhiteSpace(text))
            return;

        var hint = new TextView(UiContext)
        {
            Text = text,
            TextSize = 14,
            Gravity = GravityFlags.CenterVertical,
            Ellipsize = TextUtils.TruncateAt.End
        };
        hint.SetMaxLines(1);
        hint.SetTextColor(SecondaryForeground);
        hint.SetPadding(Dp(10), 0, Dp(10), 0);
        _candidateStrip.AddView(hint, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.MatchParent));
    }

    private int Dp(int value) =>
        (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
}
