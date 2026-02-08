using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TermSnap.Views;

/// <summary>
/// 0 또는 null을 Visible, 그 외는 Collapsed로 변환
/// </summary>
public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Visible;

        if (value is int intValue)
            return intValue == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (value is long longValue)
            return longValue == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (value is double doubleValue)
            return doubleValue == 0 ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 0이 아닌 값을 Visible, 0은 Collapsed로 변환
/// </summary>
public class NonZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return Visibility.Collapsed;

        if (value is int intValue)
            return intValue != 0 ? Visibility.Visible : Visibility.Collapsed;

        if (value is long longValue)
            return longValue != 0 ? Visibility.Visible : Visibility.Collapsed;

        if (value is double doubleValue)
            return doubleValue != 0 ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 문자열이 비어있지 않으면 Visible, 비어있으면 Collapsed
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
            return !string.IsNullOrWhiteSpace(str) ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// bool 반전 변환
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }
}

/// <summary>
/// bool을 Visibility로 변환 (반전: true -> Collapsed, false -> Visible)
/// </summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
            return visibility != Visibility.Visible;
        return false;
    }
}

/// <summary>
/// null이 아니면 Visible, null이면 Collapsed로 변환
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 문자열에 ConverterParameter 값이 포함되면 Visible, 아니면 Collapsed
/// AI 아이콘 표시/숨김에 사용
/// </summary>
public class StringContainsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str && parameter is string param)
            return str.Contains(param, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 문자열이 비어있지 않으면 Collapsed, 비어있으면 Visible
/// AI 실행 시 기본 쉘 아이콘 숨김용
/// </summary>
public class StringNotEmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
            return string.IsNullOrEmpty(str) ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 객체의 타입이 parameter와 일치하면 true, 아니면 false 반환
/// </summary>
public class TypeToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        var typeName = parameter.ToString();
        var valueType = value.GetType();

        return valueType.Name == typeName || valueType.FullName == typeName;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
