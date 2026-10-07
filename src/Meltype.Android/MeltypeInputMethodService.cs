// SPDX-License-Identifier: GPL-3.0-or-later

using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.InputMethodServices;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Meltype.Composition;
using Color = Android.Graphics.Color;

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

    // Android InputType constants. Keeping these local avoids depending on
    // framework enum naming differences between .NET for Android versions.
    private const int InputTypeMaskClass = 0x0000000f;
    private const int InputTypeClassText = 0x00000001;
    private const int InputTypeTextFlagMultiLine = 0x00020000;
    private const int ImeActionMask = 0x000000ff;

    private MeltypeSession? _session;
    private IKanjiConverter? _converter;
    private MozcNativeConverter? _nativeMozc;
    private LinearLayout? _candidateStrip;
    private TextView? _status;
    private Button? _modeButton;
    private bool _direct;
    private bool _shift;
    private bool _hasComposingText;

    private bool IsDarkTheme
    {
        get
        {
            // UI_MODE_NIGHT_MASK = 0x30, UI_MODE_NIGHT_YES = 0x20.
            var mode = (int)(Resources?.Configuration?.UiMode ?? 0);
            return (mode & 0x30) == 0x20;
        }
    }

    private Color KeyboardBackground => Color.ParseColor(IsDarkTheme ? "#202124" : "#E8EAED");
    private Color KeyBackground => Color.ParseColor(IsDarkTheme ? "#303134" : "#F8F9FA");
    private Color SpecialKeyBackground => Color.ParseColor(IsDarkTheme ? "#3C4043" : "#DADCE0");
    private Color KeyForeground => Color.ParseColor(IsDarkTheme ? "#E8EAED" : "#202124");
    private Color SecondaryForeground => Color.ParseColor(IsDarkTheme ? "#BDC1C6" : "#5F6368");
    private Color AccentBackground => Color.ParseColor(IsDarkTheme ? "#8AB4F8" : "#1A73E8");
    private Color AccentForeground => Color.ParseColor(IsDarkTheme ? "#202124" : "#FFFFFF");

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
        ClearCandidates();
    }

    public override void OnFinishInput()
    {
        if (_session is { IsComposing: true })
            Apply(_session.CommitPending());

        _session = null;
        _hasComposingText = false;
        ClearCandidates();
        base.OnFinishInput();
    }

    public override View? OnCreateInputView()
    {
        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Background = SolidRounded(KeyboardBackground, 0)
        };

        // Keep the bottom row clear of Android's gesture bar / IME switcher.
        // On gesture-navigation devices the system keyboard-switch globe can
        // otherwise overlap the Enter key and make a tap switch back to Gboard.
        root.SetPadding(Dp(5), Dp(4), Dp(5), Dp(28));

        var candidateScroll = new HorizontalScrollView(this)
        {
            HorizontalScrollBarEnabled = false,
            FillViewport = true
        };
        _candidateStrip = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        _candidateStrip.SetGravity(GravityFlags.CenterVertical);
        _candidateStrip.SetPadding(Dp(3), Dp(2), Dp(3), Dp(2));
        candidateScroll.AddView(_candidateStrip, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.MatchParent));
        root.AddView(candidateScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(46)));

        _status = new TextView(this)
        {
            Text = BaseStatus(),
            TextSize = 11,
            Gravity = GravityFlags.CenterVertical,
            Ellipsize = TextUtils.TruncateAt.End
        };
        _status.SetMaxLines(1);
        _status.SetTextColor(SecondaryForeground);
        _status.SetPadding(Dp(10), 0, Dp(10), 0);
        root.AddView(_status, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(22)));

        root.AddView(CreateCharacterRow("qwertyuiop"));

        var second = CreateCharacterRow("asdfghjkl");
        second.SetPadding(Dp(13), 0, Dp(13), 0);
        root.AddView(second);

        var third = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        third.AddView(CreateSpecialButton("⇧", ToggleShift, KeyKind.Special), WeightedKeyParams(1.28f));
        foreach (var c in "zxcvbnm")
        {
            var captured = c;
            third.AddView(CreateSpecialButton(c.ToString(), () => HandleLetter(captured)), WeightedKeyParams());
        }
        third.AddView(CreateSpecialButton("⌫", HandleBackspace, KeyKind.Special), WeightedKeyParams(1.28f));
        root.AddView(third);

        var bottom = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _modeButton = CreateSpecialButton("かな", ToggleDirectMode, KeyKind.Special);
        bottom.AddView(_modeButton, WeightedKeyParams(1.28f));
        bottom.AddView(CreateSpecialButton("、", () => HandleCharacter('、'), KeyKind.Special), WeightedKeyParams());
        bottom.AddView(CreateSpecialButton("。", () => HandleCharacter('。'), KeyKind.Special), WeightedKeyParams());
        bottom.AddView(CreateSpecialButton("ー", () => HandleCharacter('ー'), KeyKind.Special), WeightedKeyParams());
        bottom.AddView(CreateSpecialButton("space", HandleSpace), WeightedKeyParams(3.0f));
        bottom.AddView(CreateSpecialButton("↵", HandleEnter, KeyKind.Accent), WeightedKeyParams(1.55f));
        root.AddView(bottom);

        UpdateModeLabel();
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

    private LinearLayout CreateCharacterRow(string keys)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        foreach (var c in keys)
        {
            var captured = c;
            row.AddView(CreateSpecialButton(c.ToString(), () => HandleLetter(captured)), WeightedKeyParams());
        }
        return row;
    }

    private enum KeyKind
    {
        Normal,
        Special,
        Accent,
        Candidate
    }

    private Button CreateSpecialButton(string text, Action action, KeyKind kind = KeyKind.Normal)
    {
        var button = new Button(this)
        {
            Text = text,
            TextSize = kind == KeyKind.Candidate ? 15 : 17,
            Gravity = GravityFlags.Center,
            Elevation = 0
        };
        button.SetAllCaps(false);
        button.SetPadding(Dp(4), 0, Dp(4), 0);

        var background = kind switch
        {
            KeyKind.Special => SpecialKeyBackground,
            KeyKind.Accent => AccentBackground,
            KeyKind.Candidate => IsDarkTheme ? Color.ParseColor("#292A2D") : Color.ParseColor("#FFFFFF"),
            _ => KeyBackground
        };
        var foreground = kind == KeyKind.Accent ? AccentForeground : KeyForeground;
        button.Background = SolidRounded(background, kind == KeyKind.Candidate ? Dp(18) : Dp(7));
        button.SetTextColor(foreground);
        button.Click += (_, _) => action();
        return button;
    }

    private GradientDrawable SolidRounded(Color color, int radius)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(color);
        drawable.SetCornerRadius(radius);
        return drawable;
    }

    private LinearLayout.LayoutParams WeightedKeyParams(float weight = 1f)
    {
        var p = new LinearLayout.LayoutParams(0, Dp(50), weight);
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
            RefreshShiftStatus();
        }
    }

    private void HandleCharacter(char c)
    {
        var session = _session;
        var connection = CurrentInputConnection;
        if (session is null || connection is null)
            return;

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

    private void HandleEnter()
    {
        HandleVirtualKey(VkReturn, null, PerformEditorEnter);
    }

    private void PerformEditorEnter()
    {
        var connection = CurrentInputConnection;
        if (connection is null)
            return;

        var editor = CurrentInputEditorInfo;
        var inputType = editor is null ? 0 : (int)editor.InputType;
        var isMultilineText =
            (inputType & InputTypeMaskClass) == InputTypeClassText &&
            (inputType & InputTypeTextFlagMultiLine) != 0;

        // Multiline editors should receive a real newline rather than an editor action.
        if (isMultilineText)
        {
            connection.CommitText("\n", 1);
            return;
        }

        var imeOptions = editor is null ? 0 : (int)editor.ImeOptions;
        var actionId = imeOptions & ImeActionMask;

        // IME_ACTION_GO..PREVIOUS are 2..7. These are the actions expected by
        // search bars, chat send fields, next/done forms, etc.
        if (actionId is >= 2 and <= 7)
        {
            connection.PerformEditorAction((ImeAction)actionId);
            return;
        }

        // No explicit action: behave like a normal Enter key without injecting
        // raw key events, which are less reliable across Android applications.
        connection.CommitText("\n", 1);
    }

    private void HandleVirtualKey(int vk, char? ch, Action fallback)
    {
        var session = _session;
        if (session is null)
            return;

        var (before, after) = SurroundingText();
        var result = session.HandleKey(vk, ch, false, false, false, false, before, after);
        Apply(result);
        if (!result.Consumed)
            fallback();
    }

    private void ToggleShift()
    {
        _shift = !_shift;
        RefreshShiftStatus();
    }

    private void RefreshShiftStatus()
    {
        if (_status is not null && !_hasComposingText)
            _status.Text = _shift ? "Shift" : BaseStatus();
    }

    private void ToggleDirectMode()
    {
        if (_session is { IsComposing: true } session)
            Apply(session.CommitPending());

        _direct = !_direct;
        if (_session is not null)
            _session.Direct = _direct;
        UpdateModeLabel();
    }

    private void UpdateModeLabel()
    {
        if (_modeButton is not null)
            _modeButton.Text = _direct ? "ABC" : "かな";
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
            ClearCandidates();
        }
    }

    private void ShowCandidates(CompositionView view)
    {
        if (_candidateStrip is null)
            return;

        _candidateStrip.RemoveAllViews();
        if (view.Converting && view.Candidates.Count > 1)
        {
            for (var i = 0; i < view.Candidates.Count; i++)
            {
                var index = i;
                var button = CreateSpecialButton(view.Candidates[i], () =>
                {
                    if (_session is not null)
                        Apply(_session.SelectCandidate(index));
                }, KeyKind.Candidate);

                var parameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent,
                    Dp(38));
                parameters.SetMargins(Dp(3), Dp(2), Dp(3), Dp(2));
                _candidateStrip.AddView(button, parameters);
            }
        }

        if (_status is not null)
            _status.Text = !string.IsNullOrWhiteSpace(view.Hint) ? view.Hint : view.Text;
    }

    private void ClearCandidates()
    {
        _candidateStrip?.RemoveAllViews();
        if (_status is not null)
            _status.Text = _shift ? "Shift" : BaseStatus();
    }

    private string BaseStatus() => _nativeMozc is null ? "Meltype · fallback" : "Meltype · Mozc";

    private int Dp(int value) =>
        (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
}
