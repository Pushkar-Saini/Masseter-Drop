using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MasseterDrop.Models;

namespace MasseterDrop.Converters
{
    public class CategoryColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush PdfBrush = new(Color.FromRgb(255, 92, 92));
        private static readonly SolidColorBrush ImageBrush = new(Color.FromRgb(167, 139, 250));
        private static readonly SolidColorBrush VideoBrush = new(Color.FromRgb(245, 158, 11));
        private static readonly SolidColorBrush AudioBrush = new(Color.FromRgb(236, 72, 153));
        private static readonly SolidColorBrush ArchiveBrush = new(Color.FromRgb(251, 146, 60));
        private static readonly SolidColorBrush DocumentBrush = new(Color.FromRgb(96, 165, 250));
        private static readonly SolidColorBrush ExecutableBrush = new(Color.FromRgb(52, 211, 153));
        private static readonly SolidColorBrush CodeBrush = new(Color.FromRgb(34, 211, 238));
        private static readonly SolidColorBrush OtherBrush = new(Color.FromRgb(148, 163, 184));

        static CategoryColorConverter()
        {
            PdfBrush.Freeze();
            ImageBrush.Freeze();
            VideoBrush.Freeze();
            AudioBrush.Freeze();
            ArchiveBrush.Freeze();
            DocumentBrush.Freeze();
            ExecutableBrush.Freeze();
            CodeBrush.Freeze();
            OtherBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FileCategory category)
            {
                return category switch
                {
                    FileCategory.Pdf => PdfBrush,
                    FileCategory.Image => ImageBrush,
                    FileCategory.Video => VideoBrush,
                    FileCategory.Audio => AudioBrush,
                    FileCategory.Archive => ArchiveBrush,
                    FileCategory.Document => DocumentBrush,
                    FileCategory.Executable => ExecutableBrush,
                    FileCategory.Code => CodeBrush,
                    _ => OtherBrush
                };
            }
            return OtherBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
