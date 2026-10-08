// SPDX-License-Identifier: GPL-3.0-or-later

using Android.App;
using Android.OS;
using Android.Text.Method;
using Android.Text.Util;
using Android.Widget;

namespace Meltype.Android;

[Activity(Label = "ライセンスとソース", Exported = false, Theme = "@style/MeltypeTheme")]
public sealed class LicensesActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        using var reader = new StreamReader(Assets!.Open("licenses/NOTICE.txt"));
        var notices = new TextView(this) { Text = reader.ReadToEnd(), TextSize = 14 };
        var padding = (int)(20 * Resources!.DisplayMetrics!.Density);
        notices.SetPadding(padding, padding, padding, padding);
        notices.SetTextIsSelectable(true);
        Linkify.AddLinks(notices, MatchOptions.WebUrls);
        notices.MovementMethod = LinkMovementMethod.Instance;
        var scroll = new ScrollView(this);
        scroll.AddView(notices);
        SetContentView(scroll);
    }
}
