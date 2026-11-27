using System.Collections.Generic;

namespace CityTransit.Core;

/// <summary>
/// A bus line in this game is a closed loop, and the game's pathfinder refuses a vehicle that
/// has to turn around on the spot. The two things that break a planned loop are therefore a
/// dead-end road (the only way out is the way in) and an immediate reversal at a node.
/// Driving the same road twice is allowed when the loop comes back through it later
/// (a single bridge serving both directions, for example); turning around at the end of it is not.
/// </summary>
internal static class RouteRules
{
	public static int StartNode(RoadNetwork net, RouteArc arc)
	{
		return arc.Forward ? net.FromNode(arc.Segment) : net.ToNode(arc.Segment);
	}

	public static int EndNode(RoadNetwork net, RouteArc arc)
	{
		return arc.Forward ? net.ToNode(arc.Segment) : net.FromNode(arc.Segment);
	}

	/// <summary>True when every road of the route is a road the bus can drive through.</summary>
	public static bool IsOnCore(RoadNetwork net, IReadOnlyList<RouteArc> route)
	{
		for (int i = 0; i < route.Count; i++)
		{
			if (!net.IsCoreSegment(route[i].Segment))
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>
	/// True when the arcs connect end to end, close the loop, never reverse on the road they
	/// arrived on, and every road is a through road.
	/// </summary>
	public static bool HasNoTurnaround(RoadNetwork net, IReadOnlyList<RouteArc> route)
	{
		if (route == null || route.Count < 3)
		{
			return false;
		}
		if (!IsOnCore(net, route))
		{
			return false;
		}
		for (int i = 0; i < route.Count; i++)
		{
			RouteArc current = route[i];
			RouteArc next = route[(i + 1) % route.Count];
			if (EndNode(net, current) != StartNode(net, next))
			{
				return false;
			}
			if (current.Segment == next.Segment)
			{
				// In at one end and straight back out: that is the dead-end U-turn the game refuses.
				return false;
			}
		}
		return true;
	}

	/// <summary>Rules a planned loop has to obey before it is worth handing to the game.</summary>
	public static bool IsAcceptableLoop(RoadNetwork net, IReadOnlyList<RouteArc> route)
	{
		return HasNoTurnaround(net, route);
	}
}