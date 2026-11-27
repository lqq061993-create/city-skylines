using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using CityTransit.Core;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Notifications;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Routes;
using Game.SceneFlow;
using Game.Tools;
using Game.UI;
using Game.UI.Localization;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Edge = Game.Net.Edge;

namespace AutoTransitPlanner.Transit;

[CompilerGenerated]
public class AutoTransitToolSystem : ToolBaseSystem
{
	private enum Phase
	{
		Idle,
		Delete,
		WaitDelete,
		Planning,
		PlaceStops,
		WaitStops,
		BuildLines,
		PreviewLine,
		WaitLinePaths,
		ConfirmLine,
		Finish
	}

	private enum After
	{
		ResolveStops,
		ResumeLines,
		Finish
	}

	private struct StopPose
	{
		public Entity Road;
		public Edge Nodes;
		public float3 Position;
		public float3 Center;
		public float3 Outward;
		public quaternion Rotation;
	}

	private struct LinePoint
	{
		public Entity Waypoint;
		public Entity Connection;
		public float3 Position;
		public bool PassengerStop;
	}

	private const int PreviewFrames = 3;

	private const int MinWaitFrames = 3;

	private const int MaxWaitFrames = 90;

	private const double PathTimeoutSeconds = 12;

	private ToolOutputBarrier _barrier;

	private CityConfigurationSystem _cityConfig;

	private EntityQuery _definitionQuery;

	private EntityQuery _stopQuery;

	private EntityQuery _tempStopQuery;

	private EntityQuery _errorQuery;
	private EntityQuery _previewRouteQuery;
	private EntityQuery _pathUpdateQuery;
	private EntityQuery _routeConfigQuery;
	private IconCommandSystem _icons;

	private PrefabSystem _prefabSystem;

	private Phase _phase;

	private bool _pending;
	private bool _repairRequested;

	private CityScan _scan;

	private TransitPlan _plan;

	private Task<TransitPlan> _planTask;

	private readonly Dictionary<int, Entity> _stopEntities = new Dictionary<int, Entity>();

	private readonly Dictionary<int, StopPose> _stopPoses = new Dictionary<int, StopPose>();

	private readonly Dictionary<int, Entity> _stopPrefabs = new Dictionary<int, Entity>();

	private readonly HashSet<Entity> _usedStops = new HashSet<Entity>();
	private readonly HashSet<Entity> _existingLines = new HashSet<Entity>();
	private readonly List<int> _failedLegs = new List<int>();
	private List<LinePoint> _linePoints;
	private int[] _previewPoints;
	private RouteRepairSearch _routeRepairSearch;
	private List<Entity> _repairRoutes;
	private Entity _originalLine;
	private Entity _linePrefab;
	private int _lineNumber;
	private int _repairStopsOmitted;
	private Entity _pendingLine;
	private double _lineDeadline;
	private int _linesAdjusted;
	private string _replacementNote;

	private List<Entity> _oldStops = new List<Entity>();

	private List<Entity> _oldLines = new List<Entity>();

	private After _afterDelete;

	private bool _stopsDeduped;
	private bool _replaceExisting;
	private readonly HashSet<Entity> _existingStops = new HashSet<Entity>();

	private int _batchStep;

	private int _lineIndex;

	private int _linesBuilt;

	private int _wait;

	private int _seed;

	private bool _resumed;

	private int _reclaims;

	private bool _showSummary;

	private string _summary;

	private string _stopTypesText;

	private const int MaxReclaims = 3;

	public override string toolID => "AutoTransitTool";

	public bool IsBusy
	{
		get
		{
			if (!_pending)
			{
				return _phase != Phase.Idle;
			}
			return true;
		}
	}

	public override PrefabBase GetPrefab()
	{
		return null;
	}

	public override bool TrySetPrefab(PrefabBase prefab)
	{
		return false;
	}

	public void Request(bool showSummary = false)
	{
		_pending = true;
		_repairRequested = false;
		_showSummary = showSummary;
	}

	public void RequestRepair()
	{
		Request(showSummary: true);
		_repairRequested = true;
	}

