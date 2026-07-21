using System;
using System.Globalization;
using System.Windows.Data;

namespace Spectre;

public class SliderWidthConverter : IMultiValueConverter
{
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
	{
		if (values.Length == 3 && values[0] is double val && values[1] is double max && values[2] is double width)
		{
			if (max <= 0.0)
			{
				return 0.0;
			}
			return val / max * width;
		}
		return 0.0;
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
