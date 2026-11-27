using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.Tools;
using Unity.Entities;
using UnityEngine;

namespace AutoTransitPlanner.Transit;

[CompilerGenerated]
public class AutoTransitController : GameSystemBase
{
	private ToolSystem _toolSystem;

	private DefaultToolSystem _defaultTool;

	private AutoTransitToolSystem _autoTool;

	private bool _inGame;

	private float _nextRun;

	private static bool _manualRequest;
	private static bool _repairRequest;

	public static void RequestRun()
	{
		_manualRequest = true;
	}

	public static void RequestRepair()
	{
		_repairRequest = true;
	}

	protected override void OnCreate()
	{
		base.OnCreate();
		_toolSystem = base.World.GetOrCreateSystemManaged<ToolSystem>();
		_defaultTool = base.World.GetOrCreateSystemManaged<DefaultToolSystem>();
		_autoTool = base.World.GetOrCreateSystemManaged<AutoTransitToolSystem>();
	}

	protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
	{
		base.OnGameLoadingComplete(purpose, mode);
		_inGame = mode == GameMode.Game;
		RefreshStopStyles();
		_nextRun = UnityEngine.Time.realtimeSinceStartup + AutoTransitConfig.FirstRunDelay;
	}

	private void RefreshStopStyles()
	{
		try
		{
			Entity defaultTheme = base.World.GetOrCreateSystemManaged<CityConfigurationSystem>().defaultTheme;
			AutoTransitConfig.AvailableStopStyles = (from x in CityScanner.FindBusStopStyles(base.EntityManager, defaultTheme)
				select x.Name).Distinct().OrderBy((string x) => x, StringComparer.Ordinal).ToList();
		}
		catch (Exception ex)
		{
			Mod.Log.Warn("AutoTransit: could not list the bus stop models: " + ex.Message);
		}
	}

	protected override void OnUpdate()
	{
		if (!_inGame)
		{
			_manualRequest = false;
			_repairRequest = false;
			return;
		}
		if (_autoTool.IsBusy && _toolSystem.activeTool != _autoTool)
		{
			if (_autoTool.TryReclaim())
			{
				_toolSystem.activeTool = _autoTool;
			}
			return;
		}
		bool flag = AutoTransitConfig.Enabled && UnityEngine.Time.realtimeSinceStartup >= _nextRun;
		if ((_manualRequest || _repairRequest || flag) && !_autoTool.IsBusy && _toolSystem.activeTool == _defaultTool)
		{
			if (_repairRequest)
			{
				_repairRequest = false;
				_manualRequest = false;
				_autoTool.RequestRepair();
				_toolSystem.activeTool = _autoTool;
				return;
			}
			bool manualRequest = _manualRequest;
			_manualRequest = false;
			_nextRun = UnityEngine.Time.realtimeSinceStartup + AutoTransitConfig.Interval;
			// Automatic planning is for cities without passenger bus lines.
			// Re-running after a network is built would add another set of routes.
			if (!manualRequest && CityScanner.FindBusLines(EntityManager).Count != 0) return;
			_autoTool.Request(manualRequest);
			_toolSystem.activeTool = _autoTool;
		}
	}
}

