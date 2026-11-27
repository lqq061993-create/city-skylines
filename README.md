# Auto Transit Planner 0.3.0

A Cities: Skylines II code mod that reads city demand, plans passenger bus lines, and
builds the stops and routes with the in-game tools. It can also repair existing passenger
bus lines whose paths the game refuses to recalculate.

> **本分支是 Auto Transit Planner 的独立分支**（`feat/auto-transit-planner-0.3.0`）。
> 分支根目录就是该模组的完整源码，与 `main` 分支的 IndustryMaster（产业统合控制）
> 不是同一个模组，两边互不依赖。获取方式：
>
> ```bash
> git clone -b feat/auto-transit-planner-0.3.0 https://github.com/lqq061993-create/city-skylines.git
> ```

## What was wrong in 0.2.3

The planner treated "a bus can drive in and out of this node" as equivalent to "a bus can
run a line here". Both are true for a bidirectional cul-de-sac, so the planner happily put
stops, line seeds and detours inside dead-end streets. When the game then tried to build a
closed loop through such a place it had to make the bus turn around on the spot, which the
pathfinder refuses. The result was what players saw:

- legs that never become reachable (`game found no path from waypoint N to waypoint N+1`),
- lines that are skipped after burning every repair attempt,
- bus stops that stay standing but no longer sit on any route,
- long chains of retries for a single line (30+ previews in `AutoTransitPlanner.log`).

The repair path made it worse: the only edit it could make was *omitting a waypoint*, so a
stop on an unreachable road stayed in the city while the line stopped serving it.

## What 0.3.0 changes

### 1. Dead ends and one-way pockets are pruned from the graph

`CityTransit.Core/RoadNetwork.cs` now computes a drivable **core** after building the arc
graph:

1. peel every node with undirected degree <= 1 (classic 2-core peeling), then
2. peel every remaining node that has no outgoing or no incoming arc, repeating until the
   set stops shrinking (catches one-way streets and one-way pockets that still form a
   strongly connected component).

The result is exposed as `CoreNodeCount`, `HasCore`, `IsCore(node)`, `IsCoreSegment(segment)`,
`SnapToCore(...)` and `NearestCoreNode(...)`. A node on a cul-de-sac is no longer eligible
to be an access point, a seed, or a stop.

### 2. Zones in dead ends are connected to the nearest through road

`ZoneModel.Build` first snaps a demand zone to its closest road, then checks whether that
road is on the core. If it is not, the zone is re-snapped with `SnapToCore`, and if that
fails its access node becomes `NearestCoreNode`. A cul-de-sac full of houses is still
served — just from the through road it meets, not from a stop the bus would have to turn
around at.

### 3. Planned loops may not turn around in place

`CityTransit.Core/RouteRules.cs` validates every loop the builder produces:

- every arc is on a core segment,
- consecutive arcs connect tail to head (including the closing leg),
- two consecutive arcs never use the same road segment in opposite directions.

Out-and-back across a single bridge is still allowed (that is legal for a bus and often the
only way in), but reversing direction on the same segment — what a dead-end forces — is
rejected. Spurt segments cost 80x during path search and reusing an already driven segment
costs 6x, so the builder prefers a real loop over a dead-end shuttle and gives up on the
seed if no acceptable loop exists.

### 4. Stops are never placed on dead-end roads

`StopSelector.BuildCandidates` skips segments that are not on the core, so no candidate stop
position exists on a cul-de-sac.

### 5. Repair drops the unreachable end of a broken leg

`RouteRepairSearch` was rewritten:

- it still prefers dropping a via waypoint,
- it now removes the two ends of the failed leg when the budget allows (a stop pair across a
  dead end or a one-way pocket can never be connected by dropping one end),
- up to 4 removals and 24 previews per line (was 3 and 12),
- it can never drop the line below two passenger stops, and a single failing line no longer
  burns the whole preview budget on one shape.

### 6. User-visible reporting

`TransitPlanner` notes how many nodes are through roads
(`through roads: N of M nodes can be driven into and out of without turning around`), and
explains when nothing can be planned (`no drivable loop in the road network: every road is a
dead end or a one-way pocket`). The `LineBuilder` stop reason now includes `deadEnds=N`.

## Verification

- `dotnet build -c Release` — net48, 0 warnings, 0 errors.
- `tests/TransitCoreTests` (net8.0 harness compiling the same core sources): 38 checks
  covering core peeling, one-way pockets, a ring city with a cul-de-sac zone, planner
  invariants (every planned arc on core, no turnaround), `RouteRules` cases, and the repair
  search order/budget.
- The existing `transit-regression` harness that loads the built DLL and exercises
  `RoutePathValidation`, `RoadsideStopMatch` and `RouteRepairSearch`: 26 passed, 0 failed.

## Install

Copy the whole folder into:

`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\AutoTransitPlanner`

The folder must contain `AutoTransitPlanner.dll` and `mod.json`. 0.2.3 shipped without the
manifest, which is why the local install was incomplete; 0.3.0 restores it.

## Settings

Options > Mods > Auto Transit Planner:

- **Actions** — plan and build new lines, or repair existing lines with broken paths.
- **Automation** — run planning automatically on a schedule.
- **Tuning** — walk radius, stop spacing, line length limits, coverage target, fleet budget.

Logs go to `Logs\AutoTransitPlanner.log`.

## Known limits

- A dead-end street with demand is served from its nearest through road; the walk from the
  houses to the stop can be longer than the configured walk radius in extreme geometry, in
  which case the zone is reported as uncovered instead of being connected.
- The repair path never deletes bus stops the game already created; it only stops routing
  through them. Deleting them would have to go through the game's deletion pipeline and is
  left to the player for now.