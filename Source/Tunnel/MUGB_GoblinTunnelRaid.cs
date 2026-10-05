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
    // One emergence wave from a cluster. Every squad that surfaced together shares a morale pool,
    // so the whole wave breaks at once instead of squad by squad. Later waves start fresh.
    public sealed class MUGB_GoblinTunnelCohort : IExposable
    {
        public int wave;
        public int startTick;
        public int pawnsGained;
        public int pawnsLost;
        public float fleeFraction = 0.5f;
        public bool routed;
        public bool dirty;
        public List<Lord> lords = new List<Lord>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref wave, "wave");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref pawnsGained, "pawnsGained");
            Scribe_Values.Look(ref pawnsLost, "pawnsLost");
            Scribe_Values.Look(ref fleeFraction, "fleeFraction", 0.5f);
            Scribe_Values.Look(ref routed, "routed", false);
            Scribe_Values.Look(ref dirty, "dirty", false);
            Scribe_Collections.Look(ref lords, "lords", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                lords = lords ?? new List<Lord>();
                lords.RemoveAll(lord => lord == null);
            }
        }
    }

    public sealed class MUGB_GoblinTunnelCluster : IExposable
    {
        public int id;
        public int nextReinforcementTick;
        public int warningTick;
        public int wave;
        public int expectedTunnels;
        public int creationDeadlineTick;
        public bool warningSent;
        public bool sharedMorale;
        public List<MUGB_GoblinTunnelCohort> cohorts = new List<MUGB_GoblinTunnelCohort>();
        public Faction faction;
        public List<Building_GoblinTunnel> tunnels = new List<Building_GoblinTunnel>();

        // 0.8.9 fields. Only read, so an in-flight raid keeps its morale instead of resetting to none.
        private List<Lord> legacyLords;
        private int legacyGained;
        private int legacyLost;
        private float legacyFleeFraction = 0.5f;
        private bool legacyRetreating;
        private int legacyMoraleStartTick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref nextReinforcementTick, "nextReinforcementTick");
            Scribe_Values.Look(ref warningTick, "warningTick");
            Scribe_Values.Look(ref wave, "wave");
            Scribe_Values.Look(ref expectedTunnels, "expectedTunnels");
            Scribe_Values.Look(ref creationDeadlineTick, "creationDeadlineTick");
            Scribe_Values.Look(ref warningSent, "warningSent");
            Scribe_Values.Look(ref sharedMorale, "sharedMorale", false);
            Scribe_Collections.Look(ref cohorts, "cohorts", LookMode.Deep);
            Scribe_References.Look(ref faction, "faction");
            Scribe_Collections.Look(ref tunnels, "tunnels", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Collections.Look(ref legacyLords, "lords", LookMode.Reference);
                Scribe_Values.Look(ref legacyGained, "pawnsGained");
                Scribe_Values.Look(ref legacyLost, "pawnsLost");
                Scribe_Values.Look(ref legacyFleeFraction, "fleeFraction", 0.5f);
                Scribe_Values.Look(ref legacyRetreating, "retreating", false);
                Scribe_Values.Look(ref legacyMoraleStartTick, "moraleStartTick");
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                tunnels = tunnels ?? new List<Building_GoblinTunnel>();
                cohorts = cohorts ?? new List<MUGB_GoblinTunnelCohort>();
                MigrateLegacyMorale();
            }
        }

        private void MigrateLegacyMorale()
        {
            legacyLords?.RemoveAll(lord => lord == null);
            if (cohorts.Count > 0 || legacyLords.NullOrEmpty())
            {
                legacyLords = null;
                return;
            }
            cohorts.Add(new MUGB_GoblinTunnelCohort
            {
                wave = wave,
                startTick = legacyMoraleStartTick,
                pawnsGained = legacyGained,
                pawnsLost = legacyLost,
                fleeFraction = legacyFleeFraction,
                routed = legacyRetreating,
                lords = new List<Lord>(legacyLords)
            });
            legacyLords = null;
        }
    }

    public sealed class MUGB_GoblinTunnelClusterManager : MapComponent
    {
        private const int WarningLeadTicks = 2500;
        private const int InitialSquadDelayTicks = 720;
        private const int ClusterCheckIntervalTicks = 250;

        private List<MUGB_GoblinTunnelCluster> clusters = new List<MUGB_GoblinTunnelCluster>();
        private int nextClusterId = 1;
        private readonly Dictionary<Lord, MUGB_GoblinTunnelCohort> moraleByLord =
            new Dictionary<Lord, MUGB_GoblinTunnelCohort>();

        public MUGB_GoblinTunnelClusterManager(Map map) : base(map)
        {
        }

        public int CreateCluster(Faction faction, int expectedTunnels, bool sharedMorale)
        {
            int now = Find.TickManager.TicksGame;
            int nextTick = now + MUGB_GoblinTunnelSpawner.EmergenceDelayMaxTicks + InitialSquadDelayTicks + RandomReinforcementDelay();
            MUGB_GoblinTunnelCluster cluster = new MUGB_GoblinTunnelCluster
            {
                id = nextClusterId++,
                faction = faction,
                expectedTunnels = expectedTunnels,
                // Wave-wide morale is a central-tunnel behavior. Edge tunnels keep vanilla per-squad flee only.
                sharedMorale = sharedMorale && faction != null && faction.def.autoFlee && !faction.neverFlee,
                creationDeadlineTick = now + MUGB_GoblinTunnelSpawner.EmergenceDelayMaxTicks + 600,
                nextReinforcementTick = nextTick,
                warningTick = nextTick - WarningLeadTicks
            };
            clusters.Add(cluster);
            return cluster.id;
        }

        internal void RegisterSquad(int clusterId, Lord lord)
        {
            MUGB_GoblinTunnelCluster cluster = FindCluster(clusterId);
            if (cluster?.sharedMorale != true || lord == null || moraleByLord.ContainsKey(lord)) return;
            MUGB_GoblinTunnelCohort cohort = CohortForCurrentWave(cluster);
            cohort.lords.Add(lord);
            moraleByLord.Add(lord, cohort);
        }

        // Squads that surface in the same wave share one pool. A wave that arrives after an earlier
        // wave broke starts with fresh morale, which is what lets a raid keep pressing between waves.
        private MUGB_GoblinTunnelCohort CohortForCurrentWave(MUGB_GoblinTunnelCluster cluster)
        {
            MUGB_GoblinTunnelCohort cohort = cluster.cohorts.FirstOrDefault(c => c.wave == cluster.wave);
            if (cohort != null) return cohort;

            int now = Find.TickManager.TicksGame;
            cohort = new MUGB_GoblinTunnelCohort
            {
                wave = cluster.wave,
                // Ignore losses while the squad is still climbing out of the ground. The opening wave
                // also stages before it attacks, so it needs the longer grace.
                startTick = now + MUGB_GoblinTunnelSpawner.EmergenceDelayMaxTicks + 60
                    + (cluster.wave == 0 ? InitialSquadDelayTicks : 0),
                fleeFraction = cluster.faction?.def.attackersDownPercentageRangeForAutoFlee.RandomInRange ?? 0.5f
            };
            cluster.cohorts.Add(cohort);
            return cohort;
        }

        internal void NotifySquadChanged(Lord lord, int gained, int lost)
        {
            if (!moraleByLord.TryGetValue(lord, out MUGB_GoblinTunnelCohort cohort)) return;
            cohort.pawnsGained += gained;
            cohort.pawnsLost += lost;
            cohort.dirty = true;
            // Defer transitions until the current Lord notification has completely unwound.
            // The existing cluster maintenance pass processes this flag.
        }

        private void UpdateMorale(MUGB_GoblinTunnelCluster cluster, int now)
        {
            if (!cluster.sharedMorale) return;
            foreach (MUGB_GoblinTunnelCohort cohort in cluster.cohorts)
            {
                UpdateCohort(cluster, cohort, now);
            }
        }

        private void UpdateCohort(MUGB_GoblinTunnelCluster cluster, MUGB_GoblinTunnelCohort cohort, int now)
        {
            if (cohort.routed || !cohort.dirty || now < cohort.startTick) return;
            cohort.dirty = false;
            if (!map.CanEverExit || cluster.faction == null || !cluster.faction.HostileTo(Faction.OfPlayer)
                || cohort.pawnsGained == 0 || cohort.pawnsLost < cohort.pawnsGained * cohort.fleeFraction) return;

            cohort.routed = true;
            Pawn witness = null;
            foreach (Lord lord in cohort.lords)
            {
                if (lord == null || !map.lordManager.lords.Contains(lord) || lord.faction != cluster.faction) continue;
                LordToil flee = lord.Graph.lordToils.Find(toil => toil is LordToil_PanicFlee);
                if (flee == null || lord.CurLordToil == flee) continue;
                lord.GotoToil(flee);
                QuestUtility.SendQuestTargetSignals(lord.questTags, "Fleeing", lord.Named("SUBJECT"));
                witness = witness ?? lord.ownedPawns.FirstOrDefault(pawn => pawn != null && pawn.Spawned);
            }
            // Nobody left to run means the wave was wiped out, not routed. Stay quiet.
            if (witness == null) return;
            Messages.Message(
                "MessageFightersFleeing".Translate(
                    cluster.faction.def.pawnsPlural.CapitalizeFirst(), cluster.faction.Name),
                new LookTargets(witness),
                MessageTypeDefOf.NeutralEvent);
        }

        private bool HasActiveSquads(MUGB_GoblinTunnelCluster cluster)
        {
            if (!cluster.sharedMorale) return false;
            bool any = false;
            for (int c = cluster.cohorts.Count - 1; c >= 0; c--)
            {
                MUGB_GoblinTunnelCohort cohort = cluster.cohorts[c];
                for (int i = cohort.lords.Count - 1; i >= 0; i--)
                {
                    Lord lord = cohort.lords[i];
                    if (lord != null && map.lordManager.lords.Contains(lord)) continue;
                    if (lord != null) moraleByLord.Remove(lord);
                    cohort.lords.RemoveAt(i);
                }
                if (cohort.lords.Count > 0) any = true;
                else if (cohort.routed) cluster.cohorts.RemoveAt(c);
            }
            return any;
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
                UpdateMorale(cluster, now);
                PruneDestroyed(cluster);
                if (cluster.tunnels.Count == 0)
                {
                    if (now >= cluster.creationDeadlineTick && !HasActiveSquads(cluster))
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
                List<Building_GoblinTunnel> reinforced = participants
                    .Where(tunnel => tunnel.ReleaseReinforcement(cluster.wave))
                    .ToList();

                if (reinforced.Count > 0)
                {
                    Messages.Message(
                        "MUGB_GoblinTunnelReinforcementsArrived".Translate(),
                        new LookTargets(reinforced.Cast<Thing>()),
                        MessageTypeDefOf.ThreatBig,
                        historical: false);
                }

                foreach (Building_GoblinTunnel tunnel in reinforced.Where(t => t.ShouldCollapseAfterWave(cluster.wave)).ToList())
                {
                    tunnel.CollapseAfterFinalWave();
                }
                PruneDestroyed(cluster);
                if ((cluster.tunnels.Count == 0 || cluster.wave >= 7) && !HasActiveSquads(cluster))
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
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                foreach (MUGB_GoblinTunnelCluster cluster in clusters) HasActiveSquads(cluster);
            }
            Scribe_Values.Look(ref nextClusterId, "nextClusterId", 1);
            Scribe_Collections.Look(ref clusters, "clusters", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && clusters == null)
            {
                clusters = new List<MUGB_GoblinTunnelCluster>();
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            moraleByLord.Clear();
            foreach (MUGB_GoblinTunnelCluster cluster in clusters)
            {
                if (!cluster.sharedMorale) continue;
                foreach (MUGB_GoblinTunnelCohort cohort in cluster.cohorts)
                {
                    foreach (Lord lord in cohort.lords)
                    {
                        if (lord != null) moraleByLord[lord] = cohort;
                    }
                }
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

    internal static class MUGB_GoblinTunnelPawnUtility
    {
        public static bool NormalizeForRaid(Pawn pawn, Faction faction)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || faction == null)
            {
                return false;
            }

            // Tunnel workers are hostile combat pawns despite their PawnKind name and collar.
            // Clear only an accidental colony status before restoring the raid faction.
            if (pawn.IsSlaveOfColony || pawn.IsPrisonerOfColony)
            {
                pawn.guest?.SetGuestStatus(null);
            }
            if (pawn.Faction != faction)
            {
                pawn.SetFaction(faction);
            }
            return pawn.Faction == faction;
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
                if (MUGB_GoblinTunnelPawnUtility.NormalizeForRaid(pawn, faction))
                {
                    innerContainer.TryAddOrTransfer(pawn, false);
                }
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
                clusterId = map.GetComponent<MUGB_GoblinTunnelClusterManager>().CreateCluster(faction, 1, sharedMorale: false);
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
            int slaveCount = largeTunnel ? 3 : 2;
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

            initialSquad = NormalizePawns(initialSquad);
            if (initialSquad.Count == 0)
            {
                initialSquad = GenerateFallbackSquad(edgeTunnel ? 5 : 4);
                squadSizes = initialSquad.Count > 0 ? new List<int> { initialSquad.Count } : new List<int>();
            }
            if (!initialSquad.NullOrEmpty())
            {
                initialSquadSizes = !squadSizes.NullOrEmpty()
                    ? new List<int>(squadSizes)
                    : new List<int> { initialSquad.Count };
                bool storageFailed = false;
                foreach (Pawn pawn in initialSquad)
                {
                    storageFailed |= !initialContainer.TryAddOrTransfer(pawn, false);
                }
                if (storageFailed)
                {
                    initialSquadSizes = new List<int> { initialContainer.Count };
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

        public bool ReleaseReinforcement(int wave)
        {
            if (!CanReinforceAtWave(wave))
            {
                return false;
            }
            PlayDiggingEffects();
            float factor = largeTunnel ? LargeReinforcementFactor : SmallReinforcementFactor;
            int maxReinforcementSize = largeTunnel || edgeTunnel ? 5 : 4;
            List<PawnKindDef> kinds = Squads.MUGB_SquadRaidUtility.GenerateTunnelSquadKinds(
                Faction,
                assignedPoints * factor,
                maxReinforcementSize);
            List<Pawn> pawns = kinds.Select(kind => PawnGenerator.GeneratePawn(kind, Faction)).ToList();
            if (pawns.Count == 0)
            {
                pawns = GenerateFallbackSquad(Mathf.Min(3, maxReinforcementSize));
            }
            if (!SpawnAsUndergroundJump(pawns, MakeAssaultLordJob(), EmergenceDelayTicks))
            {
                return false;
            }
            lastReinforcementWave = wave;
            return true;
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
            pawns = NormalizePawns(pawns);
            if (pawns.Count == 0)
            {
                pawns = GenerateFallbackSquad(edgeTunnel ? 5 : 4);
                initialSquadSizes = pawns.Count > 0 ? new List<int> { pawns.Count } : new List<int>();
            }

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

        private List<Pawn> GenerateFallbackSquad(int maxSquadSize)
        {
            List<PawnKindDef> kinds = Squads.MUGB_SquadRaidUtility.GenerateTunnelSquadKinds(
                Faction,
                Mathf.Max(assignedPoints, 1f),
                maxSquadSize);
            if (!kinds.NullOrEmpty())
            {
                return NormalizePawns(kinds.Select(kind => PawnGenerator.GeneratePawn(kind, Faction)));
            }

            PawnKindDef worker = DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_GoblinKind_TunnelSlave");
            return NormalizePawns(GeneratePawns(worker, Mathf.Clamp(maxSquadSize, 1, 3)));
        }

        private List<Pawn> NormalizePawns(IEnumerable<Pawn> pawns)
        {
            return pawns?
                .Where(pawn => MUGB_GoblinTunnelPawnUtility.NormalizeForRaid(pawn, Faction))
                .ToList() ?? new List<Pawn>();
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

        private bool SpawnAsUndergroundJump(List<Pawn> pawns, LordJob lordJob, int delayTicks)
        {
            pawns = NormalizePawns(pawns);
            if (!Spawned || pawns.Count == 0 || lordJob == null)
            {
                return false;
            }

            List<IntVec3> destinations = FindEmergenceDestinations(pawns.Count);
            List<Thing> flyers = new List<Thing>();
            List<IntVec3> sourceCells = new List<IntVec3>();
            List<Pawn> directSpawns = new List<Pawn>();
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
                else
                {
                    directSpawns.Add(pawn);
                }
            }

            Lord lord = LordMaker.MakeNewLord(Faction, lordJob, Map);
            Map.GetComponent<MUGB_GoblinTunnelClusterManager>().RegisterSquad(clusterId, lord);
            if (flyers.Count > 0)
            {
                // Korean source intent: one tunnel wave is one squad. The whole squad jumps out together,
                // rather than looking like an endless stream of individual pawns.
                SpawnRequest request = new SpawnRequest(flyers, sourceCells, flyers.Count, 0.1f)
                {
                    initialDelay = delayTicks,
                    lord = lord
                };
                Map.deferredSpawner.AddRequest(request);
            }

            foreach (Pawn pawn in directSpawns)
            {
                if (!pawn.Spawned)
                {
                    GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(Position, Map, largeTunnel ? 6 : 5), Map);
                }
                lord.AddPawn(pawn);
            }
            return flyers.Count > 0 || directSpawns.Count > 0;
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
                && MUGBMod.Settings?.enableGoblinTunnelRaids != false
                && parms?.faction != null
                && Squads.MUGB_SquadRaidUtility.CanUseGoblinTunnelWarfare(parms.faction)
                && parms.target is Map map
                && parms.points >= 300f
                && TryFindTunnelCell(map, CenterTunnel, out _);
        }

        public override bool TryResolveRaidSpawnCenter(IncidentParms parms)
        {
            if (!MUGB_RaidArrivalGuard.IsRaid(parms, def))
                return MUGB_RaidArrivalGuard.ResolvePeacefulArrival(parms);
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
            if (!MUGB_RaidArrivalGuard.IsRaid(parms, def))
            {
                MUGB_RaidArrivalGuard.ArrivePeacefully(pawns, parms);
                return;
            }
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
            int clusterId = manager.CreateCluster(parms.faction, assignments.Count, CenterTunnel && !usedEdgeFallback);
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

        private const int LargeTunnelSquads = 4;
        private const float PackedSmallTunnelBudgetCap = 600f;

        private List<TunnelAssignment> BuildAssignments(List<Pawn> pawns, List<int> squadSizes, float raidPoints)
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

            // One squad used to mean one tunnel, so a big raid opened a dozen holes and every hole was
            // its own reinforcement tap. Pack squads into a bounded number of tunnels instead: the
            // opening wave is unchanged, but the number of taps scales gently with raid size.
            int largeCount = Mathf.Min(RollLargeTunnelCount(raidPoints, squads.Count), squads.Count / LargeTunnelSquads);
            List<TunnelAssignment> result = new List<TunnelAssignment>();
            int index = 0;
            for (int large = 0; large < largeCount; large++)
            {
                TunnelAssignment merged = new TunnelAssignment { Large = true };
                for (int i = 0; i < LargeTunnelSquads; i++, index++)
                {
                    merged.Pawns.AddRange(squads[index].Pawns);
                    merged.SquadSizes.AddRange(squads[index].SquadSizes);
                    merged.Points += squads[index].Points;
                }
                merged.Points = Mathf.Clamp(merged.Points, 1000f, 1800f);
                result.Add(merged);
            }

            int remaining = squads.Count - index;
            if (remaining > 0)
            {
                int smallTunnels = Mathf.Min(remaining, SmallTunnelLimit(raidPoints));
                int perTunnel = Mathf.CeilToInt(remaining / (float)smallTunnels);
                while (index < squads.Count)
                {
                    TunnelAssignment packed = new TunnelAssignment();
                    for (int i = 0; i < perTunnel && index < squads.Count; i++, index++)
                    {
                        packed.Pawns.AddRange(squads[index].Pawns);
                        packed.SquadSizes.AddRange(squads[index].SquadSizes);
                        packed.Points += squads[index].Points;
                    }
                    // Small tunnels reinforce at a higher rate than large ones, so an unbounded packed
                    // budget would let them out-scale the large tier. Keep them underneath it.
                    packed.Points = Mathf.Min(packed.Points, PackedSmallTunnelBudgetCap);
                    result.Add(packed);
                }
            }
            return result;
        }

        // Edge tunnels open outside the colony, so a few extra holes there cost nothing visually.
        private int SmallTunnelLimit(float points)
        {
            return Mathf.Clamp(Mathf.FloorToInt(points / 1000f), 2, CenterTunnel ? 5 : 8);
        }

        private static int RollLargeTunnelCount(float points, int squadCount)
        {
            // Points arrive already scaled by the arrival mode's pointsFactorCurve, so these thresholds
            // are measured against that reduced value rather than the storyteller's raw budget.
            int max = Mathf.Clamp(Mathf.FloorToInt(points / 1200f), 0, 3);
            int count = 0;
            for (int i = 0; i < max && squadCount - count * LargeTunnelSquads >= LargeTunnelSquads; i++)
            {
                if (Rand.Chance(0.75f))
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
                // A worse spot at the preferred tier beats a closer spot in someone's bedroom, so the
                // tier sweeps every candidate before the next tier is considered at all.
                List<IntVec3> candidates = BuildCenterTunnelCandidates(map, center);
                foreach (TunnelPlacementTier tier in PlacementTiers)
                {
                    foreach (IntVec3 candidate in candidates)
                    {
                        List<IntVec3> cells = TryFindClusterWithinRadii(map, candidate, assignments, tier);
                        if (cells.Count == assignments.Count)
                        {
                            return cells;
                        }
                    }
                }

                // Preserve the tunnel raid before falling all the way back to an ordinary walk-in.
                for (int i = 0; i < 6; i++)
                {
                    if (!TryFindEdgeTunnelCell(map, out IntVec3 edgeCenter))
                    {
                        break;
                    }
                    List<IntVec3> cells = TryFindClusterWithinRadii(map, edgeCenter, assignments, TunnelPlacementTier.Anywhere);
                    if (cells.Count == assignments.Count)
                    {
                        usedEdgeFallback = true;
                        return cells;
                    }
                }
                return new List<IntVec3>();
            }

            foreach (TunnelPlacementTier tier in PlacementTiers)
            {
                List<IntVec3> initialCells = TryFindClusterWithinRadii(map, center, assignments, tier);
                if (initialCells.Count == assignments.Count)
                {
                    return initialCells;
                }
            }
            for (int i = 0; i < 4; i++)
            {
                if (TryFindEdgeTunnelCell(map, out IntVec3 alternativeCenter))
                {
                    List<IntVec3> cells = TryFindClusterWithinRadii(map, alternativeCenter, assignments, TunnelPlacementTier.Anywhere);
                    if (cells.Count == assignments.Count)
                    {
                        return cells;
                    }
                }
            }
            return new List<IntVec3>();
        }

        private static List<IntVec3> TryFindClusterWithinRadii(Map map, IntVec3 center, List<TunnelAssignment> assignments, TunnelPlacementTier tier)
        {
            foreach (int radius in ClusterRadii)
            {
                List<IntVec3> cells = TryFindClusterCells(map, center, assignments, radius, tier);
                if (cells.Count == assignments.Count)
                {
                    return cells;
                }
            }
            return new List<IntVec3>();
        }

        // Digging up under someone's bed reads as unfair rather than threatening, so the rooms a
        // colony sleeps, heals and works in are the last resort, not the first pick.
        private enum TunnelPlacementTier
        {
            OutdoorOnly,
            AvoidLivingSpace,
            Anywhere
        }

        private static readonly TunnelPlacementTier[] PlacementTiers =
        {
            TunnelPlacementTier.OutdoorOnly,
            TunnelPlacementTier.AvoidLivingSpace,
            TunnelPlacementTier.Anywhere
        };

        private static readonly int[] ClusterRadii = { 13, 20 };

        private static List<IntVec3> TryFindClusterCells(Map map, IntVec3 center, List<TunnelAssignment> assignments, int radius, TunnelPlacementTier tier)
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
                IntVec3 chosen = candidates.FirstOrDefault(cell => CanPlaceTunnelAt(cell, map, size, tier)
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
            return CanPlaceTunnelAt(root, map, size, TunnelPlacementTier.Anywhere);
        }

        private static bool CanPlaceTunnelAt(IntVec3 root, Map map, int size, TunnelPlacementTier tier)
        {
            CellRect rect = CellRect.FromLimits(root.x, root.z, root.x + size - 1, root.z + size - 1);
            foreach (IntVec3 cell in rect)
            {
                if (!AllowedByTier(cell, map, tier))
                {
                    return false;
                }
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

        private static bool AllowedByTier(IntVec3 cell, Map map, TunnelPlacementTier tier)
        {
            if (tier == TunnelPlacementTier.Anywhere)
            {
                return true;
            }
            Room room = cell.GetRoom(map);
            // No room data means open ground as far as placement is concerned.
            if (room == null || room.PsychologicallyOutdoors)
            {
                return true;
            }
            if (tier == TunnelPlacementTier.OutdoorOnly)
            {
                return false;
            }
            // Storage, corridors and dining rooms still read as fair game; the rooms a colony sleeps,
            // heals and works in do not.
            return room.Role != RoomRoleDefOf.Bedroom
                && room.Role != RoomRoleDefOf.Barracks
                && room.Role != RoomRoleDefOf.Hospital
                && room.Role != RoomRoleDefOf.PrisonCell
                && room.Role != RoomRoleDefOf.PrisonBarracks
                && room.Role != RoomRoleDefOf.Laboratory
                && room.Role != RoomRoleDefOf.Workshop
                && !room.ContainedAndAdjacentThings.Any(thing => thing is Building_WorkTable || thing is Building_Bed);
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
