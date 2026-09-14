using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PDFBinder.App.Controls;

namespace PDFBinder.App.Converters;

/// <summary>
/// boolean値を反転するコンバーター
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b ? !b : false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b ? !b : false;
    }
}

/// <summary>
/// nullまたは空文字の場合にVisibility.Collapsedを返すコンバーター
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return Visibility.Collapsed;
        if (value is string s && string.IsNullOrWhiteSpace(s)) return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 条件一致でVisibilityを切り替えるコンバーター
/// </summary>
public class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool equals = Equals(value?.ToString(), parameter?.ToString());
        return equals ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// カウントが0の時にVisible、1以上の時にCollapsedを返すコンバーター
/// </summary>
public class InverseCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// カウントが1以上の時にVisible、0の時にCollapsedを返すコンバーター
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// ツールモードのEnum値と文字列パラメータの一致を判定するコンバーター
/// </summary>
public class ToolToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
        {
            if (Enum.TryParse<EditorToolMode>(parameter.ToString(), true, out var mode))
            {
                return mode;
            }
        }
        return Binding.DoNothing;
    }
}

/// <summary>
/// 条件一致でBoolean（True/False）を返すコンバーター
/// </summary>
public class EqualityToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Equals(value?.ToString(), parameter?.ToString());
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
        {
            if (targetType.IsEnum && Enum.TryParse(targetType, parameter.ToString(), true, out var enumVal))
            {
                return enumVal;
            }
            if (int.TryParse(parameter.ToString(), out int intVal))
            {
                return intVal;
            }
            return parameter.ToString()!;
        }
        return Binding.DoNothing;
    }
}

/// <summary>
/// 2つの浮動小数点数（太さ）が概ね等しいかどうかを判定するマルチバインディングコンバーター
/// </summary>
public class DoubleEqualsToBooleanConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 &&
            values[0] != DependencyProperty.UnsetValue &&
            values[1] != DependencyProperty.UnsetValue)
        {
            double d1 = System.Convert.ToDouble(values[0], CultureInfo.InvariantCulture);
            double d2 = System.Convert.ToDouble(values[1], CultureInfo.InvariantCulture);
            return Math.Abs(d1 - d2) < 0.05;
        }
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 2つのColorが等しい場合にVisible、異なる場合にCollapsedを返すマルチバインディングコンバーター
/// </summary>
public class ColorEqualsToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 &&
            values[0] is Color c1 &&
            values[1] is Color c2)
        {
            return c1 == c2 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 色の輝度（Luminance）に応じて、視認性の高いコントラストブラシ（黒または白）を返すコンバーター
/// </summary>
public class ColorToContrastingBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color color)
        {
            // 輝度（Luminance）の算出: Y = 0.299 R + 0.587 G + 0.114 B
            double luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
            return luminance > 140.0 ? Brushes.Black : Brushes.White;
        }
        return Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
