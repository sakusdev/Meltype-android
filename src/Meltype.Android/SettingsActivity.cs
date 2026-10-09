// SPDX-License-Identifier: GPL-3.0-or-later

using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using Google.Android.Material.Button;
using Meltype.Config;

namespace Meltype.Android;

[Activity(Name = "org.sakusdev.meltype.android.SettingsActivity", Label = "Meltype の設定",
    Exported = true, Theme = "@style/MeltypeTheme")]
public sealed class SettingsActivity : Activity
{
    private KeyboardOptions _options = new();
    private LinearLayout _root = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _options = AndroidSettingsStore.Load(this);
        var scroll = new ScrollView(this) { FillViewport = true };
        _root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _root.SetPadding(Dp(20), Dp(28), Dp(20), Dp(32));
        scroll.AddView(_root);
        _root.AddView(new TextView(this) { Text = "Meltype の設定", TextSize = 26 });
        _root.AddView(new TextView(this)
        {
            Text = "変更は自動で保存され、キーボードを開き直すと反映されます。",
            TextSize = 14
        }, RowParams());

        Section("見た目");
        Choice("キーボードの配色", "キーボードに使うライト・ダークの配色です。",
            ["端末に合わせる", "ライト", "ダーク"], (int)_options.Theme,
            index => Save(_options with { Theme = (KeyboardTheme)index }));
        var heights = new[] { 40, 48, 56, 64 };
        Choice("キーの高さ", "キーとキーボード全体の高さを調整します。",
            ["コンパクト · 40 dp", "標準 · 48 dp", "大きめ · 56 dp", "最大 · 64 dp"],
            Math.Max(0, Array.IndexOf(heights, _options.KeyHeightDp)),
            index => Save(_options with { KeyHeightDp = heights[index] }));
        Toggle("キーのプレビュー", "押した文字をキーの上に拡大表示します。パスワード欄では表示しません。",
            _options.KeyPreview, value => Save(_options with { KeyPreview = value }));

        Section("キー操作");
        Toggle("キーの振動", "キーを押したときに振動します。端末の振動設定にも従います。",
            _options.KeyVibration, value => Save(_options with { KeyVibration = value }));
        Toggle("キーの操作音", "キーを離したときに端末の操作音を鳴らします。端末の操作音設定にも従います。",
            _options.KeySound, value => Save(_options with { KeySound = value }));
        Toggle("Space のスワイプでカーソル移動", "Space を左右に滑らせて入力欄のカーソルを動かします。",
            _options.SpaceSwipe, value => Save(_options with { SpaceSwipe = value }));
        Toggle("削除キーの長押しで連続削除", "Backspace を長押しすると文字を続けて削除します。",
            _options.BackspaceRepeat, value => Save(_options with { BackspaceRepeat = value }));

        Section("入力と変換");
        Choice("テキスト欄の初期モード", "通常のテキスト欄で使います。パスワード・数字・URL などは入力欄に合わせます。",
            ["前回のモード", "日本語", "英字"], (int)_options.InitialMode,
            index => Save(_options with { InitialMode = (InitialInputMode)index }));
        Toggle("ライブ変換", "Space を押す前に、入力中の読みを漢字に変換して表示します。",
            _options.LiveConversion, value => Save(_options with { LiveConversion = value }));
        Choice("英語と日本語の判定", "英語らしい語を英字のまま入力する判定の強さです。",
            ["積極的", "標準", "慎重", "手動（提案のみ）"], (int)_options.Detection,
            index => Save(_options with { Detection = (DetectionLevel)index }));
        Choice("日本語の句読点", "日本語モードの句読点キーと変換に使います。",
            ["、。", "，．", "，。", "、．"], (int)_options.Punctuation,
            index => Save(_options with { Punctuation = (PunctuationStyle)index }));
        Toggle("誤入力の補正", "変換時に、ローマ字のよくある押し間違いを補正します。",
            _options.CorrectTypos, value => Save(_options with { CorrectTypos = value }));
        Toggle("確定後の文脈による補正", "続く語に合わせて、日本語か英語かの判定を直します。",
            _options.AutoCorrectAfterCommit, value => Save(_options with { AutoCorrectAfterCommit = value }));
        Toggle("英訳の候補", "日本語の変換候補に、同梱辞書の英訳も表示します。",
            _options.TranslationCandidates, value => Save(_options with { TranslationCandidates = value }));

