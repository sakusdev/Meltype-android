// SPDX-License-Identifier: GPL-3.0-or-later

using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;

namespace Meltype.Android;

[Activity(
    Label = "@string/app_name",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public sealed class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var padding = Dp(24);
        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        root.SetPadding(padding, padding, padding, padding);

        var title = new TextView(this)
        {
            Text = "Meltype for Android",
            TextSize = 28
        };
        root.AddView(title);

        var description = new TextView(this)
        {
            Text = "Meltype の Android IME プレビュー版です。\n\n1. 入力方法の設定で Meltype を有効にする\n2. 入力方法を選択して Meltype に切り替える",
            TextSize = 16
        };
        var descriptionParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        descriptionParams.SetMargins(0, Dp(16), 0, Dp(24));
        root.AddView(description, descriptionParams);

        var enable = new Button(this)
        {
            Text = "1. Meltype を有効にする"
        };
        enable.Click += (_, _) =>
            StartActivity(new Intent(Settings.ActionInputMethodSettings));
        root.AddView(enable, FullWidthButtonParams());

        var choose = new Button(this)
        {
            Text = "2. Meltype を選択する"
        };
        choose.Click += (_, _) =>
        {
            var manager = GetSystemService(InputMethodService) as InputMethodManager;
            manager?.ShowInputMethodPicker();
        };
        root.AddView(choose, FullWidthButtonParams());

        var note = new TextView(this)
        {
            Text = "現在は Android IME の基盤 + Meltype.Core 接続の初期実装です。漢字変換は Mozc ネイティブブリッジ追加前のため、読みをそのまま返します。",
            TextSize = 14
        };
        var noteParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        noteParams.SetMargins(0, Dp(24), 0, 0);
        root.AddView(note, noteParams);

        SetContentView(root);
    }

    private LinearLayout.LayoutParams FullWidthButtonParams()
    {
        var p = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);
        p.SetMargins(0, Dp(6), 0, Dp(6));
        return p;
    }

    private int Dp(int value) =>
        (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
}
