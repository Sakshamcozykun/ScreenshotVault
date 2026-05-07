// src/ScreenshotVault.App/Converters/Converters.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using ScreenshotVault.App.ViewModels;

namespace ScreenshotVault.App.Converters;

/// <summary>bool → Visibility.Visible / Collapsed</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, string l)
        => value is Visibility.Visible;
}

/// <summary>bool → Visibility.Collapsed / Visible  (inverted)</summary>
public sealed class BoolToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, string l)
        => value is Visibility.Collapsed;
}

/// <summary>bool → !bool</summary>
public sealed class BoolInvertConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
        => value is not true;
    public object ConvertBack(object value, Type t, object p, string l)
        => value is not true;
}

/// <summary>SwipeDirection.Left → Visible, else Collapsed</summary>
public sealed class SwipeLeftToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
        => value is SwipeDirection.Left ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, string l)
        => throw new NotImplementedException();
}

/// <summary>SwipeDirection.Right → Visible, else Collapsed</summary>
public sealed class SwipeRightToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
        => value is SwipeDirection.Right ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, string l)
        => throw new NotImplementedException();
}

/// <summary>int deletedCount → human summary string</summary>
public sealed class IntToDeletedSummaryConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        int n = value is int i ? i : 0;
        return n == 0 ? "No screenshots deleted" : $"{n} screenshot{(n == 1 ? "" : "s")} sent to trash";
    }
    public object ConvertBack(object v, Type t, object p, string l)
        => throw new NotImplementedException();
}
