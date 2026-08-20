using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using ChatApp.Core.Settings;

namespace ChatApp.UI.Services;

/// <summary>
/// Applies the light/dark theme: swaps the merged brush dictionary and requests the
/// matching FluentTheme variant. Custom brushes are consumed via DynamicResource so
/// open windows restyle live.
/// </summary>
public static class ThemeService
{
    public static void Apply(ThemeMode mode)
    {
        if (Application.Current is null) return;

        var variant = mode switch
        {
            ThemeMode.Dark => ThemeVariant.Dark,
            ThemeMode.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
        Application.Current.RequestedThemeVariant = variant;

        var effectiveDark = mode == ThemeMode.Dark ||
            (mode == ThemeMode.FollowSystem &&
             Application.Current.ActualThemeVariant == ThemeVariant.Dark);
        SwapThemeDictionary(effectiveDark ? "Dark" : "Light");
    }

    private static void SwapThemeDictionary(string name)
    {
        var app = Application.Current!;
        var dictionaries = app.Resources.MergedDictionaries;
        var themePath = $"/Themes/{name}.xaml";
        var existing = dictionaries.FirstOrDefault(d =>
            d is Avalonia.Controls.ResourceDictionary dict &&
            HasThemeSource(dict, themePath));
        if (existing is Avalonia.Controls.ResourceDictionary existingDict)
        {
            if (HasThemeSource(existingDict, themePath)) return;
            dictionaries.Remove(existingDict);
        }
        var uri = new Uri($"avares://ChatApp.UI/Themes/{name}.xaml");
        var loaded = (Avalonia.Controls.ResourceDictionary)AvaloniaXamlLoader.Load(uri);
        dictionaries.Insert(0, loaded);
    }

    /// <summary>Matches a merged dictionary whose XAML source points at the given theme path.</summary>
    private static bool HasThemeSource(Avalonia.Controls.ResourceDictionary dictionary, string themePath)
    {
        try
        {
            var source = dictionary.GetType().GetProperty("Source")?.GetValue(dictionary) as Uri;
            return source is not null &&
                source.OriginalString.Contains(themePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
