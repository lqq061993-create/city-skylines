using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CityTransit.Core;

public sealed class RoadNetwork
{
	private struct Arc
	{
		public int To;

		public int Segment;

		public bool Forward;

		public double Length;

		public double Cost;
	}

	private const double GridCell = 150.0;

	private const double SampleStep = 40.0;

	private const double MaxSnapDistance = 700.0;

	private readonly List<Arc>[] _out;

	private readonly List<Arc>[] _in;

	private readonly List<Arc>[] _walk;

	private readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();

	private readonly Dictionary<int, int> _nodeIndex = new Dictionary<int, int>();

	private readonly bool[] _core;

	private readonly bool[] _coreSegment;

	public IReadOnlyList<RoadSegment> Segments { get; }

	public int NodeCount { get; }

	public int[] NodeIds { get; }

	public int[] SccOf { get; }

	public int[] SccSize { get; }

	// Nodes that survive dead-end and one-way pruning, i.e. places a bus can visit and leave again
	// without turning around on the spot. Only those may carry stops or route detours.
	public int CoreNodeCount { get; }

	public bool HasCore => CoreNodeCount > 0;

	public RoadNetwork(IReadOnlyList<RoadSegment> segments)
	{
		Segments = segments;
		List<int> list = new List<int>();
		foreach (RoadSegment segment in segments)
		{
			Register(segment.FromNode, list);
			Register(segment.ToNode, list);
		}
		NodeIds = list.ToArray();
		NodeCount = NodeIds.Length;
		_out = NewLists(NodeCount);
		_in = NewLists(NodeCount);
		_walk = NewLists(NodeCount);
		for (int i = 0; i < segments.Count; i++)
		{
			RoadSegment roadSegment = segments[i];
			int num = _nodeIndex[roadSegment.FromNode];
			int num2 = _nodeIndex[roadSegment.ToNode];
			double cost = roadSegment.Length * roadSegment.CostFactor;
			if (roadSegment.AllowWalking)
			{
				_walk[num].Add(new Arc
				{
					To = num2,
					Segment = i,
					Forward = true,
					Length = roadSegment.Length,
					Cost = roadSegment.Length
				});
				_walk[num2].Add(new Arc
				{
					To = num,
					Segment = i,
					Forward = false,
					Length = roadSegment.Length,
					Cost = roadSegment.Length
				});
			}
			if (roadSegment.AllowForward)
			{
				_out[num].Add(new Arc
				{
					To = num2,
					Segment = i,
					Forward = true,
					Length = roadSegment.Length,
					Cost = cost
				});
				_in[num2].Add(new Arc
				{
					To = num,
					Segment = i,
					Forward = true,
					Length = roadSegment.Length,
					Cost = cost
				});
			}
			if (roadSegment.AllowBackward)
			{
				_out[num2].Add(new Arc
				{
					To = num,
					Segment = i,
					Forward = false,
					Length = roadSegment.Length,
					Cost = cost
				});
				_in[num].Add(new Arc
				{
					To = num2,
					Segment = i,
					Forward = false,
					Length = roadSegment.Length,
					Cost = cost
				});
			}
			IndexSegment(i, roadSegment);
		}
		SccOf = new int[NodeCount];
		SccSize = ComputeScc();
		_core = ComputeCore(out int coreNodes);
		CoreNodeCount = coreNodes;
		_coreSegment = new bool[segments.Count];
		for (int k = 0; k < segments.Count; k++)
		{
			RoadSegment roadSegment = segments[k];
			_coreSegment[k] = _core[FromNode(k)] && _core[ToNode(k)] && (roadSegment.AllowForward || roadSegment.AllowBackward);
		}
	}

