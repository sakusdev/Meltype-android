// SPDX-License-Identifier: GPL-3.0-or-later

using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using Google.Android.Material.Button;

namespace Meltype.Android;

[Activity(
    Label = "@string/app_name",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/MeltypeTheme")]
public sealed class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var scroll = new ScrollView(this) { FillViewport = true };
        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical
        };
        root.SetGravity(GravityFlags.CenterHorizontal);
        root.SetPadding(Dp(24), Dp(36), Dp(24), Dp(32));
        scroll.AddView(root, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent));

        var title = new TextView(this)
        {
            Text = "Meltype",
            TextSize = 32,
            Gravity = GravityFlags.CenterHorizontal
        };
        root.AddView(title, MatchWidth());

        var subtitle = new TextView(this)
        {
            Text = "Android IME · Native Mozc",
            TextSize = 15,
            Gravity = GravityFlags.CenterHorizontal
        };
        var subtitleParams = MatchWidth();
        subtitleParams.SetMargins(0, Dp(4), 0, Dp(32));
        root.AddView(subtitle, subtitleParams);

        root.AddView(StepLabel("1", "Meltype を入力方法として有効にします"));
        var enable = new MaterialButton(this)
        {
            Text = "入力方法の設定を開く"
        };
        enable.SetAllCaps(false);
        enable.Click += (_, _) =>
            StartActivity(new Intent(Settings.ActionInputMethodSettings));
        root.AddView(enable, FullWidthButtonParams());

        root.AddView(StepLabel("2", "有効化したら現在のキーボードを Meltype に切り替えます"));
        var choose = new MaterialButton(this)
        {
            Text = "Meltype に切り替える"
        };
        choose.SetAllCaps(false);
        choose.Click += (_, _) =>
        {
            var manager = GetSystemService(InputMethodService) as InputMethodManager;
            manager?.ShowInputMethodPicker();
        };
        root.AddView(choose, FullWidthButtonParams());

        var settings = new MaterialButton(this) { Text = "Meltype の設定" };
        settings.SetAllCaps(false);
        settings.Click += (_, _) => StartActivity(new Intent(this, typeof(SettingsActivity)));
        root.AddView(settings, FullWidthButtonParams());

        var note = new TextView(this)
        {
            Text = "Meltype.Core の英語 / 日本語判定と、arm64-v8a 向け Native Mozc 変換を利用します。",
            TextSize = 14
        };
        var noteParams = MatchWidth();
        noteParams.SetMargins(Dp(4), Dp(28), Dp(4), 0);
        root.AddView(note, noteParams);

        var licenses = new MaterialButton(this) { Text = "ライセンスとソース" };
        licenses.SetAllCaps(false);
        licenses.Click += (_, _) => StartActivity(new Intent(this, typeof(LicensesActivity)));
        root.AddView(licenses, FullWidthButtonParams());

        SetContentView(scroll);
    }

    private LinearLayout StepLabel(string number, string text)
    {
        var row = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        row.SetGravity(GravityFlags.CenterVertical);

        var badge = new TextView(this)
        {
            Text = number,
            TextSize = 14,
            Gravity = GravityFlags.Center
        };
        row.AddView(badge, new LinearLayout.LayoutParams(Dp(32), Dp(32)));

        var label = new TextView(this)
        {
            Text = text,
            TextSize = 15
        };
        var labelParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        labelParams.SetMargins(Dp(10), 0, 0, 0);
        row.AddView(label, labelParams);

        var parameters = MatchWidth();
        parameters.SetMargins(0, Dp(12), 0, Dp(6));
        row.LayoutParameters = parameters;
        return row;
    }

    private LinearLayout.LayoutParams FullWidthButtonParams()
    {
        var p = MatchWidth();
        p.Height = Dp(52);
        p.SetMargins(0, Dp(4), 0, Dp(12));
        return p;
    }

    private static LinearLayout.LayoutParams MatchWidth() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

    private int Dp(int value) =>
        (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);
}
