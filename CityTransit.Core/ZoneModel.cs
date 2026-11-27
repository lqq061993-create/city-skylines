using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CityTransit.Core;

internal sealed class ZoneModel
{
	private sealed class CellAccumulator
	{
		public double Rx;

		public double Ry;

		public double Rw;

		public double Dx;

		public double Dy;

		public double Dw;

		public double Sx;

		public double Sy;

		public double Sw;

		public double Lx;

		public double Ly;

		public double Lw;
	}

	[CompilerGenerated]
	private sealed class _003CNear_003Ed__32 : IEnumerable<Zone>, IEnumerable, IEnumerator<Zone>, IDisposable, IEnumerator
	{
		private int _003C_003E1__state;

		private Zone _003C_003E2__current;

		private int _003C_003El__initialThreadId;

		public ZoneModel _003C_003E4__this;

		private Vec2 p;

		public Vec2 _003C_003E3__p;

		private double radius;

		public double _003C_003E3__radius;

		private int _003Cx1_003E5__2;

		private int _003Cy0_003E5__3;

		private int _003Cy1_003E5__4;

		private int _003Cx_003E5__5;

		private int _003Cy_003E5__6;

		private List<Zone>.Enumerator _003C_003E7__wrap6;

		Zone IEnumerator<Zone>.Current
		{
			[DebuggerHidden]
			get
			{
				return _003C_003E2__current;
			}
		}

		object IEnumerator.Current
		{
			[DebuggerHidden]
			get
			{
				return _003C_003E2__current;
			}
		}

		[DebuggerHidden]
		public _003CNear_003Ed__32(int _003C_003E1__state)
		{
			this._003C_003E1__state = _003C_003E1__state;
			_003C_003El__initialThreadId = Environment.CurrentManagedThreadId;
		}

		[DebuggerHidden]
		void IDisposable.Dispose()
		{
			int num = _003C_003E1__state;
			if (num == -3 || num == 1)
			{
				try
				{
				}
				finally
				{
					_003C_003Em__Finally1();
				}
			}
			_003C_003E7__wrap6 = default(List<Zone>.Enumerator);
			_003C_003E1__state = -2;
		}

		private bool MoveNext()
		{
			try
			{
				int num = _003C_003E1__state;
				ZoneModel zoneModel = _003C_003E4__this;
				if (num != 0)
				{
					if (num != 1)
					{
						return false;
					}
					_003C_003E1__state = -3;
					goto IL_020c;
				}
				_003C_003E1__state = -1;
				if (zoneModel._near == null)
				{
					zoneModel._near = new Dictionary<long, List<Zone>>();
					foreach (Zone zone in zoneModel.Zones)
					{
						long key = NearKey((int)Math.Floor(zone.Center.X / 300.0), (int)Math.Floor(zone.Center.Y / 300.0));
						if (!zoneModel._near.TryGetValue(key, out var value))
						{
							value = (zoneModel._near[key] = new List<Zone>());
						}
						value.Add(zone);
					}
				}
				int num2 = (int)Math.Floor((p.X - radius) / 300.0);
				_003Cx1_003E5__2 = (int)Math.Floor((p.X + radius) / 300.0);
				_003Cy0_003E5__3 = (int)Math.Floor((p.Y - radius) / 300.0);
				_003Cy1_003E5__4 = (int)Math.Floor((p.Y + radius) / 300.0);
				_003Cx_003E5__5 = num2;
				goto IL_0260;
				IL_020c:
				while (_003C_003E7__wrap6.MoveNext())
				{
					Zone current2 = _003C_003E7__wrap6.Current;
					if (Vec2.Distance(current2.Center, p) <= radius)
					{
						_003C_003E2__current = current2;
						_003C_003E1__state = 1;
						return true;
					}
				}
				_003C_003Em__Finally1();
				_003C_003E7__wrap6 = default(List<Zone>.Enumerator);
				goto IL_022b;
				IL_022b:
				_003Cy_003E5__6++;
				goto IL_023d;
				IL_0260:
				if (_003Cx_003E5__5 <= _003Cx1_003E5__2)
				{
					_003Cy_003E5__6 = _003Cy0_003E5__3;
					goto IL_023d;
				}
				return false;
				IL_023d:
				if (_003Cy_003E5__6 <= _003Cy1_003E5__4)
				{
					if (zoneModel._near.TryGetValue(NearKey(_003Cx_003E5__5, _003Cy_003E5__6), out var value2))
					{
						_003C_003E7__wrap6 = value2.GetEnumerator();
						_003C_003E1__state = -3;
						goto IL_020c;
					}
					goto IL_022b;
				}
				_003Cx_003E5__5++;
				goto IL_0260;
			}
			catch
			{
				//try-fault
				((IDisposable)this).Dispose();
				throw;
			}
		}

