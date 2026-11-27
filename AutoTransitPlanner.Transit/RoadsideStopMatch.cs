using Game.Net;
using Unity.Entities;
using Unity.Mathematics;

namespace AutoTransitPlanner.Transit;

internal static class RoadsideStopMatch
{
	private const float MaxHorizontalDistance = 20f;
	private const float MaxHeightDifference = 4f;

	public static bool IsMatch(Entity plannedRoad, Edge plannedNodes, bool plannedRoadAlive,
		float3 center, float3 outward, float3 plannedPosition, Entity attachedRoad, Edge attachedNodes, float3 stopPosition)
	{
		if (plannedRoad != attachedRoad && (plannedRoadAlive || !SameEndpoints(plannedNodes, attachedNodes)))
		{
			return false;
		}
		return math.distancesq(stopPosition.xz, plannedPosition.xz) <= MaxHorizontalDistance * MaxHorizontalDistance
			&& math.abs(stopPosition.y - plannedPosition.y) <= MaxHeightDifference
			&& math.dot(stopPosition.xz - center.xz, outward.xz) > 0f;
	}

	private static bool SameEndpoints(Edge first, Edge second)
	{
		return (first.m_Start == second.m_Start && first.m_End == second.m_End)
			|| (first.m_Start == second.m_End && first.m_End == second.m_Start);
	}
}
