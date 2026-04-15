using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MediaOverlay.Converters;

/// <summary>
/// Multi-purpose bool converter used in MainWindow.xaml:
///
///   ConverterParameter=play   → Visible when NOT playing (show Play icon)
///   ConverterParameter=pause  → Visible when playing     (show Pause icon)
///   ConverterParameter=invert → Visible when value is FALSE (album art placeholder)
///   (no parameter)            → same as "invert" for general use
/// </summary>
public class BoolToPlayPauseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        string mode = parameter as string ?? string.Empty;

        bool visible = mode switch
        {
            "play"   => !flag,  // show Play triangle when NOT playing
            "pause"  => flag,   // show Pause bars when playing
            "invert" => !flag,  // show placeholder when NO album art
            _        => !flag,
        };

        // If the target is Visibility (used for show/hide elements)
        if (targetType == typeof(Visibility))
            return visible ? Visibility.Visible : Visibility.Collapsed;

        // Otherwise return the bool itself
        return visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