		bool IEnumerator.MoveNext()
		{
			//ILSpy generated this explicit interface implementation from .override directive in MoveNext
			return this.MoveNext();
		}

		private void _003C_003Em__Finally1()
		{
			_003C_003E1__state = -1;
			((IDisposable)_003C_003E7__wrap6).Dispose();
		}

		[DebuggerHidden]
		void IEnumerator.Reset()
		{
			throw new NotSupportedException();
		}

		[DebuggerHidden]
		IEnumerator<Zone> IEnumerable<Zone>.GetEnumerator()
		{
			_003CNear_003Ed__32 _003CNear_003Ed__;
			if (_003C_003E1__state == -2 && _003C_003El__initialThreadId == Environment.CurrentManagedThreadId)
			{
				_003C_003E1__state = 0;
				_003CNear_003Ed__ = this;
			}
			else
			{
				_003CNear_003Ed__ = new _003CNear_003Ed__32(0)
				{
					_003C_003E4__this = _003C_003E4__this
				};
			}
			_003CNear_003Ed__.p = _003C_003E3__p;
			_003CNear_003Ed__.radius = _003C_003E3__radius;
			return _003CNear_003Ed__;
		}

		[DebuggerHidden]
		IEnumerator IEnumerable.GetEnumerator()
		{
			return ((IEnumerable<Zone>)this).GetEnumerator();
		}
	}

	private const double NearCell = 300.0;

	private Dictionary<long, List<Zone>> _near;

	public List<Zone> Zones { get; } = new List<Zone>();


	public Dictionary<int, List<int>> BySegment { get; } = new Dictionary<int, List<int>>();


	public Dictionary<long, double> Flow { get; } = new Dictionary<long, double>();


	public long[] FlowKeys { get; private set; }

	public double[] FlowValue { get; private set; }

	public int[][] AdjOther { get; private set; }

	public int[][] AdjFlow { get; private set; }

	public double TotalWeight => Zones.Sum((Zone z) => z.Weight);

	public static long PairKey(int a, int b)
	{
		if (a >= b)
		{
			return ((long)b << 32) | (uint)a;
		}
		return ((long)a << 32) | (uint)b;
	}

	public static int KeyA(long key)
	{
		return (int)(key >> 32);
	}

	public static int KeyB(long key)
	{
		return (int)(key & 0xFFFFFFFFu);
	}

	[IteratorStateMachine(typeof(_003CNear_003Ed__32))]
	public IEnumerable<Zone> Near(Vec2 p, double radius)
	{
		//yield-return decompiler failed: Unexpected instruction in Iterator.Dispose()
		return new _003CNear_003Ed__32(-2)
		{
			_003C_003E4__this = this,
			_003C_003E3__p = p,
			_003C_003E3__radius = radius
		};
	}

	private static long NearKey(int x, int y)
	{
		return ((long)x << 32) ^ (uint)y;
	}

