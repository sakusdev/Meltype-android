// SPDX-License-Identifier: GPL-3.0-or-later

using Android.Content;
using Android.Util;
using Meltype.Config;

namespace Meltype.Android;

internal static class AndroidSettingsStore
{
    private const string PreferenceFile = "keyboard-settings";

    public static KeyboardOptions Load(Context context)
    {
        var preferences = context.GetSharedPreferences(PreferenceFile, FileCreationMode.Private)
            ?? throw new InvalidOperationException("Keyboard preferences are unavailable");
        try
        {
            return new KeyboardOptions
            {
                Theme = (KeyboardTheme)preferences.GetInt(nameof(KeyboardOptions.Theme), 0),
                KeyHeightDp = preferences.GetInt(nameof(KeyboardOptions.KeyHeightDp), 48),
                InitialMode = (InitialInputMode)preferences.GetInt(nameof(KeyboardOptions.InitialMode), 0),
                KeyVibration = preferences.GetBoolean(nameof(KeyboardOptions.KeyVibration), true),
                KeySound = preferences.GetBoolean(nameof(KeyboardOptions.KeySound), false),
                KeyPreview = preferences.GetBoolean(nameof(KeyboardOptions.KeyPreview), true),
                SpaceSwipe = preferences.GetBoolean(nameof(KeyboardOptions.SpaceSwipe), true),
                BackspaceRepeat = preferences.GetBoolean(nameof(KeyboardOptions.BackspaceRepeat), true),
                LiveConversion = preferences.GetBoolean(nameof(KeyboardOptions.LiveConversion), true),
                Detection = (DetectionLevel)preferences.GetInt(nameof(KeyboardOptions.Detection), (int)DetectionLevel.Balanced),
                Punctuation = (PunctuationStyle)preferences.GetInt(nameof(KeyboardOptions.Punctuation), 0),
                CorrectTypos = preferences.GetBoolean(nameof(KeyboardOptions.CorrectTypos), true),
                AutoCorrectAfterCommit = preferences.GetBoolean(nameof(KeyboardOptions.AutoCorrectAfterCommit), true),
                TranslationCandidates = preferences.GetBoolean(nameof(KeyboardOptions.TranslationCandidates), true),
                PersonalizedLearning = preferences.GetBoolean(nameof(KeyboardOptions.PersonalizedLearning), true),
            }.Normalize();
        }
        catch (Java.Lang.ClassCastException)
        {
            Log.Warn("MeltypeSettings", "Invalid preference types; using defaults");
            return new KeyboardOptions { PersonalizedLearning = false };
        }
    }

    public static void Save(Context context, KeyboardOptions options)
    {
        options = options.Normalize();
        var preferences = context.GetSharedPreferences(PreferenceFile, FileCreationMode.Private)
            ?? throw new InvalidOperationException("Keyboard preferences are unavailable");
        using var editor = preferences.Edit()
            ?? throw new InvalidOperationException("Keyboard preferences cannot be edited");
        editor.PutInt(nameof(KeyboardOptions.Theme), (int)options.Theme);
        editor.PutInt(nameof(KeyboardOptions.KeyHeightDp), options.KeyHeightDp);
        editor.PutInt(nameof(KeyboardOptions.InitialMode), (int)options.InitialMode);
        editor.PutBoolean(nameof(KeyboardOptions.KeyVibration), options.KeyVibration);
        editor.PutBoolean(nameof(KeyboardOptions.KeySound), options.KeySound);
        editor.PutBoolean(nameof(KeyboardOptions.KeyPreview), options.KeyPreview);
        editor.PutBoolean(nameof(KeyboardOptions.SpaceSwipe), options.SpaceSwipe);
        editor.PutBoolean(nameof(KeyboardOptions.BackspaceRepeat), options.BackspaceRepeat);
        editor.PutBoolean(nameof(KeyboardOptions.LiveConversion), options.LiveConversion);
        editor.PutInt(nameof(KeyboardOptions.Detection), (int)options.Detection);
        editor.PutInt(nameof(KeyboardOptions.Punctuation), (int)options.Punctuation);
        editor.PutBoolean(nameof(KeyboardOptions.CorrectTypos), options.CorrectTypos);
        editor.PutBoolean(nameof(KeyboardOptions.AutoCorrectAfterCommit), options.AutoCorrectAfterCommit);
        editor.PutBoolean(nameof(KeyboardOptions.TranslationCandidates), options.TranslationCandidates);
        editor.PutBoolean(nameof(KeyboardOptions.PersonalizedLearning), options.PersonalizedLearning);
        editor.Apply();
    }

    public static bool PreferredEnglish(Context context)
    {
        try
        {
            return context.GetSharedPreferences(PreferenceFile, FileCreationMode.Private)
                ?.GetBoolean("LastPreferredEnglish", false) ?? false;
        }
        catch (Java.Lang.ClassCastException) { return false; }
    }

    public static void SavePreferredEnglish(Context context, bool value)
    {
        using var editor = context.GetSharedPreferences(PreferenceFile, FileCreationMode.Private)?.Edit();
        editor?.PutBoolean("LastPreferredEnglish", value);
        editor?.Apply();
    }
}
