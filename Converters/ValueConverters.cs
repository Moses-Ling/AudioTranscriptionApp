using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioTranscriptionApp.Converters
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool && (bool)value) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility && (Visibility)value == Visibility.Visible;
        }
    }

    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool && (bool)value);
        }
    }

    public class AudioLevelToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || !(values[0] is float) || !(values[1] is double))
                return 0;

            float audioLevel = (float)values[0];
            double containerWidth = (double)values[1];

            // Convert level to percentage (0-100)
            int levelPercentage = (int)(audioLevel * 100);

            // Cap at 100%
            if (levelPercentage > 100)
                levelPercentage = 100;

            // Calculate the width of the level bar
            double barWidth = (levelPercentage / 100.0) * containerWidth;

            return barWidth;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AudioLevelToPercentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is float audioLevel)
            {
                // Convert to percentage (0-1)
                return audioLevel;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
