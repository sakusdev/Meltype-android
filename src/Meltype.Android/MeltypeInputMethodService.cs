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
    private const int VkLeft = 0x25;
    private const int VkRight = 0x27;
    private const int ImeActionMask = 0x000000ff;

    private enum InputLayer
    {
        Japanese,
        Latin,
        Numbers,
        Symbols
    }

    private enum EmojiCategory
    {
        Recent,
        Smileys,
        People,
        Nature,
        Food,
        Activities,
        Travel,
        Symbols
    }

    private static readonly string[] EmojiFrequent =
    [
        "😂", "❤️", "🤣", "👍", "😭", "🙏", "😘", "🥰",
        "😍", "😊", "🎉", "🔥", "✨", "🥹", "😎", "🤔",
        "👏", "💀", "💯", "✅", "💕", "😅", "🙌", "👀"
    ];

    private static readonly string[] EmojiSmileys =
    [
        "😀", "😃", "😄", "😁", "😆", "😅", "😂", "🤣",
        "😊", "😇", "🙂", "🙃", "😉", "😍", "🥰", "😘",
        "😋", "😎", "🤓", "🧐", "🤔", "🥹", "😭", "😡"
    ];

    private static readonly string[] EmojiPeople =
    [
        "👋", "🤚", "🖐️", "✋", "👌", "🤌", "✌️", "🤞",
        "🫶", "🤟", "🤘", "👍", "👎", "👏", "🙌", "🙏",
        "💪", "🫡", "👀", "🧠", "🧑", "👩", "👨", "🧑‍💻"
    ];

    private static readonly string[] EmojiNature =
    [
        "🐶", "🐱", "🐭", "🐹", "🐰", "🦊", "🐻", "🐼",
        "🐨", "🐯", "🦁", "🐸", "🐵", "🐧", "🐦", "🦄",
        "🌸", "🌹", "🌻", "🌲", "🍀", "🌙", "⭐", "🌈"
    ];

    private static readonly string[] EmojiFood =
    [
        "🍎", "🍊", "🍋", "🍌", "🍉", "🍇", "🍓", "🍒",
        "🍔", "🍟", "🍕", "🌭", "🍿", "🍣", "🍜", "🍙",
        "🍰", "🍩", "🍪", "🍫", "☕", "🍵", "🥤", "🍺"
    ];

    private static readonly string[] EmojiActivities =
    [
        "⚽", "🏀", "🏈", "⚾", "🎾", "🏐", "🎱", "🏓",
        "🎮", "🕹️", "🎲", "🎯", "🎸", "🎹", "🎧", "🎤",
        "📷", "🎬", "🎨", "🏆", "🥇", "🚴", "🏃", "🏊"
    ];

    private static readonly string[] EmojiTravel =
    [
        "🚗", "🚕", "🚌", "🚓", "🚑", "🚒", "🚚", "🏍️",
        "🚲", "✈️", "🚀", "🚁", "🚆", "🚇", "🚢", "⛵",
        "🏠", "🏢", "🏙️", "🗼", "🗻", "🏖️", "🌍", "🗺️"
    ];

    private static readonly string[] EmojiSymbols =
    [
        "❤️", "🩷", "🧡", "💛", "💚", "💙", "💜", "🖤",
        "🤍", "💔", "💕", "💯", "✅", "❌", "⚠️", "❗",
        "❓", "‼️", "♻️", "✨", "🔥", "💫", "⭐", "🔔"
    ];

    private readonly List<(MaterialButton Button, char Character)> _letterButtons = [];
    private readonly List<string> _recentEmojis = [];
    private readonly EditorSelectionTracker _selection = new();
    private EditorInputPolicy _inputPolicy = new(false, false, false, true);
    private KeyboardOptions _keyboardOptions = new();
    private InputLayer _preferredTextLayer = InputLayer.Japanese;

    private MeltypeSession? _session;
    private IKanjiConverter? _converter;
    private MozcNativeConverter? _nativeMozc;
    private Context? _uiContext;
    private LinearLayout? _candidateStrip;
    private LinearLayout? _keyArea;
    private MaterialButton? _modeButton;
    private MaterialButton? _shiftButton;
    private MaterialButton? _spaceButton;
    private MaterialButton? _enterButton;
    private MaterialButton? _backspaceButton;
    private PopupWindow? _keyPreviewPopup;
    private TextView? _keyPreviewText;
    private ActionRunnable? _backspaceRepeatRunnable;
    private bool _backspaceRepeating;
    private bool _backspaceRepeated;
    private float _spaceLastX;
    private bool _spaceWasSwiped;
    private long _lastSpaceInputAtMs;
    private InputLayer _inputLayer = InputLayer.Japanese;
    private EmojiCategory _emojiCategory = EmojiCategory.Recent;
    private bool _emojiPanelOpen;
    private bool _direct;
    private bool _shift;
    private bool _hasComposingText;

    private Context UiContext =>
        _uiContext ??= new ContextThemeWrapper(this, Resource.Style.MeltypeTheme);

    private bool IsDarkTheme
    {
        get
        {
            if (_keyboardOptions.Theme != KeyboardTheme.System)
                return _keyboardOptions.Theme == KeyboardTheme.Dark;
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
            _keyboardOptions = AndroidSettingsStore.Load(this);
            _preferredTextLayer = AndroidSettingsStore.PreferredEnglish(this) ? InputLayer.Latin : InputLayer.Japanese;
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
        _keyArea = null;
        _modeButton = null;
        _shiftButton = null;
        StopBackspaceRepeat();
        _spaceButton = null;
        _enterButton = null;
        _backspaceButton = null;
        DismissKeyPreview();
        _keyPreviewText = null;
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
            var previousOptions = _keyboardOptions;
            _keyboardOptions = AndroidSettingsStore.Load(this);
            if (!_keyboardOptions.PersonalizedLearning) _recentEmojis.Clear();
            _inputPolicy = EditorInputPolicy.From((int)(attribute?.InputType ?? 0), (int)(attribute?.ImeOptions ?? 0));
            _selection.Reset(attribute?.InitialSelStart ?? -1, attribute?.InitialSelEnd ?? -1);
            _inputLayer = _inputPolicy.Numeric ? InputLayer.Numbers :
                _keyboardOptions.StartInEnglish(_inputPolicy, _preferredTextLayer == InputLayer.Latin)
                    ? InputLayer.Latin : InputLayer.Japanese;
            _direct = _inputLayer != InputLayer.Japanese;
            _emojiPanelOpen = false;
            _shift = false;
            DismissKeyPreview();
            CreateSession();
            _hasComposingText = false;

            UpdateEnterKey(attribute);
            if (_keyArea is not null && previousOptions != _keyboardOptions)
                SetInputView(BuildInputView());
            else
            {
                RebuildKeyArea();
                ShowIdleTopBar();
            }
        }
        catch (Exception ex)
        {
            Warn("OnStartInput", ex);
            _session = MeltypeSession.CreateDefault(
                new AndroidFallbackConverter(),
                moreCandidates: null,
                wordChecker: null,
                allowPersonalizedLearning: _keyboardOptions.CanLearn(_inputPolicy),
                settingsOverride: _keyboardOptions.ToCoreSettings());
            _session.Direct = _direct;
            _hasComposingText = false;
        }
    }

    public override void OnStartInputView(EditorInfo? info, bool restarting)
    {
        base.OnStartInputView(info, restarting);
        SafeRun("ReloadSettings", () =>
        {
            var updated = AndroidSettingsStore.Load(this);
            if (updated == _keyboardOptions) return;

            // Finish the editor's visible text without teaching the old session
            // after the user has disabled learning in the settings screen.
            _session?.ResetInputContext();
            FinishEditorComposition();
            _keyboardOptions = updated;
            if (!_keyboardOptions.PersonalizedLearning) _recentEmojis.Clear();
            CreateSession();
            SetInputView(BuildInputView());
        });
        Log.Info(LogTag,
            $"input-view start restart={restarting} package={info?.PackageName ?? "?"} inputType={(int)(info?.InputType ?? 0)} imeOptions={(int)(info?.ImeOptions ?? 0)}");
    }

    private void CreateSession()
    {
        _session = MeltypeSession.CreateDefault(
            _converter ?? new AndroidFallbackConverter(),
            _nativeMozc is null ? null : _nativeMozc.Candidates,
            wordChecker: null,
            allowPersonalizedLearning: _keyboardOptions.CanLearn(_inputPolicy),
            settingsOverride: _keyboardOptions.ToCoreSettings());
        _session.Direct = _direct;
    }

    public override void OnUpdateSelection(int oldSelStart, int oldSelEnd, int newSelStart, int newSelEnd, int candidatesStart, int candidatesEnd)
    {
        base.OnUpdateSelection(oldSelStart, oldSelEnd, newSelStart, newSelEnd, candidatesStart, candidatesEnd);
        SafeRun("OnUpdateSelection", () =>
        {
            if (!_selection.Observe(newSelStart, newSelEnd, candidatesStart, candidatesEnd))
                return;

            // The editor already moved the caret. Preserve its text rather than
            // committing an old Core buffer at the newly selected position.
            _session?.ResetInputContext();
            FinishEditorComposition();
            if (!_emojiPanelOpen) ShowIdleTopBar();
        });
    }

    public override void OnFinishInputView(bool finishingInput)
    {
        StopBackspaceRepeat();
        DismissKeyPreview();
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
        root.SetPadding(Dp(5), 0, Dp(5), Dp(38));

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
            Dp(52)));

        _keyArea = new LinearLayout(context)
        {
            Orientation = Orientation.Vertical
        };
        root.AddView(_keyArea, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent));

        RebuildKeyArea();
        ShowIdleTopBar();

        return root;
    }

    private void RebuildKeyArea()
    {
        var area = _keyArea;
        if (area is null)
            return;

        DismissKeyPreview();
        StopBackspaceRepeat();
        area.RemoveAllViews();
        _letterButtons.Clear();
        _shiftButton = null;
        _backspaceButton = null;
        _modeButton = null;
        _spaceButton = null;
        _enterButton = null;

        if (_emojiPanelOpen)
        {
            BuildEmojiKeyboard(area);
            return;
        }

        if (_inputLayer is InputLayer.Japanese or InputLayer.Latin)
            BuildTextKeyboard(area);
        else
            BuildNumberSymbolKeyboard(area);

        UpdateModeLabel();
        RefreshShiftVisual();
        UpdateEnterKey(CurrentInputEditorInfo);
    }

    private void BuildTextKeyboard(LinearLayout area)
    {
        area.AddView(CreateCharacterRow("qwertyuiop", "1234567890"));

        var second = new LinearLayout(UiContext) { Orientation = Orientation.Horizontal };
        foreach (var c in "asdfghjkl")
            second.AddView(CreateLetterKey(c), WeightedKeyParams());
        second.AddView(
            CreateKey(
                _inputLayer == InputLayer.Japanese ? "ー" : "-",
                () => HandleCharacter(_inputLayer == InputLayer.Japanese ? 'ー' : '-'),
                KeyKind.Normal),
            WeightedKeyParams());
        area.AddView(second);

        var third = new LinearLayout(UiContext) { Orientation = Orientation.Horizontal };
        _shiftButton = CreateIconKey(Resource.Drawable.ic_key_shift, ToggleShift, KeyKind.Special, "Shift");
        third.AddView(_shiftButton, WeightedKeyParams(1.34f));
        foreach (var c in "zxcvbnm")
            third.AddView(CreateLetterKey(c), WeightedKeyParams());
        AddBackspaceKey(third);
        area.AddView(third);

        area.AddView(BuildCommonBottomRow());
    }

    private void BuildNumberSymbolKeyboard(LinearLayout area)
    {
        if (_inputLayer == InputLayer.Numbers)
        {
            area.AddView(CreateDirectRow(["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"]));
            area.AddView(CreateDirectRow(["@", "#", "$", "_", "&", "-", "+", "(", ")", "/"]));

            var third = new LinearLayout(UiContext) { Orientation = Orientation.Horizontal };
            third.AddView(CreateKey("#+=", ToggleSymbolsPage, KeyKind.Special), WeightedKeyParams(1.34f));
            foreach (var symbol in new[] { "*", "\"", "'", ":", ";", "!", "?" })
                third.AddView(CreateDirectKey(symbol), WeightedKeyParams());
            AddBackspaceKey(third);
            area.AddView(third);
        }
        else
        {
            area.AddView(CreateDirectRow(["~", "\\", "|", "•", "√", "π", "÷", "×", "¶", "Δ"]));
            area.AddView(CreateDirectRow(["£", "¢", "€", "¥", "^", "°", "=", "{", "}", "%"]));

            var third = new LinearLayout(UiContext) { Orientation = Orientation.Horizontal };
            third.AddView(CreateKey("123", ToggleSymbolsPage, KeyKind.Special), WeightedKeyParams(1.34f));
            foreach (var symbol in new[] { "<", ">", "[", "]", "_", "+", "=" })
                third.AddView(CreateDirectKey(symbol), WeightedKeyParams());
            AddBackspaceKey(third);
            area.AddView(third);
        }

        area.AddView(BuildCommonBottomRow());
    }

    private LinearLayout CreateDirectRow(string[] labels)
    {
        var row = new LinearLayout(UiContext) { Orientation = Orientation.Horizontal };
        foreach (var label in labels)
            row.AddView(CreateDirectKey(label), WeightedKeyParams());
        return row;
    }

    private MaterialButton CreateDirectKey(string text) =>
        CreateKey(text, () => CommitDirectText(text), KeyKind.Normal);

    private void CommitDirectText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            if (CurrentInputConnection is null) return;
            if (_session is { IsComposing: true } session)
                Apply(session.CommitPending());
            _session?.ResetInputContext();
            CommitEditorText(text);
            if (!_emojiPanelOpen) ShowIdleTopBar();
        }
        catch (Exception ex)
        {
            Warn("CommitDirectText", ex);
        }
    }

    private void AddBackspaceKey(LinearLayout row)
    {
        _backspaceButton = CreateIconKey(
            Resource.Drawable.ic_key_backspace,
            () =>
            {
                if (_backspaceRepeated)
                {
                    _backspaceRepeated = false;
                    return;
                }

                HandleBackspace();
            },
            KeyKind.Special,
            "削除");
        AttachBackspaceRepeat(_backspaceButton);
        row.AddView(_backspaceButton, WeightedKeyParams(1.34f));
    }

    private LinearLayout BuildCommonBottomRow()
    {
        var bottom = new LinearLayout(UiContext) { Orientation = Orientation.Horizontal };

        _modeButton = CreateKey(string.Empty, CycleInputLayer, KeyKind.PillSpecial);
        bottom.AddView(_modeButton, WeightedKeyParams(1.45f));

        var (japaneseComma, japanesePeriod) = _keyboardOptions.PunctuationCharacters;
        var comma = _inputLayer == InputLayer.Japanese ? japaneseComma : ",";
        var period = _inputLayer == InputLayer.Japanese ? japanesePeriod : ".";
        bottom.AddView(CreateDirectKey(comma), WeightedKeyParams(.92f));
        bottom.AddView(
            CreateIconKey(Resource.Drawable.ic_toolbar_emoji, ShowEmojiBar, KeyKind.Special, "絵文字"),
            WeightedKeyParams(1.0f));

        _spaceButton = CreateKey(string.Empty, HandleSpace, KeyKind.Normal);
        AttachSpaceSwipe(_spaceButton);
        bottom.AddView(_spaceButton, WeightedKeyParams(2.15f));

        bottom.AddView(CreateDirectKey(period), WeightedKeyParams(.96f));
        bottom.AddView(
            CreateIconKey(Resource.Drawable.ic_key_cursor_left, () => MoveCursor(global::Android.Views.Keycode.DpadLeft), KeyKind.Special, "カーソルを左へ"),
            WeightedKeyParams(.96f));
        bottom.AddView(
            CreateIconKey(Resource.Drawable.ic_key_cursor_right, () => MoveCursor(global::Android.Views.Keycode.DpadRight), KeyKind.Special, "カーソルを右へ"),
            WeightedKeyParams(.96f));

        _enterButton = CreateIconKey(Resource.Drawable.ic_key_return, HandleEnter, KeyKind.Accent, "改行");
        bottom.AddView(_enterButton, WeightedKeyParams(1.45f));
        return bottom;
    }

    private void BuildEmojiKeyboard(LinearLayout area)
    {
        var items = GetEmojiItems(_emojiCategory);
        const int columns = 8;
        const int rows = 3;
        var index = 0;

        for (var rowIndex = 0; rowIndex < rows; rowIndex++)
        {
            var row = new LinearLayout(UiContext) { Orientation = Orientation.Horizontal };
            for (var column = 0; column < columns; column++)
            {
                if (index < items.Length)
                {
                    var emoji = items[index++];
                    var button = CreateKey(emoji, () => CommitEmoji(emoji), KeyKind.Candidate);
                    button.TextSize = 25;
                    row.AddView(button, WeightedKeyParams());
                }
                else
                {
                    row.AddView(new View(UiContext), WeightedKeyParams());
                }
            }
            area.AddView(row);
        }

        var controls = new LinearLayout(UiContext) { Orientation = Orientation.Horizontal };
        controls.AddView(CreateKey("ABC", CloseEmojiPanel, KeyKind.PillSpecial), WeightedKeyParams(1.25f));
        controls.AddView(CreateDirectKey(","), WeightedKeyParams(.8f));

        _spaceButton = CreateKey("space", HandleSpace, KeyKind.Normal);
        AttachSpaceSwipe(_spaceButton);
        controls.AddView(_spaceButton, WeightedKeyParams(3.1f));

        _backspaceButton = CreateIconKey(
            Resource.Drawable.ic_key_backspace,
            () =>
            {
                if (_backspaceRepeated)
                {
                    _backspaceRepeated = false;
                    return;
                }
                HandleBackspace();
            },
            KeyKind.Special,
            "削除");
        AttachBackspaceRepeat(_backspaceButton);
        controls.AddView(_backspaceButton, WeightedKeyParams(1.15f));

        _enterButton = CreateIconKey(Resource.Drawable.ic_key_return, HandleEnter, KeyKind.Accent, "改行");
        controls.AddView(_enterButton, WeightedKeyParams(1.25f));
        area.AddView(controls);
        UpdateEnterKey(CurrentInputEditorInfo);
    }

    private string[] GetEmojiItems(EmojiCategory category)
    {
        if (category != EmojiCategory.Recent)
        {
            return category switch
            {
                EmojiCategory.Smileys => EmojiSmileys,
                EmojiCategory.People => EmojiPeople,
                EmojiCategory.Nature => EmojiNature,
                EmojiCategory.Food => EmojiFood,
                EmojiCategory.Activities => EmojiActivities,
                EmojiCategory.Travel => EmojiTravel,
                EmojiCategory.Symbols => EmojiSymbols,
                _ => EmojiFrequent
            };
        }

        var result = new List<string>(24);
        foreach (var emoji in _recentEmojis)
        {
            if (!result.Contains(emoji))
                result.Add(emoji);
            if (result.Count == 24)
                break;
        }
        foreach (var emoji in EmojiFrequent)
        {
            if (!result.Contains(emoji))
                result.Add(emoji);
            if (result.Count == 24)
                break;
        }
        return result.ToArray();
    }

    private void CommitEmoji(string emoji)
    {
        try
        {
            CommitDirectText(emoji);
            if (_keyboardOptions.CanLearn(_inputPolicy))
            {
                _recentEmojis.Remove(emoji);
                _recentEmojis.Insert(0, emoji);
                if (_recentEmojis.Count > 24)
                    _recentEmojis.RemoveAt(_recentEmojis.Count - 1);
            }
        }
        catch (Exception ex)
        {
            Warn("CommitEmoji", ex);
        }
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
        AttachKeyPreview(button);

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
            HapticFeedbackEnabled = _keyboardOptions.KeyVibration,
            SoundEffectsEnabled = _keyboardOptions.KeySound,
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
                    SafeHaptic(button);
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

    private MaterialButton CreateIconKey(
        int iconResource,
        Action action,
        KeyKind kind,
        string description)
    {
        var button = CreateKey(string.Empty, action, kind);
        button.ContentDescription = description;
        SetKeyIcon(
            button,
            iconResource,
            kind is KeyKind.Accent && !IsDarkTheme ? OnPrimary : KeyForeground);
        return button;
    }

    private void SetKeyIcon(MaterialButton button, int iconResource, Color tint)
    {
        try
        {
            var icon = UiContext.GetDrawable(iconResource)?.Mutate();
            if (icon is null)
                return;

            icon.SetTint(tint);
            var size = Dp(24);
            icon.SetBounds(0, 0, size, size);
            button.SetCompoundDrawables(icon, null, null, null);
            button.CompoundDrawablePadding = 0;
            button.SetPadding(0, 0, 0, 0);
        }
        catch (Exception ex)
        {
            Warn("SetKeyIcon", ex);
        }
    }

    private void ApplyKeyAppearance(MaterialButton button, KeyKind kind)
    {
        var background = kind switch
        {
            KeyKind.Special or KeyKind.PillSpecial => SpecialKeyBackground,
            KeyKind.Accent => IsDarkTheme ? SpecialKeyBackground : Primary,
            KeyKind.Candidate => KeyboardBackground,
            KeyKind.CandidateSelected => CandidateBackground,
            KeyKind.Toolbar => KeyboardBackground,
            _ => KeyBackground
        };

        var foreground = kind switch
        {
            KeyKind.Accent => IsDarkTheme ? KeyForeground : OnPrimary,
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
        var p = new LinearLayout.LayoutParams(0, Dp(_keyboardOptions.KeyHeightDp), weight);
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

        if (_direct)
        {
            CommitEditorText(c.ToString());
            return;
        }

        var session = _session;
        if (session is null)
        {
            CommitEditorText(c.ToString());
            return;
        }

        var vk = char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : c <= 0x7f ? c : 0;
        var (before, after) = SurroundingText();
        var result = session.HandleKey(vk, c, _shift, false, false, false, before, after);
        Apply(result);

        if (!result.Consumed)
            CommitEditorText(c.ToString());
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
                CommitEditorText(" ");
                if (!_emojiPanelOpen) ShowIdleTopBar();
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
                    CommitEditorText(" ");
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
            if (OperatingSystem.IsAndroidVersionAtLeast(28))
                RequestShowSelf(ShowFlags.Implicit);
            else
                ShowWindow(true);
        }
        catch (Exception ex)
        {
            Warn("HandleSpace/RequestShowSelf", ex);
        }
    }

    private void HandleBackspace() =>
        HandleVirtualKey(VkBack, null, DeleteEditorText);

    private void DeleteEditorText()
    {
        var connection = CurrentInputConnection;
        if (connection is null) return;

        if (!string.IsNullOrEmpty(connection.GetSelectedText((GetTextFlags)0)))
        {
            CommitEditorText(string.Empty);
            return;
        }

        var before = connection.GetTextBeforeCursor(128, (GetTextFlags)0);
        if (before is not null)
        {
            var length = EditorText.BackspaceLength(before);
            if (length == 0) return;
            _selection.DeleteBefore(length);
            if (connection.DeleteSurroundingText(length, 0)) return;
        }

        connection.SendKeyEvent(new KeyEvent(KeyEventActions.Down, global::Android.Views.Keycode.Del));
        connection.SendKeyEvent(new KeyEvent(KeyEventActions.Up, global::Android.Views.Keycode.Del));
    }

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

    private void MoveCursor(global::Android.Views.Keycode keycode, bool selectClause = true)
    {
        if (selectClause && !_direct && _session is { IsComposing: true })
        {
            HandleVirtualKey(keycode == global::Android.Views.Keycode.DpadLeft ? VkLeft : VkRight,
                null, () => MoveEditorCursor(keycode));
            return;
        }

        MoveEditorCursor(keycode);
    }

    private void MoveEditorCursor(global::Android.Views.Keycode keycode)
    {
        var connection = CurrentInputConnection;
        if (connection is null)
            return;

        if (_session is { IsComposing: true } session)
            Apply(session.CommitPending());
        _session?.ResetInputContext();
        FinishEditorComposition();
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

            if (!_keyboardOptions.SpaceSwipe)
            {
                _spaceWasSwiped = false;
                return;
            }

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
                            MoveCursor(keycode, selectClause: false);

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
            if (!_keyboardOptions.BackspaceRepeat)
            {
                StopBackspaceRepeat();
                _backspaceRepeated = false;
                return;
            }
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

    private void AttachKeyPreview(MaterialButton button)
    {
        button.Touch += (_, e) =>
        {
            try
            {
                switch (e.Event?.Action)
                {
                    case MotionEventActions.Down:
                        ShowKeyPreview(button);
                        break;
                    case MotionEventActions.Up:
                    case MotionEventActions.Cancel:
                        DismissKeyPreview();
                        break;
                }
            }
            catch (Exception ex)
            {
                Warn("KeyPreviewTouch", ex);
                DismissKeyPreview();
            }

            e.Handled = false;
        };
    }

    private void ShowKeyPreview(MaterialButton button)
    {
        if (_inputPolicy.Sensitive || !_keyboardOptions.KeyPreview) return;
        DismissKeyPreview();

        var label = new TextView(UiContext)
        {
            Text = button.Text,
            TextSize = 27,
            Gravity = GravityFlags.Center,
            Focusable = false,
            FocusableInTouchMode = false
        };
        label.Typeface = global::Android.Graphics.Typeface.Create(
            "sans-serif-medium",
            global::Android.Graphics.TypefaceStyle.Normal);
        label.SetTextColor(KeyForeground);

        var background = new global::Android.Graphics.Drawables.GradientDrawable();
        background.SetColor(SpecialKeyBackground);
        background.SetCornerRadius(Dp(9));
        label.Background = background;

        var width = Math.Max(button.Width, Dp(46));
        var height = Dp(58);
        var popup = new PopupWindow(label, width, height, false)
        {
            Focusable = false,
            OutsideTouchable = false
        };

        _keyPreviewText = label;
        _keyPreviewPopup = popup;
        popup.ShowAsDropDown(button, 0, -(button.Height + height + Dp(5)));
    }

    private void DismissKeyPreview()
    {
        try
        {
            _keyPreviewPopup?.Dismiss();
        }
        catch (Exception ex)
        {
            Warn("DismissKeyPreview", ex);
        }
        finally
        {
            _keyPreviewPopup = null;
            _keyPreviewText = null;
        }
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
        if (!_keyboardOptions.KeyVibration) return;
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
        SetKeyIcon(
            _shiftButton,
            Resource.Drawable.ic_key_shift,
            _shift ? OnPrimaryContainer : KeyForeground);
    }

    private void CycleInputLayer()
    {
        var next = _inputLayer switch
        {
            InputLayer.Japanese => InputLayer.Latin,
            InputLayer.Latin => InputLayer.Numbers,
            InputLayer.Numbers => InputLayer.Japanese,
            InputLayer.Symbols => InputLayer.Japanese,
            _ => InputLayer.Japanese
        };
        SetInputLayer(next);
    }

    private void ToggleSymbolsPage() =>
        SetInputLayer(_inputLayer == InputLayer.Symbols ? InputLayer.Numbers : InputLayer.Symbols);

    private void SetInputLayer(InputLayer layer)
    {
        if (_session is { IsComposing: true } session)
            Apply(session.CommitPending());

        if (_inputPolicy.Sensitive && layer == InputLayer.Japanese)
            layer = _inputPolicy.Numeric ? InputLayer.Numbers : InputLayer.Latin;
        if (!_inputPolicy.Direct && layer is InputLayer.Japanese or InputLayer.Latin)
        {
            _preferredTextLayer = layer;
            AndroidSettingsStore.SavePreferredEnglish(this, layer == InputLayer.Latin);
        }
        _emojiPanelOpen = false;
        _inputLayer = layer;
        _direct = layer != InputLayer.Japanese;
        _shift = false;

        if (_session is not null)
            _session.Direct = _direct;

        RebuildKeyArea();
        ShowIdleTopBar();
    }

    private void UpdateModeLabel()
    {
        if (_modeButton is not null)
        {
            var (text, description) = _inputLayer switch
            {
                InputLayer.Japanese => ("ABC", "英字入力へ"),
                InputLayer.Latin => ("123", "数字入力へ"),
                InputLayer.Numbers => ("あ", "日本語入力へ"),
                InputLayer.Symbols => ("あ", "日本語入力へ"),
                _ => ("ABC", "入力モード切替")
            };
            _modeButton.Text = text;
            _modeButton.ContentDescription = description;
        }

        if (_spaceButton is not null)
        {
            _spaceButton.Text = _inputLayer switch
            {
                InputLayer.Japanese => "日本語",
                InputLayer.Latin => "English",
                _ => "space"
            };
        }
    }

    private void UpdateEnterKey(EditorInfo? editor)
    {
        if (_enterButton is null)
            return;

        var action = editor is null ? 0 : ((int)editor.ImeOptions & ImeActionMask);
        var (icon, description) = action switch
        {
            2 => (Resource.Drawable.ic_key_arrow_forward, "移動"),
            3 => (Resource.Drawable.ic_key_search, "検索"),
            4 => (Resource.Drawable.ic_key_send, "送信"),
            5 => (Resource.Drawable.ic_key_arrow_forward, "次へ"),
            6 => (Resource.Drawable.ic_key_done, "完了"),
            7 => (Resource.Drawable.ic_key_cursor_left, "前へ"),
            _ => (Resource.Drawable.ic_key_return, "改行")
        };

        _enterButton.Text = string.Empty;
        _enterButton.ContentDescription = description;
        SetKeyIcon(
            _enterButton,
            icon,
            IsDarkTheme ? KeyForeground : OnPrimary);
    }

    private void ShowInputMethodPicker()
    {
        var manager = GetSystemService(InputMethodService) as InputMethodManager;
        manager?.ShowInputMethodPicker();
    }

    private void OpenSettings()
    {
        var intent = new Intent(this, typeof(SettingsActivity));
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
            CommitDirectText(text);
    }

    private void ShowEmojiBar()
    {
        if (_emojiPanelOpen)
        {
            CloseEmojiPanel();
            return;
        }

        if (_session is { IsComposing: true } session)
            Apply(session.CommitPending());

        _emojiPanelOpen = true;
        _emojiCategory = EmojiCategory.Recent;
        ShowEmojiCategoryBar();
        RebuildKeyArea();
    }

    private void CloseEmojiPanel()
    {
        _emojiPanelOpen = false;
        RebuildKeyArea();
        ShowIdleTopBar();
    }

    private void ShowEmojiCategoryBar()
    {
        var strip = _candidateStrip;
        if (strip is null)
            return;

        strip.RemoveAllViews();
        AddEmojiCategory("🕘", EmojiCategory.Recent, "最近");
        AddEmojiCategory("😀", EmojiCategory.Smileys, "顔");
        AddEmojiCategory("👋", EmojiCategory.People, "人");
        AddEmojiCategory("🐻", EmojiCategory.Nature, "自然");
        AddEmojiCategory("🍔", EmojiCategory.Food, "食べ物");
        AddEmojiCategory("⚽", EmojiCategory.Activities, "アクティビティ");
        AddEmojiCategory("🚗", EmojiCategory.Travel, "乗り物");
        AddEmojiCategory("♥", EmojiCategory.Symbols, "記号");
    }

    private void AddEmojiCategory(string label, EmojiCategory category, string description)
    {
        var strip = _candidateStrip;
        if (strip is null)
            return;

        var kind = category == _emojiCategory
            ? KeyKind.CandidateSelected
            : KeyKind.Candidate;
        var button = CreateKey(label, () => SelectEmojiCategory(category), kind);
        button.TextSize = 20;
        button.ContentDescription = description;
        var p = new LinearLayout.LayoutParams(Dp(42), Dp(42));
        p.SetMargins(Dp(1), Dp(1), Dp(1), Dp(1));
        strip.AddView(button, p);
    }

    private void SelectEmojiCategory(EmojiCategory category)
    {
        _emojiCategory = category;
        ShowEmojiCategoryBar();
        RebuildKeyArea();
    }

    private (string? Before, string? After) SurroundingText()
    {
        if (_hasComposingText || _inputPolicy.Sensitive)
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

        var connection = CurrentInputConnection;
        if (connection is null) return;
        var batchStarted = false;
        try
        {
            batchStarted = connection.BeginBatchEdit();

            foreach (var edit in result.Commits)
            {
                if (edit.DeleteBefore > 0)
                {
                    _selection.DeleteBefore(edit.DeleteBefore);
                    connection.DeleteSurroundingText(edit.DeleteBefore, 0);
                }

                if (!string.IsNullOrEmpty(edit.Text))
                    CommitEditorText(edit.Text);

                _hasComposingText = false;
            }

            if (result.View is { } view)
            {
                _selection.Replace(view.Text.Length, composing: view.Text.Length > 0);
                connection.SetComposingText(view.Text, 1);
                _hasComposingText = view.Text.Length > 0;

                if (updateUi)
                    ShowCandidates(view);
            }
            else
            {
                if (finishComposition)
                    FinishEditorComposition();
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
        finally
        {
            if (batchStarted)
                SafeRun("Apply/EndBatchEdit", () => connection.EndBatchEdit());
        }
    }

    private void CommitEditorText(string text)
    {
        var connection = CurrentInputConnection;
        if (connection is null) return;
        _selection.Replace(text.Length, composing: false);
        connection.CommitText(text, 1);
        _hasComposingText = false;
    }

    private void FinishEditorComposition()
    {
        _selection.FinishComposition();
        CurrentInputConnection?.FinishComposingText();
        _hasComposingText = false;
    }

    private void ShowCandidates(CompositionView view)
    {
        if (_inputPolicy.Sensitive)
        {
            ShowIdleTopBar();
            return;
        }
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
            AddToolbarSpacer();
            AddToolbarIcon(Resource.Drawable.ic_toolbar_emoji, ShowEmojiBar, "絵文字");
            AddToolbarSpacer();
            AddToolbarIcon(Resource.Drawable.ic_toolbar_translate, CycleInputLayer, "入力モードを切り替える");
            AddToolbarSpacer();
            AddToolbarIcon(Resource.Drawable.ic_toolbar_clipboard, PasteClipboard, "クリップボードから貼り付ける");
            AddToolbarSpacer();
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
            HapticFeedbackEnabled = _keyboardOptions.KeyVibration,
            SoundEffectsEnabled = _keyboardOptions.KeySound
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
                        SafeHaptic(button);
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
            Dp(44),
            Dp(44));
        strip.AddView(button, parameters);
    }

    private void AddToolbarSpacer()
    {
        var strip = _candidateStrip;
        if (strip is null)
            return;

        strip.AddView(new View(UiContext), new LinearLayout.LayoutParams(
            0,
            Dp(1),
            1f));
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
