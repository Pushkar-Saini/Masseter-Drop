using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MasseterDrop.Models;

namespace MasseterDrop.Converters
{
    public class CategoryIconGeometryConverter : IValueConverter
    {
        // Fluent/Material style vector path geometries for file categories
        private static readonly Geometry PdfGeometry = Geometry.Parse("M6 2C4.9 2 4 2.9 4 4v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6H6zm7 7V3.5L18.5 9H13z M8 13h2.5c.8 0 1.5.7 1.5 1.5S11.3 16 10.5 16H8v3H6v-7h2v1zm0 2h2.5c.3 0 .5-.2.5-.5s-.2-.5-.5-.5H8v1zm5-2h3v1.5h-1.5v1h1.5V17H13v-4z");
        private static readonly Geometry ImageGeometry = Geometry.Parse("M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z");
        private static readonly Geometry VideoGeometry = Geometry.Parse("M17 10.5V7c0-.55-.45-1-1-1H4c-.55 0-1 .45-1 1v10c0 .55.45 1 1 1h12c.55 0 1-.45 1-1v-3.5l4 4v-11l-4 4zM10 14.5v-5l4 2.5-4 2.5z");
        private static readonly Geometry AudioGeometry = Geometry.Parse("M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z");
        private static readonly Geometry ArchiveGeometry = Geometry.Parse("M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-6 8h-4v-2h4v2zm0-4h-4V8h4v2z");
        private static readonly Geometry DocumentGeometry = Geometry.Parse("M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z");
        private static readonly Geometry ExecutableGeometry = Geometry.Parse("M12 2L4 5v6.09c0 5.05 3.41 9.76 8 10.91 4.59-1.15 8-5.86 8-10.91V5l-8-3zm1 14h-2v-2h2v2zm0-4h-2V7h2v5z");
        private static readonly Geometry CodeGeometry = Geometry.Parse("M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0l4.6-4.6-4.6-4.6L16 6l6 6-6 6-1.4-1.4z");
        private static readonly Geometry OtherGeometry = Geometry.Parse("M6 2c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6H6zm7 7V3.5L18.5 9H13z");

        static CategoryIconGeometryConverter()
        {
            PdfGeometry.Freeze();
            ImageGeometry.Freeze();
            VideoGeometry.Freeze();
            AudioGeometry.Freeze();
            ArchiveGeometry.Freeze();
            DocumentGeometry.Freeze();
            ExecutableGeometry.Freeze();
            CodeGeometry.Freeze();
            OtherGeometry.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FileCategory category)
            {
                return category switch
                {
                    FileCategory.Pdf => PdfGeometry,
                    FileCategory.Image => ImageGeometry,
                    FileCategory.Video => VideoGeometry,
                    FileCategory.Audio => AudioGeometry,
                    FileCategory.Archive => ArchiveGeometry,
                    FileCategory.Document => DocumentGeometry,
                    FileCategory.Executable => ExecutableGeometry,
                    FileCategory.Code => CodeGeometry,
                    _ => OtherGeometry
                };
            }
            return OtherGeometry;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
