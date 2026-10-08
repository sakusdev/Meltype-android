// SPDX-License-Identifier: GPL-3.0-or-later

using Android.App;
using Android.Content;
using Android.InputMethodServices;
using Android.Text;
using Android.Util;
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
    private const string LogTag = "MeltypeIME";
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
    private MaterialButton? _backspaceButton;
    private ActionRunnable? _backspaceRepeatRunnable;
    private bool _backspaceRepeating;
    private bool _backspaceRepeated;
    private float _spaceLastX;
    private bool _spaceWasSwiped;
    private long _lastSpaceInputAtMs;
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

    // Gboard-inspired neutral surfaces while keeping the app on a Material 3 theme.
    private Color KeyboardBackground => Color.ParseColor(IsDarkTheme ? "#171814" : "#F3F3F3");
    private Color KeyBackground => Color.ParseColor(IsDarkTheme ? "#2A2B28" : "#FFFFFF");
    private Color SpecialKeyBackground => Color.ParseColor(IsDarkTheme ? "#3B3C39" : "#DADCE0");
    private Color Primary => Color.ParseColor(IsDarkTheme ? "#C8AEFF" : "#7656A8");
    private Color OnPrimary => Color.ParseColor(IsDarkTheme ? "#2B2430" : "#FFFFFF");
    private Color PrimaryContainer => Color.ParseColor(IsDarkTheme ? "#47404F" : "#EADDFF");
    private Color OnPrimaryContainer => Color.ParseColor(IsDarkTheme ? "#F3ECFA" : "#21005D");
    private Color KeyForeground => Color.ParseColor(IsDarkTheme ? "#F2F2F2" : "#202124");
    private Color SecondaryForeground => Color.ParseColor(IsDarkTheme ? "#B8BAB5" : "#5F6368");
    private Color CandidateBackground => Color.ParseColor(IsDarkTheme ? "#242522" : "#EEF0F1");

    public override void OnCreate()
    {
        base.OnCreate();

        try
        {
            var baseDirectory = FilesDir?.AbsolutePath ?? CacheDir?.AbsolutePath ?? ".";
            var profileDirectory = Path.Combine(baseDirectory, "mozc-profile");
            var dataFile = ExtractMozcData(baseDirectory);
            _nativeMozc = dataFile is null
                ? null
                : MozcNativeConverter.TryCreate(profileDirectory, dataFile);
            _converter = (IKanjiConverter?)_nativeMozc ?? new AndroidFallbackConverter();
        }
        catch (Exception ex)
        {
            Warn("OnCreate/Mozc", ex);
            _nativeMozc = null;
            _converter = new AndroidFallbackConverter();
        }
    }

    public override void OnDestroy()
    {
        try
        {
            _nativeMozc?.Dispose();
        }
        catch (Exception ex)
        {
            Warn("OnDestroy/Mozc", ex);
        }

        _nativeMozc = null;
        _converter = null;
        _session = null;
        _candidateStrip = null;
        _modeButton = null;
        _shiftButton = null;
        StopBackspaceRepeat();
        _spaceButton = null;
        _enterButton = null;
        _backspaceButton = null;
        _backspaceRepeatRunnable?.Dispose();
        _backspaceRepeatRunnable = null;
        _uiContext = null;

        base.OnDestroy();
    }

    public override void OnStartInput(EditorInfo? attribute, bool restarting)
    {
        base.OnStartInput(attribute, restarting);

        try
        {
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
        catch (Exception ex)
        {
            Warn("OnStartInput", ex);
            _session = MeltypeSession.CreateDefault(
                new AndroidFallbackConverter(),
                moreCandidates: null,
                wordChecker: null);
            _session.Direct = _direct;
            _hasComposingText = false;
        }
    }

    public override void OnStartInputView(EditorInfo? info, bool restarting)
    {
        base.OnStartInputView(info, restarting);
        Log.Info(LogTag,
            $"input-view start restart={restarting} package={info?.PackageName ?? "?"} inputType={(int)(info?.InputType ?? 0)} imeOptions={(int)(info?.ImeOptions ?? 0)}");
    }

    public override void OnFinishInputView(bool finishingInput)
    {
        StopBackspaceRepeat();
        var sinceSpace = _lastSpaceInputAtMs == 0
            ? -1
            : Environment.TickCount64 - _lastSpaceInputAtMs;
        Log.Info(LogTag,
            $"input-view finish finishingInput={finishingInput} sinceSpaceMs={sinceSpace}");
        base.OnFinishInputView(finishingInput);
    }

    public override void OnFinishInput()
    {
        // InputConnection can already be invalid by the time Android calls this.
        // Never allow cleanup / pending composition to crash the IME process.
        try
        {
            if (_session is { IsComposing: true } session)
                Apply(session.CommitPending(), updateUi: false);
        }
        catch (Exception ex)
        {
            Warn("OnFinishInput", ex);
        }
        finally
        {
            _session = null;
            _hasComposingText = false;
            base.OnFinishInput();
        }
    }

    public override View? OnCreateInputView()
    {
        try
        {
            return BuildInputView();
        }
        catch (Exception ex)
        {
            Warn("OnCreateInputView", ex);

            // Last-resort minimal view. Keeping an IME visible is preferable to
            // Android falling back to another keyboard after a view exception.
            var fallback = new LinearLayout(this)
            {
                Orientation = Orientation.Vertical
            };
            fallback.SetBackgroundColor(KeyboardBackground);
            var label = new TextView(this)
            {
                Text = "Meltype",
                Gravity = GravityFlags.Center,
                TextSize = 16
            };
            label.SetTextColor(KeyForeground);
            fallback.AddView(label, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, Dp(48)));
            return fallback;
        }
    }

    private View BuildInputView()
    {
        var context = UiContext;
        _letterButtons.Clear();

        var root = new LinearLayout(context)
        {
            Orientation = Orientation.Vertical
        };
        root.SetBackgroundColor(KeyboardBackground);
        root.SetPadding(Dp(5), 0, Dp(5), Dp(14));

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
        _candidateStrip.SetPadding(Dp(2), 0, Dp(2), 0);
        candidateScroll.AddView(_candidateStrip, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));
        root.AddView(candidateScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(46)));

        root.AddView(CreateCharacterRow("qwertyuiop", "1234567890"));

        var second = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        foreach (var c in "asdfghjkl")
            second.AddView(CreateLetterKey(c), WeightedKeyParams());
        second.AddView(CreateKey("ー", () => HandleCharacter('ー'), KeyKind.Normal), WeightedKeyParams());
        root.AddView(second);

        var third = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        _shiftButton = CreateKey("⇧", ToggleShift, KeyKind.Special);
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

        var bottom = new LinearLayout(context) { Orientation = Orientation.Horizontal };

        _modeButton = CreateKey("あa1", ToggleDirectMode, KeyKind.PillSpecial);
        bottom.AddView(_modeButton, WeightedKeyParams(1.45f));

        bottom.AddView(
            CreateKey("、", () => HandleCharacter('、'), KeyKind.Special),
            WeightedKeyParams(.92f));

        bottom.AddView(
            CreateKey("☺", ShowEmojiBar, KeyKind.Special),
            WeightedKeyParams(1.0f));

        _spaceButton = CreateKey("日本語", HandleSpace, KeyKind.Normal);
        AttachSpaceSwipe(_spaceButton);
        bottom.AddView(_spaceButton, WeightedKeyParams(2.15f));

        bottom.AddView(
            CreateKey("。", () => HandleCharacter('。'), KeyKind.Special),
            WeightedKeyParams(.96f));

        bottom.AddView(
            CreateKey("◀", () => MoveCursor(global::Android.Views.Keycode.DpadLeft), KeyKind.Special),
            WeightedKeyParams(.96f));

        bottom.AddView(
            CreateKey("▶", () => MoveCursor(global::Android.Views.Keycode.DpadRight), KeyKind.Special),
            WeightedKeyParams(.96f));

        _enterButton = CreateKey("↵", HandleEnter, KeyKind.Accent);
        bottom.AddView(_enterButton, WeightedKeyParams(1.45f));
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
        catch (Exception ex)
        {
            Warn("ExtractMozcData", ex);
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

        var frame = new FrameLayout(UiContext)
        {
            Focusable = false,
            FocusableInTouchMode = false
        };

        frame.AddView(button, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent));

        var hintView = new TextView(UiContext)
        {
            Text = hint,
            TextSize = 8,
            Gravity = GravityFlags.Center,
            Clickable = false,
            Focusable = false,
            FocusableInTouchMode = false
        };
        hintView.SetTextColor(SecondaryForeground);

        var hintParams = new FrameLayout.LayoutParams(Dp(16), Dp(16))
        {
            Gravity = GravityFlags.Top | GravityFlags.Right
        };
        hintParams.SetMargins(0, 0, Dp(2), 0);
        frame.AddView(hintView, hintParams);
        return frame;
    }

    private enum KeyKind
    {
        Normal,
        Special,
        PillSpecial,
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
                KeyKind.Candidate or KeyKind.CandidateSelected => 16,
                KeyKind.Toolbar => 20,
                _ when text.Length >= 3 => 15,
                _ => 20
            },
            Gravity = GravityFlags.Center,
            Elevation = 0,
            HapticFeedbackEnabled = true,
            Focusable = false,
            FocusableInTouchMode = false
        };

        button.SetAllCaps(false);
        button.SetMinWidth(0);
        button.SetMinHeight(0);
        button.Typeface = global::Android.Graphics.Typeface.Create(
            "sans-serif-medium",
            global::Android.Graphics.TypefaceStyle.Normal);
        button.SetPadding(
            Dp(kind is KeyKind.Candidate or KeyKind.CandidateSelected ? 10 : 2),
            0,
            Dp(kind is KeyKind.Candidate or KeyKind.CandidateSelected ? 10 : 2),
            0);

        ApplyKeyAppearance(button, kind);

        button.Touch += (_, e) =>
        {
            try
            {
                if (e.Event?.Action == MotionEventActions.Down)
                    button.PerformHapticFeedback(FeedbackConstants.VirtualKey);
            }
            catch (Exception ex)
            {
                Warn("Haptic", ex);
            }

            e.Handled = false;
        };

        if (kind is KeyKind.Normal or KeyKind.Special or KeyKind.PillSpecial or KeyKind.Accent)
            AttachLetterPressVisual(button);

        button.Click += (_, _) => SafeRun("KeyAction", action);
        return button;
    }

    private void ApplyKeyAppearance(MaterialButton button, KeyKind kind)
    {
        var background = kind switch
        {
            KeyKind.Special or KeyKind.PillSpecial => SpecialKeyBackground,
            KeyKind.Accent => Primary,
            KeyKind.Candidate => KeyboardBackground,
            KeyKind.CandidateSelected => CandidateBackground,
            KeyKind.Toolbar => KeyboardBackground,
            _ => KeyBackground
        };

        var foreground = kind switch
        {
            KeyKind.Accent => OnPrimary,
            KeyKind.CandidateSelected => KeyForeground,
            _ => KeyForeground
        };

        button.BackgroundTintList = ColorStateList.ValueOf(background);
        button.SetTextColor(foreground);
        button.CornerRadius = Dp(kind switch
        {
            KeyKind.Candidate or KeyKind.CandidateSelected => 7,
            KeyKind.PillSpecial or KeyKind.Accent => 22,
            KeyKind.Toolbar => 20,
            _ => 6
        });
        button.InsetTop = 0;
        button.InsetBottom = 0;
    }

    private LinearLayout.LayoutParams WeightedKeyParams(float weight = 1f)
    {
        var p = new LinearLayout.LayoutParams(0, Dp(48), weight);
        p.SetMargins(Dp(2), Dp(2), Dp(2), Dp(2));
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

    private void HandleSpace()
    {
        if (_spaceWasSwiped)
        {
            _spaceWasSwiped = false;
            return;
        }

        _lastSpaceInputAtMs = Environment.TickCount64;
        var connection = CurrentInputConnection;
        if (connection is null)
            return;

        var session = _session;
        var batchStarted = false;

        try
        {
            batchStarted = connection.BeginBatchEdit();

            // A plain space outside composition must stay a plain editor edit.
            // Running it through MeltypeSession used to emit an empty View and then
            // FinishComposingText(), which some editors interpret as the end of the
            // active IME interaction and may hide the keyboard.
            if (session is null || !session.IsComposing || _direct)
            {
                connection.CommitText(" ", 1);
                _hasComposingText = false;
                ShowIdleTopBar();
            }
            else
            {
                var (before, after) = SurroundingText();
                var result = session.HandleKey(
                    VkSpace, ' ', false, false, false, false, before, after);

                // CommitText already resolves Android composing spans when the
                // controller commits an English word + space. Avoid the extra
                // FinishComposingText() call specifically on Space.
                Apply(result, finishComposition: false);

                if (!result.Consumed)
                    connection.CommitText(" ", 1);
            }
        }
        catch (Exception ex)
        {
            Warn("HandleSpace", ex);
        }
        finally
        {
            if (batchStarted)
            {
                try
                {
                    connection.EndBatchEdit();
                }
                catch (Exception ex)
                {
                    Warn("HandleSpace/EndBatchEdit", ex);
                }
            }
        }

        try
        {
            RequestShowSelf(ShowFlags.Implicit);
        }
        catch (Exception ex)
        {
            Warn("HandleSpace/RequestShowSelf", ex);
        }
    }

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
        var clipboard = GetSystemService(ClipboardService)
            as global::Android.Content.ClipboardManager;
        var clip = clipboard?.PrimaryClip;

        if (clip is null || clip.ItemCount == 0)
            return;

        var text = clip.GetItemAt(0)?.CoerceToText(this)?.ToString();
        if (!string.IsNullOrEmpty(text))
            CurrentInputConnection?.CommitText(text, 1);
    }

    private void ShowEmojiBar()
    {
        var strip = _candidateStrip;
        if (strip is null)
            return;

        strip.RemoveAllViews();

        foreach (var emoji in EmojiShortlist)
        {
            var captured = emoji;
            var button = CreateKey(
                captured,
                () =>
                {
                    CurrentInputConnection?.CommitText(captured, 1);
                    ShowIdleTopBar();
                },
                KeyKind.Candidate);

            var parameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                Dp(38));
            parameters.SetMargins(Dp(2), Dp(1), Dp(2), Dp(1));
            strip.AddView(button, parameters);
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
        catch (Exception ex)
        {
            Warn("SurroundingText", ex);
            return (null, null);
        }
    }

    private void Apply(
        SessionResult? result,
        bool updateUi = true,
        bool finishComposition = true)
    {
        if (result is null)
            return;

        try
        {
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

                if (updateUi)
                    ShowCandidates(view);
            }
            else
            {
                if (finishComposition)
                    connection.FinishComposingText();
                _hasComposingText = false;

                if (updateUi)
                    ShowIdleTopBar();
            }
        }
        catch (Exception ex)
        {
            Warn("Apply", ex);
            _hasComposingText = false;
        }
    }

    private void ShowCandidates(CompositionView view)
    {
        try
        {
            var strip = _candidateStrip;
            if (strip is null)
                return;

            strip.RemoveAllViews();

            if (view.Converting && view.Candidates.Count > 0)
            {
                for (var i = 0; i < view.Candidates.Count; i++)
                {
                    var index = i;
                    var kind = i == view.SelectedIndex
                        ? KeyKind.CandidateSelected
                        : KeyKind.Candidate;

                    var button = CreateKey(
                        view.Candidates[i],
                        () =>
                        {
                            if (_session is not null)
                                Apply(_session.SelectCandidate(index));
                        },
                        kind);

                    var parameters = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.WrapContent,
                        Dp(38));
                    parameters.SetMargins(Dp(2), Dp(1), Dp(2), Dp(1));
                    strip.AddView(button, parameters);
                }

                return;
            }

            AddCandidateHint(
                !string.IsNullOrWhiteSpace(view.Hint)
                    ? view.Hint
                    : view.Text);
        }
        catch (Exception ex)
        {
            Warn("ShowCandidates", ex);
        }
    }

    private void ShowIdleTopBar()
    {
        try
        {
            var strip = _candidateStrip;
            if (strip is null)
                return;

            strip.RemoveAllViews();

            AddToolbarIcon(Resource.Drawable.ic_toolbar_apps, ShowInputMethodPicker, "入力方法を切り替える");
            AddToolbarIcon(Resource.Drawable.ic_toolbar_emoji, ShowEmojiBar, "絵文字");
            AddToolbarIcon(Resource.Drawable.ic_toolbar_translate, ToggleDirectMode, "入力モードを切り替える");
            AddToolbarIcon(Resource.Drawable.ic_toolbar_clipboard, PasteClipboard, "クリップボードから貼り付ける");
            AddToolbarIcon(Resource.Drawable.ic_toolbar_settings, OpenSettings, "Meltype 設定");
        }
        catch (Exception ex)
        {
            Warn("ShowIdleTopBar", ex);
        }
    }

    private void AddToolbarIcon(int iconResource, Action action, string description)
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
    }

    private void AddCandidateHint(string? text)
    {
        var strip = _candidateStrip;
        if (strip is null || string.IsNullOrWhiteSpace(text))
            return;

        var hint = new TextView(UiContext)
        {
            Text = text,
            TextSize = 14,
            Gravity = GravityFlags.CenterVertical,
            Ellipsize = TextUtils.TruncateAt.End,
            Focusable = false,
            FocusableInTouchMode = false
        };
        hint.SetMaxLines(1);
        hint.SetTextColor(SecondaryForeground);
        hint.SetPadding(Dp(10), 0, Dp(10), 0);

        strip.AddView(hint, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.MatchParent));
    }

    private void SafeRun(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Warn(operation, ex);
            _hasComposingText = false;
        }
    }

    private static void Warn(string operation, Exception ex)
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
        (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
}
