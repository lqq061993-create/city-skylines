using System;

namespace CityTransit.Core;

public readonly struct Vec2
{
	public readonly double X;

	public readonly double Y;

	public Vec2(double x, double y)
	{
		X = x;
		Y = y;
	}

	public static double Distance(Vec2 a, Vec2 b)
	{
		double num = a.X - b.X;
		double num2 = a.Y - b.Y;
		return Math.Sqrt(num * num + num2 * num2);
	}

	public static Vec2 Lerp(Vec2 a, Vec2 b, double t)
	{
		return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
	}

	public static Vec2 operator -(Vec2 v)
	{
		return new Vec2(0.0 - v.X, 0.0 - v.Y);
	}
}
