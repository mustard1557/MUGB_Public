using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace MUGB
{
    public class RaidStrategyWorker_GoblinMortarTunnelSiege : RaidStrategyWorker
    {
        public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind)
        {
            return base.CanUseWith(parms, groupKind)
                && parms.points >= 850f
                && Squads.MUGB_SquadRaidUtility.CanUseGoblinTunnelWarfare(parms.faction)
                && parms.target is Map map
                && MUGB_GoblinMortarSiegeUtility.TryFindSiegeCenter(map, out _);
        }

        protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed)
        {
            return null;
        }
    }

    public class PawnsArrivalModeWorker_GoblinMortarTunnelSiege : PawnsArrivalModeWorker
    {
        public override bool CanUseWith(IncidentParms parms)
        {
            return parms?.target is Map map
                && parms.points >= 850f
                && Squads.MUGB_SquadRaidUtility.CanUseGoblinTunnelWarfare(parms.faction)
                && MUGB_GoblinMortarSiegeUtility.TryFindSiegeCenter(map, out _);
        }

        public override bool TryResolveRaidSpawnCenter(IncidentParms parms)
        {
            if (!(parms?.target is Map map) || !MUGB_GoblinMortarSiegeUtility.TryFindSiegeCenter(map, out IntVec3 center))
            {
                return false;
            }
            parms.spawnCenter = center;
            parms.spawnRotation = Rot4.FromAngleFlat((map.Center - center).AngleFlat);
            return true;
        }

        public override void Arrive(List<Pawn> pawns, IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (!MUGB_GoblinMortarSiegeUtility.TryFindSiegeCenter(map, out IntVec3 center))
            {
                SpawnFallbackRaid(pawns, parms, map);
                return;
            }

            List<int> squadSizes;
            if (!Squads.MUGB_SquadRaidUtility.TryConsumeSquadLayout(parms, out squadSizes))
            {
                squadSizes = BuildFallbackSquadSizes(pawns.Count);
            }

            MUGB_GoblinMortarTunnelSpawner spawner = (MUGB_GoblinMortarTunnelSpawner)ThingMaker.MakeThing(MUGBDefOf.MUGB_GoblinMortarTunnelSpawner);
            spawner.Initialize(pawns, squadSizes, parms.faction, parms.points, MortarCountFor(parms.points));
            GenSpawn.Spawn(spawner, center, map, Rot4.North);
            parms.spawnCenter = center;
            MUGB_GoblinTunnelLetterTargets.Register(parms, new LookTargets(center, map));
            Messages.Message("MUGB_GoblinMortarSiegeIncoming".Translate(), spawner, MessageTypeDefOf.ThreatBig, false);
            pawns.Clear();
        }

        private static int MortarCountFor(float points)
        {
            return points >= 5000f ? 3 : points >= 3000f ? 2 : 1;
        }

        private static List<int> BuildFallbackSquadSizes(int count)
        {
            List<int> result = new List<int>();
            while (count > 0)
            {
                int size = Mathf.Min(6, count);
                if (count - size > 0 && count - size < 3) size -= 3 - (count - size);
                result.Add(size);
                count -= size;
            }
            return result;
        }

        private static void SpawnFallbackRaid(List<Pawn> pawns, IncidentParms parms, Map map)
        {
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Hostile, false))
            {
                entry = CellFinder.RandomEdgeCell(map);
            }
            foreach (Pawn pawn in pawns)
            {
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(entry, map, 8), map);
            }
            if (pawns.Count > 0)
            {
                LordMaker.MakeNewLord(parms.faction, MUGB_GoblinMortarSiegeUtility.MakeAssaultLord(parms.faction), map, pawns);
            }
        }
    }

    public class MUGB_GoblinMortarTunnelSpawner : ThingWithComps, IThingHolder
    {
        private ThingOwner<Pawn> guards;
        private List<int> squadSizes = new List<int>();
        private Faction faction;
        private float raidPoints;
        private int mortarCount;
        private int completeTick;
        private Sustainer sustainer;
        private Effecter effecter;

        public new IThingHolder ParentHolder => Map;

        public MUGB_GoblinMortarTunnelSpawner()
        {
            guards = new ThingOwner<Pawn>(this, false, LookMode.Deep, false);
        }

        public void Initialize(IEnumerable<Pawn> pawns, IEnumerable<int> sizes, Faction faction, float points, int mortars)
        {
            this.faction = faction;
            raidPoints = points;
            mortarCount = mortars;
            squadSizes = sizes?.Where(x => x > 0).ToList() ?? new List<int>();
            foreach (Pawn pawn in pawns) guards.TryAdd(pawn, false);
        }

        public ThingOwner GetDirectlyHeldThings() => guards;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, guards);

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad) completeTick = Find.TickManager.TicksGame + 1050;
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                if (!Spawned) return;
                sustainer = SoundDefOf.Tunnel.TrySpawnSustainer(SoundInfo.InMap(this, MaintenanceType.PerTick));
                effecter = def.building?.groundSpawnerSustainedEffecter?.Spawn(this, map);
            });
        }

        protected override void Tick()
        {
            base.Tick();
            sustainer?.Maintain();
            effecter?.EffectTick(this, this);
            if (Find.TickManager.TicksGame >= completeTick) Complete();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false) { }

        private void Complete()
        {
            Map map = Map;
            IntVec3 cell = Position;
            def.building?.groundSpawnerCompleteEffecter?.SpawnMaintained(cell, map);
            List<Pawn> pawnList = new List<Pawn>();
            for (int i = 0; i < guards.Count; i++) pawnList.Add(guards[i]);
            guards.Clear();
            Cleanup();
            Building_GoblinMortarTunnel tunnel = (Building_GoblinMortarTunnel)ThingMaker.MakeThing(MUGBDefOf.MUGB_GoblinMortarTunnel);
            tunnel.SetFaction(faction);
            GenSpawn.Spawn(tunnel, cell, map);
            map.GetComponent<MUGB_GoblinMortarSiegeManager>().StartSiege(tunnel, faction, raidPoints, mortarCount, pawnList, squadSizes);
            Destroy(DestroyMode.Vanish);
        }

        private void Cleanup()
        {
            effecter?.Cleanup();
            effecter = null;
            if (sustainer != null && !sustainer.Ended) sustainer.End();
            sustainer = null;
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            Cleanup();
            if (guards?.Count > 0) guards.ClearAndDestroyContents(DestroyMode.Vanish);
            base.Destroy(mode);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref guards, "guards", this);
            Scribe_Collections.Look(ref squadSizes, "squadSizes", LookMode.Value);
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref raidPoints, "raidPoints");
            Scribe_Values.Look(ref mortarCount, "mortarCount");
            Scribe_Values.Look(ref completeTick, "completeTick");
        }
    }

    public class Building_GoblinMortarTunnel : Building
    {
        private int siegeId;
        public void SetSiegeId(int id) => siegeId = id;

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            MUGB_GoblinMortarSiegeManager manager = Map?.GetComponent<MUGB_GoblinMortarSiegeManager>();
            if (manager != null && manager.TryGetStatus(siegeId, out int ticks, out int waves))
            {
                if (!text.NullOrEmpty()) text += "\n";
                text += "MUGB_GoblinMortarNextSupply".Translate(ticks.ToStringTicksToPeriod());
                text += "\n" + "MUGB_GoblinMortarRemainingSupplies".Translate(waves);
                text += "\n" + "MUGB_GoblinTunnelDestroyHint".Translate();
            }
            return text;
        }

        public bool TryGetReinforcementTicks(out int ticks)
        {
            ticks = 0;
            MUGB_GoblinMortarSiegeManager manager = Map?.GetComponent<MUGB_GoblinMortarSiegeManager>();
            return manager != null && manager.TryGetStatus(siegeId, out ticks, out int remaining) && remaining > 0;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref siegeId, "siegeId");
        }
    }

    public class Building_GoblinMortarSupportTunnel : Building_GoblinMortarTunnel { }

    public sealed class MUGB_GoblinMortarSupportEntry : IExposable
    {
        public Building_GoblinMortarSupportTunnel tunnel;
        public float assignedPoints;
        public List<Pawn> initialPawns = new List<Pawn>();

        public void ExposeData()
        {
            Scribe_References.Look(ref tunnel, "tunnel");
            Scribe_Values.Look(ref assignedPoints, "assignedPoints");
            Scribe_Collections.Look(ref initialPawns, "initialPawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) initialPawns = initialPawns ?? new List<Pawn>();
        }
    }

    public sealed class MUGB_GoblinMortarSiegeRecord : IExposable
    {
        public int id;
        public Faction faction;
        public Building_GoblinMortarTunnel tunnel;
        public IntVec3 center;
        public float raidPoints;
        public int mortarCount;
        public int mainSpawnTick;
        public int nextWaveTick;
        public int completedWaves;
        public bool mainSpawned;
        public bool assaulting;
        public bool warningSent;
        public float mainAssignedPoints;
        public int artilleryLostTick = -1;
        public List<Pawn> crew = new List<Pawn>();
        public List<Pawn> slaves = new List<Pawn>();
        public List<Pawn> guards = new List<Pawn>();
        public List<int> mainGuardSquadSizes = new List<int>();
        public List<Building> mortars = new List<Building>();
        public List<MUGB_GoblinMortarSupportEntry> supports = new List<MUGB_GoblinMortarSupportEntry>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id"); Scribe_References.Look(ref faction, "faction");
            Scribe_References.Look(ref tunnel, "tunnel"); Scribe_Values.Look(ref center, "center");
            Scribe_Values.Look(ref raidPoints, "raidPoints"); Scribe_Values.Look(ref mortarCount, "mortarCount");
            Scribe_Values.Look(ref mainSpawnTick, "mainSpawnTick"); Scribe_Values.Look(ref nextWaveTick, "nextWaveTick");
            Scribe_Values.Look(ref completedWaves, "completedWaves"); Scribe_Values.Look(ref mainSpawned, "mainSpawned");
            Scribe_Values.Look(ref assaulting, "assaulting");
            Scribe_Values.Look(ref warningSent, "warningSent"); Scribe_Values.Look(ref mainAssignedPoints, "mainAssignedPoints");
            Scribe_Values.Look(ref artilleryLostTick, "artilleryLostTick", -1);
            Scribe_Collections.Look(ref crew, "crew", LookMode.Reference); Scribe_Collections.Look(ref slaves, "slaves", LookMode.Reference);
            Scribe_Collections.Look(ref guards, "guards", LookMode.Reference); Scribe_Collections.Look(ref mortars, "mortars", LookMode.Reference);
            Scribe_Collections.Look(ref mainGuardSquadSizes, "mainGuardSquadSizes", LookMode.Value);
            Scribe_Collections.Look(ref supports, "supports", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                crew = crew ?? new List<Pawn>(); slaves = slaves ?? new List<Pawn>(); guards = guards ?? new List<Pawn>(); mainGuardSquadSizes = mainGuardSquadSizes ?? new List<int>(); mortars = mortars ?? new List<Building>(); supports = supports ?? new List<MUGB_GoblinMortarSupportEntry>();
                if (mainGuardSquadSizes.Count == 0 && guards.Count > 0)
                {
                    for (int remaining = guards.Count; remaining > 0; remaining -= Mathf.Min(6, remaining))
                    {
                        mainGuardSquadSizes.Add(Mathf.Min(6, remaining));
                    }
                }
            }
        }
    }

    public sealed class MUGB_GoblinMortarSiegeManager : MapComponent
    {
        private const int CheckInterval = 250;
        private const int EmergencePreludeTicks = 180;
        private List<MUGB_GoblinMortarSiegeRecord> sieges = new List<MUGB_GoblinMortarSiegeRecord>();
        private int nextId = 1;

        public MUGB_GoblinMortarSiegeManager(Map map) : base(map) { }

        public void StartSiege(Building_GoblinMortarTunnel tunnel, Faction faction, float points, int mortars, List<Pawn> guards, List<int> squadSizes)
        {
            int now = Find.TickManager.TicksGame;
            MUGB_GoblinMortarSiegeRecord record = new MUGB_GoblinMortarSiegeRecord
            {
                id = nextId++, tunnel = tunnel, faction = faction, center = tunnel.Position, raidPoints = points,
                mortarCount = mortars, mainSpawnTick = now + Rand.RangeInclusive(720, 960), nextWaveTick = now + RandomSupplyInterval()
            };
            SplitGuards(record, guards, squadSizes);
            tunnel.SetSiegeId(record.id);
            SpawnSupportTunnels(record);
            sieges.Add(record);
            SpawnInitialSlaves(record);
        }

        public bool TryGetStatus(int id, out int ticks, out int remaining)
        {
            MUGB_GoblinMortarSiegeRecord record = sieges.FirstOrDefault(x => x.id == id);
            ticks = record == null ? 0 : Mathf.Max(0, record.nextWaveTick - Find.TickManager.TicksGame);
            remaining = record == null ? 0 : Mathf.Max(0, 3 - record.completedWaves);
            return record != null;
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % CheckInterval != 0) return;
            for (int i = sieges.Count - 1; i >= 0; i--)
            {
                MUGB_GoblinMortarSiegeRecord record = sieges[i];
                Prune(record);
                if (!record.mainSpawned && now >= record.mainSpawnTick)
                {
                    if (record.tunnel == null || record.tunnel.Destroyed) SpawnGuardsAsAssault(record);
                    else SpawnMainForce(record);
                }
                bool anyTunnelAlive = record.tunnel?.Spawned == true || record.supports.Any(s => s.tunnel?.Spawned == true);
                if (record.mainSpawned && record.completedWaves < 3 && anyTunnelAlive)
                {
                    if (!record.warningSent && record.completedWaves < 3 && now >= record.nextWaveTick - 2500)
                    {
                        record.warningSent = true;
                        List<Thing> targets = new List<Thing>();
                        if (record.tunnel?.Spawned == true) targets.Add(record.tunnel);
                        targets.AddRange(record.supports.Where(s => s.tunnel?.Spawned == true).Select(s => (Thing)s.tunnel));
                        if (targets.Count > 0) Messages.Message("MUGB_GoblinTunnelReinforcementWarning".Translate(), new LookTargets(targets), MessageTypeDefOf.ThreatSmall, false);
                    }
                    if (now >= record.nextWaveTick)
                    {
                        ReleaseWave(record);
                        record.completedWaves++;
                        record.nextWaveTick = now + RandomSupplyInterval();
                        record.warningSent = false;
                    }
                }
                else if (record.mainSpawned && !anyTunnelAlive && record.completedWaves < 3)
                {
                    record.completedWaves = 3;
                }

                if (record.mainSpawned && !record.assaulting)
                {
                    int usableCrew = record.crew.Count(p => p?.Spawned == true && !p.Downed && !p.Dead);
                    if (usableCrew == 0)
                    {
                        if (record.artilleryLostTick < 0) record.artilleryLostTick = now;
                        if (now - record.artilleryLostTick >= 2500) BeginAssault(record);
                    }
                    else record.artilleryLostTick = -1;

                    if (record.completedWaves >= 3)
                    {
                        BeginAssault(record);
                    }
                    else if (record.mortars.Count == 0 && !HasPendingMortar(record)) BeginAssault(record);
                    else if ((record.tunnel == null || record.tunnel.Destroyed) && !HasUsableShells(record))
                    {
                        BeginAssault(record);
                    }
                }
                if (record.assaulting && !AllInitialPawns(record).Any() && record.completedWaves >= 3)
                {
                    sieges.RemoveAt(i);
                }
            }
        }

        private void SpawnInitialSlaves(MUGB_GoblinMortarSiegeRecord record)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_GoblinKind_TunnelSlave");
            for (int i = 0; i < 2 && kind != null; i++) record.slaves.Add(PawnGenerator.GeneratePawn(kind, record.faction));
            MUGB_GoblinMortarSiegeUtility.SpawnJumping(record.slaves, record.tunnel, new LordJob_DefendPoint(record.center, 8f, 16f));
        }

        private void SpawnMainForce(MUGB_GoblinMortarSiegeRecord record)
        {
            record.mainSpawned = true;
            int crewCount = record.mortarCount == 1 ? 4 : record.mortarCount == 2 ? 6 : 8;
            PawnKindDef crewKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_GoblinKind_SiegeCrew")
                ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_GoblinKind_TunnelVanguard");
            for (int i = 0; i < crewCount && crewKind != null; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(crewKind, record.faction);
                pawn.skills?.GetSkill(SkillDefOf.Construction)?.EnsureMinLevelWithMargin(6);
                record.crew.Add(pawn);
            }
            int siegeSupportCount = record.mainGuardSquadSizes.Count > 0
                ? Mathf.Min(record.mainGuardSquadSizes[0], record.guards.Count)
                : 0;
            List<Pawn> siegeWorkforce = record.crew
                .Concat(record.guards.Take(siegeSupportCount))
                .ToList();
            PlayDigging(record.tunnel);
            MUGB_GoblinMortarSiegeUtility.SpawnJumping(siegeWorkforce, record.tunnel, new MUGB_LordJob_GoblinSiegeWork(record.center), EmergencePreludeTicks, record.slaves.Where(p => p?.Spawned == true));
            int guardIndex = siegeSupportCount;
            for (int squadIndex = siegeSupportCount > 0 ? 1 : 0; squadIndex < record.mainGuardSquadSizes.Count; squadIndex++)
            {
                int requestedSize = record.mainGuardSquadSizes[squadIndex];
                int count = Mathf.Min(Mathf.Clamp(requestedSize, 1, 6), record.guards.Count - guardIndex);
                if (count <= 0) break;
                List<Pawn> squad = record.guards.GetRange(guardIndex, count);
                guardIndex += count;
                MUGB_GoblinMortarSiegeUtility.SpawnJumping(squad, record.tunnel, new LordJob_DefendPoint(record.center, 10f, 20f), EmergencePreludeTicks);
            }
            while (guardIndex < record.guards.Count)
            {
                int count = Mathf.Min(6, record.guards.Count - guardIndex);
                List<Pawn> squad = record.guards.GetRange(guardIndex, count);
                guardIndex += count;
                MUGB_GoblinMortarSiegeUtility.SpawnJumping(squad, record.tunnel, new LordJob_DefendPoint(record.center, 10f, 20f), EmergencePreludeTicks);
            }
            foreach (MUGB_GoblinMortarSupportEntry support in record.supports)
            {
                if (support.tunnel?.Spawned == true)
                {
                    PlayDigging(support.tunnel);
                    MUGB_GoblinMortarSiegeUtility.SpawnJumping(support.initialPawns, support.tunnel, new LordJob_DefendPoint(record.center, 10f, 20f), EmergencePreludeTicks);
                }
                else if (record.tunnel?.Spawned == true)
                {
                    MUGB_GoblinMortarSiegeUtility.SpawnJumping(support.initialPawns, record.tunnel, new LordJob_DefendPoint(record.center, 10f, 20f), EmergencePreludeTicks);
                }
            }
            PlaceSiegeBlueprintsAndSupplies(record);
        }

        private void PlaceSiegeBlueprintsAndSupplies(MUGB_GoblinMortarSiegeRecord record)
        {
            List<Blueprint_Build> blueprints = new List<Blueprint_Build>();
            foreach (IntVec3 cell in MUGB_GoblinMortarSiegeUtility.MortarCells(record.center, record.mortarCount))
            {
                Blueprint_Build bp = GenConstruct.PlaceBlueprintForBuild(MUGBDefOf.MUGB_GoblinMortar, cell, map, Rot4.North, record.faction, null);
                if (bp != null) blueprints.Add(bp);
            }
            MUGB_GoblinMortarSiegeUtility.PlaceDefenses(record.center, map, record.faction, record.mortarCount, blueprints);
            Dictionary<ThingDef, int> costs = new Dictionary<ThingDef, int>();
            foreach (Blueprint_Build bp in blueprints)
            {
                foreach (ThingDefCountClass cost in bp.TotalMaterialCost()) costs[cost.thingDef] = costs.TryGetValue(cost.thingDef, out int old) ? old + cost.count : cost.count;
            }
            foreach (KeyValuePair<ThingDef, int> pair in costs) SpawnStack(pair.Key, pair.Value, record);
            SpawnShells(record, 15 * record.mortarCount);
            SpawnPemmican(record, InitialFood(record.raidPoints));
        }

        private void ReleaseWave(MUGB_GoblinMortarSiegeRecord record)
        {
            bool mainTunnelAlive = record.tunnel?.Spawned == true && !record.tunnel.Destroyed;
            if (mainTunnelAlive) ReleaseAssaultFrom(record.tunnel, record.mainAssignedPoints * 0.5f, record);
            foreach (MUGB_GoblinMortarSupportEntry support in record.supports)
                if (support.tunnel?.Spawned == true) ReleaseAssaultFrom(support.tunnel, support.assignedPoints * 0.5f, record);
            if (mainTunnelAlive)
            {
                SpawnShells(record, 10 * record.mortars.Count);
                SpawnPemmican(record, WaveFood(record.raidPoints));
            }
            List<Thing> targets = new List<Thing>();
            if (mainTunnelAlive) targets.Add(record.tunnel);
            targets.AddRange(record.supports.Where(s => s.tunnel?.Spawned == true).Select(s => (Thing)s.tunnel));
            if (targets.Count > 0) Messages.Message("MUGB_GoblinMortarSuppliesArrived".Translate(), new LookTargets(targets), MessageTypeDefOf.ThreatBig, false);
        }

        private void SpawnShells(MUGB_GoblinMortarSiegeRecord record, int total)
        {
            if (total <= 0 || record.tunnel == null || record.tunnel.Destroyed) return;
            SpawnStack(MUGBDefOf.MUGB_GoblinHighExplosiveShell, Mathf.RoundToInt(total * 0.8f), record);
            SpawnStack(MUGBDefOf.MUGB_GoblinStinkMortarShell, total - Mathf.RoundToInt(total * 0.8f), record);
        }

        private void SpawnPemmican(MUGB_GoblinMortarSiegeRecord record, int count)
        {
            if (count <= 0) return;
            while (count > 0)
            {
                Thing thing = ThingMaker.MakeThing(ThingDefOf.Pemmican);
                thing.stackCount = Mathf.Min(count, thing.def.stackLimit);
                count -= thing.stackCount;
                thing.TryGetComp<CompIngredients>()?.RegisterIngredient(ThingDefOf.Meat_Human);
                PlaceSupplyThing(thing, record);
            }
        }

        private void SpawnStack(ThingDef def, int count, MUGB_GoblinMortarSiegeRecord record)
        {
            if (def == null || count <= 0) return;
            while (count > 0)
            {
                Thing thing = ThingMaker.MakeThing(def);
                thing.stackCount = Mathf.Min(count, def.stackLimit);
                count -= thing.stackCount;
                PlaceSupplyThing(thing, record);
            }
        }

        private void PlaceSupplyThing(Thing thing, MUGB_GoblinMortarSiegeRecord record)
        {
            IntVec3 anchor = record.center + new IntVec3(0, 0, -2);
            if (!anchor.InBounds(map) || !anchor.Walkable(map))
            {
                anchor = CellFinder.RandomClosewalkCellNear(record.center, map, 6);
            }
            if (!GenPlace.TryPlaceThing(thing, anchor, map, ThingPlaceMode.Near) && !thing.Destroyed)
            {
                IntVec3 fallback = CellFinder.RandomClosewalkCellNear(record.center, map, 8);
                GenSpawn.Spawn(thing, fallback, map);
            }
            if (thing.Spawned)
            {
                thing.SetForbidden(true, false);
            }
        }

        private static int InitialFood(float points) => points < 1200f ? 22 : points < 3000f ? 30 : points < 5000f ? 45 : 60;
        private static int WaveFood(float points) => points < 1200f ? 15 : points < 3000f ? 22 : points < 5000f ? 34 : 45;

        private void BeginAssault(MUGB_GoblinMortarSiegeRecord record)
        {
            if (record.assaulting) return;
            record.assaulting = true;
            List<Pawn> pawns = AllInitialPawns(record)
                .Where(p => p.Spawned && !p.Dead && !p.Destroyed)
                .ToList();
            foreach (Pawn pawn in pawns) pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
            List<Pawn> active = pawns.Where(pawn => !pawn.Downed).ToList();
            List<Pawn> downed = pawns.Where(pawn => pawn.Downed).ToList();
            if (active.Count > 0)
            {
                LordMaker.MakeNewLord(record.faction, MUGB_GoblinMortarSiegeUtility.MakeAssaultLord(record.faction), map, active);
            }
            if (downed.Count > 0)
            {
                LordMaker.MakeNewLord(
                    record.faction,
                    new LordJob_ExitMapBest(LocomotionUrgency.Jog, canDig: false, canDefendSelf: true),
                    map,
                    downed);
            }
        }

        private void SpawnGuardsAsAssault(MUGB_GoblinMortarSiegeRecord record)
        {
            record.mainSpawned = true;
            record.assaulting = true;
            List<Pawn> all = new List<Pawn>(record.slaves);
            all.AddRange(record.guards);
            foreach (MUGB_GoblinMortarSupportEntry support in record.supports) all.AddRange(support.initialPawns);
            MUGB_GoblinMortarSiegeUtility.SpawnNear(all, record.center, map, MUGB_GoblinMortarSiegeUtility.MakeAssaultLord(record.faction));
        }

        private static IEnumerable<Pawn> AllInitialPawns(MUGB_GoblinMortarSiegeRecord record)
        {
            return record.crew
                .Concat(record.slaves)
                .Concat(record.guards)
                .Concat(record.supports.Where(s => s != null).SelectMany(s => s.initialPawns ?? Enumerable.Empty<Pawn>()))
                .Where(p => p != null && !p.Dead && !p.Destroyed)
                .Distinct();
        }

        private bool HasPendingMortar(MUGB_GoblinMortarSiegeRecord record)
        {
            return map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).Any(t =>
            {
                if (t.Faction != record.faction || !t.Position.InHorDistOf(record.center, 24f)) return false;
                if (t is Blueprint_Build bp) return bp.def.entityDefToBuild == MUGBDefOf.MUGB_GoblinMortar;
                if (t is Frame frame) return frame.def.entityDefToBuild == MUGBDefOf.MUGB_GoblinMortar;
                return false;
            });
        }

        private bool HasUsableShells(MUGB_GoblinMortarSiegeRecord record)
        {
            int radiusSquared = 24 * 24;
            bool onGround = map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_GoblinHighExplosiveShell)
                .Concat(map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_GoblinStinkMortarShell))
                .Any(t => t.Spawned && t.Position.DistanceToSquared(record.center) <= radiusSquared);
            if (onGround) return true;
            foreach (Building mortar in record.mortars)
            {
                CompChangeableProjectile comp = (mortar as Building_TurretGun)?.gun?.TryGetComp<CompChangeableProjectile>();
                if (comp?.Loaded == true) return true;
            }
            return false;
        }

        private static void Prune(MUGB_GoblinMortarSiegeRecord record)
        {
            record.crew.RemoveAll(p => p == null || p.Dead || p.Destroyed); record.slaves.RemoveAll(p => p == null || p.Dead || p.Destroyed);
            record.guards.RemoveAll(p => p == null || p.Dead || p.Destroyed); record.mortars.RemoveAll(b => b == null || b.Destroyed || !b.Spawned);
            if (record.tunnel?.Map != null)
            {
                record.mortars = record.tunnel.Map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_GoblinMortar)
                    .OfType<Building>().Where(b => b.Faction == record.faction && b.Position.InHorDistOf(record.center, 24f)).ToList();
            }
            record.supports.RemoveAll(s => s == null);
            foreach (MUGB_GoblinMortarSupportEntry support in record.supports)
            {
                support.initialPawns.RemoveAll(p => p == null || p.Dead || p.Destroyed);
                if (support.tunnel == null || support.tunnel.Destroyed || !support.tunnel.Spawned)
                {
                    support.tunnel = null;
                }
            }
        }

        private static void SplitGuards(MUGB_GoblinMortarSiegeRecord record, List<Pawn> pawns, List<int> squadSizes)
        {
            int index = 0;
            for (int squad = 0; squad < squadSizes.Count && index < pawns.Count; squad++)
            {
                int count = Mathf.Min(squadSizes[squad], pawns.Count - index);
                List<Pawn> group = pawns.GetRange(index, count);
                index += count;
                float points = group.Sum(p => p.kindDef?.combatPower ?? 0f);
                // The mortar supply tunnel carries one scored escort squad. Every additional
                // complete squad gets its own support tunnel, matching ordinary tunnel raids.
                if (squad == 0)
                {
                    record.guards.AddRange(group);
                    for (int remaining = group.Count; remaining > 0; remaining -= Mathf.Min(6, remaining))
                    {
                        record.mainGuardSquadSizes.Add(Mathf.Min(6, remaining));
                    }
                    record.mainAssignedPoints += points;
                }
                else
                {
                    record.supports.Add(new MUGB_GoblinMortarSupportEntry { assignedPoints = points, initialPawns = group });
                }
            }
            if (index < pawns.Count)
            {
                List<Pawn> remainder = pawns.GetRange(index, pawns.Count - index);
                record.guards.AddRange(remainder);
                for (int remainingCount = remainder.Count; remainingCount > 0; remainingCount -= Mathf.Min(6, remainingCount))
                {
                    record.mainGuardSquadSizes.Add(Mathf.Min(6, remainingCount));
                }
                record.mainAssignedPoints += remainder.Sum(p => p.kindDef?.combatPower ?? 0f);
            }
        }

        private void SpawnSupportTunnels(MUGB_GoblinMortarSiegeRecord record)
        {
            List<IntVec3> nearCandidates = GenRadial.RadialCellsAround(record.center, 13f, false)
                .Where(c => c.InBounds(map) && !c.Fogged(map) && c.Standable(map) && c.GetTerrain(map)?.IsWater != true && c.GetEdifice(map) == null)
                .InRandomOrder().ToList();
            List<IntVec3> extendedCandidates = GenRadial.RadialCellsAround(record.center, 20f, false)
                .Where(c => c.DistanceToSquared(record.center) > 13 * 13
                    && c.InBounds(map)
                    && !c.Fogged(map)
                    && c.Standable(map)
                    && c.GetTerrain(map)?.IsWater != true
                    && c.GetEdifice(map) == null)
                .InRandomOrder().ToList();
            List<IntVec3> candidates = nearCandidates.Concat(extendedCandidates).ToList();
            foreach (MUGB_GoblinMortarSupportEntry support in record.supports.ToList())
            {
                IntVec3 cell = candidates.FirstOrDefault(c => c.DistanceToSquared(record.center) >= 16 && record.supports.All(s => s.tunnel == null || s.tunnel.Position.DistanceToSquared(c) >= 16));
                if (!cell.IsValid) { record.guards.AddRange(support.initialPawns); record.mainGuardSquadSizes.Add(Mathf.Min(6, support.initialPawns.Count)); record.mainAssignedPoints += support.assignedPoints; record.supports.Remove(support); continue; }
                Building_GoblinMortarSupportTunnel tunnel = (Building_GoblinMortarSupportTunnel)ThingMaker.MakeThing(MUGBDefOf.MUGB_GoblinMortarSupportTunnel);
                tunnel.SetFaction(record.faction); tunnel.SetSiegeId(record.id); GenSpawn.Spawn(tunnel, cell, map);
                support.tunnel = tunnel;
                MUGBDefOf.MUGB_GoblinTunnelSpawnerA?.building?.groundSpawnerCompleteEffecter?.SpawnMaintained(cell, map);
            }
        }

        private static void ReleaseAssaultFrom(Building_GoblinMortarTunnel tunnel, float points, MUGB_GoblinMortarSiegeRecord record)
        {
            if (tunnel?.Spawned != true || points <= 0f) return;
            PlayDigging(tunnel);
            List<PawnKindDef> kinds = Squads.MUGB_SquadRaidUtility.GenerateTunnelSquadKinds(record.faction, points, 5);
            List<Pawn> pawns = kinds.Select(kind => PawnGenerator.GeneratePawn(kind, record.faction)).ToList();
            MUGB_GoblinMortarSiegeUtility.SpawnJumping(pawns, tunnel, MUGB_GoblinMortarSiegeUtility.MakeAssaultLord(record.faction), EmergencePreludeTicks);
        }

        private static void PlayDigging(Thing tunnel)
        {
            if (tunnel?.Spawned != true) return;
            if (ThingMaker.MakeThing(MUGBDefOf.MUGB_GoblinTunnelDiggingFX) is MUGB_TunnelDiggingFX diggingFx)
            {
                GenSpawn.Spawn(diggingFx, tunnel.Position, tunnel.Map);
                diggingFx.Initialize(false);
            }
        }

        private static int RandomSupplyInterval() => Rand.RangeInclusive(20000, 25000);

        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref nextId, "nextId", 1); Scribe_Collections.Look(ref sieges, "sieges", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) sieges = sieges ?? new List<MUGB_GoblinMortarSiegeRecord>();
        }
    }

    public sealed class MUGB_LordJob_GoblinSiegeWork : LordJob
    {
        private IntVec3 center;
        public MUGB_LordJob_GoblinSiegeWork() { }
        public MUGB_LordJob_GoblinSiegeWork(IntVec3 center) { this.center = center; }
        public override StateGraph CreateGraph() { StateGraph graph = new StateGraph(); graph.AddToil(new MUGB_LordToil_GoblinSiegeWork(center)); return graph; }
        public override void ExposeData() { Scribe_Values.Look(ref center, "center"); }
    }

    public sealed class MUGB_LordToil_GoblinSiegeWork : LordToil
    {
        private const float BuilderFraction = 0.4f;

        // 공성 캠프는 중심에서 최대 16셀 남짓(방어 말뚝 기준) 퍼집니다.
        // 보급 통로까지 여유 있게 덮도록 잡았습니다.
        private const float CleanupRadius = 30f;

        private IntVec3 center;
        public MUGB_LordToil_GoblinSiegeWork(IntVec3 center) { this.center = center; }
        public override IntVec3 FlagLoc => center;
        public override bool ForceHighStoryDanger => true;

        /// <summary>
        /// 한국어 의도: 공성이 끝났을 때 고블린이 미처 짓지 못한 잔해를 치웁니다.
        ///
        /// 청사진(Blueprint)은 체력이 없어 공격할 수 없고, 적 팩션 소유라 플레이어가 취소할
        /// 수도 없습니다. 그래서 정리하지 않으면 고블린을 전멸시킨 뒤에도 파란 윤곽이 맵에
        /// 영구히 남습니다. 골조(Frame)는 공격이라도 가능하지만 같이 치우는 게 자연스럽습니다.
        ///
        /// 처리 방식은 바닐라 LordToil_Siege.Cleanup()을 그대로 따릅니다.
        /// 완성된 건물은 부수지 않고 주인만 없애서, 플레이어가 철거하거나 그대로 쓸 수 있는
        /// 전리품으로 남깁니다.
        ///
        /// 이 로드잡의 상태 그래프에는 토일이 하나뿐이라 중간 전환이 없습니다. 따라서 여기는
        /// 공성 인원이 모두 죽거나 사라져 로드가 해제될 때만 실행됩니다.
        /// </summary>
        public override void Cleanup()
        {
            base.Cleanup();

            Map map = Map;
            if (map == null)
            {
                return;
            }

            float radiusSquared = CleanupRadius * CleanupRadius;
            DestroyOwnedThingsInGroup(map, ThingRequestGroup.Blueprint, radiusSquared);
            DestroyOwnedThingsInGroup(map, ThingRequestGroup.BuildingFrame, radiusSquared);

            foreach (Building building in lord.ownedBuildings.ToList())
            {
                if (building != null && !building.Destroyed && building.Faction == lord.faction)
                {
                    building.SetFaction(null);
                }
            }
        }

        private void DestroyOwnedThingsInGroup(Map map, ThingRequestGroup group, float radiusSquared)
        {
            // 목록을 복사한 뒤 지웁니다. 원본은 파괴할 때마다 줄어들기 때문입니다.
            foreach (Thing thing in map.listerThings.ThingsInGroup(group).ToList())
            {
                if (thing == null || thing.Destroyed || thing.Faction != lord.faction)
                {
                    continue;
                }

                if ((thing.Position - center).LengthHorizontalSquared < radiusSquared)
                {
                    thing.Destroy(DestroyMode.Cancel);
                }
            }
        }
        public override void UpdateAllDuties()
        {
            List<Pawn> activeCrews = lord.ownedPawns
                .Where(pawn => pawn != null
                    && !pawn.Dead
                    && !pawn.Downed
                    && pawn.kindDef?.defName == "MUGB_GoblinKind_SiegeCrew")
                .ToList();
            int completedMortars = Map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_GoblinMortar)
                .Count(thing => thing.Faction == lord.faction
                    && thing.Spawned
                    && thing.Position.InHorDistOf(center, 24f));
            int operatorCount = Mathf.Min(completedMortars, activeCrews.Count);
            HashSet<Pawn> operators = new HashSet<Pawn>(activeCrews
                .Where(pawn => pawn.mindState.duty?.def == DutyDefOf.ManClosestTurret)
                .Take(operatorCount));
            foreach (Pawn pawn in activeCrews)
            {
                if (operators.Count >= operatorCount) break;
                operators.Add(pawn);
            }

            List<Pawn> builderCandidates = lord.ownedPawns
                .Where(pawn => !operators.Contains(pawn) && CanBeBuilder(pawn))
                .ToList();
            int desiredBuilderCount = Mathf.Max(1, Mathf.RoundToInt(builderCandidates.Count * BuilderFraction));
            HashSet<Pawn> builders = new HashSet<Pawn>(builderCandidates
                .Where(pawn => pawn.mindState.duty?.def == DutyDefOf.Build)
                .Take(desiredBuilderCount));

            Pawn guardBuilder = builderCandidates.FirstOrDefault(pawn => pawn.kindDef?.defName != "MUGB_GoblinKind_SiegeCrew"
                && pawn.kindDef?.defName != "MUGB_GoblinKind_TunnelSlave");
            if (guardBuilder != null && builders.Count < desiredBuilderCount)
            {
                builders.Add(guardBuilder);
            }
            foreach (Pawn pawn in builderCandidates.InRandomOrder())
            {
                if (builders.Count >= desiredBuilderCount) break;
                builders.Add(pawn);
            }

            foreach (Pawn pawn in lord.ownedPawns)
            {
                if (operators.Contains(pawn))
                {
                    // Vanilla ManClosestTurret handles shell hauling, loading, manning and firing.
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.ManClosestTurret, center) { radius = 24f };
                    continue;
                }

                if (builders.Contains(pawn))
                {
                    SetAsBuilder(pawn);
                }
                else
                {
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.Defend, center) { radius = 24f };
                }
            }
        }

        private static bool CanBeBuilder(Pawn pawn)
        {
            return pawn != null
                && !pawn.Dead
                && !pawn.Downed
                && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction)
                && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Firefighter);
        }

        private void SetAsBuilder(Pawn pawn)
        {
            pawn.mindState.duty = new PawnDuty(DutyDefOf.Build, center) { radius = 24f };
            pawn.skills?.GetSkill(SkillDefOf.Construction)?.EnsureMinLevelWithMargin(MUGBDefOf.MUGB_GoblinMortar.constructionSkillPrerequisite);
            pawn.workSettings?.EnableAndInitialize();
            if (pawn.workSettings == null) return;

            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (work == WorkTypeDefOf.Construction) pawn.workSettings.SetPriority(work, 1);
                else pawn.workSettings.Disable(work);
            }
        }

        public override void LordToilTick()
        {
            base.LordToilTick();
            // Match vanilla LordToil_Siege: duties are refreshed periodically, while the
            // Build duty's think tree decides when to construct, load, man and fire artillery.
            if (lord.ticksInToil == 450 || (lord.ticksInToil > 450 && lord.ticksInToil % 500 == 0))
            {
                UpdateAllDuties();
            }
        }
    }

    public static class MUGB_GoblinMortarSiegeUtility
    {
        public static bool TryFindSiegeCenter(Map map, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            for (int i = 0; i < 60; i++)
            {
                if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 edge, map, CellFinder.EdgeRoadChance_Hostile, false)) continue;
                IntVec3 candidate = CellFinder.RandomClosewalkCellNear(edge, map, 18);
                if (CanFit(candidate, map)) { result = candidate; return true; }
            }
            return false;
        }

        private static bool CanFit(IntVec3 center, Map map)
        {
            foreach (IntVec3 cell in CellRect.CenteredOn(center, 10, 7).Cells)
            {
                if (!cell.InBounds(map) || cell.Fogged(map) || IsWaterOrBridge(cell, map) || !cell.Walkable(map)) return false;
            }
            return MortarCells(center, 3).All(c => !map.roofGrid.Roofed(c) && !map.roofGrid.Roofed(c + IntVec3.South));
        }

        private static bool IsWaterOrBridge(IntVec3 cell, Map map)
        {
            TerrainDef terrain = cell.GetTerrain(map);
            TerrainDef underTerrain = map.terrainGrid.UnderTerrainAt(cell);
            TerrainDef foundation = map.terrainGrid.FoundationAt(cell);
            return terrain?.IsWater == true
                || terrain?.bridge == true
                || underTerrain?.IsWater == true
                || foundation?.bridge == true;
        }

        public static IEnumerable<IntVec3> MortarCells(IntVec3 center, int count)
        {
            if (count == 1) yield return center + new IntVec3(0, 0, 4);
            else if (count == 2) { yield return center + new IntVec3(-2, 0, 4); yield return center + new IntVec3(2, 0, 4); }
            else { yield return center + new IntVec3(-4, 0, 4); yield return center + new IntVec3(0, 0, 4); yield return center + new IntVec3(4, 0, 4); }
        }

        public static void PlaceDefenses(IntVec3 center, Map map, Faction faction, int mortarCount, List<Blueprint_Build> output)
        {
            ThingDef palisade = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_EmbPalisade");
            ThingDef spike = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_CavalrySpike");
            ThingDef innerCover = palisade ?? ThingDefOf.Barricade;
            int halfWidth = mortarCount == 1 ? 6 : mortarCount == 2 ? 8 : 10;
            int front = 8;
            int back = -4;
            HashSet<IntVec3> accessCorridor = BuildSupplyAccessCorridor(center, mortarCount);

            int topGap = Rand.RangeInclusive(3, 4);
            int bottomGap = Rand.RangeInclusive(3, 4);
            int topLeftHorizontal = halfWidth - Rand.RangeInclusive(1, 2);
            int bottomLeftHorizontal = halfWidth - Rand.RangeInclusive(1, 2);
            int topRightHorizontal = 2 * halfWidth + 1 - topGap - topLeftHorizontal;
            int bottomRightHorizontal = 2 * halfWidth + 1 - bottomGap - bottomLeftHorizontal;

            int leftVerticalGap = Rand.RangeInclusive(3, 4);
            int rightVerticalGap = Rand.RangeInclusive(3, 4);
            int topLeftVertical = Rand.RangeInclusive(4, 5);
            int topRightVertical = Rand.RangeInclusive(4, 5);
            int bottomLeftVertical = front - back + 1 - leftVerticalGap - topLeftVertical;
            int bottomRightVertical = front - back + 1 - rightVerticalGap - topRightVertical;

            // Four independently sized corner modules form a broken square. Their local L shape
            // is mirrored into each corner, keeping 3-4 cell openings instead of a sealed wall.
            PlaceCornerModule(center + new IntVec3(-halfWidth, 0, front), 1, -1, topLeftHorizontal, topLeftVertical, innerCover, accessCorridor, map, faction, output);
            PlaceCornerModule(center + new IntVec3(halfWidth, 0, front), -1, -1, topRightHorizontal, topRightVertical, innerCover, accessCorridor, map, faction, output);
            PlaceCornerModule(center + new IntVec3(-halfWidth, 0, back), 1, 1, bottomLeftHorizontal, bottomLeftVertical, innerCover, accessCorridor, map, faction, output);
            PlaceCornerModule(center + new IntVec3(halfWidth, 0, back), -1, 1, bottomRightHorizontal, bottomRightVertical, innerCover, accessCorridor, map, faction, output);

            if (spike != null)
            {
                int spikeHalf = halfWidth + 2;
                for (int x = -spikeHalf; x <= spikeHalf; x += 4)
                {
                    TryBlueprint(spike, center + new IntVec3(x, 0, front + 2), accessCorridor, map, faction, output);
                    TryBlueprint(spike, center + new IntVec3(x, 0, back - 2), accessCorridor, map, faction, output);
                }
                for (int z = back; z <= front; z += 4)
                {
                    TryBlueprint(spike, center + new IntVec3(-spikeHalf, 0, z), accessCorridor, map, faction, output);
                    TryBlueprint(spike, center + new IntVec3(spikeHalf, 0, z), accessCorridor, map, faction, output);
                }
            }
        }

        private static HashSet<IntVec3> BuildSupplyAccessCorridor(IntVec3 center, int mortarCount)
        {
            HashSet<IntVec3> reserved = new HashSet<IntVec3>();
            for (int z = -4; z <= 3; z++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    reserved.Add(center + new IntVec3(x, 0, z));
                }
            }

            List<IntVec3> mortarCells = MortarCells(center, mortarCount).ToList();
            int minX = mortarCells.Min(cell => cell.x) - 1;
            int maxX = mortarCells.Max(cell => cell.x) + 1;
            for (int x = minX; x <= maxX; x++)
            {
                reserved.Add(new IntVec3(x, 0, center.z + 2));
                reserved.Add(new IntVec3(x, 0, center.z + 3));
            }
            foreach (IntVec3 mortarCell in mortarCells)
            {
                reserved.Add(mortarCell);
                reserved.Add(mortarCell + IntVec3.South);
            }
            return reserved;
        }

        private static void PlaceCornerModule(IntVec3 innerCorner, int inwardX, int inwardZ, int horizontalLength, int verticalLength, ThingDef innerCover,
            HashSet<IntVec3> reserved, Map map, Faction faction, List<Blueprint_Build> output)
        {
            for (int i = 0; i < horizontalLength; i++)
            {
                IntVec3 inner = innerCorner + new IntVec3(inwardX * i, 0, 0);
                TryBlueprint(innerCover, inner, reserved, map, faction, output);
                TryBlueprint(ThingDefOf.Sandbags, inner + new IntVec3(0, 0, -inwardZ), reserved, map, faction, output);
            }
            for (int i = 1; i < verticalLength; i++)
            {
                IntVec3 inner = innerCorner + new IntVec3(0, 0, inwardZ * i);
                TryBlueprint(innerCover, inner, reserved, map, faction, output);
                TryBlueprint(ThingDefOf.Sandbags, inner + new IntVec3(-inwardX, 0, 0), reserved, map, faction, output);
            }
            TryBlueprint(ThingDefOf.Sandbags, innerCorner + new IntVec3(-inwardX, 0, -inwardZ), reserved, map, faction, output);
        }

        private static void TryBlueprint(ThingDef def, IntVec3 cell, HashSet<IntVec3> reserved, Map map, Faction faction, List<Blueprint_Build> output)
        {
            if (def == null
                || reserved.Contains(cell)
                || !cell.InBounds(map)
                || cell.Fogged(map)
                || cell.GetEdifice(map) != null
                || cell.GetTerrain(map)?.IsWater == true
                || cell.GetThingList(map).Any(thing => thing is Blueprint || thing is Frame)) return;
            Blueprint_Build bp = GenConstruct.PlaceBlueprintForBuild(def, cell, map, Rot4.North, faction, GenStuff.DefaultStuffFor(def));
            if (bp != null) output.Add(bp);
        }

        public static LordJob MakeAssaultLord(Faction faction) => new LordJob_AssaultColony(faction, true, true, false, false, true, false, false);

        public static void SpawnJumping(List<Pawn> pawns, Thing source, LordJob lordJob, int delay = 0, IEnumerable<Pawn> existingPawns = null)
        {
            if (pawns.NullOrEmpty() || source?.Spawned != true) return;
            Map map = source.Map;
            List<IntVec3> candidates = GenRadial.RadialCellsAround(source.Position, 4f, true)
                .Where(cell => cell.InBounds(map) && cell.Walkable(map) && !cell.Fogged(map) && cell.GetFirstPawn(map) == null)
                .InRandomOrder()
                .ToList();
            List<IntVec3> destinations = new List<IntVec3>(pawns.Count);
            for (int i = 0; i < pawns.Count; i++)
            {
                destinations.Add(i < candidates.Count
                    ? candidates[i]
                    : CellFinder.RandomClosewalkCellNear(source.Position, map, 5));
            }

            List<Thing> flyers = new List<Thing>();
            List<IntVec3> starts = new List<IntVec3>();
            CellRect sourceRect = source.OccupiedRect();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                IntVec3 start = sourceRect.RandomCell;
                IntVec3 destination = destinations[i];
                GenSpawn.Spawn(pawn, start, map, Rot4.FromAngleFlat((map.Center - source.Position).AngleFlat));
                pawn.rotationTracker.FaceCell(destination);
                PawnFlyer flyer = PawnFlyer.MakeFlyer(ThingDefOf.PawnFlyer_Stun, pawn, destination, null, null, false, start.ToVector3() + new Vector3(0f, 0f, -1f));
                if (flyer != null) { flyers.Add(flyer); starts.Add(start); }
            }
            if (flyers.Count > 0)
            {
                Lord lord = LordMaker.MakeNewLord(source.Faction, lordJob, map);
                if (existingPawns != null)
                {
                    foreach (Pawn pawn in existingPawns.Where(p => p?.Spawned == true))
                    {
                        pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
                        lord.AddPawn(pawn);
                    }
                }
                map.deferredSpawner.AddRequest(new SpawnRequest(flyers, starts, flyers.Count, 0.1f) { initialDelay = delay, lord = lord });
            }
        }

        public static void SpawnNear(List<Pawn> pawns, IntVec3 center, Map map, LordJob lordJob)
        {
            foreach (Pawn pawn in pawns) if (!pawn.Spawned) GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(center, map, 7), map);
            if (pawns.Count > 0) LordMaker.MakeNewLord(pawns[0].Faction, lordJob, map, pawns.Where(p => p.Spawned));
        }
    }
}