	/// <summary>
	/// Peels off everything a bus can only reach by driving in and back out again:
	/// first every undirected dead end (a node with one road), then every node that has no
	/// driving way in or no driving way out inside what is left (one-way pockets).
	/// </summary>
	private bool[] ComputeCore(out int coreCount)
	{
		int n = NodeCount;
		bool[] alive = new bool[n];
		var incident = new List<int>[n];
		int[] degree = new int[n];
		for (int i = 0; i < n; i++)
		{
			alive[i] = true;
			incident[i] = new List<int>(4);
		}
		for (int s = 0; s < Segments.Count; s++)
		{
			RoadSegment segment = Segments[s];
			if (!segment.AllowForward && !segment.AllowBackward)
			{
				continue;
			}
			int a = _nodeIndex[segment.FromNode];
			int b = _nodeIndex[segment.ToNode];
			if (a == b)
			{
				degree[a] += 2;
				incident[a].Add(s);
				continue;
			}
			degree[a]++;
			degree[b]++;
			incident[a].Add(s);
			incident[b].Add(s);
		}
		var queue = new Queue<int>();
		for (int j = 0; j < n; j++)
		{
			if (degree[j] <= 1)
			{
				queue.Enqueue(j);
			}
		}
		while (queue.Count > 0)
		{
			int node = queue.Dequeue();
			if (!alive[node])
			{
				continue;
			}
			alive[node] = false;
			foreach (int s in incident[node])
			{
				int other = OtherEnd(s, node);
				if (other == node || !alive[other])
				{
					continue;
				}
				degree[other]--;
				if (degree[other] <= 1)
				{
					queue.Enqueue(other);
				}
			}
		}
		queue.Clear();
		for (int k = 0; k < n; k++)
		{
			if (alive[k])
			{
				queue.Enqueue(k);
			}
		}
		while (queue.Count > 0)
		{
			int node = queue.Dequeue();
			if (!alive[node] || (HasArcTo(_out[node], alive) && HasArcTo(_in[node], alive)))
			{
				continue;
			}
			alive[node] = false;
			foreach (int s in incident[node])
			{
				int other = OtherEnd(s, node);
				if (other != node && alive[other])
				{
					queue.Enqueue(other);
				}
			}
		}
		coreCount = 0;
		for (int m = 0; m < n; m++)
		{
			if (alive[m])
			{
				coreCount++;
			}
		}
		return alive;
	}

	private int OtherEnd(int segment, int node)
	{
		return (FromNode(segment) == node) ? ToNode(segment) : FromNode(segment);
	}

	private static bool HasArcTo(List<Arc> arcs, bool[] alive)
	{
		foreach (Arc arc in arcs)
		{
			if (alive[arc.To])
			{
				return true;
			}
		}
		return false;
	}

	public bool IsCore(int node)
	{
		return node >= 0 && node < NodeCount && _core[node];
	}

	public bool IsCoreSegment(int segment)
	{
		return segment >= 0 && segment < _coreSegment.Length && _coreSegment[segment];
	}

	private void Register(int id, List<int> ids)
	{
		if (!_nodeIndex.ContainsKey(id))
		{
			_nodeIndex[id] = ids.Count;
			ids.Add(id);
		}
	}

	private static List<Arc>[] NewLists(int n)
	{
		List<Arc>[] array = new List<Arc>[n];
		for (int i = 0; i < n; i++)
		{
			array[i] = new List<Arc>(4);
		}
		return array;
	}

	public int NodeIndex(int nodeId)
	{
		return _nodeIndex[nodeId];
	}

	public int FromNode(int segment)
	{
		return _nodeIndex[Segments[segment].FromNode];
	}

	public int ToNode(int segment)
	{
		return _nodeIndex[Segments[segment].ToNode];
	}

	private int[] ComputeScc()
	{
		int nodeCount = NodeCount;
		int[] array = new int[nodeCount];
		int[] array2 = new int[nodeCount];
		bool[] array3 = new bool[nodeCount];
		for (int i = 0; i < nodeCount; i++)
		{
			array[i] = -1;
		}
		Stack<int> stack = new Stack<int>();
		Stack<(int, int)> stack2 = new Stack<(int, int)>();
		List<int> list = new List<int>();
		int num = 0;
		for (int j = 0; j < nodeCount; j++)
		{
			if (array[j] != -1)
			{
				continue;
			}
			stack2.Push((j, 0));
			while (stack2.Count > 0)
			{
				var (num2, num3) = stack2.Pop();
				if (num3 == 0)
				{
					array[num2] = (array2[num2] = num++);
					stack.Push(num2);
					array3[num2] = true;
				}
				bool flag = false;
				for (int k = num3; k < _out[num2].Count; k++)
				{
					int to = _out[num2][k].To;
					if (array[to] == -1)
					{
						stack2.Push((num2, k + 1));
						stack2.Push((to, 0));
						flag = true;
						break;
					}
					if (array3[to])
					{
						array2[num2] = Math.Min(array2[num2], array[to]);
					}
				}
				if (flag)
				{
					continue;
				}
				if (array2[num2] == array[num2])
				{
					int count = list.Count;
					int num4 = 0;
					int num5;
					do
					{
						num5 = stack.Pop();
						array3[num5] = false;
						SccOf[num5] = count;
						num4++;
					}
					while (num5 != num2);
					list.Add(num4);
				}
				if (stack2.Count > 0)
				{
					(int, int) tuple2 = stack2.Peek();
					array2[tuple2.Item1] = Math.Min(array2[tuple2.Item1], array2[num2]);
				}
			}
		}
		return list.ToArray();
	}

