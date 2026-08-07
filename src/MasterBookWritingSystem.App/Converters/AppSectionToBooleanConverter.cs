using System.Globalization;
using System.Windows.Data;
using MasterBookWritingSystem.App.Navigation;

namespace MasterBookWritingSystem.App.Converters;

public sealed class AppSectionToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AppSection current || parameter is null)
        {
            return false;
        }

        if (!Enum.TryParse<AppSection>(parameter.ToString(), ignoreCase: true, out var target))
        {
            return false;
        }

        return current == target;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