        Section("プライバシー");
        Toggle("入力内容から学習する", "Meltype・Mozc の学習と、この起動中の最近使った絵文字を更新します。パスワード欄や学習禁止の入力欄では常に停止します。",
            _options.PersonalizedLearning, value => Save(_options with { PersonalizedLearning = value }));
        _root.AddView(new TextView(this)
        {
            Text = "設定と学習データは端末内に保存します。入力内容をネットワークへ送信しません。学習をオフにしても保存済みの学習データは残ります。",
            TextSize = 13
        }, RowParams());

        var reset = new MaterialButton(this) { Text = "設定を初期値に戻す" };
        reset.SetAllCaps(false);
        reset.Click += (_, _) => new AlertDialog.Builder(this)
            .SetTitle("設定を初期値に戻しますか？")
            .SetMessage("キーボードの設定を初期値に戻します。保存済みの学習データは削除しません。")
            .SetNegativeButton("キャンセル", (_, _) => { })
            .SetPositiveButton("戻す", (_, _) => { Save(new KeyboardOptions()); Recreate(); })
            .Show();
        _root.AddView(reset, RowParams());
        var close = new MaterialButton(this) { Text = "戻る" };
        close.SetAllCaps(false);
        close.Click += (_, _) => Finish();
        _root.AddView(close, RowParams());
        SetContentView(scroll);
    }

    private void Save(KeyboardOptions options)
    {
        _options = options.Normalize();
        AndroidSettingsStore.Save(this, _options);
    }

    private void Section(string title)
    {
        var label = new TextView(this) { Text = title, TextSize = 18 };
        var parameters = RowParams();
        parameters.SetMargins(0, Dp(24), 0, Dp(4));
        _root.AddView(label, parameters);
    }

    private LinearLayout Labels(string title, string description)
    {
        var labels = new LinearLayout(this) { Orientation = Orientation.Vertical };
        labels.AddView(new TextView(this) { Text = title, TextSize = 16 });
        labels.AddView(new TextView(this) { Text = description, TextSize = 13 });
        return labels;
    }

    private void Toggle(string title, string description, bool value, Action<bool> changed)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(Labels(title, description), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        var toggle = new Switch(this) { Checked = value, ContentDescription = title };
        toggle.CheckedChange += (_, args) => changed(args.IsChecked);
        row.AddView(toggle);
        row.Click += (_, _) => toggle.Checked = !toggle.Checked;
        _root.AddView(row, RowParams());
    }

    private void Choice(string title, string description, string[] labels, int selected, Action<int> changed)
    {
        _root.AddView(Labels(title, description), RowParams());
        var choose = new MaterialButton(this) { Text = labels[selected], ContentDescription = title + ": " + labels[selected] };
        choose.SetAllCaps(false);
        choose.Click += (_, _) =>
        {
            AlertDialog? dialog = null;
            dialog = new AlertDialog.Builder(this)
                .SetTitle(title)
                .SetSingleChoiceItems(labels, selected, (_, args) =>
                {
                    selected = args.Which;
                    changed(selected);
                    choose.Text = labels[selected];
                    choose.ContentDescription = title + ": " + labels[selected];
                    dialog?.Dismiss();
                })
                .SetNegativeButton("キャンセル", (_, _) => { })
                .Create();
            dialog.Show();
        };
        _root.AddView(choose, RowParams());
    }

    private LinearLayout.LayoutParams RowParams()
    {
        var parameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        parameters.SetMargins(0, Dp(6), 0, Dp(6));
        return parameters;
    }

    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density + .5f);
}
