using System.Globalization;
using System.Windows;
using System.Windows.Data;
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
            if (int.TryParse(parameter.ToString(), out int intVal))
            {
                return intVal;
            }
            return parameter.ToString()!;
        }
        return Binding.DoNothing;
    }
}
