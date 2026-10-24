using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HumbleBundleAssets.Converters
{
    public class BooleanToVisibilityInvertedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
            {
                return v != Visibility.Visible;
            }
            return false;
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PreviewSupportBackgroundConverter : IValueConverter
    {
        // Dark theme: supported items have neutral dark slate card background,
        // unsupported items have distinct subtle tinted background (e.g. muted warm plum/amber #2A2228)
        private static readonly SolidColorBrush SupportedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E222B"));
        private static readonly SolidColorBrush UnsupportedBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B1E22"));

        static PreviewSupportBackgroundConverter()
        {
            SupportedBrush.Freeze();
            UnsupportedBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSupported && !isSupported)
            {
                return UnsupportedBrush;
            }
            return SupportedBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PreviewBadgeTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSupported)
            {
                return isSupported ? "Preview Available" : "No Preview";
            }
            return "No Preview";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PreviewBadgeBackgroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush SupportedBadge = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B3D2F"));
        private static readonly SolidColorBrush UnsupportedBadge = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3E2024"));

        static PreviewBadgeBackgroundConverter()
        {
            SupportedBadge.Freeze();
            UnsupportedBadge.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSupported && isSupported)
            {
                return SupportedBadge;
            }
            return UnsupportedBadge;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class PreviewBadgeForegroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush SupportedText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6EE7B7"));
        private static readonly SolidColorBrush UnsupportedText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));

        static PreviewBadgeForegroundConverter()
        {
            SupportedText.Freeze();
            UnsupportedText.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSupported && isSupported)
            {
                return SupportedText;
            }
            return UnsupportedText;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
