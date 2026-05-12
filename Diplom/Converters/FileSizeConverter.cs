using System.Globalization;
using System.Windows.Data;

namespace Diplom.Converters
{
    public class FileSizeConverter: IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
                double len = bytes;
                int order = 0;

                while (len >= 1024 && order < sizes.Length)
                {
                    order++;
                    len /= 1024;
                }

                return $"{len:0.##} {sizes[order]}";
            }
            return "0 Б";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
