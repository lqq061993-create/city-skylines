using System;
using System.Collections.Generic;

namespace CityTransit.Core;

public sealed class RoadSegment
{
	private readonly Vec2[] _points;

	private readonly double[] _cum;

	public int Id { get; }

	public int FromNode { get; }

	public int ToNode { get; }

	public bool AllowForward { get; }

	public bool AllowBackward { get; }

	public bool AllowStops { get; }

	public bool AllowWalking { get; }

	public double CostFactor { get; }

	public double Width { get; }

	public double Length { get; }

	public Vec2 Start => _points[0];

	public Vec2 End => _points[_points.Length - 1];

	public IEnumerable<Vec2> Points => _points;

	public RoadSegment(int id, int fromNode, int toNode, IReadOnlyList<Vec2> polyline, bool allowForward = true, bool allowBackward = true, bool allowStops = true, bool allowWalking = true, double costFactor = 1.0, double width = 0.0)
	{
		if (polyline.Count < 2)
		{
			throw new ArgumentException("A road needs at least two points.", "polyline");
		}
		Id = id;
		FromNode = fromNode;
		ToNode = toNode;
		AllowForward = allowForward;
		AllowBackward = allowBackward;
		AllowStops = allowStops;
		AllowWalking = allowWalking;
		CostFactor = ((costFactor <= 0.0) ? 1.0 : costFactor);
		Width = ((width < 0.0) ? 0.0 : width);
		_points = new Vec2[polyline.Count];
		_cum = new double[polyline.Count];
		for (int i = 0; i < polyline.Count; i++)
		{
			_points[i] = polyline[i];
			if (i > 0)
			{
				_cum[i] = _cum[i - 1] + Vec2.Distance(_points[i - 1], _points[i]);
			}
		}
		Length = _cum[_cum.Length - 1];
	}

	public Vec2 PointAt(double dist, out Vec2 direction)
	{
		dist = Math.Max(0.0, Math.Min(Length, dist));
		int i;
		for (i = 1; i < _cum.Length - 1 && _cum[i] < dist; i++)
		{
		}
		double num = _cum[i] - _cum[i - 1];
		double t = ((num <= 0.0) ? 0.0 : ((dist - _cum[i - 1]) / num));
		Vec2 a = _points[i - 1];
		Vec2 b = _points[i];
		direction = ((num <= 0.0) ? new Vec2(1.0, 0.0) : new Vec2((b.X - a.X) / num, (b.Y - a.Y) / num));
		return Vec2.Lerp(a, b, t);
	}

	public double Project(Vec2 p, out double distanceToRoad)
	{
		double result = 0.0;
		double num = double.MaxValue;
		for (int i = 1; i < _points.Length; i++)
		{
			Vec2 vec = _points[i - 1];
			Vec2 vec2 = _points[i];
			double num2 = vec2.X - vec.X;
			double num3 = vec2.Y - vec.Y;
			double num4 = num2 * num2 + num3 * num3;
			double val = ((num4 <= 0.0) ? 0.0 : (((p.X - vec.X) * num2 + (p.Y - vec.Y) * num3) / num4));
			val = Math.Max(0.0, Math.Min(1.0, val));
			double num5 = Vec2.Distance(p, new Vec2(vec.X + num2 * val, vec.Y + num3 * val));
			if (num5 < num)
			{
				num = num5;
				result = _cum[i - 1] + (_cum[i] - _cum[i - 1]) * val;
			}
		}
		distanceToRoad = num;
		return result;
	}
}
