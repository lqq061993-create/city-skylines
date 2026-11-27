using System.Collections.Generic;

namespace CityTransit.Core;

internal sealed class StopSelection
{
	public List<StopCandidate> Chosen = new List<StopCandidate>();

	public HashSet<int> Zones = new HashSet<int>();

	public double Value;

	public double RouteLength;

	public double Unserved;

	public double SharedFraction;
}
