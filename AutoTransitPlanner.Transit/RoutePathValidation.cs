using Game.Pathfind;
using Game.Routes;
using Unity.Mathematics;

namespace AutoTransitPlanner.Transit;

internal enum SegmentPathStatus
{
	Waiting,
	Failed,
	Ready
}

internal static class RoutePathValidation
{
	private const PathFlags Unsettled = PathFlags.Pending | PathFlags.Scheduled | PathFlags.Obsolete;

	public static bool HasConfirmedFailure(PathInformation path)
	{
		return (path.m_State & Unsettled) == 0 && (path.m_Distance < 0f || (path.m_State & PathFlags.Failed) != 0);
	}

	public static SegmentPathStatus GetSegmentStatus(PathInformation path, PathTargets targets, float3 start, float3 end, bool hasPathElements)
	{
		if ((path.m_State & Unsettled) != 0 ||
			!(math.distancesq(targets.m_ReadyStartPosition, start) < 1f) ||
			!(math.distancesq(targets.m_ReadyEndPosition, end) < 1f))
		{
			return SegmentPathStatus.Waiting;
		}

		if (HasConfirmedFailure(path))
		{
			return SegmentPathStatus.Failed;
		}

		return math.isfinite(path.m_Distance) && (path.m_Distance > 0f || hasPathElements)
			? SegmentPathStatus.Ready
			: SegmentPathStatus.Waiting;
	}

}
