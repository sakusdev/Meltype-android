// SPDX-License-Identifier: GPL-3.0-or-later

using Android.App;
using Android.Content;
using Android.InputMethodServices;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Meltype.Composition;

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

    private MeltypeSession? _session;
    private LinearLayout? _candidateStrip;
    private TextView? _status;
    private Button? _modeButton;
    private bool _direct;
    private bool _shift;
    private bool _hasComposingText;

    public override void OnStartInput(EditorInfo? attribute, bool restarting)
    {
        base.OnStartInput(attribute, restarting);
        _session = MeltypeSession.CreateDefault(
            new AndroidFallbackConverter(),
            moreCandidates: null,
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
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(Dp(4), Dp(4), Dp(4), Dp(6));

        var candidateScroll = new HorizontalScrollView(this)
        {
            HorizontalScrollBarEnabled = false
        };
        _candidateStrip = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        candidateScroll.AddView(_candidateStrip, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent));
        root.AddView(candidateScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(44)));

        _status = new TextView(this)
        {
            Text = "Meltype",
            Gravity = GravityFlags.CenterVertical
        };
        _status.SetPadding(Dp(8), 0, Dp(8), 0);
        root.AddView(_status, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(28)));

        root.AddView(CreateCharacterRow("qwertyuiop"));
        root.AddView(CreateCharacterRow("asdfghjkl"));

        var third = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        third.AddView(CreateSpecialButton("⇧", ToggleShift), WeightedKeyParams(1.2f));
        foreach (var c in "zxcvbnm")
        {
            var captured = c;
            third.AddView(CreateSpecialButton(c.ToString(), () => HandleLetter(captured)), WeightedKeyParams());
        }
        third.AddView(CreateSpecialButton("⌫", HandleBackspace), WeightedKeyParams(1.2f));
        root.AddView(third);

        var bottom = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _modeButton = CreateSpecialButton("かな", ToggleDirectMode);
        bottom.AddView(_modeButton, WeightedKeyParams(1.2f));
        bottom.AddView(CreateSpecialButton("、", () => HandleCharacter('、')), WeightedKeyParams());
        bottom.AddView(CreateSpecialButton("。", () => HandleCharacter('。')), WeightedKeyParams());
        bottom.AddView(CreateSpecialButton("ー", () => HandleCharacter('ー')), WeightedKeyParams());
        bottom.AddView(CreateSpecialButton("Space", HandleSpace), WeightedKeyParams(3.0f));
        bottom.AddView(CreateSpecialButton("Enter", HandleEnter), WeightedKeyParams(1.6f));
        root.AddView(bottom);

        UpdateModeLabel();
        return root;
    }

    private View CreateCharacterRow(string keys)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        foreach (var c in keys)
        {
            var captured = c;
            row.AddView(CreateSpecialButton(c.ToString(), () => HandleLetter(captured)), WeightedKeyParams());
        }
        return row;
    }

    private Button CreateSpecialButton(string text, Action action)
    {
        var button = new Button(this) { Text = text, TextSize = 16 };
        button.SetAllCaps(false);
        button.Click += (_, _) => action();
        return button;
    }

    private LinearLayout.LayoutParams WeightedKeyParams(float weight = 1f)
    {
        var p = new LinearLayout.LayoutParams(0, Dp(50), weight);
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
        HandleVirtualKey(VkReturn, null, () =>
        {
            var connection = CurrentInputConnection;
            if (connection is null)
                return;

            connection.SendKeyEvent(new global::Android.Views.KeyEvent(
                global::Android.Views.KeyEventActions.Down,
                global::Android.Views.Keycode.Enter));
            connection.SendKeyEvent(new global::Android.Views.KeyEvent(
                global::Android.Views.KeyEventActions.Up,
                global::Android.Views.Keycode.Enter));
        });
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
            _status.Text = _shift ? "Shift" : "Meltype";
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
                });
                _candidateStrip.AddView(button, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.WrapContent,
                    Dp(42)));
            }
        }

        if (_status is not null)
            _status.Text = !string.IsNullOrWhiteSpace(view.Hint) ? view.Hint : view.Text;
    }

    private void ClearCandidates()
    {
        _candidateStrip?.RemoveAllViews();
        if (_status is not null)
            _status.Text = _shift ? "Shift" : "Meltype";
    }

    private int Dp(int value) =>
        (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
}
