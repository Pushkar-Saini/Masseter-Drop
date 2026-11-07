using System;
using System.Globalization;
using System.Windows.Data;
using MasseterDrop.Models;

namespace MasseterDrop.Converters
{
    public class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                return DownloadItem.FormatBytes(bytes);
            }
            if (value is int intBytes)
            {
                return DownloadItem.FormatBytes(intBytes);
            }
            return "0 B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
