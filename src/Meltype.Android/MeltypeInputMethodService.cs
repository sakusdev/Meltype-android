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

    private readonly List<(MaterialButton Button, char Character)> _letterButtons = [];

    private MeltypeSession? _session;
    private IKanjiConverter? _converter;
    private MozcNativeConverter? _nativeMozc;
    private Context? _uiContext;
    private LinearLayout? _candidateStrip;
    private MaterialButton? _modeButton;
    private MaterialButton? _shiftButton;
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

    // Material 3 baseline palette. The app itself is Theme.Material3; these
    // explicit keyboard tokens keep key contrast stable inside the IME window.
    private Color KeyboardBackground => Color.ParseColor(IsDarkTheme ? "#1D1B20" : "#F7F2FA");
    private Color KeyBackground => Color.ParseColor(IsDarkTheme ? "#36343B" : "#FFFFFF");
    private Color SpecialKeyBackground => Color.ParseColor(IsDarkTheme ? "#49454F" : "#E7E0EC");
    private Color Primary => Color.ParseColor(IsDarkTheme ? "#D0BCFF" : "#6750A4");
    private Color OnPrimary => Color.ParseColor(IsDarkTheme ? "#381E72" : "#FFFFFF");
    private Color PrimaryContainer => Color.ParseColor(IsDarkTheme ? "#4F378B" : "#EADDFF");
    private Color OnPrimaryContainer => Color.ParseColor(IsDarkTheme ? "#EADDFF" : "#21005D");
    private Color KeyForeground => Color.ParseColor(IsDarkTheme ? "#E6E0E9" : "#1D1B20");
    private Color SecondaryForeground => Color.ParseColor(IsDarkTheme ? "#CAC4D0" : "#49454F");
    private Color CandidateBackground => Color.ParseColor(IsDarkTheme ? "#2B2930" : "#F0EAF2");

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
        ShowIdleCandidateBar();
    }

    public override void OnFinishInput()
    {
        if (_session is { IsComposing: true })
            Apply(_session.CommitPending());

        _session = null;
        _hasComposingText = false;
        ShowIdleCandidateBar();
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
        // Reserve space above Android's gesture bar / IME-switcher affordance.
        root.SetPadding(Dp(5), Dp(3), Dp(7), Dp(22));

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
        _candidateStrip.SetPadding(Dp(5), Dp(3), Dp(5), Dp(3));
        candidateScroll.AddView(_candidateStrip, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.MatchParent));
        root.AddView(candidateScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(50)));

        root.AddView(CreateCharacterRow("qwertyuiop"));

        var second = CreateCharacterRow("asdfghjkl");
        second.SetPadding(Dp(14), 0, Dp(14), 0);
        root.AddView(second);

        var third = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        _shiftButton = CreateKey("⇧", ToggleShift, KeyKind.Special);
        third.AddView(_shiftButton, WeightedKeyParams(1.32f));
        foreach (var c in "zxcvbnm")
            third.AddView(CreateLetterKey(c), WeightedKeyParams());
        third.AddView(CreateKey("⌫", HandleBackspace, KeyKind.Special), WeightedKeyParams(1.32f));
        root.AddView(third);

        var bottom = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        _modeButton = CreateKey("あ", ToggleDirectMode, KeyKind.Special);
        bottom.AddView(_modeButton, WeightedKeyParams(1.2f));
        bottom.AddView(CreateKey("、", () => HandleCharacter('、'), KeyKind.Special), WeightedKeyParams(.92f));
        bottom.AddView(CreateKey("ー", () => HandleCharacter('ー'), KeyKind.Special), WeightedKeyParams(.92f));
        bottom.AddView(CreateKey("space", HandleSpace, KeyKind.Normal), WeightedKeyParams(3.55f));
        bottom.AddView(CreateKey("。", () => HandleCharacter('。'), KeyKind.Special), WeightedKeyParams(.92f));
        _enterButton = CreateKey("↵", HandleEnter, KeyKind.Accent);
        bottom.AddView(_enterButton, WeightedKeyParams(1.38f));
        root.AddView(bottom);

        UpdateModeLabel();
        RefreshShiftVisual();
        UpdateEnterKey(CurrentInputEditorInfo);
        ShowIdleCandidateBar();
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
        var row = new LinearLayout(UiContext) { Orientation = Orientation.Horizontal };
        foreach (var c in keys)
            row.AddView(CreateLetterKey(c), WeightedKeyParams());
        return row;
    }

    private MaterialButton CreateLetterKey(char character)
    {
        var button = CreateKey(character.ToString(), () => HandleLetter(character));
        _letterButtons.Add((button, character));
        return button;
    }

    private enum KeyKind
    {
        Normal,
        Special,
        Accent,
        Candidate,
        CandidateSelected
    }

    private MaterialButton CreateKey(string text, Action action, KeyKind kind = KeyKind.Normal)
    {
        var button = new MaterialButton(UiContext)
        {
            Text = text,
            TextSize = kind is KeyKind.Candidate or KeyKind.CandidateSelected ? 15 : 17,
            Gravity = GravityFlags.Center,
            Elevation = 0
        };
        button.SetAllCaps(false);
        button.SetPadding(Dp(kind is KeyKind.Candidate or KeyKind.CandidateSelected ? 12 : 4), 0,
            Dp(kind is KeyKind.Candidate or KeyKind.CandidateSelected ? 12 : 4), 0);
        ApplyKeyAppearance(button, kind);
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
        button.CornerRadius = Dp(kind is KeyKind.Candidate or KeyKind.CandidateSelected ? 20 : 12);
    }

    private LinearLayout.LayoutParams WeightedKeyParams(float weight = 1f)
    {
        var p = new LinearLayout.LayoutParams(0, Dp(52), weight);
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

        // Custom editor actions are not dispatched by SendDefaultEditorAction.
        if (editor is not null && editor.ActionId > 0 && editor.ActionLabel is not null)
        {
            if (connection.PerformEditorAction((ImeAction)editor.ActionId))
                return;
        }

        // Let InputMethodService resolve Search/Send/Next/Done/etc and respect
        // IME_FLAG_NO_ENTER_ACTION. This is the canonical Android IME path.
        if (SendDefaultEditorAction(true))
            return;

        // For multiline / no-action editors, Android decides whether newline is
        // committed as text or delivered as a soft-keyboard Enter event.
        SendKeyChar('\n');
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
            ShowIdleCandidateBar();
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
        ShowIdleCandidateBar();
    }

    private void UpdateModeLabel()
    {
        if (_modeButton is null)
            return;

        _modeButton.Text = _direct ? "ABC" : "あ";
        _modeButton.ContentDescription = _direct ? "英字直接入力" : "Meltype 日本語入力";
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
            ShowIdleCandidateBar();
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
                    Dp(42));
                parameters.SetMargins(Dp(3), Dp(1), Dp(3), Dp(1));
                _candidateStrip.AddView(button, parameters);
            }
            return;
        }

        AddCandidateHint(!string.IsNullOrWhiteSpace(view.Hint) ? view.Hint : view.Text);
    }

    private void ShowIdleCandidateBar()
    {
        if (_candidateStrip is null)
            return;

        _candidateStrip.RemoveAllViews();
        var mode = _direct ? "ABC" : "あ";
        var engine = _nativeMozc is null ? "fallback" : "Mozc";
        AddCandidateHint(_shift ? $"{mode} · Shift" : $"{mode} · {engine}");
    }

    private void AddCandidateHint(string? text)
    {
        if (_candidateStrip is null || string.IsNullOrWhiteSpace(text))
            return;

        var hint = new TextView(UiContext)
        {
            Text = text,
            TextSize = 13,
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
