using System;

namespace Spectre;

public struct HslColor
{
	public double H; // 0-360
	public double S; // 0-1
	public double L; // 0-1
	public byte A;

	public static HslColor FromRgb(byte r, byte g, byte b, byte a = 255)
	{
		double rd = r / 255.0;
		double gd = g / 255.0;
		double bd = b / 255.0;
		double max = Math.Max(rd, Math.Max(gd, bd));
		double min = Math.Min(rd, Math.Min(gd, bd));
		double h, s, l;
		l = (max + min) / 2.0;

		if (max == min)
		{
			h = s = 0.0; // achromatic
		}
		else
		{
			double d = max - min;
			s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
			if (max == rd)
			{
				h = (gd - bd) / d + (gd < bd ? 6.0 : 0.0);
			}
			else if (max == gd)
			{
				h = (bd - rd) / d + 2.0;
			}
			else
			{
				h = (rd - gd) / d + 4.0;
			}
			h /= 6.0;
		}

		return new HslColor { H = h * 360.0, S = s, L = l, A = a };
	}

	public System.Windows.Media.Color ToRgb()
	{
		double r, g, b;
		if (S == 0.0)
		{
			r = g = b = L; // achromatic
		}
		else
		{
			double q = L < 0.5 ? L * (1.0 + S) : L + S - L * S;
			double p = 2.0 * L - q;
			r = HueToRgb(p, q, H / 360.0 + 1.0 / 3.0);
			g = HueToRgb(p, q, H / 360.0);
			b = HueToRgb(p, q, H / 360.0 - 1.0 / 3.0);
		}
		return System.Windows.Media.Color.FromArgb(A, (byte)Math.Round(r * 255.0), (byte)Math.Round(g * 255.0), (byte)Math.Round(b * 255.0));
	}

	private static double HueToRgb(double p, double q, double t)
	{
		if (t < 0.0) t += 1.0;
		if (t > 1.0) t -= 1.0;
		if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
		if (t < 1.0 / 2.0) return q;
		if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
		return p;
	}
}