	public static ZoneModel Build(CityModel city, RoadNetwork net, PlannerSettings s)
	{
		ZoneModel zoneModel = new ZoneModel();
		double cell = s.ZoneSize;
		Dictionary<long, int> dictionary = new Dictionary<long, int>();
		Dictionary<long, CellAccumulator> cells = new Dictionary<long, CellAccumulator>();
		foreach (DemandPoint resident in city.Residents)
		{
			if (!(resident.Weight <= 0.0))
			{
				CellAccumulator cellAccumulator = CellOf(resident.Position);
				cellAccumulator.Rx += resident.Position.X * resident.Weight;
				cellAccumulator.Ry += resident.Position.Y * resident.Weight;
				cellAccumulator.Rw += resident.Weight;
			}
		}
		foreach (DemandPoint destination in city.Destinations)
		{
			if (!(destination.Weight <= 0.0))
			{
				CellAccumulator cellAccumulator2 = CellOf(destination.Position);
				cellAccumulator2.Dx += destination.Position.X * destination.Weight;
				cellAccumulator2.Dy += destination.Position.Y * destination.Weight;
				cellAccumulator2.Dw += destination.Weight;
			}
		}
		if (s.ShoppingRate > 0.0)
		{
			foreach (DemandPoint shop in city.Shops)
			{
				if (!(shop.Weight <= 0.0))
				{
					CellAccumulator cellAccumulator3 = CellOf(shop.Position);
					cellAccumulator3.Sx += shop.Position.X * shop.Weight;
					cellAccumulator3.Sy += shop.Position.Y * shop.Weight;
					cellAccumulator3.Sw += shop.Weight;
				}
			}
		}
		if (s.LeisureRate > 0.0)
		{
			foreach (DemandPoint item in city.Leisure)
			{
				if (!(item.Weight <= 0.0))
				{
					CellAccumulator cellAccumulator4 = CellOf(item.Position);
					cellAccumulator4.Lx += item.Position.X * item.Weight;
					cellAccumulator4.Ly += item.Position.Y * item.Weight;
					cellAccumulator4.Lw += item.Weight;
				}
			}
		}
		foreach (KeyValuePair<long, CellAccumulator> item2 in cells.OrderBy((KeyValuePair<long, CellAccumulator> k) => k.Key))
		{
			CellAccumulator value = item2.Value;
			double num = value.Rw + value.Dw;
			double num2 = value.Sw + value.Lw;
			if (num <= 0.0 && num2 <= 0.0)
			{
				continue;
			}
			Vec2 center = ((num > 0.0) ? new Vec2((value.Rx + value.Dx) / num, (value.Ry + value.Dy) / num) : new Vec2((value.Sx + value.Lx) / num2, (value.Sy + value.Ly) / num2));
			Zone zone = new Zone
			{
				Id = zoneModel.Zones.Count,
				Center = center,
				Residents = value.Rw,
				Destinations = value.Dw,
				ShopAppeal = value.Sw,
				LeisureAppeal = value.Lw
			};
			zone.Snap = net.Snap(zone.Center, 700.0, walkableOnly: true);
			if (zone.Snap.Valid && !net.IsCoreSegment(zone.Snap.Segment))
			{
				// Houses in a cul-de-sac snap to the dead-end street itself, which no bus line may
				// enter. Reach the zone from the nearest road that a bus can drive through instead.
				SnapResult coreSnap = net.SnapToCore(zone.Center, s.WalkRadius, walkableOnly: true);
				if (coreSnap.Valid)
				{
					zone.Snap = coreSnap;
				}
			}
			if (zone.Snap.Valid && net.IsCoreSegment(zone.Snap.Segment))
			{
				RoadSegment roadSegment = net.Segments[zone.Snap.Segment];
				int num3 = net.FromNode(zone.Snap.Segment);
				int num4 = net.ToNode(zone.Snap.Segment);
				bool flag2 = zone.Snap.Offset <= roadSegment.Length - zone.Snap.Offset;
				zone.AccessNode = (flag2 ? num3 : num4);
				if (!zoneModel.BySegment.TryGetValue(zone.Snap.Segment, out var value2))
				{
					value2 = (zoneModel.BySegment[zone.Snap.Segment] = new List<int>());
				}
				value2.Add(zone.Id);
			}
			else if (zone.Snap.Valid)
			{
				zone.AccessNode = net.NearestCoreNode(zone.Center, s.WalkRadius);
			}
			dictionary[item2.Key] = zone.Id;
			zoneModel.Zones.Add(zone);
		}
		if (city.Trips.Count > 0)
		{
			foreach (Trip trip in city.Trips)
			{
				if (!(trip.Weight <= 0.0) && dictionary.TryGetValue(CellKey(trip.From), out var value3) && dictionary.TryGetValue(CellKey(trip.To), out var value4) && value3 != value4)
				{
					zoneModel.AddFlow(value3, value4, trip.Weight * s.CommuteWeight);
				}
			}
		}
		else
		{
			zoneModel.AddGravity((Zone z) => z.Residents, (Zone z) => z.Destinations, s.CommuteWeight, s, countArrivals: false);
		}
		if (s.ShoppingRate > 0.0)
		{
			zoneModel.AddGravity((Zone z) => z.Residents, (Zone z) => z.ShopAppeal, s.ShoppingRate, s, countArrivals: true, s.MinErrandDistance, s.MaxErrandDistance);
		}
		if (s.LeisureRate > 0.0)
		{
			zoneModel.AddGravity((Zone z) => z.Residents, (Zone z) => z.LeisureAppeal, s.LeisureRate, s, countArrivals: true, s.MinErrandDistance, s.MaxErrandDistance);
		}
		foreach (Zone zone2 in zoneModel.Zones)
		{
			zone2.Value = zone2.Weight;
		}
		zoneModel.BuildFlowAdjacency();
		return zoneModel;
		long CellKey(Vec2 p)
		{
			return ((long)(int)Math.Floor(p.X / cell) << 32) ^ (uint)(int)Math.Floor(p.Y / cell);
		}
		CellAccumulator CellOf(Vec2 p)
		{
			long key = CellKey(p);
			if (!cells.TryGetValue(key, out var value5))
			{
				value5 = (cells[key] = new CellAccumulator());
			}
			return value5;
		}
	}

