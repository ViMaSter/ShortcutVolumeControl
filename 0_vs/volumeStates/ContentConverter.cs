using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Data;

namespace VolumeControl.Converter
{
    public class DllIconConverter : IValueConverter
    {
        public string FileName { get; set; }
        public int Number { get; set; }

        [DllImport("Shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        private static extern int ExtractIconEx(string sFile, int iIndex, out IntPtr piLargeVersion, out IntPtr piSmallVersion, int amountIcons);

        #region IValueConverter Members
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (ExtractIconEx(FileName, Number, out IntPtr large, out IntPtr small, 1) == -1)
            {
                return null;
            }

            return Icon.FromHandle(large).ToImageSource();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
        #endregion
    }

    public class VolumePercentageConverter : IValueConverter
    {
        private static double GetDoubleValue(object parameter, double defaultValue)
        {
            double a;
            if (parameter != null)
            {
                try
                {
                    a = System.Convert.ToDouble(parameter, new NumberFormatInfo());
                }
                catch(InvalidCastException)
                {
                    a = defaultValue;
                }
            }
            else
            {
                a = defaultValue;
            }
            return a;
        }

        #region IValueConverter Members
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return ((int)Math.Round((GetDoubleValue(value, 0.0) * 100))) + "%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return double.Parse(((string)value).TrimEnd('%'), new NumberFormatInfo()) / 100;
        }
        #endregion
    }

}