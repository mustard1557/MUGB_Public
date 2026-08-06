using HarmonyLib;
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
    public sealed class MUGB_GoblinTunnelCluster : IExposable
    {
        public int id;
        public int nextReinforcementTick;
        public int warningTick;
        public int wave;
        public int expectedTunnels;
        public int creationDeadlineTick;
        public bool warningSent;
        public Faction faction;
        public List<Building_GoblinTunnel> tunnels = new List<Building_GoblinTunnel>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref nextReinforcementTick, "nextReinforcementTick");
            Scribe_Values.Look(ref warningTick, "warningTick");
            Scribe_Values.Look(ref wave, "wave");
            Scribe_Values.Look(ref expectedTunnels, "expectedTunnels");
            Scribe_Values.Look(ref creationDeadlineTick, "creationDeadlineTick");
            Scribe_Values.Look(ref warningSent, "warningSent");
            Scribe_References.Look(ref faction, "faction");
            Scribe_Collections.Look(ref tunnels, "tunnels", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && tunnels == null)
            {
                tunnels = new List<Building_GoblinTunnel>();
            }
        }
    }

    public sealed class MUGB_GoblinTunnelClusterManager : MapComponent
    {
        private const int WarningLeadTicks = 2500;
        private const int InitialSquadDelayTicks = 720;
        private const int ClusterCheckIntervalTicks = 250;

        private List<MUGB_GoblinTunnelCluster> clusters = new List<MUGB_GoblinTunnelCluster>();
        private int nextClusterId = 1;

        public MUGB_GoblinTunnelClusterManager(Map map) : base(map)
        {
        }

        public int CreateCluster(Faction faction, int expectedTunnels)
        {
            int now = Find.TickManager.TicksGame;
            int nextTick = now + MUGB_GoblinTunnelSpawner.EmergenceDelayMaxTicks + InitialSquadDelayTicks + RandomReinforcementDelay();
            MUGB_GoblinTunnelCluster cluster = new MUGB_GoblinTunnelCluster
            {
                id = nextClusterId++,
                faction = faction,
                expectedTunnels = expectedTunnels,
                creationDeadlineTick = now + MUGB_GoblinTunnelSpawner.EmergenceDelayMaxTicks + 600,
                nextReinforcementTick = nextTick,
                warningTick = nextTick - WarningLeadTicks
            };
            clusters.Add(cluster);
            return cluster.id;
        }

        public void RegisterTunnel(int clusterId, Building_GoblinTunnel tunnel)
        {
            MUGB_GoblinTunnelCluster cluster = FindCluster(clusterId);
            if (cluster != null && tunnel != null && !cluster.tunnels.Contains(tunnel))
            {
                cluster.tunnels.Add(tunnel);
            }
        }

        public bool TryGetStatus(int clusterId, out int ticksUntilWave, out int wave, out int livingTunnels)
        {
            MUGB_GoblinTunnelCluster cluster = FindCluster(clusterId);
            if (cluster == null)
            {
                ticksUntilWave = 0;
                wave = 0;
                livingTunnels = 0;
                return false;
            }

            PruneDestroyed(cluster);
            ticksUntilWave = Mathf.Max(0, cluster.nextReinforcementTick - Find.TickManager.TicksGame);
            wave = cluster.wave;
            livingTunnels = cluster.tunnels.Count;
            return livingTunnels > 0;
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % ClusterCheckIntervalTicks != 0)
            {
                return;
            }

            for (int i = clusters.Count - 1; i >= 0; i--)
            {
                MUGB_GoblinTunnelCluster cluster = clusters[i];
                PruneDestroyed(cluster);
                if (cluster.tunnels.Count == 0)
                {
                    if (now >= cluster.creationDeadlineTick)
                    {
                        clusters.RemoveAt(i);
                    }
                    continue;
                }

                if (!cluster.warningSent && now >= cluster.warningTick)
                {
                    cluster.warningSent = true;
                    Messages.Message(
                        "MUGB_GoblinTunnelReinforcementWarning".Translate(),
                        new LookTargets(cluster.tunnels.Cast<Thing>()),
                        MessageTypeDefOf.ThreatSmall,
                        historical: false);
                }

                if (now < cluster.nextReinforcementTick)
                {
                    continue;
                }

                cluster.wave++;
                List<Building_GoblinTunnel> participants = cluster.tunnels
                    .Where(tunnel => tunnel.CanReinforceAtWave(cluster.wave))
                    .ToList();
                foreach (Building_GoblinTunnel tunnel in participants)
                {
                    tunnel.ReleaseReinforcement(cluster.wave);
                }

                if (participants.Count > 0)
                {
                    Messages.Message(
                        "MUGB_GoblinTunnelReinforcementsArrived".Translate(),
                        new LookTargets(participants.Cast<Thing>()),
                        MessageTypeDefOf.ThreatBig,
                        historical: false);
                }

                foreach (Building_GoblinTunnel tunnel in participants.Where(t => t.ShouldCollapseAfterWave(cluster.wave)).ToList())
                {
                    tunnel.CollapseAfterFinalWave();
                }
                PruneDestroyed(cluster);
                if (cluster.tunnels.Count == 0 || cluster.wave >= 7)
                {
                    clusters.RemoveAt(i);
                    continue;
                }

                int delay = RandomReinforcementDelay();
                cluster.nextReinforcementTick = now + delay;
                cluster.warningTick = cluster.nextReinforcementTick - WarningLeadTicks;
                cluster.warningSent = false;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextClusterId, "nextClusterId", 1);
            Scribe_Collections.Look(ref clusters, "clusters", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && clusters == null)
            {
                clusters = new List<MUGB_GoblinTunnelCluster>();
            }
        }

        private MUGB_GoblinTunnelCluster FindCluster(int id)
        {
            return clusters.FirstOrDefault(cluster => cluster.id == id);
        }

        private static void PruneDestroyed(MUGB_GoblinTunnelCluster cluster)
        {
            cluster.tunnels.RemoveAll(tunnel => tunnel == null || tunnel.Destroyed || !tunnel.Spawned);
        }

        private static int RandomReinforcementDelay()
        {
            // RimWorld uses 2,500 ticks per in-game hour. Keep the final interval itself
            // within 8-10 hours so a second squad cannot arrive unexpectedly early.
            return Rand.RangeInclusive(20000, 25000);
        }
    }

    public class MUGB_GoblinTunnelSpawner : ThingWithComps, IThingHolder
    {
        private const int DefaultSpawnDelayTicks = 1050;
        private const int SpawnDelayJitterTicks = 60;
        private const float FilthSpawnMTB = 0.45f;
        private const float DustSpawnMTB = 0.35f;

        public const int EmergenceDelayMaxTicks = DefaultSpawnDelayTicks + SpawnDelayJitterTicks;

        private ThingOwner<Pawn> innerContainer;
        private int spawnTick = -1;
        private int clusterId;
        private float assignedPoints;
        private List<int> initialSquadSizes = new List<int>();
        private Faction tunnelFaction;
        private bool edgeTunnel;
        private bool largeTunnel;
        private bool completing;
        private Sustainer sustainer;
        private Effecter sustainedFx;

        public new IThingHolder ParentHolder => Map;

        public MUGB_GoblinTunnelSpawner()
        {
            innerContainer = new ThingOwner<Pawn>(this, false, LookMode.Deep, false);
        }

        public void Init(IEnumerable<Pawn> pawns, IEnumerable<int> squadSizes, Faction faction, bool edgeTunnel, bool largeTunnel, int clusterId, float assignedPoints)
        {
            tunnelFaction = faction;
            this.edgeTunnel = edgeTunnel;
            this.largeTunnel = largeTunnel;
            this.clusterId = clusterId;
            this.assignedPoints = assignedPoints;
            initialSquadSizes = squadSizes?.Where(size => size > 0).ToList() ?? new List<int>();
            innerContainer.ClearAndDestroyContents(DestroyMode.Vanish);
            foreach (Pawn pawn in pawns)
            {
                innerContainer.TryAdd(pawn, false);
            }
        }

        public ThingOwner GetDirectlyHeldThings() => innerContainer;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, innerContainer);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                spawnTick = Find.TickManager.TicksGame + DefaultSpawnDelayTicks + Rand.RangeInclusive(-SpawnDelayJitterTicks, SpawnDelayJitterTicks);
            }
            CreateFX();
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned)
            {
                return;
            }
            sustainer?.Maintain();
            sustainedFx?.EffectTick(this, this);
            if (Rand.MTBEventOccurs(FilthSpawnMTB, 60f, 1f)
                && CellFinder.TryFindRandomReachableNearbyCell(this.OccupiedRect().RandomCell, Map, 2.8f, TraverseParms.For(TraverseMode.NoPassClosedDoors), null, null, out IntVec3 filthCell))
            {
                FilthMaker.TryMakeFilth(filthCell, Map, Rand.Bool ? ThingDefOf.Filth_Dirt : ThingDefOf.Filth_RubbleRock);
            }
            if (Rand.MTBEventOccurs(DustSpawnMTB, 60f, 1f))
            {
                FleckMaker.ThrowDustPuff(DrawPos + new Vector3(Rand.Range(-0.8f, 0.8f), 0f, Rand.Range(-0.8f, 0.8f)), Map, Rand.Range(1.2f, 2f));
            }
            if (spawnTick > 0 && Find.TickManager.TicksGame >= spawnTick)
            {
                CompleteEmergence();
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
        }

        public override string GetInspectString()
        {
            StringBuilder builder = new StringBuilder(base.GetInspectString());
            builder.AppendLineIfNotEmpty();
            if (spawnTick > Find.TickManager.TicksGame)
            {
                builder.Append("Emergence".Translate() + ": " + (spawnTick - Find.TickManager.TicksGame).ToStringTicksToPeriod());
            }
            return builder.ToString();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Values.Look(ref spawnTick, "spawnTick", -1);
            Scribe_Values.Look(ref clusterId, "clusterId");
            Scribe_Values.Look(ref assignedPoints, "assignedPoints");
            Scribe_Collections.Look(ref initialSquadSizes, "initialSquadSizes", LookMode.Value);
            Scribe_Values.Look(ref edgeTunnel, "edgeTunnel");
            Scribe_Values.Look(ref largeTunnel, "largeTunnel");
            Scribe_References.Look(ref tunnelFaction, "tunnelFaction");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && innerContainer == null)
            {
                innerContainer = new ThingOwner<Pawn>(this, false, LookMode.Deep, false);
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && initialSquadSizes == null)
            {
                initialSquadSizes = new List<int>();
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (!completing)
            {
                innerContainer?.ClearAndDestroyContents(DestroyMode.Vanish);
            }
            CleanupFX();
            base.Destroy(mode);
        }

        private void CompleteEmergence()
        {
            Map map = Map;
            IntVec3 loc = Position;
            Faction faction = tunnelFaction ?? (innerContainer.Count > 0 ? innerContainer[0].Faction : null);
            def.building?.groundSpawnerCompleteEffecter?.SpawnMaintained(loc, map);
            CleanupFX();

            ThingDef tunnelDef = largeTunnel ? MUGBDefOf.MUGB_GoblinTunnelB : MUGBDefOf.MUGB_GoblinTunnelA;
            Building_GoblinTunnel tunnel = (Building_GoblinTunnel)ThingMaker.MakeThing(tunnelDef);
            tunnel.SetFaction(faction);
            GenSpawn.Spawn(tunnel, loc, map, Rot4.North);
            if (clusterId <= 0)
            {
                clusterId = map.GetComponent<MUGB_GoblinTunnelClusterManager>().CreateCluster(faction, 1);
            }
            tunnel.Initialize(clusterId, assignedPoints, edgeTunnel, largeTunnel);
            map.GetComponent<MUGB_GoblinTunnelClusterManager>().RegisterTunnel(clusterId, tunnel);

            List<Pawn> initialSquad = new List<Pawn>();
            for (int i = 0; i < innerContainer.Count; i++)
            {
                initialSquad.Add(innerContainer[i]);
            }
            innerContainer.Clear();
            tunnel.ReleaseInitialForces(initialSquad, initialSquadSizes);
            completing = true;
            Destroy(DestroyMode.Vanish);
        }

        private void CreateFX()
        {
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                sustainer = SoundDefOf.Tunnel.TrySpawnSustainer(SoundInfo.InMap(this, MaintenanceType.PerTick));
                sustainedFx = def.building?.groundSpawnerSustainedEffecter?.Spawn(this, Map);
            });
        }

        private void CleanupFX()
        {
            sustainedFx?.Cleanup();
            sustainedFx = null;
            if (sustainer != null && !sustainer.Ended)
            {
                sustainer.End();
            }
            sustainer = null;
        }
    }

    public class Building_GoblinTunnel : Building, IThingHolder
    {
        private const int InitialSquadDelayTicks = 720;
        private const int EmergenceDelayTicks = 180;
        private const float SmallReinforcementFactor = 0.60f;
        private const float LargeReinforcementFactor = 0.40f;

        private int clusterId;
        private float assignedPoints;
        private bool edgeTunnel;
        private bool largeTunnel;
        private ThingOwner<Pawn> initialContainer;
        private List<int> initialSquadSizes = new List<int>();
        private int initialReleaseTick = -1;
        private int lastReinforcementWave;

        public new IThingHolder ParentHolder => Map;

        public Building_GoblinTunnel()
        {
            initialContainer = new ThingOwner<Pawn>(this, false, LookMode.Deep, false);
        }

        public ThingOwner GetDirectlyHeldThings() => initialContainer;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, initialContainer);
        }

        public void Initialize(int clusterId, float assignedPoints, bool edgeTunnel, bool largeTunnel)
        {
            this.clusterId = clusterId;
            this.assignedPoints = assignedPoints;
            this.edgeTunnel = edgeTunnel;
            this.largeTunnel = largeTunnel;
        }

        public void ReleaseInitialForces(List<Pawn> initialSquad, List<int> squadSizes)
        {
            int slaveCount = largeTunnel ? 5 : 2;
            List<Pawn> slaves = GeneratePawns(DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_GoblinKind_TunnelSlave"), slaveCount);
            if (slaves.Count > 0)
            {
                if (Rand.Chance(0.10f) && slaves.Count >= 2)
                {
                    Pawn sapper = slaves[0];
                    SpawnAsUndergroundJump(
                        new List<Pawn> { sapper },
                        new MUGB_LordJob_StageThenTunnelAssault(Faction, Position, Rand.Int, true, InitialSquadDelayTicks),
                        0);
                    SpawnAsUndergroundJump(
                        slaves.Skip(1).ToList(),
                        new MUGB_LordJob_StageThenEscortSapper(Faction, Position, sapper, InitialSquadDelayTicks),
                        0);
                }
                else
                {
                    SpawnAsUndergroundJump(
                        slaves,
                        new MUGB_LordJob_StageThenTunnelAssault(Faction, Position, Rand.Int, false, InitialSquadDelayTicks),
                        0);
                }
            }
            if (!initialSquad.NullOrEmpty())
            {
                initialSquadSizes = !squadSizes.NullOrEmpty()
                    ? new List<int>(squadSizes)
                    : new List<int> { initialSquad.Count };
                foreach (Pawn pawn in initialSquad)
                {
                    initialContainer.TryAdd(pawn, false);
                }
                initialReleaseTick = Find.TickManager.TicksGame + InitialSquadDelayTicks;
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (initialReleaseTick > 0 && Find.TickManager.TicksGame >= initialReleaseTick)
            {
                ReleaseStoredInitialSquads();
            }
        }

        public bool CanReinforceAtWave(int wave)
        {
            return Spawned && !Destroyed && wave > lastReinforcementWave && wave >= 1 && wave <= (largeTunnel ? 7 : 4);
        }

        public bool ShouldCollapseAfterWave(int wave)
        {
            return wave >= (largeTunnel ? 7 : 4);
        }

        public void ReleaseReinforcement(int wave)
        {
            if (!CanReinforceAtWave(wave))
            {
                return;
            }
            lastReinforcementWave = wave;
            PlayDiggingEffects();
            float factor = largeTunnel ? LargeReinforcementFactor : SmallReinforcementFactor;
            int maxReinforcementSize = edgeTunnel ? 5 : 4;
            List<PawnKindDef> kinds = Squads.MUGB_SquadRaidUtility.GenerateTunnelSquadKinds(
                Faction,
                assignedPoints * factor,
                maxReinforcementSize);
            List<Pawn> pawns = kinds.Select(kind => PawnGenerator.GeneratePawn(kind, Faction)).ToList();
            if (pawns.Count > 0)
            {
                SpawnAsUndergroundJump(pawns, MakeAssaultLordJob(), EmergenceDelayTicks);
            }
        }

        public void CollapseAfterFinalWave()
        {
            if (!Spawned)
            {
                return;
            }
            PlayDiggingEffects();
            Destroy(DestroyMode.Vanish);
        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            MUGB_MedievalBaseManager baseManager = Map?.GetComponent<MUGB_MedievalBaseManager>();
            if (baseManager?.IsDefenseTunnel(this) == true)
            {
                string baseStatus = baseManager.TunnelInspectString();
                return text.NullOrEmpty() ? baseStatus : text + "\n" + baseStatus;
            }
            MUGB_GoblinTunnelClusterManager manager = Map?.GetComponent<MUGB_GoblinTunnelClusterManager>();
            if (manager != null && manager.TryGetStatus(clusterId, out int ticks, out int wave, out int living))
            {
                if (!text.NullOrEmpty())
                {
                    text += "\n";
                }
                int maxWaves = largeTunnel ? 7 : 4;
                text += "MUGB_GoblinTunnelNextReinforcement".Translate(ticks.ToStringTicksToPeriod());
                text += "\n" + "MUGB_GoblinTunnelRemainingWaves".Translate(Mathf.Max(0, maxWaves - wave));
                text += "\n" + "MUGB_GoblinTunnelLivingCount".Translate(living);
                text += "\n" + "MUGB_GoblinTunnelDestroyHint".Translate();
            }
            return text;
        }

        public bool TryGetReinforcementTicks(out int ticks)
        {
            ticks = 0;
            MUGB_GoblinTunnelClusterManager manager = Map?.GetComponent<MUGB_GoblinTunnelClusterManager>();
            return manager != null && manager.TryGetStatus(clusterId, out ticks, out _, out _);
        }

        public override void PostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostApplyDamage(dinfo, totalDamageDealt);
            Map?.GetComponent<MUGB_MedievalBaseManager>()?.NotifyTunnelAttacked(this, dinfo);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref clusterId, "clusterId");
            Scribe_Values.Look(ref assignedPoints, "assignedPoints");
            Scribe_Values.Look(ref edgeTunnel, "edgeTunnel");
            Scribe_Values.Look(ref largeTunnel, "largeTunnel");
            Scribe_Deep.Look(ref initialContainer, "innerContainer", this);
            Scribe_Collections.Look(ref initialSquadSizes, "initialSquadSizes", LookMode.Value);
            Scribe_Values.Look(ref initialReleaseTick, "initialReleaseTick", -1);
            Scribe_Values.Look(ref lastReinforcementWave, "lastReinforcementWave", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && initialContainer == null)
            {
                initialContainer = new ThingOwner<Pawn>(this, false, LookMode.Deep, false);
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && initialSquadSizes == null)
            {
                initialSquadSizes = new List<int>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && initialContainer.Count > 0 && initialReleaseTick < 0)
            {
                initialSquadSizes = new List<int> { initialContainer.Count };
                initialReleaseTick = Find.TickManager.TicksGame + InitialSquadDelayTicks;
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            initialContainer?.ClearAndDestroyContents(DestroyMode.Vanish);
            base.Destroy(mode);
        }

        private void ReleaseStoredInitialSquads()
        {
            initialReleaseTick = -1;
            List<Pawn> pawns = new List<Pawn>();
            for (int i = 0; i < initialContainer.Count; i++)
            {
                pawns.Add(initialContainer[i]);
            }
            initialContainer.Clear();

            int pawnIndex = 0;
            foreach (int requestedSize in initialSquadSizes)
            {
                int size = Mathf.Min(requestedSize, pawns.Count - pawnIndex);
                if (size <= 0)
                {
                    break;
                }
                List<Pawn> squad = pawns.GetRange(pawnIndex, size);
                SpawnAsUndergroundJump(
                    squad,
                    MUGB_SuicideSapperUtility.ContainsSuicideBomber(squad)
                        ? MakeSuicideSapperLordJob()
                        : MakeAssaultLordJob(),
                    0);
                pawnIndex += size;
            }
            initialSquadSizes.Clear();
        }

        private List<Pawn> GeneratePawns(PawnKindDef kind, int count)
        {
            List<Pawn> pawns = new List<Pawn>();
            if (kind == null || Faction == null)
            {
                return pawns;
            }
            for (int i = 0; i < count; i++)
            {
                pawns.Add(PawnGenerator.GeneratePawn(kind, Faction));
            }
            return pawns;
        }

        private LordJob MakeAssaultLordJob()
        {
            return new LordJob_AssaultColony(
                Faction,
                canKidnap: true,
                canTimeoutOrFlee: false,
                sappers: false,
                useAvoidGridSmart: false,
                canSteal: true,
                breachers: false,
                canPickUpOpportunisticWeapons: false);
        }

        private LordJob MakeSuicideSapperLordJob()
        {
            return new LordJob_AssaultColony(
                Faction,
                canKidnap: true,
                canTimeoutOrFlee: false,
                sappers: true,
                useAvoidGridSmart: true,
                canSteal: true,
                breachers: false,
                canPickUpOpportunisticWeapons: false);
        }

        private void SpawnAsUndergroundJump(List<Pawn> pawns, LordJob lordJob, int delayTicks)
        {
            if (!Spawned || pawns.NullOrEmpty())
            {
                return;
            }

            List<IntVec3> destinations = FindEmergenceDestinations(pawns.Count);
            List<Thing> flyers = new List<Thing>();
            List<IntVec3> sourceCells = new List<IntVec3>();
            CellRect sourceRect = this.OccupiedRect();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                IntVec3 source = sourceRect.RandomCell;
                IntVec3 destination = destinations[i];
                GenSpawn.Spawn(pawn, source, Map, Rot4.FromAngleFlat((Map.Center - Position).AngleFlat));
                pawn.rotationTracker.FaceCell(destination);
                PawnFlyer flyer = PawnFlyer.MakeFlyer(
                    ThingDefOf.PawnFlyer_Stun,
                    pawn,
                    destination,
                    null,
                    null,
                    false,
                    source.ToVector3() + new Vector3(0f, 0f, -1f));
                if (flyer != null)
                {
                    flyers.Add(flyer);
                    sourceCells.Add(source);
                }
            }

            if (flyers.Count == 0)
            {
                return;
            }
            // Korean source intent: one tunnel wave is one squad. The whole squad jumps out together,
            // rather than looking like an endless stream of individual pawns.
            SpawnRequest request = new SpawnRequest(flyers, sourceCells, flyers.Count, 0.1f)
            {
                initialDelay = delayTicks,
                lord = LordMaker.MakeNewLord(Faction, lordJob, Map)
            };
            Map.deferredSpawner.AddRequest(request);
        }

        private List<IntVec3> FindEmergenceDestinations(int count)
        {
            List<IntVec3> candidates = GenRadial.RadialCellsAround(Position, largeTunnel ? 5f : 4f, true)
                .Where(cell => cell.InBounds(Map) && !cell.Fogged(Map) && cell.Walkable(Map) && cell.GetFirstPawn(Map) == null)
                .InRandomOrder()
                .ToList();
            List<IntVec3> result = new List<IntVec3>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(i < candidates.Count ? candidates[i] : CellFinder.RandomClosewalkCellNear(Position, Map, largeTunnel ? 6 : 5));
            }
            return result;
        }

        private void PlayDiggingEffects()
        {
            if (ThingMaker.MakeThing(MUGBDefOf.MUGB_GoblinTunnelDiggingFX) is MUGB_TunnelDiggingFX diggingFx)
            {
                GenSpawn.Spawn(diggingFx, Position, Map);
                diggingFx.Initialize(largeTunnel);
            }
        }
    }

    public sealed class MUGB_TunnelDiggingFX : Thing
    {
        private const int DurationTicks = 180;
        private int destroyTick;
        private int nextDustTick;
        private int nextFilthTick;
        private bool largeTunnel;
        private Sustainer sustainer;
        private Effecter sustainedFx;

        public void Initialize(bool large)
        {
            largeTunnel = large;
            if (Spawned)
            {
                CreateSustainedEffect();
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                destroyTick = Find.TickManager.TicksGame + DurationTicks;
                nextDustTick = Find.TickManager.TicksGame;
                nextFilthTick = Find.TickManager.TicksGame + 45;
            }
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                if (Spawned)
                {
                    sustainer = SoundDefOf.Tunnel.TrySpawnSustainer(SoundInfo.InMap(this, MaintenanceType.PerTick));
                    CreateSustainedEffect();
                }
            });
        }

        protected override void Tick()
        {
            base.Tick();
            sustainer?.Maintain();
            sustainedFx?.EffectTick(this, this);
            int now = Find.TickManager.TicksGame;
            if (now >= nextDustTick && Map != null)
            {
                nextDustTick = now + Rand.RangeInclusive(18, 30);
                FleckMaker.ThrowDustPuff(DrawPos + new Vector3(Rand.Range(-0.9f, 0.9f), 0f, Rand.Range(-0.9f, 0.9f)), Map, Rand.Range(0.8f, 1.45f));
            }
            if (now >= nextFilthTick && Map != null)
            {
                nextFilthTick = now + Rand.RangeInclusive(55, 80);
                if (CellFinder.TryFindRandomReachableNearbyCell(Position, Map, largeTunnel ? 3.5f : 2.5f, TraverseParms.For(TraverseMode.NoPassClosedDoors), null, null, out IntVec3 cell))
                {
                    FilthMaker.TryMakeFilth(cell, Map, Rand.Bool ? ThingDefOf.Filth_Dirt : ThingDefOf.Filth_RubbleRock);
                }
            }
            if (now >= destroyTick)
            {
                ThingDef spawnerDef = largeTunnel ? MUGBDefOf.MUGB_GoblinTunnelSpawnerB : MUGBDefOf.MUGB_GoblinTunnelSpawnerA;
                spawnerDef?.building?.groundSpawnerCompleteEffecter?.SpawnMaintained(Position, Map);
                Destroy(DestroyMode.Vanish);
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (sustainer != null && !sustainer.Ended)
            {
                sustainer.End();
            }
            sustainer = null;
            sustainedFx?.Cleanup();
            sustainedFx = null;
            base.Destroy(mode);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref destroyTick, "destroyTick");
            Scribe_Values.Look(ref nextDustTick, "nextDustTick");
            Scribe_Values.Look(ref nextFilthTick, "nextFilthTick");
            Scribe_Values.Look(ref largeTunnel, "largeTunnel", false);
        }

        private void CreateSustainedEffect()
        {
            if (sustainedFx != null || Map == null)
            {
                return;
            }
            ThingDef spawnerDef = largeTunnel ? MUGBDefOf.MUGB_GoblinTunnelSpawnerB : MUGBDefOf.MUGB_GoblinTunnelSpawnerA;
            sustainedFx = spawnerDef?.building?.groundSpawnerSustainedEffecter?.Spawn(this, Map);
        }
    }

    public sealed class MUGB_LordJob_StageThenTunnelAssault : LordJob
    {
        private Faction faction;
        private IntVec3 stageLoc;
        private int raidSeed;
        private bool breachers;
        private int delayTicks;

        public MUGB_LordJob_StageThenTunnelAssault()
        {
        }

        public MUGB_LordJob_StageThenTunnelAssault(Faction faction, IntVec3 stageLoc, int raidSeed, bool breachers, int delayTicks)
        {
            this.faction = faction;
            this.stageLoc = stageLoc;
            this.raidSeed = raidSeed;
            this.breachers = breachers;
            this.delayTicks = delayTicks;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            LordToil_Stage stage = (LordToil_Stage)(graph.StartingToil = new LordToil_Stage(stageLoc));
            LordJob_AssaultColony assault = new LordJob_AssaultColony(
                faction,
                canKidnap: true,
                canTimeoutOrFlee: false,
                sappers: false,
                useAvoidGridSmart: false,
                canSteal: true,
                breachers: breachers,
                canPickUpOpportunisticWeapons: false);
            LordToil assaultStart = graph.AttachSubgraph(assault.CreateGraph()).StartingToil;
            Transition begin = new Transition(stage, assaultStart);
            begin.AddTrigger(new Trigger_TicksPassed(delayTicks));
            begin.AddTrigger(new Trigger_FractionPawnsLost(0.3f));
            begin.AddPostAction(new TransitionAction_WakeAll());
            graph.AddTransition(begin);
            return graph;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref stageLoc, "stageLoc");
            Scribe_Values.Look(ref raidSeed, "raidSeed");
            Scribe_Values.Look(ref breachers, "breachers");
            Scribe_Values.Look(ref delayTicks, "delayTicks");
        }
    }

    public sealed class MUGB_LordJob_StageThenEscortSapper : LordJob
    {
        private Faction faction;
        private IntVec3 stageLoc;
        private Pawn sapper;
        private int delayTicks;

        public MUGB_LordJob_StageThenEscortSapper()
        {
        }

        public MUGB_LordJob_StageThenEscortSapper(Faction faction, IntVec3 stageLoc, Pawn sapper, int delayTicks)
        {
            this.faction = faction;
            this.stageLoc = stageLoc;
            this.sapper = sapper;
            this.delayTicks = delayTicks;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            LordToil_Stage stage = (LordToil_Stage)(graph.StartingToil = new LordToil_Stage(stageLoc));
            LordToil_EscortPawn escort = new LordToil_EscortPawn(sapper, 7f);
            graph.AddToil(escort);
            LordToil assault = graph.AttachSubgraph(new LordJob_AssaultColony(
                faction,
                canKidnap: true,
                canTimeoutOrFlee: false,
                sappers: false,
                useAvoidGridSmart: false,
                canSteal: true,
                breachers: false,
                canPickUpOpportunisticWeapons: false).CreateGraph()).StartingToil;

            Transition begin = new Transition(stage, escort);
            begin.AddTrigger(new Trigger_TicksPassed(delayTicks));
            begin.AddTrigger(new Trigger_FractionPawnsLost(0.3f));
            begin.AddPostAction(new TransitionAction_WakeAll());
            graph.AddTransition(begin);

            Transition sapperLost = new Transition(escort, assault);
            sapperLost.AddTrigger(new Trigger_Custom(signal => signal.type == TriggerSignalType.Tick
                && (sapper == null || sapper.Dead || !sapper.SpawnedOrAnyParentSpawned)));
            graph.AddTransition(sapperLost);
            return graph;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref stageLoc, "stageLoc");
            Scribe_References.Look(ref sapper, "sapper");
            Scribe_Values.Look(ref delayTicks, "delayTicks");
        }
    }

    public class PawnsArrivalModeWorker_GoblinTunnel : PawnsArrivalModeWorker
    {
        protected virtual bool CenterTunnel => false;

        public override bool CanUseWith(IncidentParms parms)
        {
            return base.CanUseWith(parms)
                && parms?.faction != null
                && Squads.MUGB_SquadRaidUtility.CanUseGoblinTunnelWarfare(parms.faction)
                && parms.target is Map map
                && parms.points >= 300f
                && TryFindTunnelCell(map, CenterTunnel, out _);
        }

        public override bool TryResolveRaidSpawnCenter(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (!TryFindTunnelCell(map, CenterTunnel, out IntVec3 cell))
            {
                return false;
            }
            parms.spawnCenter = cell;
            parms.spawnRotation = Rot4.FromAngleFlat((map.Center - cell).AngleFlat);
            return true;
        }

        public override void Arrive(List<Pawn> pawns, IncidentParms parms)
        {
            if (pawns.NullOrEmpty() || !(parms.target is Map map))
            {
                return;
            }

            if (!Squads.MUGB_SquadRaidUtility.TryConsumeSquadLayout(parms, out List<int> squadSizes)
                || squadSizes.Sum() != pawns.Count
                || squadSizes.Any(size => size < 3))
            {
                squadSizes = BuildFallbackSquadSizes(pawns.Count);
            }

            List<TunnelAssignment> assignments = BuildAssignments(pawns, squadSizes, parms.points);
            List<IntVec3> cells = FindClusterCells(map, parms.spawnCenter, assignments, out bool usedEdgeFallback);
            if (cells.Count != assignments.Count)
            {
                PawnsArrivalModeWorker fallback = PawnsArrivalModeDefOf.EdgeWalkIn.Worker;
                if (fallback.TryResolveRaidSpawnCenter(parms))
                {
                    fallback.Arrive(pawns, parms);
                }
                return;
            }

            MUGB_GoblinTunnelClusterManager manager = map.GetComponent<MUGB_GoblinTunnelClusterManager>();
            int clusterId = manager.CreateCluster(parms.faction, assignments.Count);
            List<MUGB_GoblinTunnelSpawner> spawners = new List<MUGB_GoblinTunnelSpawner>();
            for (int i = 0; i < assignments.Count; i++)
            {
                TunnelAssignment assignment = assignments[i];
                ThingDef spawnerDef = assignment.Large ? MUGBDefOf.MUGB_GoblinTunnelSpawnerB : MUGBDefOf.MUGB_GoblinTunnelSpawnerA;
                MUGB_GoblinTunnelSpawner spawner = (MUGB_GoblinTunnelSpawner)ThingMaker.MakeThing(spawnerDef);
                int tunnelSize = assignment.Large ? 3 : 2;
                DestroyConstructedFloor(cells[i], map, tunnelSize);
                spawner.Init(assignment.Pawns, assignment.SquadSizes, parms.faction, !CenterTunnel || usedEdgeFallback, assignment.Large, clusterId, assignment.Points);
                GenSpawn.Spawn(spawner, cells[i], map, Rot4.North);
                spawners.Add(spawner);
            }

            parms.spawnCenter = spawners[0].Position;
            MUGB_GoblinTunnelLetterTargets.Register(parms, spawners);
            bool centralPlacement = CenterTunnel && !usedEdgeFallback;
            Messages.Message(
                (centralPlacement ? "MUGB_GoblinTunnelCenterIncoming" : "MUGB_GoblinTunnelEdgeIncoming").Translate(),
                new LookTargets(spawners.Cast<Thing>()),
                MessageTypeDefOf.ThreatBig,
                historical: false);
            pawns.Clear();
        }

        private static List<int> BuildFallbackSquadSizes(int pawnCount)
        {
            List<int> sizes = new List<int>();
            while (pawnCount > 0)
            {
                int size = Mathf.Min(6, pawnCount);
                if (pawnCount - size > 0 && pawnCount - size < 3)
                {
                    size -= 3 - (pawnCount - size);
                }
                sizes.Add(size);
                pawnCount -= size;
            }
            return sizes;
        }

        private static List<TunnelAssignment> BuildAssignments(List<Pawn> pawns, List<int> squadSizes, float raidPoints)
        {
            List<TunnelAssignment> squads = new List<TunnelAssignment>();
            int pawnIndex = 0;
            float defaultPoints = Mathf.Min(Squads.MUGB_SquadRaidUtility.SmallTunnelBudgetCap, raidPoints / Mathf.Max(1, squadSizes.Count));
            foreach (int size in squadSizes)
            {
                TunnelAssignment assignment = new TunnelAssignment { Points = defaultPoints };
                for (int i = 0; i < size && pawnIndex < pawns.Count; i++, pawnIndex++)
                {
                    assignment.Pawns.Add(pawns[pawnIndex]);
                }
                assignment.SquadSizes.Add(assignment.Pawns.Count);
                squads.Add(assignment);
            }

            int largeCount = RollLargeTunnelCount(raidPoints, squads.Count);
            List<TunnelAssignment> result = new List<TunnelAssignment>();
            int index = 0;
            for (int large = 0; large < largeCount; large++)
            {
                const int squadsToMerge = 3;
                if (squads.Count - index < squadsToMerge)
                {
                    break;
                }
                TunnelAssignment merged = new TunnelAssignment { Large = true };
                for (int i = 0; i < squadsToMerge; i++, index++)
                {
                    merged.Pawns.AddRange(squads[index].Pawns);
                    merged.SquadSizes.AddRange(squads[index].SquadSizes);
                    merged.Points += squads[index].Points;
                }
                merged.Points = Mathf.Clamp(merged.Points, 1250f, 1500f);
                result.Add(merged);
            }
            while (index < squads.Count)
            {
                result.Add(squads[index++]);
            }
            return result;
        }

        private static int RollLargeTunnelCount(float points, int squadCount)
        {
            float chance;
            int max;
            if (points < 1500f) return 0;
            if (points < 2500f) { chance = 0.15f; max = 1; }
            else if (points < 4000f) { chance = 0.30f; max = 1; }
            else if (points < 6000f) { chance = 0.50f; max = 1; }
            else if (points < 8000f) { chance = 0.65f; max = 2; }
            else { chance = 0.75f; max = 2; }

            int count = 0;
            for (int i = 0; i < max && squadCount - count * 3 >= 3; i++)
            {
                if (Rand.Chance(chance))
                {
                    count++;
                }
            }
            return count;
        }

        private List<IntVec3> FindClusterCells(Map map, IntVec3 center, List<TunnelAssignment> assignments, out bool usedEdgeFallback)
        {
            usedEdgeFallback = false;
            if (CenterTunnel)
            {
                foreach (IntVec3 candidate in BuildCenterTunnelCandidates(map, center))
                {
                    List<IntVec3> cells = TryFindClusterWithinRadii(map, candidate, assignments);
                    if (cells.Count == assignments.Count)
                    {
                        return cells;
                    }
                }

                // Preserve the tunnel raid before falling all the way back to an ordinary walk-in.
                for (int i = 0; i < 6; i++)
                {
                    if (!TryFindEdgeTunnelCell(map, out IntVec3 edgeCenter))
                    {
                        break;
                    }
                    List<IntVec3> cells = TryFindClusterWithinRadii(map, edgeCenter, assignments);
                    if (cells.Count == assignments.Count)
                    {
                        usedEdgeFallback = true;
                        return cells;
                    }
                }
                return new List<IntVec3>();
            }

            List<IntVec3> initialCells = TryFindClusterWithinRadii(map, center, assignments);
            if (initialCells.Count == assignments.Count)
            {
                return initialCells;
            }
            for (int i = 0; i < 4; i++)
            {
                if (TryFindEdgeTunnelCell(map, out IntVec3 alternativeCenter))
                {
                    List<IntVec3> cells = TryFindClusterWithinRadii(map, alternativeCenter, assignments);
                    if (cells.Count == assignments.Count)
                    {
                        return cells;
                    }
                }
            }
            return new List<IntVec3>();
        }

        private static List<IntVec3> TryFindClusterWithinRadii(Map map, IntVec3 center, List<TunnelAssignment> assignments)
        {
            List<IntVec3> cells = TryFindClusterCells(map, center, assignments, 13);
            return cells.Count == assignments.Count ? cells : TryFindClusterCells(map, center, assignments, 20);
        }

        private static List<IntVec3> TryFindClusterCells(Map map, IntVec3 center, List<TunnelAssignment> assignments, int radius)
        {
            const int minSpacingSquared = 16;
            List<IntVec3> candidates = GenRadial.RadialCellsAround(center, radius, true)
                .Where(cell => cell.InBounds(map) && !cell.Fogged(map))
                .InRandomOrder()
                .ToList();
            List<IntVec3> result = new List<IntVec3>();
            foreach (TunnelAssignment assignment in assignments.OrderByDescending(a => a.Large))
            {
                int size = assignment.Large ? 3 : 2;
                IntVec3 chosen = candidates.FirstOrDefault(cell => CanPlaceTunnelAt(cell, map, size)
                    && result.All(existing => existing.DistanceToSquared(cell) >= minSpacingSquared));
                if (!chosen.IsValid)
                {
                    return new List<IntVec3>();
                }
                result.Add(chosen);
                candidates.Remove(chosen);
            }
            return result;
        }

        protected static bool TryFindTunnelCell(Map map, bool center, out IntVec3 result)
        {
            if (center)
            {
                return TryFindCenterTunnelCell(map, out result);
            }
            return TryFindEdgeTunnelCell(map, out result);
        }

        private static bool TryFindEdgeTunnelCell(Map map, out IntVec3 result)
        {
            for (int i = 0; i < 80; i++)
            {
                if (RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 edge, map, CellFinder.EdgeRoadChance_Hostile, allowFogged: false))
                {
                    IntVec3 candidate = CellFinder.RandomClosewalkCellNear(edge, map, 16);
                    if (CanPlaceTunnelAt(candidate, map, 2))
                    {
                        result = candidate;
                        return true;
                    }
                }
            }
            return CellFinder.TryFindRandomCell(map, cell => CanPlaceTunnelAt(cell, map, 2), out result);
        }

        private static bool TryFindCenterTunnelCell(Map map, out IntVec3 result)
        {
            List<IntVec3> candidates = BuildCenterTunnelCandidates(map, IntVec3.Invalid);
            if (candidates.Count > 0)
            {
                result = candidates[0];
                return true;
            }
            result = IntVec3.Invalid;
            return false;
        }

        private static List<IntVec3> BuildCenterTunnelCandidates(Map map, IntVec3 preferred)
        {
            const int maxCandidates = 32;
            HashSet<IntVec3> seen = new HashSet<IntVec3>();
            List<IntVec3> result = new List<IntVec3>();

            void AddCandidate(IntVec3 cell)
            {
                if (result.Count < maxCandidates && cell.IsValid && seen.Add(cell) && CanPlaceTunnelAt(cell, map, 2))
                {
                    result.Add(cell);
                }
            }

            AddCandidate(preferred);

            foreach (Building building in map.listerBuildings.allBuildingsColonist
                .Where(building => building?.Spawned == true)
                .InRandomOrder()
                .Take(6))
            {
                for (int i = 0; i < 2; i++)
                {
                    AddCandidate(CellFinder.RandomClosewalkCellNear(building.Position, map, 14));
                }
            }

            List<IntVec3> homeCells = map.areaManager.Home.ActiveCells
                .Where(cell => !cell.Fogged(map))
                .InRandomOrder()
                .Take(8)
                .ToList();
            foreach (IntVec3 homeCell in homeCells)
            {
                AddCandidate(homeCell);
            }

            // Dense home areas may have no empty 2x2 footprint. Try just outside them before an edge fallback.
            foreach (IntVec3 homeCell in homeCells)
            {
                AddCandidate(CellFinder.RandomClosewalkCellNear(homeCell, map, 10));
            }

            return result;
        }

        private static void DestroyConstructedFloor(IntVec3 root, Map map, int size)
        {
            foreach (IntVec3 cell in CellRect.FromLimits(root.x, root.z, root.x + size - 1, root.z + size - 1))
            {
                TerrainDef terrain = cell.GetTerrain(map);
                TerrainDef underTerrain = map.terrainGrid.UnderTerrainAt(cell);
                if (terrain?.IsFloor == true
                    && terrain.bridge == false
                    && underTerrain != null
                    && !underTerrain.IsWater
                    && map.terrainGrid.CanRemoveTopLayerAt(cell))
                {
                    map.terrainGrid.RemoveTopLayer(cell, doLeavings: false);
                }
            }
        }

        private static bool CanPlaceTunnelAt(IntVec3 root, Map map, int size)
        {
            CellRect rect = CellRect.FromLimits(root.x, root.z, root.x + size - 1, root.z + size - 1);
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map))
                {
                    return false;
                }
                TerrainDef terrain = cell.GetTerrain(map);
                TerrainDef underTerrain = map.terrainGrid.UnderTerrainAt(cell);
                TerrainDef foundation = map.terrainGrid.FoundationAt(cell);
                if (cell.Fogged(map)
                    || terrain?.IsWater == true
                    || terrain?.bridge == true
                    || underTerrain?.IsWater == true
                    || foundation?.bridge == true
                    || !cell.Standable(map)
                    || cell.GetEdifice(map) != null)
                {
                    return false;
                }
            }
            return true;
        }

        private sealed class TunnelAssignment
        {
            public readonly List<Pawn> Pawns = new List<Pawn>();
            public readonly List<int> SquadSizes = new List<int>();
            public float Points;
            public bool Large;
        }
    }

    public class PawnsArrivalModeWorker_GoblinTunnelCenter : PawnsArrivalModeWorker_GoblinTunnel
    {
        protected override bool CenterTunnel => true;

        // VEF patches Arrive on every concrete worker type via reflection. Keep a real override here
        // so it does not attempt to patch the edge worker's inherited MethodInfo twice.
        public override void Arrive(List<Pawn> pawns, IncidentParms parms)
        {
            base.Arrive(pawns, parms);
        }
    }

    public static class MUGB_GoblinTunnelLetterTargets
    {
        private static readonly Dictionary<IncidentParms, LookTargets> PendingTargets = new Dictionary<IncidentParms, LookTargets>();

        public static void Register(IncidentParms parms, IEnumerable<MUGB_GoblinTunnelSpawner> spawners)
        {
            if (parms != null)
            {
                PendingTargets[parms] = new LookTargets(spawners.Cast<Thing>());
            }
        }

        public static void Register(IncidentParms parms, LookTargets targets)
        {
            if (parms != null && targets != null)
            {
                PendingTargets[parms] = targets;
            }
        }

        public static bool TryConsume(IncidentParms parms, out LookTargets targets)
        {
            if (parms != null && PendingTargets.TryGetValue(parms, out targets))
            {
                PendingTargets.Remove(parms);
                return true;
            }
            targets = null;
            return false;
        }
    }

    public sealed class Alert_ActiveGoblinTunnels : Alert
    {
        private const int RefreshIntervalTicks = 250;

        private readonly List<Thing> activeTunnels = new List<Thing>();
        private int nextRefreshTick = -1;
        private int nearestReinforcementTicks = -1;

        public Alert_ActiveGoblinTunnels()
        {
            defaultPriority = AlertPriority.High;
        }

        public override AlertReport GetReport()
        {
            RefreshCache();
            return AlertReport.CulpritsAre(activeTunnels);
        }

        public override string GetLabel()
        {
            RefreshCache();
            return "MUGB_ActiveGoblinTunnelsLabel".Translate(activeTunnels.Count);
        }

        public override TaggedString GetExplanation()
        {
            RefreshCache();
            return nearestReinforcementTicks >= 0
                ? "MUGB_ActiveGoblinTunnelsDescTimed".Translate(nearestReinforcementTicks.ToStringTicksToPeriod())
                : "MUGB_ActiveGoblinTunnelsDesc".Translate();
        }

        private void RefreshCache()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (nextRefreshTick >= 0 && now < nextRefreshTick)
            {
                return;
            }

            nextRefreshTick = now + RefreshIntervalTicks;
            activeTunnels.Clear();
            nearestReinforcementTicks = -1;
            if (Current.ProgramState != ProgramState.Playing || Faction.OfPlayer == null)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                AddTunnels(map, MUGBDefOf.MUGB_GoblinTunnelA);
                AddTunnels(map, MUGBDefOf.MUGB_GoblinTunnelB);
                AddTunnels(map, MUGBDefOf.MUGB_GoblinMortarTunnel);
                AddTunnels(map, MUGBDefOf.MUGB_GoblinMortarSupportTunnel);
            }
        }

        private void AddTunnels(Map map, ThingDef tunnelDef)
        {
            if (map == null || tunnelDef == null)
            {
                return;
            }

            List<Thing> tunnels = map.listerThings.ThingsOfDef(tunnelDef);
            for (int i = 0; i < tunnels.Count; i++)
            {
                Thing tunnel = tunnels[i];
                if (tunnel?.Spawned != true || tunnel.Faction?.HostileTo(Faction.OfPlayer) != true)
                {
                    continue;
                }

                int ticks = 0;
                bool hasTimer = false;
                if (tunnel is Building_GoblinMortarTunnel mortar)
                {
                    hasTimer = mortar.TryGetReinforcementTicks(out ticks);
                }
                else if (tunnel is Building_GoblinTunnel regular)
                {
                    hasTimer = regular.TryGetReinforcementTicks(out ticks);
                }
                if (!hasTimer)
                {
                    continue;
                }

                activeTunnels.Add(tunnel);
                if (nearestReinforcementTicks < 0 || ticks < nearestReinforcementTicks)
                {
                    nearestReinforcementTicks = ticks;
                }
            }
        }
    }

    [HarmonyPatch(typeof(IncidentWorker), "SendStandardLetter", new Type[]
    {
        typeof(TaggedString), typeof(TaggedString), typeof(LetterDef), typeof(IncidentParms),
        typeof(LookTargets), typeof(NamedArgument[])
    })]
    public static class IncidentWorker_SendStandardLetter_GoblinTunnelTargetsPatch
    {
        public static void Prefix(IncidentParms parms, ref LookTargets lookTargets)
        {
            if (MUGB_GoblinTunnelLetterTargets.TryConsume(parms, out LookTargets tunnelTargets))
            {
                lookTargets = tunnelTargets;
            }
        }
    }

    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget))]
    public static class AttackTargetFinder_BestAttackTarget_GoblinTunnelPatch
    {
        public static void Prefix(IAttackTargetSearcher searcher, ref Predicate<Thing> validator)
        {
            if (!(searcher is Pawn pawn) || pawn.Faction != Faction.OfPlayer || pawn.Drafted)
            {
                return;
            }

            Predicate<Thing> original = validator;
            validator = thing => !(thing is Building_GoblinTunnel)
                && !(thing is Building_GoblinMortarTunnel)
                && (original == null || original(thing));
        }
    }
}