	private void AddFlow(int a, int b, double w)
	{
		long key = PairKey(a, b);
		Flow.TryGetValue(key, out var value);
		Flow[key] = value + w;
	}

	private void AddGravity(Func<Zone, double> origin, Func<Zone, double> appeal, double rate, PlannerSettings s, bool countArrivals, double minDistance = double.NaN, double maxDistance = double.NaN)
	{
		if (double.IsNaN(minDistance))
		{
			minDistance = s.MinTripDistance;
		}
		if (double.IsNaN(maxDistance))
		{
			maxDistance = s.MaxTripDistance;
		}
		Dictionary<long, List<Zone>> dictionary = new Dictionary<long, List<Zone>>();
		foreach (Zone zone in Zones)
		{
			if (!(appeal(zone) <= 0.0))
			{
				long key = GKey((int)Math.Floor(zone.Center.X / 500.0), (int)Math.Floor(zone.Center.Y / 500.0));
				if (!dictionary.TryGetValue(key, out var value))
				{
					value = (dictionary[key] = new List<Zone>());
				}
				value.Add(zone);
			}
		}
		if (dictionary.Count == 0)
		{
			return;
		}
		int num = (int)Math.Ceiling(maxDistance / 500.0);
		foreach (Zone zone2 in Zones)
		{
			double num2 = origin(zone2) * rate;
			if (num2 <= 0.0)
			{
				continue;
			}
			int num3 = (int)Math.Floor(zone2.Center.X / 500.0);
			int num4 = (int)Math.Floor(zone2.Center.Y / 500.0);
			List<(Zone, double)> list2 = new List<(Zone, double)>();
			for (int i = num3 - num; i <= num3 + num; i++)
			{
				for (int j = num4 - num; j <= num4 + num; j++)
				{
					if (!dictionary.TryGetValue(GKey(i, j), out var value2))
					{
						continue;
					}
					foreach (Zone item3 in value2)
					{
						if (item3.Id != zone2.Id)
						{
							double num5 = Vec2.Distance(zone2.Center, item3.Center);
							if (!(num5 < minDistance) && !(num5 > maxDistance))
							{
								list2.Add((item3, appeal(item3) / (num5 * Math.Sqrt(num5))));
							}
						}
					}
				}
			}
			if (list2.Count == 0)
			{
				continue;
			}
			List<(Zone, double)> list3 = (from x in list2
				orderby x.Item2 descending, x.Item1.Id
				select x).Take(s.GravityNeighbours).ToList();
			double num6 = list3.Sum<(Zone, double)>(((Zone zone, double score) x) => x.score);
			foreach (var item4 in list3)
			{
				Zone item = item4.Item1;
				double item2 = item4.Item2;
				double num7 = num2 * item2 / num6;
				AddFlow(zone2.Id, item.Id, num7);
				if (countArrivals)
				{
					item.Destinations += num7;
				}
			}
		}
		static long GKey(int x, int y)
		{
			return ((long)x << 32) ^ (uint)y;
		}
	}

	private void BuildFlowAdjacency()
	{
		long[] array2 = (FlowKeys = Flow.Keys.OrderBy((long k) => k).ToArray());
		FlowValue = array2.Select((long k) => Flow[k]).ToArray();
		List<int>[] array3 = new List<int>[Zones.Count];
		List<int>[] array4 = new List<int>[Zones.Count];
		for (int i = 0; i < Zones.Count; i++)
		{
			array3[i] = new List<int>();
			array4[i] = new List<int>();
		}
		for (int j = 0; j < array2.Length; j++)
		{
			int num = KeyA(array2[j]);
			int num2 = KeyB(array2[j]);
			array3[num].Add(num2);
			array4[num].Add(j);
			array3[num2].Add(num);
			array4[num2].Add(j);
		}
		AdjOther = array3.Select((List<int> l) => l.ToArray()).ToArray();
		AdjFlow = array4.Select((List<int> l) => l.ToArray()).ToArray();
	}
}