	protected override void OnCreate()
	{
		base.OnCreate();
		m_ToolSystem.EventToolChanged += OnToolChanged;
		_barrier = base.World.GetOrCreateSystemManaged<ToolOutputBarrier>();
		_cityConfig = base.World.GetOrCreateSystemManaged<CityConfigurationSystem>();
		_prefabSystem = base.World.GetOrCreateSystemManaged<PrefabSystem>();
		_definitionQuery = GetDefinitionQuery();
		_tempStopQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Routes.TransportStop>(), ComponentType.ReadOnly<Temp>());
		_errorQuery = GetEntityQuery(ComponentType.ReadOnly<Error>());
		_stopQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Routes.TransportStop>(), ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
		_previewRouteQuery = GetEntityQuery(ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<Temp>(),
			ComponentType.ReadOnly<RouteWaypoint>(), ComponentType.ReadOnly<RouteSegment>(), ComponentType.ReadOnly<PrefabRef>(),
			ComponentType.Exclude<Deleted>());
		_pathUpdateQuery = GetEntityQuery(ComponentType.ReadOnly<Game.Common.Event>(), ComponentType.ReadOnly<PathUpdated>());
		_routeConfigQuery = GetEntityQuery(ComponentType.ReadOnly<RouteConfigurationData>());
		_icons = World.GetOrCreateSystemManaged<IconCommandSystem>();
	}

	protected override void OnDestroy()
	{
		m_ToolSystem.EventToolChanged -= OnToolChanged;
		base.OnDestroy();
	}

	private void OnToolChanged(ToolBaseSystem tool)
	{
		if (IsBusy && tool != this)
		{
			IEnumerable<string> values = new StackTrace(1, fNeedFileInfo: false).ToString().Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Take(14);
			Mod.Log.Warn($"AutoTransit: tool replaced by '{tool?.toolID}' during phase {_phase}:\n" + string.Join("\n", values));
		}
	}

	public bool TryReclaim()
	{
		if (++_reclaims <= 3)
		{
			return true;
		}
		Mod.Log.Error($"AutoTransit: the tool was taken away {3} times, giving up in phase {_phase}; press the button again");
		Abort();
		return false;
	}

	private void Abort()
	{
		_phase = Phase.Idle;
		_pending = false;
		_repairRequested = false;
		_repairRoutes = null;
		_batchStep = 0;
		_planTask = null;
		_resumed = false;
	}

	protected override void OnStopRunning()
	{
		base.EntityManager.DestroyEntity(_definitionQuery);
		if (IsBusy)
		{
			if (_batchStep <= 3)
			{
				_batchStep = 0;
			}
			_resumed = true;
			Mod.Log.Warn($"AutoTransit: the tool was replaced in phase {_phase}, taking it back");
		}
		else
		{
			Abort();
		}
		base.OnStopRunning();
	}

	protected override JobHandle OnUpdate(JobHandle inputDeps)
	{
		base.applyMode = ApplyMode.None;
		JobHandle jobHandle = DestroyDefinitions(_definitionQuery, _barrier, inputDeps);
		if (_resumed)
		{
			_resumed = false;
			if (_phase == Phase.WaitLinePaths) _phase = Phase.PreviewLine;
			base.applyMode = ApplyMode.Clear;
			_barrier.AddJobHandleForProducer(jobHandle);
			return jobHandle;
		}
		switch (_phase)
		{
		case Phase.Idle:
			if (_pending)
			{
				_pending = false;
				_batchStep = 0;
				_reclaims = 0;
				_summary = null;
				base.applyMode = ApplyMode.Clear;
				_replaceExisting = _showSummary && AutoTransitConfig.RemoveOld;
				_stopsDeduped = false;
				_existingStops.Clear();
				_existingLines.Clear();
				_usedStops.Clear();
				_linesBuilt = 0;
				_linesAdjusted = 0;
				_lineIndex = 0;
				_repairStopsOmitted = 0;
				_repairRoutes = null;
				_plan = null;
				_replacementNote = null;
				_stopTypesText = null;
				if (_repairRequested) StartRepair();
				else StartPlanning();
			}
			else
			{
				ReturnToDefaultTool();
			}
			break;
		case Phase.Delete:
			if (_oldStops.Count == 0 && _oldLines.Count == 0)
			{
				ContinueAfterDelete();
				break;
			}
			if (_batchStep == 0)
			{
				Mod.Log.Info($"AutoTransit: removing {_oldStops.Count} bus stops and {_oldLines.Count} bus lines");
			}
			if (RunBatch(CreateDeleteDefinitions))
			{
				_wait = 0;
				_phase = Phase.WaitDelete;
			}
			break;
		case Phase.WaitDelete:
			_wait++;
			if (_wait >= 3 && (AllGone() || _wait >= 90))
			{
				Mod.Log.Info($"AutoTransit[delete frame {_wait}]: {_oldStops.Count((Entity e) => base.EntityManager.Exists(e) && !base.EntityManager.HasComponent<Deleted>(e))} stops, {_oldLines.Count((Entity e) => base.EntityManager.Exists(e) && !base.EntityManager.HasComponent<Deleted>(e))} lines still alive");
				if (_afterDelete == After.Finish)
				{
					_replacementNote = AllGone()
						? "Replaced the previous bus lines. Stops reused by the new routes were kept."
						: "The game did not remove every old bus stop or line.";
				}
				else if (_afterDelete == After.ResumeLines && !AllGone())
				{
					Mod.Log.Warn("AutoTransit: the game did not remove an unverified new route. Check the route in the transport panel.");
				}
				_oldStops.Clear();
				_oldLines.Clear();
				ContinueAfterDelete();
			}
			break;
		case Phase.Planning:
			FinishPlanning();
			break;
		case Phase.PlaceStops:
			if (RunBatch(CreateStopDefinitions))
			{
				_wait = 0;
				_phase = Phase.WaitStops;
			}
			break;
		case Phase.WaitStops:
			_wait++;
			if (_wait == 3)
			{
				LogStopDiagnostics();
			}
			if (_wait < 3 || (!ResolveStopEntities() && _wait < 90))
			{
				break;
			}
			LogStopDiagnostics();
			if (!_stopsDeduped)
			{
				_stopsDeduped = true;
				List<Entity> list = FindDuplicateStops();
				if (list.Count > 0)
				{
					Mod.Log.Warn($"AutoTransit: {list.Count} duplicate stops found, removing them");
					_oldStops = list;
					_oldLines = new List<Entity>();
					_afterDelete = After.ResolveStops;
					_batchStep = 0;
					_phase = Phase.Delete;
					break;
				}
			}
			_lineIndex = 0;
			_phase = Phase.BuildLines;
			break;
		case Phase.BuildLines:
			BuildNextLine();
			break;
		case Phase.PreviewLine:
			if (!PreviewInputsExist())
			{
				SkipLine("a route or stop changed before the preview");
				break;
			}
			_lineDeadline = UnityEngine.Time.realtimeSinceStartupAsDouble + PathTimeoutSeconds;
			RefreshLinePreview();
			_phase = Phase.WaitLinePaths;
			break;
		case Phase.WaitLinePaths:
			WaitForLinePaths();
			break;
		case Phase.ConfirmLine:
			ConfirmLine();
			break;
		case Phase.Finish:
			if (_repairRoutes != null)
			{
				_summary = $"Repaired {_linesBuilt}/{_repairRoutes.Count} bus lines with failed paths. Stops omitted from their routes: {_repairStopsOmitted}.";
				if (_linesBuilt < _repairRoutes.Count) _summary += " See AutoTransitPlanner.log for the remaining routes.";
				Mod.Log.Info("AutoTransit: " + _summary);
			}
			else if (_plan != null && _plan.Lines.Count > 0)
			{
				_summary = BuildSummary();
				foreach (string line in _summary.Split('\n')) Mod.Log.Info("AutoTransit: summary - " + line);
			}
			_phase = Phase.Idle;
			ReturnToDefaultTool();
			if (_showSummary && _summary != null)
			{
				ShowSummary(_summary);
			}
			_showSummary = false;
			_summary = null;
			break;
		}
		_barrier.AddJobHandleForProducer(jobHandle);
		return jobHandle;
	}

	private void ContinueAfterDelete()
	{
		switch (_afterDelete)
		{
		case After.ResolveStops:
			_wait = 2;
			_phase = Phase.WaitStops;
			break;
		case After.ResumeLines:
			_phase = Phase.BuildLines;
			break;
		case After.Finish:
			_phase = Phase.Finish;
			break;
		}
	}

	private bool IsAlive(Entity e)
	{
		if (base.EntityManager.Exists(e))
		{
			return !base.EntityManager.HasComponent<Deleted>(e);
		}
		return false;
	}

	private bool AllGone()
	{
		if (!_oldStops.Any((Entity e) => base.EntityManager.Exists(e) && !base.EntityManager.HasComponent<Deleted>(e)))
		{
			return !_oldLines.Any((Entity e) => base.EntityManager.Exists(e) && !base.EntityManager.HasComponent<Deleted>(e));
		}
		return false;
	}

	private List<Entity> FindDuplicateStops()
	{
		var duplicates = new List<Entity>();
		var kept = new Dictionary<Entity, List<float3>>();
		foreach (Entity stop in CityScanner.FindRoadsideBusStops(EntityManager)
			.OrderByDescending(_existingStops.Contains).ThenBy(e => e.Index))
		{
			if (!EntityManager.TryGetComponent<Attached>(stop, out var attached) || attached.m_Parent == Entity.Null) continue;
			if (!kept.TryGetValue(attached.m_Parent, out var positions))
			{
				positions = new List<float3>();
				kept.Add(attached.m_Parent, positions);
			}
			float3 position = EntityManager.GetComponentData<Game.Objects.Transform>(stop).m_Position;
			if (!_existingStops.Contains(stop) && positions.Any(existing => math.distancesq(existing, position) < 2.5f * 2.5f))
				duplicates.Add(stop);
			else
				positions.Add(position);
		}
		return duplicates;
	}

	private bool RunBatch(Action createDefinitions)
	{
		if (_batchStep < 3)
		{
			createDefinitions();
			base.applyMode = ((_batchStep != 0) ? ApplyMode.Clear : ApplyMode.None);
			_batchStep++;
			return false;
		}
		if (_batchStep == 3)
		{
			createDefinitions();
			base.applyMode = ApplyMode.Apply;
			_batchStep++;
			return false;
		}
		base.applyMode = ApplyMode.Clear;
		_batchStep = 0;
		return true;
	}

	private void ReturnToDefaultTool()
	{
		if (m_ToolSystem.activeTool == this)
		{
			m_ToolSystem.activeTool = m_DefaultToolSystem;
		}
	}

	private void StartPlanning()
	{
		_scan = CityScanner.Scan(base.EntityManager, _cityConfig.defaultTheme);
		CityModel model2 = _scan.Model;
		Mod.Log.Info($"AutoTransit: scanned roads={model2.Roads.Count} (one-way={_scan.OneWaySegments}), residents={model2.Residents.Count}, " + $"destinations={model2.Destinations.Count}, commuters={_scan.Commuters}, trips={model2.Trips.Count}, " + $"shops={_scan.ShopBuildings}, leisure={_scan.LeisureBuildings}, " + $"stationStops={model2.ExistingStops.Count}, busDepots={_scan.BusDepots} (room for {_scan.BusDepotCapacity} buses)");
		if (_scan.BusDepots == 0)
		{
			Mod.Log.Warn("AutoTransit: no bus depot found - lines will be built but no buses will run until you build one.");
		}
		if (!_scan.Usable)
		{
			Mod.Log.Info($"AutoTransit: nothing to do (stopPrefab={_scan.StopPrefab != Entity.Null}, linePrefab={_scan.LinePrefab != Entity.Null})");
			_phase = Phase.Idle;
			ReturnToDefaultTool();
			return;
		}
		CityModel model = _scan.Model;
		PlannerSettings settings = AutoTransitConfig.Planner;
		if (AutoTransitConfig.SizeFleet && AutoTransitConfig.FitToDepots && _scan.BusDepotCapacity > 0)
		{
			settings.FleetBudget = _scan.BusDepotCapacity;
		}
		_planTask = Task.Run(() => TransitPlanner.Plan(model, settings));
		_phase = Phase.Planning;
	}

	private void FinishPlanning()
	{
		if (_planTask == null)
		{
			_phase = Phase.Finish;
		}
		else
		{
			if (!_planTask.IsCompleted)
			{
				return;
			}
			if (_planTask.IsFaulted)
			{
				Mod.Log.Error(_planTask.Exception?.GetBaseException() ?? new Exception("planning failed"));
				_planTask = null;
				_phase = Phase.Finish;
				return;
			}
			_plan = _planTask.Result;
			_planTask = null;
			Mod.Log.Info($"AutoTransit: planned {_plan.Stops.Count} stops, {_plan.Lines.Count} lines, coverage {_plan.Coverage:P0}, residents within walking distance {_plan.ResidentCoverage:P0} " + $"(zones={_plan.Zones}, drivable nodes={_plan.UsableNodes}/{_plan.Nodes})");
			foreach (string note in _plan.Notes)
			{
				Mod.Log.Info("AutoTransit: note - " + note);
			}
			for (int i = 0; i < _plan.Lines.Count; i++)
			{
				PlannedLine plannedLine = _plan.Lines[i];
				Mod.Log.Info($"AutoTransit: line {i + 1}: {plannedLine.Stops.Count} stops, {plannedLine.Length / 1000.0:F1} km, " + $"{plannedLine.SuggestedVehicles} buses, headway {plannedLine.SuggestedHeadway:F0}s");
			}
			if (_plan.Stops.Count == 0 || _plan.Lines.Count == 0)
			{
				_phase = Phase.Finish;
				return;
			}
			AssignStopPrefabs();
			int num = _plan.Lines.Sum((PlannedLine l) => l.SuggestedVehicles);
			if (_plan.FleetLimited)
			{
				Mod.Log.Warn($"AutoTransit: the bus depots hold only {_scan.BusDepotCapacity} buses, so the plan was cut to {_plan.Lines.Count} lines " + $"(they would like {_plan.FleetWanted} buses, and more lines would follow). Build more bus depots and press the button again.");
			}
			else if (AutoTransitConfig.SizeFleet && _scan.BusDepots > 0 && num > _scan.BusDepotCapacity)
			{
				Mod.Log.Warn($"AutoTransit: the lines want about {num} buses but the bus depots only hold {_scan.BusDepotCapacity}; " + "build more bus depots or the lines will run with fewer buses");
			}
			_seed = (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
			Mod.Log.Info("AutoTransit: stop prefab '" + _prefabSystem.GetPrefabName(_scan.StopPrefab) + "', line prefab '" + _prefabSystem.GetPrefabName(_scan.LinePrefab) + "', " + $"{_stopQuery.CalculateEntityCount()} other transport stops already in the city (train, taxi, stations)");
			_stopEntities.Clear();
			_stopPoses.Clear();
			_batchStep = 0;
			_existingStops.UnionWith(CityScanner.FindRoadsideBusStops(EntityManager));
			if (_replaceExisting) _existingLines.UnionWith(CityScanner.FindBusLines(EntityManager));
			foreach (PlannedStop stop in _plan.Stops)
			{
				if (ComputeStopPose(_scan.SegmentEntities[stop.SegmentId], stop, out var pose)) _stopPoses.Add(stop.Id, pose);
			}
			ResolveStopEntities();
			Mod.Log.Info($"AutoTransit: reusing {_stopEntities.Count} existing stops on the planned road and curb");
			_phase = Phase.PlaceStops;
		}
	}

	private void AssignStopPrefabs()
	{
		_stopPrefabs.Clear();
		foreach (StopStyle stopStyle3 in _scan.StopStyles)
		{
			Mod.Log.Info("AutoTransit: stop model " + stopStyle3);
		}
		if (_scan.IgnoredStopModels.Count > 0)
		{
			Mod.Log.Info("AutoTransit: ignored internal stop models: " + string.Join(", ", _scan.IgnoredStopModels));
		}
		List<StopStyle> list = _scan.StopStyles.Where((StopStyle x) => x.ThemeRank <= 1).ToList();
		if (list.Count == 0)
		{
			list = _scan.StopStyles;
		}
		if (list.Count == 0)
		{
			return;
		}
		string wanted = AutoTransitConfig.StopStyle;
		StopStyle stopStyle = ((wanted == "Automatic") ? null : _scan.StopStyles.FirstOrDefault((StopStyle x) => x.Name == wanted));
		if (wanted != "Automatic" && stopStyle == null)
		{
			Mod.Log.Warn("AutoTransit: the bus stop model '" + wanted + "' does not exist in this game, using automatic selection");
		}
		_stopTypesText = null;
		StopStyle stopStyle2;
		StopStyle basic;
		if (stopStyle != null)
		{
			stopStyle2 = (basic = stopStyle);
		}
		else
		{
			basic = (from x in list
				orderby x.Cost, x.Comfort
				select x).ThenBy((StopStyle x) => x.Name, StringComparer.Ordinal).First();
			List<StopStyle> list2 = list.Where((StopStyle x) => x != basic && x.Comfort > basic.Comfort).ToList();
			stopStyle2 = ((list2.Count == 0) ? basic : (from x in list2
				orderby (x.Comfort - basic.Comfort) / Math.Max(1f, (float)x.Cost - (float)basic.Cost) descending, x.Comfort descending, x.Loading descending
				select x).ThenBy((StopStyle x) => x.Name, StringComparer.Ordinal).First());
		}
		foreach (PlannedStop stop in _plan.Stops)
		{
			_stopPrefabs[stop.Id] = (stop.Comfort ? stopStyle2 : basic).Prefab;
		}
		int num = ((stopStyle == null) ? _plan.Stops.Count((PlannedStop x) => x.Comfort) : 0);
		long num2 = ((stopStyle != null) ? (stopStyle.Cost * _plan.Stops.Count) : (stopStyle2.Cost * num + basic.Cost * (_plan.Stops.Count - num)));
		string text = ((num2 > 0) ? $" List price of all new stops: about {num2:N0}." : "");
		_stopTypesText = ((stopStyle != null) ? ("Every stop is a " + stopStyle.Name + "." + text) : ((basic == stopStyle2) ? ("Every stop is a " + basic.Name + " (the game has no other bus stop model for your theme)." + text) : $"Stop types: {num} x {stopStyle2.Name} at busy stops and transfers, {_plan.Stops.Count - num} x {basic.Name} elsewhere.{text}"));
		Mod.Log.Info((stopStyle != null) ? ("AutoTransit: every stop uses '" + stopStyle.Name + "'") : ($"AutoTransit: basic stops use '{basic.Name}', comfortable stops use '{stopStyle2.Name}': {num} comfortable, " + $"{_plan.Stops.Count - num} basic stops"));
	}

	private string BuildSummary()
	{
		int busDepots = _scan.BusDepots;
		int depotCapacityEach = _scan.DepotCapacityEach;
		int busDepotCapacity = _scan.BusDepotCapacity;
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append($"Game-verified bus lines: {_linesBuilt}/{_plan.Lines.Count}. Stops served: {_usedStops.Count}.");
		if (_linesAdjusted > 0)
		{
			stringBuilder.Append($"\nAdjusted {_linesAdjusted} line(s) by leaving out a missing or unreachable stop.");
		}
		if (_linesBuilt < _plan.Lines.Count)
		{
			stringBuilder.Append($"\nSkipped {_plan.Lines.Count - _linesBuilt} line(s). See AutoTransitPlanner.log for details.");
		}
		if (_replacementNote != null) stringBuilder.Append('\n').Append(_replacementNote);
		if (_linesBuilt == 0) return stringBuilder.ToString();
		stringBuilder.Append($"\nOriginal plan estimated about {_plan.FullFleet} buses (at least {_plan.FullMinFleet}).");
		if (depotCapacityEach > 0)
		{
			int num = (_plan.FullFleet + depotCapacityEach - 1) / depotCapacityEach;
			int num2 = (_plan.FullMinFleet + depotCapacityEach - 1) / depotCapacityEach;
			stringBuilder.Append($"\nDepots needed: about {num} (at least {num2}); one depot holds about {depotCapacityEach} buses.");
			stringBuilder.Append("\nYour city has " + Count(busDepots, "bus depot") + " (" + Count(busDepotCapacity, "bus") + ").");
			if (num > busDepots)
			{
				stringBuilder.Append(_plan.FleetLimited ? $"\nMissing: about {num - busDepots} depots. Add depots and press the button again to grow the network." : $"\nMissing: about {num - busDepots} depots, so the lines will run with fewer buses.");
			}
			else
			{
				stringBuilder.Append("\nYour depots are enough.");
			}
		}
		else
		{
			stringBuilder.Append("\nYour city has " + Count(busDepots, "bus depot") + " (" + Count(busDepotCapacity, "bus") + ").");
		}
		if (_stopTypesText != null)
		{
			stringBuilder.Append("\nOriginal plan. ").Append(_stopTypesText);
		}
		if (busDepots == 0)
		{
			stringBuilder.Append("\nWithout a bus depot no buses will run on any line.");
		}
		return stringBuilder.ToString();
		static string Count(int n, string noun)
		{
			if (n != 1)
			{
				return string.Format("{0} {1}{2}", n, noun, noun.EndsWith("s") ? "es" : "s");
			}
			return $"{n} {noun}";
		}
	}

	private void ShowSummary(string message)
	{
		try
		{
			UserInterface userInterface = GameManager.instance?.userInterface;
			if (userInterface != null)
			{
				object obj = userInterface.GetType().GetProperty("appBindings")?.GetValue(userInterface);
				MethodInfo methodInfo = obj?.GetType().GetMethod("ShowMessageDialog", new Type[2]
				{
					typeof(MessageDialog),
					typeof(Action<int>)
				});
				if (methodInfo == null)
				{
					Mod.Log.Warn("AutoTransit: the game has no message dialog to show the summary");
					return;
				}
				MessageDialog messageDialog = new MessageDialog(LocalizedString.Value("Bus plan ready"), LocalizedString.Value(message), LocalizedString.Value("OK"));
				methodInfo.Invoke(obj, new object[2]
				{
					messageDialog,
					(Action<int>)delegate
					{
					}
				});
			}
		}
		catch (Exception ex)
		{
			Mod.Log.Warn("AutoTransit: could not show the summary dialog: " + ex.Message);
		}
	}

	private void CreateDeleteDefinitions()
	{
		EntityCommandBuffer ecb = _barrier.CreateCommandBuffer();
		HashSet<Entity> hashSet = new HashSet<Entity>();
		foreach (Entity oldLine in _oldLines)
		{
			if (!IsAlive(oldLine)) continue;
			Entity e = ecb.CreateEntity();
			ecb.AddComponent(e, new CreationDefinition
			{
				m_Prefab = base.EntityManager.GetComponentData<PrefabRef>(oldLine).m_Prefab,
				m_Original = oldLine,
				m_Flags = CreationFlags.Delete
			});
			Color32 color = (base.EntityManager.HasComponent<Game.Routes.Color>(oldLine) ? base.EntityManager.GetComponentData<Game.Routes.Color>(oldLine).m_Color : default(Color32));
			ecb.AddComponent(e, new ColorDefinition
			{
				m_Color = color
			});
			ecb.AddBuffer<WaypointDefinition>(e);
			ecb.AddComponent(e, default(Updated));
		}
		foreach (Entity oldStop in _oldStops)
		{
			if (!IsAlive(oldStop)) continue;
			Game.Objects.Transform componentData = base.EntityManager.GetComponentData<Game.Objects.Transform>(oldStop);
			Entity e2 = ecb.CreateEntity();
			ecb.AddComponent(e2, new CreationDefinition
			{
				m_Original = oldStop,
				m_Flags = CreationFlags.Delete
			});
			ecb.AddComponent(e2, new ObjectDefinition
			{
				m_Position = componentData.m_Position,
				m_LocalPosition = componentData.m_Position,
				m_Rotation = componentData.m_Rotation,
				m_LocalRotation = componentData.m_Rotation,
				m_ParentMesh = -1,
				m_Probability = 100,
				m_PrefabSubIndex = -1
			});
			ecb.AddComponent(e2, default(Updated));
			if (base.EntityManager.HasComponent<Attached>(oldStop))
			{
				Entity parent = base.EntityManager.GetComponentData<Attached>(oldStop).m_Parent;
				if (hashSet.Add(parent))
				{
					CreateEdgeRefresh(ecb, parent);
				}
			}
		}
	}

	private void CreateEdgeRefresh(EntityCommandBuffer ecb, Entity edgeEntity)
	{
		if (base.EntityManager.HasComponent<Edge>(edgeEntity) && base.EntityManager.HasComponent<Curve>(edgeEntity))
		{
			Edge componentData = base.EntityManager.GetComponentData<Edge>(edgeEntity);
			Bezier4x3 bezier = base.EntityManager.GetComponentData<Curve>(edgeEntity).m_Bezier;
			Entity e = ecb.CreateEntity();
			ecb.AddComponent(e, new CreationDefinition
			{
				m_Original = edgeEntity,
				m_Flags = CreationFlags.Align
			});
			ecb.AddComponent(e, default(Updated));
			NetCourse component = default(NetCourse);
			component.m_Curve = bezier;
			component.m_Length = MathUtils.Length(bezier);
			component.m_FixedIndex = -1;
			component.m_StartPosition.m_Entity = componentData.m_Start;
			component.m_StartPosition.m_Position = bezier.a;
			component.m_StartPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(bezier));
			component.m_StartPosition.m_CourseDelta = 0f;
			component.m_EndPosition.m_Entity = componentData.m_End;
			component.m_EndPosition.m_Position = bezier.d;
			component.m_EndPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(bezier));
			component.m_EndPosition.m_CourseDelta = 1f;
			ecb.AddComponent(e, component);
		}
	}

	private void CreateStopDefinitions()
	{
		EntityCommandBuffer entityCommandBuffer = _barrier.CreateCommandBuffer();
		foreach (var target in _stopPoses)
		{
			int stopId = target.Key;
			StopPose pose = target.Value;
			if (_stopEntities.TryGetValue(stopId, out Entity existing) && IsAlive(existing) || !IsAlive(pose.Road)) continue;
			Entity e = entityCommandBuffer.CreateEntity();
			entityCommandBuffer.AddComponent(e, new CreationDefinition
			{
				m_Prefab = _stopPrefabs.TryGetValue(stopId, out var prefab) ? prefab : _scan.StopPrefab,
				m_Attached = pose.Road,
				m_Flags = CreationFlags.Attach,
				m_RandomSeed = _seed + stopId
			});
			entityCommandBuffer.AddComponent(e, new ObjectDefinition
			{
				m_Position = pose.Position,
				m_LocalPosition = pose.Position,
				m_Rotation = pose.Rotation,
				m_LocalRotation = pose.Rotation,
				m_Scale = new float3(1f),
				m_Intensity = 1f,
				m_ParentMesh = -1,
				m_Probability = 100,
				m_PrefabSubIndex = -1
			});
			entityCommandBuffer.AddComponent(e, default(Updated));
		}
	}

	private bool ComputeStopPose(Entity road, PlannedStop stop, out StopPose pose)
	{
		pose = default;
		if (!IsAlive(road) || !EntityManager.TryGetComponent<Curve>(road, out var curve) || !EntityManager.TryGetComponent<Edge>(road, out var nodes))
		{
			return false;
		}
		MathUtils.Distance(curve.m_Bezier.xz, new float2((float)stop.Position.X, (float)stop.Position.Y), out float t);
		float3 center = MathUtils.Position(curve.m_Bezier, t);
		float3 direction = math.normalizesafe(new float3((float)stop.Direction.X, 0f, (float)stop.Direction.Y), new float3(0f, 0f, 1f));
		float3 outward = new float3(direction.z, 0f, -direction.x);
		if (_cityConfig.leftHandTraffic)
		{
			outward = -outward;
		}
		pose = new StopPose
		{
			Road = road,
			Nodes = nodes,
			Center = center,
			Outward = outward,
			Position = center + outward * (RoadHalfWidth(road) + AutoTransitConfig.StopSideOffset),
			Rotation = quaternion.LookRotationSafe(-outward, math.up())
		};
		return true;
	}

	private float RoadHalfWidth(Entity edge)
	{
		if (base.EntityManager.HasComponent<PrefabRef>(edge))
		{
			Entity prefab = base.EntityManager.GetComponentData<PrefabRef>(edge).m_Prefab;
			if (base.EntityManager.HasComponent<NetGeometryData>(prefab))
			{
				return base.EntityManager.GetComponentData<NetGeometryData>(prefab).m_DefaultWidth * 0.5f;
			}
		}
		return 4f;
	}

	private void LogStopDiagnostics()
	{
		Mod.Log.Info($"AutoTransit[frame {_wait}]: stops={_stopQuery.CalculateEntityCount()}, " + $"tempStops={_tempStopQuery.CalculateEntityCount()}, errors={_errorQuery.CalculateEntityCount()}, " + $"definitionsLeft={_definitionQuery.CalculateEntityCount()}");
	}

	private bool ResolveStopEntities()
	{
		_stopEntities.Clear();
		var candidates = new List<(float Distance, int StopId, Entity Stop)>();
		var stops = new List<(Entity Stop, Entity Road, Edge Nodes, float3 Position)>();
		using (NativeArray<Entity> entities = _stopQuery.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity stop in entities)
			{
				if (EntityManager.HasComponent<Owner>(stop) || !EntityManager.TryGetComponent<PrefabRef>(stop, out var prefab) ||
					!EntityManager.TryGetComponent<TransportStopData>(prefab.m_Prefab, out var stopData) ||
					stopData.m_TransportType != TransportType.Bus || !stopData.m_PassengerTransport ||
					!EntityManager.TryGetComponent<Attached>(stop, out var attached) || !IsAlive(attached.m_Parent) ||
					!EntityManager.TryGetComponent<Edge>(attached.m_Parent, out var nodes))
				{
					continue;
				}
				stops.Add((stop, attached.m_Parent, nodes, EntityManager.GetComponentData<Game.Objects.Transform>(stop).m_Position));
			}
		}
		foreach (var target in _stopPoses)
		{
			StopPose pose = target.Value;
			bool roadAlive = IsAlive(pose.Road);
			foreach (var stop in stops)
			{
				if (RoadsideStopMatch.IsMatch(pose.Road, pose.Nodes, roadAlive, pose.Center, pose.Outward, pose.Position,
					stop.Road, stop.Nodes, stop.Position))
				{
					candidates.Add((math.distancesq(stop.Position, pose.Position), target.Key, stop.Stop));
				}
			}
		}
		var matchedStops = new HashSet<Entity>();
		foreach (var candidate in candidates.OrderBy(candidate => candidate.Distance).ThenBy(candidate => candidate.Stop.Index))
		{
			if (!_stopEntities.ContainsKey(candidate.StopId) && matchedStops.Add(candidate.Stop))
				_stopEntities.Add(candidate.StopId, candidate.Stop);
		}
		return _stopEntities.Count == _stopPoses.Count;
	}

	private void BuildNextLine()
	{
		if (_repairRoutes != null)
		{
			PrepareNextRepair();
			return;
		}
		if (_lineIndex >= _plan.Lines.Count)
		{
			FinishBuildingLines();
			return;
		}
		_originalLine = Entity.Null;
		_linePrefab = _scan.LinePrefab;
		_lineNumber = _lineIndex + 1;
		PlannedLine line = _plan.Lines[_lineIndex];
		_linePoints = new List<LinePoint>(line.Stops.Count);
		foreach (PlannedStop stop in line.Stops)
		{
			if (_stopEntities.TryGetValue(stop.Id, out Entity entity) && IsAlive(entity) &&
				EntityManager.TryGetComponent<Game.Objects.Transform>(entity, out var transform))
			{
				_linePoints.Add(new LinePoint { Connection = entity, Position = transform.m_Position, PassengerStop = true });
			}
		}
		if (_linePoints.Count < 2)
		{
			SkipLine($"only {_linePoints.Count}/{line.Stops.Count} stops matched the planned road and curb");
			return;
		}
		if (_linePoints.Count != line.Stops.Count)
		{
			Mod.Log.Warn($"AutoTransit: {LineLabel}: using {_linePoints.Count}/{line.Stops.Count} matched stops");
		}
		SelectInitialPoints();
		_phase = Phase.PreviewLine;
	}

	private void StartRepair()
	{
		_repairRoutes = CityScanner.FindBusLines(EntityManager).Where(CollectExistingFailures).ToList();
		Mod.Log.Info($"AutoTransit: found {_repairRoutes.Count} existing passenger bus lines with confirmed path failures");
		_phase = Phase.BuildLines;
	}

	private bool CollectExistingFailures(Entity line)
	{
		_failedLegs.Clear();
		if (!IsAlive(line) || !EntityManager.TryGetComponent<Route>(line, out var route) ||
			(route.m_Flags & RouteFlags.Complete) == 0 ||
			!EntityManager.TryGetBuffer(line, isReadOnly: true, out DynamicBuffer<RouteSegment> segments)) return false;
		for (int leg = 0; leg < segments.Length; leg++)
		{
			if (EntityManager.TryGetComponent<PathInformation>(segments[leg].m_Segment, out var path) &&
				RoutePathValidation.HasConfirmedFailure(path)) _failedLegs.Add(leg);
		}
		return _failedLegs.Count != 0;
	}

	private void PrepareNextRepair()
	{
		if (_lineIndex >= _repairRoutes.Count)
		{
			_phase = Phase.Finish;
			return;
		}
		_originalLine = _repairRoutes[_lineIndex];
		_lineNumber = EntityManager.TryGetComponent<RouteNumber>(_originalLine, out var number) ? number.m_Number : _originalLine.Index;
		if (!CollectExistingFailures(_originalLine))
		{
			AdvanceLine();
			return;
		}
		if (!EntityManager.TryGetComponent<PrefabRef>(_originalLine, out var prefab) ||
			!EntityManager.TryGetBuffer(_originalLine, isReadOnly: true, out DynamicBuffer<RouteWaypoint> waypoints) ||
			waypoints.Length < 2 || _failedLegs[_failedLegs.Count - 1] >= waypoints.Length)
		{
			SkipLine("the existing line has incomplete route data");
			return;
		}
		_linePrefab = prefab.m_Prefab;
		_linePoints = new List<LinePoint>(waypoints.Length);
		foreach (RouteWaypoint waypoint in waypoints)
		{
			Entity entity = waypoint.m_Waypoint;
			if (!EntityManager.TryGetComponent<Game.Routes.Position>(entity, out var position))
			{
				SkipLine("an existing waypoint is unavailable");
				return;
			}
			Entity connection = LiveConnection(entity);
			_linePoints.Add(new LinePoint
			{
				Waypoint = entity,
				Connection = connection,
				Position = position.m_Position,
				PassengerStop = connection != Entity.Null && EntityManager.HasComponent<Game.Routes.TransportStop>(connection)
			});
		}
		SelectInitialPoints();
		RetryUnreachableLine();
	}

	private void SelectInitialPoints()
	{
		_previewPoints = Enumerable.Range(0, _linePoints.Count).ToArray();
		_routeRepairSearch = new RouteRepairSearch(_linePoints.Select(point => point.PassengerStop).ToArray());
	}

	private Entity LiveConnection(Entity waypoint)
	{
		return EntityManager.TryGetComponent<Connected>(waypoint, out var connected) && IsAlive(connected.m_Connected)
			? connected.m_Connected : Entity.Null;
	}

	private bool PreviewInputsExist()
	{
		foreach (int index in _previewPoints)
		{
			Entity connection = _linePoints[index].Connection;
			if (connection != Entity.Null && !IsAlive(connection)) return false;
		}
		if (_originalLine == Entity.Null) return true;
		if (!IsAlive(_originalLine) ||
			!EntityManager.TryGetBuffer(_originalLine, isReadOnly: true, out DynamicBuffer<RouteWaypoint> waypoints) ||
			waypoints.Length != _linePoints.Count) return false;
		for (int i = 0; i < waypoints.Length; i++)
		{
			Entity waypoint = waypoints[i].m_Waypoint;
			if (waypoint != _linePoints[i].Waypoint || LiveConnection(waypoint) != _linePoints[i].Connection) return false;
		}
		return true;
	}

	private string LineLabel => _originalLine == Entity.Null ? $"new line {_lineNumber}" : $"existing line {_lineNumber}";

	private void RefreshLinePreview()
	{
		EntityCommandBuffer commands = _barrier.CreateCommandBuffer();
		Entity definition = commands.CreateEntity();
		commands.AddComponent(definition, new CreationDefinition
		{
			m_Original = _originalLine,
			m_Prefab = _linePrefab,
			m_RandomSeed = _seed + _lineIndex
		});
		DynamicBuffer<WaypointDefinition> waypoints = commands.AddBuffer<WaypointDefinition>(definition);
		// New routes close by repeating their first point. Native edits close the original route automatically.
		foreach (int index in _previewPoints)
		{
			LinePoint point = _linePoints[index];
			waypoints.Add(new WaypointDefinition(point.Position) { m_Connection = point.Connection, m_Original = point.Waypoint });
		}
		if (_originalLine == Entity.Null)
		{
			waypoints.Add(waypoints[0]);
			commands.AddComponent(definition, new ColorDefinition { m_Color = LineColor(_lineIndex) });
		}
		commands.AddComponent(definition, default(Updated));
		base.applyMode = ApplyMode.Clear;
	}

	private bool TryGetLinePreview(out Entity route)
	{
		route = Entity.Null;
		using (NativeArray<Entity> previews = _previewRouteQuery.ToEntityArray(Allocator.Temp))
		{
			foreach (Entity candidate in previews)
			{
				Temp temp = EntityManager.GetComponentData<Temp>(candidate);
				TempFlags required = _originalLine == Entity.Null ? TempFlags.Create : TempFlags.Modify;
				if ((temp.m_Flags & required) == 0 || temp.m_Original != _originalLine ||
					EntityManager.GetComponentData<PrefabRef>(candidate).m_Prefab != _linePrefab ||
					(EntityManager.GetComponentData<Route>(candidate).m_Flags & RouteFlags.Complete) == 0)
				{
					continue;
				}
				if (!MatchesDraft(candidate, preview: true)) continue;
				if (route != Entity.Null) return false;
				route = candidate;
			}
		}
		return route != Entity.Null;
	}

	private bool MatchesDraft(Entity route, bool preview)
	{
		if (!EntityManager.TryGetBuffer(route, isReadOnly: true, out DynamicBuffer<RouteWaypoint> waypoints) ||
			!EntityManager.TryGetBuffer(route, isReadOnly: true, out DynamicBuffer<RouteSegment> segments) ||
			waypoints.Length != _previewPoints.Length || segments.Length != waypoints.Length) return false;
		for (int i = 0; i < waypoints.Length; i++)
		{
			LinePoint point = _linePoints[_previewPoints[i]];
			Entity waypoint = waypoints[i].m_Waypoint;
			if (!IsAlive(waypoint) || LiveConnection(waypoint) != point.Connection) return false;
			if (point.Waypoint != Entity.Null)
			{
				Entity original = preview && EntityManager.TryGetComponent<Temp>(waypoint, out var temp) ? temp.m_Original : waypoint;
				if (original != point.Waypoint) return false;
			}
		}
		return true;
	}

	private bool HasPreviewPathUpdates(DynamicBuffer<RouteSegment> segments)
	{
		if (_pathUpdateQuery.IsEmptyIgnoreFilter) return false;
		using (NativeArray<PathUpdated> updates = _pathUpdateQuery.ToComponentDataArray<PathUpdated>(Allocator.Temp))
		{
			foreach (PathUpdated update in updates)
			{
				foreach (RouteSegment leg in segments)
				{
					if (leg.m_Segment == update.m_Owner) return true;
				}
			}
		}
		return false;
	}

	private void WaitForLinePaths()
	{
		if (!PreviewInputsExist())
		{
			SkipLine("a route or stop changed during the preview");
			return;
		}
		if (UnityEngine.Time.realtimeSinceStartupAsDouble >= _lineDeadline)
		{
			SkipLine($"the game did not verify all paths within {PathTimeoutSeconds} seconds");
			return;
		}
		if (!TryGetLinePreview(out Entity route)) return;
		DynamicBuffer<RouteSegment> segments = EntityManager.GetBuffer<RouteSegment>(route, isReadOnly: true);
		if (HasPreviewPathUpdates(segments))
		{
			// The native route tool refreshes the preview so validation sees completed paths.
			RefreshLinePreview();
			return;
		}
		if (!ReadSettledPaths(route, segments)) return;
		if (_failedLegs.Count != 0)
		{
			RetryUnreachableLine();
			return;
		}
		if (!GetAllowApply())
		{
			SkipLine($"game validation refused the preview ({_errorQuery.CalculateEntityCount()} tool errors)");
			return;
		}
		_pendingLine = route;
		_lineDeadline = UnityEngine.Time.realtimeSinceStartupAsDouble + PathTimeoutSeconds;
		base.applyMode = ApplyMode.Apply;
		_phase = Phase.ConfirmLine;
	}

	private bool ReadSettledPaths(Entity route, DynamicBuffer<RouteSegment> segments)
	{
		DynamicBuffer<RouteWaypoint> waypoints = EntityManager.GetBuffer<RouteWaypoint>(route, isReadOnly: true);
		_failedLegs.Clear();
		for (int i = 0; i < segments.Length; i++)
		{
			Entity segment = segments[i].m_Segment;
			Entity start = waypoints[i].m_Waypoint;
			Entity end = waypoints[(i + 1) % waypoints.Length].m_Waypoint;
			if (!EntityManager.TryGetComponent<PathInformation>(segment, out var path) ||
				!EntityManager.TryGetComponent<PathTargets>(segment, out var targets) ||
				!EntityManager.TryGetComponent<Game.Routes.Position>(start, out var from) ||
				!EntityManager.TryGetComponent<Game.Routes.Position>(end, out var to)) return false;
			bool hasPath = EntityManager.TryGetBuffer(segment, isReadOnly: true, out DynamicBuffer<PathElement> elements) && elements.Length > 0;
			SegmentPathStatus status = RoutePathValidation.GetSegmentStatus(path, targets, from.m_Position, to.m_Position, hasPath);
			if (status == SegmentPathStatus.Waiting) return false;
			if (status == SegmentPathStatus.Failed) _failedLegs.Add(i);
		}
		return true;
	}

	private void RetryUnreachableLine()
	{
		foreach (int leg in _failedLegs)
		{
			int from = _previewPoints[leg];
			int to = _previewPoints[(leg + 1) % _previewPoints.Length];
			Mod.Log.Warn($"AutoTransit: {LineLabel}: game found no path from waypoint {from + 1} {PointLocation(from)} to waypoint {to + 1} {PointLocation(to)}");
		}
		int[] next = _routeRepairSearch.Next(_previewPoints, _failedLegs);
		if (next == null)
		{
			SkipLine("the game could not connect every leg");
			return;
		}
		_previewPoints = next;
		var omitted = Enumerable.Range(0, _linePoints.Count).Except(next).Select(i => $"{i + 1} {PointLocation(i)}");
		Mod.Log.Info($"AutoTransit: {LineLabel}: retrying without waypoints {string.Join(", ", omitted)}");
		base.applyMode = ApplyMode.Clear;
		_phase = Phase.PreviewLine;
	}

	private string PointLocation(int index)
	{
		float3 position = _linePoints[index].Position;
		return $"({position.x:F0}, {position.y:F0}, {position.z:F0})";
	}

	private void ConfirmLine()
	{
		bool repair = _originalLine != Entity.Null;
		Entity committed = repair ? _originalLine : _pendingLine;
		if ((!repair || !IsAlive(_pendingLine)) && IsAlive(committed) && !EntityManager.HasComponent<Temp>(committed) &&
			MatchesDraft(committed, preview: false))
		{
			var segments = EntityManager.GetBuffer<RouteSegment>(committed, isReadOnly: true);
			if (ReadSettledPaths(committed, segments) && _failedLegs.Count == 0)
			{
				RecordVerifiedLine(committed, repair);
				AdvanceLine();
				return;
			}
		}
		if (UnityEngine.Time.realtimeSinceStartupAsDouble >= _lineDeadline || !IsAlive(committed))
		{
			if (!repair && IsAlive(committed) && !EntityManager.HasComponent<Temp>(committed))
			{
				SkipLine("the committed route still has unverified paths; removing this new route");
				_oldLines = new List<Entity> { committed };
				_oldStops = new List<Entity>();
				_afterDelete = After.ResumeLines;
				_phase = Phase.Delete;
				return;
			}
			SkipLine("the game did not confirm the route after applying it");
		}
	}

	private void RecordVerifiedLine(Entity line, bool repair)
	{
		_linesBuilt++;
		if (repair)
		{
			int omittedStops = _linePoints.Count(point => point.PassengerStop) - _previewPoints.Count(i => _linePoints[i].PassengerStop);
			_repairStopsOmitted += omittedStops;
			ClearRepairedPathWarnings(line);
			Mod.Log.Info($"AutoTransit: {LineLabel}: game verified all {_previewPoints.Length} legs after editing in place; omitted {omittedStops} passenger stops from the line");
			return;
		}
		PlannedLine plan = _plan.Lines[_lineIndex];
		if (_previewPoints.Length != plan.Stops.Count) _linesAdjusted++;
		_usedStops.UnionWith(_previewPoints.Select(i => _linePoints[i].Connection));
		Mod.Log.Info($"AutoTransit: {LineLabel}: game verified all {_previewPoints.Length} legs after creating the route");
		if (AutoTransitConfig.SizeFleet) FleetSizingSystem.Enqueue(line, _lineNumber, plan.SuggestedVehicles);
	}

	private void ClearRepairedPathWarnings(Entity line)
	{
		if (!_routeConfigQuery.TryGetSingleton(out RouteConfigurationData config) || config.m_PathfindNotification == Entity.Null) return;
		// A path reused from the preview may have completed while it was temporary, before the game could clear the old warning.
		var commands = _icons.CreateCommandBuffer();
		foreach (RouteWaypoint waypoint in EntityManager.GetBuffer<RouteWaypoint>(line, isReadOnly: true))
		{
			commands.Remove(waypoint.m_Waypoint, config.m_PathfindNotification);
		}
	}

	private void SkipLine(string reason)
	{
		Mod.Log.Warn($"AutoTransit: {LineLabel} skipped: {reason}");
		AdvanceLine();
	}

	private void AdvanceLine()
	{
		_pendingLine = Entity.Null;
		_lineIndex++;
		base.applyMode = ApplyMode.Clear;
		_phase = Phase.BuildLines;
	}

	private void FinishBuildingLines()
	{
		_phase = Phase.Finish;
		Mod.Log.Info($"AutoTransit: done, {_stopEntities.Count}/{_plan.Stops.Count} stops matched, {_linesBuilt}/{_plan.Lines.Count} game-verified lines committed, {_linesAdjusted} adjusted");
		if (!_replaceExisting || _existingLines.Count == 0 && _existingStops.Count == 0) return;
		if (_linesBuilt != _plan.Lines.Count)
		{
			_replacementNote = "Kept the previous network because some planned routes could not be verified.";
			return;
		}
		_oldLines = _existingLines.Where(IsAlive).ToList();
		_oldStops = _existingStops.Where(stop => IsAlive(stop) && !_usedStops.Contains(stop) && !HasOtherLine(stop)).ToList();
		_afterDelete = After.Finish;
		_phase = Phase.Delete;
	}

	private bool HasOtherLine(Entity stop)
	{
		if (EntityManager.TryGetBuffer(stop, isReadOnly: true, out DynamicBuffer<ConnectedRoute> connections))
		{
			foreach (ConnectedRoute connection in connections)
			{
				if (!IsAlive(connection.m_Waypoint)) continue;
				if (!EntityManager.TryGetComponent<Owner>(connection.m_Waypoint, out var owner) || !_existingLines.Contains(owner.m_Owner)) return true;
			}
		}
		return false;
	}

	private static Color32 LineColor(int index)
	{
		return UnityEngine.Color.HSVToRGB((float)index * 0.618034f % 1f, 0.75f, 0.9f);
	}
}

