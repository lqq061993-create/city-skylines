using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTransitPlanner.Transit;

/// <summary>
/// Builds the sequence of waypoint edits the route tool tries after the game refuses a leg.
/// Removing one waypoint fixes most broken existing lines; when the budget allows it, dropping
/// the two ends of the failed leg is tried as well (a stop pair across a dead end or a one-way
/// pocket cannot be connected no matter which single end is dropped).
/// </summary>
internal sealed class RouteRepairSearch
{
	private const int MaxPreviews = 24;

	private readonly bool[] _passengerStops;

	private readonly int _maxRemovals;

	private readonly HashSet<string> _seen = new HashSet<string>();

	private readonly Queue<int[]> _pending = new Queue<int[]>();

	private readonly Queue<int[]> _pendingPairs = new Queue<int[]>();

	private int _previews;

	public RouteRepairSearch(bool[] passengerStops)
	{
		_passengerStops = passengerStops;
		_maxRemovals = Math.Min(4, Math.Max(1, passengerStops.Length / 3));
	}

	public int[] Next(int[] candidate, IReadOnlyList<int> failedLegs)
	{
		if (_previews >= MaxPreviews || candidate == null || candidate.Length < 3)
		{
			return null;
		}
		if (failedLegs != null && failedLegs.Count != 0)
		{
			int removals = _passengerStops.Length - candidate.Length;
			if (removals < _maxRemovals)
			{
				int from = failedLegs[0] % candidate.Length;
				int to = (from + 1) % candidate.Length;
				if (!_passengerStops[candidate[from]] && _passengerStops[candidate[to]])
				{
					(from, to) = (to, from);
				}
				EnqueueWithout(candidate, to);
				EnqueueWithout(candidate, from);
				if (_pending.Count <= 2 && removals + 2 <= _maxRemovals)
				{
					EnqueuePair(candidate, failedLegs[0] % candidate.Length);
				}
			}
		}
		if (_pending.Count != 0)
		{
			_previews++;
			return _pending.Dequeue();
		}
		if (_pendingPairs.Count != 0)
		{
			_previews++;
			return _pendingPairs.Dequeue();
		}
		return null;
	}

	private void EnqueueWithout(int[] candidate, int omit)
	{
		List<int> remaining = new List<int>(candidate.Length - 1);
		for (int i = 0; i < candidate.Length; i++)
		{
			if (i != omit)
			{
				remaining.Add(candidate[i]);
			}
		}
		Enqueue(remaining, pair: false);
	}

	private void EnqueuePair(int[] candidate, int leg)
	{
		int n = candidate.Length;
		List<int> remaining = new List<int>(n - 2);
		for (int i = 0; i < n; i++)
		{
			if (i != leg && i != (leg + 1) % n)
			{
				remaining.Add(candidate[i]);
			}
		}
		Enqueue(remaining, pair: true);
	}

	private void Enqueue(List<int> remaining, bool pair)
	{
		if (remaining.Count(i => _passengerStops[i]) < 2)
		{
			return;
		}
		if (!_seen.Add(string.Join(",", remaining)))
		{
			return;
		}
		(pair ? _pendingPairs : _pending).Enqueue(remaining.ToArray());
	}
}