	public bool IsUsable(int node, int minSize = 6)
	{
		return SccSize[SccOf[node]] >= minSize;
	}

	public double[] Distances(int source, bool reverse, double cutoff, out int[] prevNode, out int[] prevSegment, out bool[] prevForward, Func<int, double> segmentFactor = null, int stopAt = -1)
	{
		double[] array = new double[NodeCount];
		prevNode = new int[NodeCount];
		prevSegment = new int[NodeCount];
		prevForward = new bool[NodeCount];
		for (int i = 0; i < NodeCount; i++)
		{
			array[i] = double.MaxValue;
			prevNode[i] = -1;
			prevSegment[i] = -1;
		}
		List<Arc>[] array2 = (reverse ? _in : _out);
		MinHeap minHeap = new MinHeap();
		array[source] = 0.0;
		minHeap.Push(0.0, source);
		while (minHeap.Count > 0)
		{
			int value;
			double num = minHeap.Pop(out value);
			if (num > array[value])
			{
				continue;
			}
			if (num > cutoff || value == stopAt)
			{
				break;
			}
			foreach (Arc item in array2[value])
			{
				if (SccOf[item.To] == SccOf[source])
				{
					double num2 = item.Cost * (segmentFactor?.Invoke(item.Segment) ?? 1.0);
					double num3 = num + num2;
					if (num3 < array[item.To])
					{
						array[item.To] = num3;
						prevNode[item.To] = value;
						prevSegment[item.To] = item.Segment;
						prevForward[item.To] = item.Forward;
						minHeap.Push(num3, item.To);
					}
				}
			}
		}
		return array;
	}

	public List<RouteArc> ShortestPath(int from, int to, Func<int, double> segmentFactor = null)
	{
		if (from == to)
		{
			return new List<RouteArc>();
		}
		if (SccOf[from] != SccOf[to])
		{
			return null;
		}
		if (Distances(from, reverse: false, double.MaxValue, out var prevNode, out var prevSegment, out var prevForward, segmentFactor, to)[to] == double.MaxValue)
		{
			return null;
		}
		return Rebuild(to, prevNode, prevSegment, prevForward);
	}

	public static List<RouteArc> Rebuild(int to, int[] prevNode, int[] prevSeg, bool[] prevFwd)
	{
		List<RouteArc> list = new List<RouteArc>();
		int num = to;
		while (prevNode[num] != -1)
		{
			list.Add(new RouteArc(prevSeg[num], prevFwd[num]));
			num = prevNode[num];
		}
		list.Reverse();
		return list;
	}

	public double ArcCost(int segment)
	{
		return Segments[segment].Length * Segments[segment].CostFactor;
	}

	public double Length(IEnumerable<RouteArc> arcs)
	{
		double num = 0.0;
		foreach (RouteArc arc in arcs)
		{
			num += Segments[arc.Segment].Length;
		}
		return num;
	}

	public Dictionary<int, double> WalkNodes(int segment, double offset, double radius)
	{
		Dictionary<int, double> dictionary = new Dictionary<int, double>();
		MinHeap minHeap = new MinHeap();
		RoadSegment roadSegment = Segments[segment];
		int node = FromNode(segment);
		int node2 = ToNode(segment);
		Relax(dictionary, minHeap, node, offset, radius);
		Relax(dictionary, minHeap, node2, roadSegment.Length - offset, radius);
		while (minHeap.Count > 0)
		{
			int value;
			double num = minHeap.Pop(out value);
			if (num > dictionary[value])
			{
				continue;
			}
			foreach (Arc item in _walk[value])
			{
				Relax(dictionary, minHeap, item.To, num + item.Length, radius);
			}
		}
		return dictionary;
	}

	private static void Relax(Dictionary<int, double> dist, MinHeap heap, int node, double d, double radius)
	{
		if (!(d > radius) && (!dist.TryGetValue(node, out var value) || !(value <= d)))
		{
			dist[node] = d;
			heap.Push(d, node);
		}
	}

	public IEnumerable<(int segment, bool nodeIsFrom)> SegmentsAt(int node)
	{
		foreach (var arc in _walk[node])
			yield return (arc.Segment, arc.Forward);
	}

