using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PomodoroTimer.Spike.Services;

public sealed class ThemeService : IDisposable
{
    public ThemeService()
    {
        Apply();
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs args) =>
        Application.Current.Dispatcher.BeginInvoke(new Action(Apply));

    public void Apply()
    {
        var resources = Application.Current.Resources;
        if (SystemParameters.HighContrast)
        {
            resources["PageBrush"] = SystemColors.WindowBrush;
            resources["CardBrush"] = SystemColors.WindowBrush;
            resources["TextBrush"] = SystemColors.WindowTextBrush;
            resources["SecondaryBrush"] = SystemColors.WindowTextBrush;
            resources["AccentBrush"] = SystemColors.HighlightBrush;
            resources["OnAccentBrush"] = SystemColors.HighlightTextBrush;
            resources["BorderBrush"] = SystemColors.WindowTextBrush;
            return;
        }
        var dark = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int value && value == 0;
        Set("PageBrush", dark ? "#161A1D" : "#F5F7F8");
        Set("CardBrush", dark ? "#20262B" : "#FFFFFF");
        Set("TextBrush", dark ? "#F2F5F7" : "#18232B");
        Set("SecondaryBrush", dark ? "#ABB8C2" : "#52616B");
        Set("AccentBrush", dark ? "#70D8C0" : "#006B5E");
        Set("OnAccentBrush", dark ? "#18232B" : "#FFFFFF");
        Set("BorderBrush", dark ? "#52616B" : "#CDD6DB");
        void Set(string key, string color) => resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    // This preview intentionally has no animations; reduced-motion is respected.
    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
}
