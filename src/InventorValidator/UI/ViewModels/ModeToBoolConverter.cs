using System.Globalization;
using System.Windows.Data;

namespace InventorValidator.UI.ViewModels;

public class ModeToBoolConverter : IValueConverter
{
    public static readonly ModeToBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string currentStr && parameter is string targetStr)
        {
            return string.Equals(currentStr, targetStr, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is string targetStr)
        {
            return targetStr;
        }
        return Binding.DoNothing;
    }
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }
        return System.Windows.Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is System.Windows.Visibility vis)
        {
            return vis != System.Windows.Visibility.Visible;
        }
        return false;
    }
}