	private static long Key(int x, int y)
	{
		return ((long)x << 32) ^ (uint)y;
	}

	private void IndexSegment(int index, RoadSegment s)
	{
		int num = Math.Max(1, (int)Math.Ceiling(s.Length / 40.0));
		for (int i = 0; i <= num; i++)
		{
			Vec2 direction;
			Vec2 vec = s.PointAt(s.Length * (double)i / (double)num, out direction);
			long key = Key((int)Math.Floor(vec.X / 150.0), (int)Math.Floor(vec.Y / 150.0));
			if (!_grid.TryGetValue(key, out var value))
			{
				value = (_grid[key] = new List<int>());
			}
			if (value.Count == 0 || value[value.Count - 1] != index)
			{
				value.Add(index);
			}
		}
	}

	public SnapResult Snap(Vec2 p, double maxDistance = 700.0, bool walkableOnly = false)
	{
		return SnapNear(p, maxDistance, walkableOnly, coreOnly: false);
	}

	/// <summary>Snaps to the nearest road that a bus can drive through, never to a dead end.</summary>
	public SnapResult SnapToCore(Vec2 p, double maxDistance, bool walkableOnly = false)
	{
		return SnapNear(p, maxDistance, walkableOnly, coreOnly: true);
	}

	/// <summary>Nearest node of the drivable core, or -1 when there is none within the radius.</summary>
	public int NearestCoreNode(Vec2 p, double maxDistance)
	{
		if (maxDistance <= 0.0)
		{
			return -1;
		}
		int best = -1;
		double bestDistance = double.MaxValue;
		int cx = (int)Math.Floor(p.X / 150.0);
		int cy = (int)Math.Floor(p.Y / 150.0);
		int rings = (int)Math.Ceiling(maxDistance / 150.0) + 1;
		HashSet<int> seen = new HashSet<int>();
		for (int ring = 0; ring <= rings; ring++)
		{
			for (int x = cx - ring; x <= cx + ring; x++)
			{
				for (int y = cy - ring; y <= cy + ring; y++)
				{
					if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) != ring || !_grid.TryGetValue(Key(x, y), out var segments))
					{
						continue;
					}
					foreach (int segment in segments)
					{
						if (!seen.Add(segment) || !IsCoreSegment(segment))
						{
							continue;
						}
						Consider(FromNode(segment), Segments[segment].Start);
						Consider(ToNode(segment), Segments[segment].End);
					}
				}
			}
			if (best >= 0 && bestDistance <= (double)ring * 150.0)
			{
				break;
			}
		}
		return best;
		void Consider(int node, Vec2 position)
		{
			if (!_core[node])
			{
				return;
			}
			double distance = Vec2.Distance(p, position);
			if (distance < bestDistance && distance <= maxDistance)
			{
				bestDistance = distance;
				best = node;
			}
		}
	}

	private SnapResult SnapNear(Vec2 p, double maxDistance, bool walkableOnly, bool coreOnly)
	{
		int num = (int)Math.Floor(p.X / 150.0);
		int num2 = (int)Math.Floor(p.Y / 150.0);
		int num3 = (int)Math.Ceiling(maxDistance / 150.0) + 1;
		int num4 = -1;
		double num5 = double.MaxValue;
		double offset = 0.0;
		HashSet<int> hashSet = new HashSet<int>();
		for (int i = 0; i <= num3; i++)
		{
			for (int j = num - i; j <= num + i; j++)
			{
				for (int k = num2 - i; k <= num2 + i; k++)
				{
					if (Math.Max(Math.Abs(j - num), Math.Abs(k - num2)) != i || !_grid.TryGetValue(Key(j, k), out var value))
					{
						continue;
					}
					foreach (int item in value)
					{
						if (hashSet.Add(item) && (!walkableOnly || Segments[item].AllowWalking) && (!coreOnly || _coreSegment[item]))
						{
							double distanceToRoad;
							double num6 = Segments[item].Project(p, out distanceToRoad);
							if (distanceToRoad < num5)
							{
								num5 = distanceToRoad;
								num4 = item;
								offset = num6;
							}
						}
					}
				}
			}
			if (num4 >= 0 && num5 <= (double)i * 150.0)
			{
				break;
			}
		}
		if (num4 < 0 || !(num5 <= maxDistance))
		{
			return new SnapResult(-1, 0.0, double.MaxValue);
		}
		return new SnapResult(num4, offset, num5);
	}
}
