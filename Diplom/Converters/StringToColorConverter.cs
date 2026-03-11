using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Diplom.Converters
{
    public class StringToColorConverter: IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string colorName)
            {
                return colorName switch
                {
                    "Green" => Colors.Green,
                    "Red" => Colors.Red,
                    "Orange" => Colors.Orange,
                    "Blue" => Colors.Blue,
                    "Gray" => Colors.Gray,
                    _ => Colors.Gray
                };
            }
            return Colors.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
