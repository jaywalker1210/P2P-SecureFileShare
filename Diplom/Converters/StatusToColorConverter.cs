using Diplom.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Diplom.Converters
{
    public class StatusToColorConverter: IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FileTransfer.TransferStatus status)
            {
                return status switch
                {
                    FileTransfer.TransferStatus.Completed => Color.Green,
                    FileTransfer.TransferStatus.InProgress => Color.Blue,
                    FileTransfer.TransferStatus.Failed => Color.Red,
                    _ => Color.Gray,
                };
            }
            return Color.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
