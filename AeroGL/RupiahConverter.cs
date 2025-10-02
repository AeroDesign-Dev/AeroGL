using System;
using System.Globalization;
using System.Windows.Data;

namespace AeroGL
{
    public class RupiahConverter : IValueConverter
    {
        private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "";
            if (!decimal.TryParse(value.ToString(), out var v)) return value.ToString();

            var s = "Rp " + Math.Abs(v).ToString("N2", Id);
            return v < 0 ? "(" + s + ")" : s;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
