using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using RimWorld.BaseGen;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MUGB
{
    [HarmonyPatch]
    public static class MUGB_KCSGMedievalSettlementPointsPatch
    {
        internal static Faction DebugFactionOverride;

        public static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("KCSG.SymbolResolver_Settlement");
            return type == null ? null : AccessTools.Method(type, "Resolve");
        }

        public static void Prefix(ref ResolveParams rp)
        {
            if (DebugFactionOverride != null)
            {
                rp.faction = DebugFactionOverride;
            }
            if (!MUGB_MedievalBaseUtility.IsMedievalGoblinFaction(rp.faction))
            {
                return;
            }

            rp.settlementPawnGroupPoints = rp.faction.def == MUGBDefOf.MUGB_GoblinSavageMedieval
                ? Rand.Range(1450f, 1650f)
                : Rand.Range(1350f, 1550f);
        }
    }

    [HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.IsDefeated))]
    public static class MUGB_MedievalBaseDefeatGuardPatch
    {
        public static void Postfix(Map map, Faction faction, ref bool __result)
        {
            if (__result && map?.GetComponent<MUGB_MedievalBaseManager>()?.HasPendingThreats(faction) == true)
            {
                __result = false;
            }
        }
    }

    public static class MUGB_MedievalBaseUtility
    {
        public static bool IsMedievalGoblinFaction(Faction faction)
        {
            return faction?.def == MUGBDefOf.MUGB_GoblinCivilMedieval
                || faction?.def == MUGBDefOf.MUGB_GoblinSavageMedieval;
        }

        public static bool IsEffectiveDefender(Pawn pawn, Map map, Faction faction)
        {
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.Map != map || pawn.Faction != faction
                || !pawn.RaceProps.Humanlike || pawn.IsPrisoner || pawn.WorkTagIsDisabled(WorkTags.Violent)
                || pawn.InMentalState)
            {
                return false;
            }

            string duty = pawn.mindState?.duty?.def?.defName ?? string.Empty;
            return duty.IndexOf("Exit", StringComparison.OrdinalIgnoreCase) < 0
                && duty.IndexOf("Flee", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }

    public sealed class MUGB_MedievalBaseManager : MapComponent
    {
        private const int DefenderScanInterval = 1250;
        private const int TurretRefreshInterval = 250;
        private const int ExternalArrivalDelay = 1250;
        private const int TunnelWarningLead = 2500;
        private const int EmergencePreludeTicks = 180;

        private bool initialized;
        private bool tunnelActivated;
        private bool externalScheduled;
        private bool externalArrived;
        private bool lullWarningSent;
        private bool rewardPlaced;
        private int nextTunnelWaveTick = -1;
        private int tunnelWarningTick = -1;
        private int externalArrivalTick = -1;
        private int nextTunnelWaveIndex;
        private IntVec3 baseCenter;
        private Faction faction;
        private Building_GoblinTunnel defenseTunnel;
        private Lord turretLord;
        private List<Pawn> initialDefenders = new List<Pawn>();
        private List<Pawn> restrainedPrisoners = new List<Pawn>();
        private List<float> tunnelWaveBudgets = new List<float>();

        public MUGB_MedievalBaseManager(Map map) : base(map)
        {
        }

        public override void MapGenerated()
        {
            base.MapGenerated();
            if (!initialized)
            {
                TryInitialize();
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int now = Find.TickManager.TicksGame;
            if (!initialized)
            {
                if (now % TurretRefreshInterval == map.uniqueID % TurretRefreshInterval)
                {
                    TryInitialize();
                }
                return;
            }

            if (now % TurretRefreshInterval == map.uniqueID % TurretRefreshInterval)
            {
                RefreshTurretCrews();
            }
            if (now % DefenderScanInterval == map.uniqueID % DefenderScanInterval)
            {
                CheckDefenderThresholds();
                ReleaseFreedPrisoners();
                NotifyQuietRetreatPossible();
            }

            if (tunnelActivated && defenseTunnel?.Spawned == true && nextTunnelWaveIndex < tunnelWaveBudgets.Count)
            {
                if (tunnelWarningTick > 0 && now >= tunnelWarningTick)
                {
                    tunnelWarningTick = -1;
                    Messages.Message("MUGB_BaseTunnelWarning".Translate(), defenseTunnel, MessageTypeDefOf.ThreatSmall);
                }
                if (nextTunnelWaveTick > 0 && now >= nextTunnelWaveTick)
                {
                    SpawnNextTunnelWave();
                }
            }

            if (externalScheduled && !externalArrived && externalArrivalTick > 0 && now >= externalArrivalTick)
            {
                SpawnExternalReinforcement();
            }
        }

        private void TryInitialize()
        {
            faction = faction ?? map.ParentFaction;
            if (!MUGB_MedievalBaseUtility.IsMedievalGoblinFaction(faction))
            {
                return;
            }

            defenseTunnel = map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_GoblinTunnelB)
                .OfType<Building_GoblinTunnel>()
                .FirstOrDefault(tunnel => tunnel.Faction == faction && tunnel.Spawned);
            if (defenseTunnel == null)
            {
                return;
            }

            baseCenter = defenseTunnel.Position;
            initialDefenders = map.mapPawns.SpawnedPawnsInFaction(faction)
                .Where(pawn => pawn.RaceProps.Humanlike && !pawn.IsPrisoner && !pawn.WorkTagIsDisabled(WorkTags.Violent))
                .ToList();
            if (initialDefenders.Count < 3)
            {
                return;
            }

            SpawnCommander();
            initialDefenders = map.mapPawns.SpawnedPawnsInFaction(faction)
                .Where(pawn => pawn.RaceProps.Humanlike && !pawn.IsPrisoner && !pawn.WorkTagIsDisabled(WorkTags.Violent))
                .ToList();
            PrepareTunnelBudget();
            PrepareArtilleryCells();
            TopUpMortarShells();
            SpawnPrisoners();
            AddBattlefieldScenery();
            PlaceReward();
            RefreshTurretCrews();
            initialized = true;
        }

        internal void InitializeDebug(Faction debugFaction)
        {
            if (initialized)
            {
                return;
            }
            faction = debugFaction;
            TryInitialize();
        }

        private void SpawnCommander()
        {
            bool alreadyPresent = initialDefenders.Any(pawn => pawn.kindDef?.defName?.Contains("SquadLeader") == true);
            if (alreadyPresent)
            {
                return;
            }

            string kindName = faction.def == MUGBDefOf.MUGB_GoblinSavageMedieval
                ? "MUGB_GoblinKind_SquadLeaderLateMelee"
                : "MUGB_GoblinKind_SquadLeaderEarlyMelee";
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(kindName);
            if (kind == null)
            {
                return;
            }

            Pawn commander = PawnGenerator.GeneratePawn(kind, faction);
            GenSpawn.Spawn(commander, CellFinder.RandomClosewalkCellNear(baseCenter, map, 8), map);
            Lord defendLord = map.lordManager.lords.FirstOrDefault(lord => lord.faction == faction && lord.LordJob is LordJob_DefendBase);
            if (defendLord != null)
            {
                defendLord.AddPawn(commander);
            }
            else
            {
                LordMaker.MakeNewLord(faction, new LordJob_DefendBase(faction, baseCenter, 25000, false), map, new[] { commander });
            }
        }

        private void PrepareTunnelBudget()
        {
            int waveCount = Rand.RangeInclusive(4, 5);
            float total = Rand.Range(800f, 1050f);
            tunnelWaveBudgets.Clear();
            for (int i = 0; i < waveCount; i++)
            {
                tunnelWaveBudgets.Add(total / waveCount);
            }
        }

        private void CheckDefenderThresholds()
        {
            if (initialDefenders.NullOrEmpty())
            {
                return;
            }

            int effective = initialDefenders.Count(pawn => MUGB_MedievalBaseUtility.IsEffectiveDefender(pawn, map, faction));
            float ratio = effective / (float)initialDefenders.Count;
            if (!tunnelActivated && ratio <= 0.40f)
            {
                ActivateDefenseTunnel();
            }
            if (!externalScheduled && ratio <= 0.20f)
            {
                ScheduleExternalReinforcement();
            }
        }

        public void NotifyTunnelAttacked(Building_GoblinTunnel tunnel, DamageInfo dinfo)
        {
            if (!initialized || tunnelActivated || tunnel != defenseTunnel)
            {
                return;
            }
            if (dinfo.Instigator?.Faction?.HostileTo(faction) == true)
            {
                ActivateDefenseTunnel();
            }
        }

        private void ActivateDefenseTunnel()
        {
            if (tunnelActivated || defenseTunnel?.Spawned != true)
            {
                return;
            }

            tunnelActivated = true;
            PlayDigging(defenseTunnel, true);
            List<Pawn> firstWave = MakeFreeRegularSquad();
            if (tunnelWaveBudgets.Count > 0)
            {
                firstWave.AddRange(GenerateBudgetSquad(tunnelWaveBudgets[0]));
                nextTunnelWaveIndex = 1;
            }
            SpawnFromTunnel(firstWave);
            ScheduleNextTunnelWave();
        }

        private List<Pawn> MakeFreeRegularSquad()
        {
            List<string> regularNames = new List<string>
            {
                "MUGB_GoblinKind_MedievalLowSoldier",
                "MUGB_GoblinKind_MedievalSpear",
                "MUGB_GoblinKind_MedievalRanged",
                "MUGB_GoblinKind_MedievalGunner"
            };
            string leaderName = faction.def == MUGBDefOf.MUGB_GoblinSavageMedieval
                ? "MUGB_GoblinKind_SquadLeaderLateMelee"
                : "MUGB_GoblinKind_SquadLeaderEarlyMelee";
            List<PawnKindDef> kinds = new List<PawnKindDef> { DefDatabase<PawnKindDef>.GetNamedSilentFail(leaderName) };
            for (int i = 0; i < 4; i++)
            {
                kinds.Add(DefDatabase<PawnKindDef>.GetNamedSilentFail(regularNames.RandomElement()));
            }
            return kinds.Where(kind => kind != null).Select(kind => PawnGenerator.GeneratePawn(kind, faction)).ToList();
        }

        private List<Pawn> GenerateBudgetSquad(float points)
        {
            return Squads.MUGB_SquadRaidUtility.GenerateTunnelSquadKinds(faction, points)
                .Take(6)
                .Select(kind => PawnGenerator.GeneratePawn(kind, faction))
                .ToList();
        }

        private void SpawnNextTunnelWave()
        {
            if (defenseTunnel?.Spawned != true || nextTunnelWaveIndex >= tunnelWaveBudgets.Count)
            {
                return;
            }
            PlayDigging(defenseTunnel, true);
            SpawnFromTunnel(GenerateBudgetSquad(tunnelWaveBudgets[nextTunnelWaveIndex]));
            nextTunnelWaveIndex++;
            if (nextTunnelWaveIndex >= tunnelWaveBudgets.Count)
            {
                defenseTunnel.Destroy(DestroyMode.Vanish);
                nextTunnelWaveTick = -1;
                tunnelWarningTick = -1;
            }
            else
            {
                ScheduleNextTunnelWave();
            }
        }

        private void SpawnFromTunnel(List<Pawn> pawns)
        {
            if (!pawns.NullOrEmpty() && defenseTunnel?.Spawned == true)
            {
                MUGB_GoblinMortarSiegeUtility.SpawnJumping(
                    pawns,
                    defenseTunnel,
                    new LordJob_DefendBase(faction, baseCenter, 25000, false),
                    EmergencePreludeTicks);
            }
        }

        private void ScheduleNextTunnelWave()
        {
            if (nextTunnelWaveIndex >= tunnelWaveBudgets.Count || defenseTunnel?.Spawned != true)
            {
                nextTunnelWaveTick = -1;
                tunnelWarningTick = -1;
                return;
            }
            nextTunnelWaveTick = Find.TickManager.TicksGame + Rand.RangeInclusive(7500, 12500);
            tunnelWarningTick = nextTunnelWaveTick - TunnelWarningLead;
        }

        private void ScheduleExternalReinforcement()
        {
            externalScheduled = true;
            externalArrivalTick = Find.TickManager.TicksGame + ExternalArrivalDelay;
            Messages.Message("MUGB_BaseExternalWarning".Translate(), new TargetInfo(baseCenter, map), MessageTypeDefOf.ThreatSmall);
        }

        private void SpawnExternalReinforcement()
        {
            externalArrived = true;
            externalArrivalTick = -1;
            List<Pawn> pawns = GenerateBudgetSquad(Rand.Range(400f, 600f));
            if (pawns.NullOrEmpty())
            {
                return;
            }
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Hostile, false))
            {
                entry = CellFinder.RandomEdgeCell(map);
            }
            foreach (Pawn pawn in pawns)
            {
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(entry, map, 5), map);
            }
            LordMaker.MakeNewLord(faction, new LordJob_DefendBase(faction, baseCenter, 25000, false), map, pawns);
            Messages.Message("MUGB_BaseExternalArrived".Translate(), pawns[0], MessageTypeDefOf.ThreatBig);
        }

        private void RefreshTurretCrews()
        {
            List<Building_TurretGun> turrets = map.listerThings.AllThings
                .OfType<Building_TurretGun>()
                .Where(turret => turret.Spawned && turret.Faction == faction && turret.def.HasComp(typeof(CompMannable)))
                .ToList();
            int needed = turrets.Count;
            if (needed <= 0)
            {
                return;
            }

            List<Pawn> current = turretLord?.ownedPawns
                .Where(pawn => MUGB_MedievalBaseUtility.IsEffectiveDefender(pawn, map, faction))
                .Take(needed)
                .ToList() ?? new List<Pawn>();
            IEnumerable<Pawn> candidates = initialDefenders
                .Where(pawn => MUGB_MedievalBaseUtility.IsEffectiveDefender(pawn, map, faction) && !current.Contains(pawn))
                .OrderByDescending(pawn => pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0);
            current.AddRange(candidates.Take(needed - current.Count));
            if (current.Count == 0)
            {
                return;
            }

            if (turretLord == null || turretLord.ownedPawns.Count != current.Count || current.Any(pawn => pawn.GetLord() != turretLord))
            {
                turretLord?.RemoveAllPawns();
                turretLord = LordMaker.MakeNewLord(faction, new LordJob_ManTurrets(), map);
                foreach (Pawn pawn in current)
                {
                    pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
                    turretLord.AddPawn(pawn);
                }
            }
        }

        private void TopUpMortarShells()
        {
            ThingDef explosive = MUGBDefOf.MUGB_GoblinHighExplosiveShell;
            ThingDef stink = MUGBDefOf.MUGB_GoblinStinkMortarShell;
            foreach (Thing mortar in map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_GoblinMortar).Where(thing => thing.Faction == faction))
            {
                int target = Rand.RangeInclusive(12, 20);
                int existing = map.listerThings.AllThings.Count(thing => (thing.def == explosive || thing.def == stink) && thing.Position.InHorDistOf(mortar.Position, 40f));
                for (int i = existing; i < target; i++)
                {
                    Thing shell = ThingMaker.MakeThing(Rand.Chance(0.8f) ? explosive : stink);
                    GenPlace.TryPlaceThing(shell, CellFinder.RandomClosewalkCellNear(mortar.Position, map, 6), map, ThingPlaceMode.Near);
                }
            }
        }

        private void PrepareArtilleryCells()
        {
            foreach (Thing mortar in map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_GoblinMortar).Where(thing => thing.Faction == faction))
            {
                map.roofGrid.SetRoof(mortar.Position, null);
                if (mortar.def.hasInteractionCell)
                {
                    map.roofGrid.SetRoof(mortar.InteractionCell, null);
                }
            }
        }

        private void SpawnPrisoners()
        {
            PawnKindDef villager = PawnKindDefOf.Villager;
            HediffDef restrained = DefDatabase<HediffDef>.GetNamedSilentFail("MUGB_Restrained");
            List<Faction> origins = Find.FactionManager.AllFactionsVisible
                .Where(other => !other.IsPlayer && !other.defeated && other.def.humanlikeFaction && !IsGoblinFaction(other))
                .ToList();
            if (villager == null || origins.Count == 0)
            {
                return;
            }

            List<Thing> restraintProps = map.listerThings.AllThings
                .Where(thing => thing.Spawned && thing.Position.InHorDistOf(baseCenter, 34f)
                    && IsPrisonerRestraint(thing))
                .OrderByDescending(thing => thing is Building_Bed)
                .ToList();
            int count = Rand.RangeInclusive(3, 5);
            for (int i = 0; i < count; i++)
            {
                Faction origin = origins.RandomElement();
                PawnGenerationRequest request = new PawnGenerationRequest(
                    villager,
                    origin,
                    fixedBiologicalAge: Rand.Range(18f, 25.99f),
                    fixedChronologicalAge: Rand.Range(18f, 25.99f),
                    fixedGender: Gender.Female,
                    developmentalStages: DevelopmentalStage.Adult,
                    dontGiveWeapon: true,
                    forceNoGear: true);
                Pawn prisoner = PawnGenerator.GeneratePawn(request);
                if (GoblinUtility.HasGoblinCoreMarker(prisoner))
                {
                    prisoner.genes.SetXenotype(XenotypeDefOf.Baseliner);
                }
                Thing restraint = restraintProps.Count > i ? restraintProps[i] : null;
                Building_Bed restraintBed = restraint as Building_Bed;
                IntVec3 near = restraint?.Position ?? baseCenter;
                GenSpawn.Spawn(prisoner, restraintBed != null ? restraintBed.Position : FindClearCellNear(near, 5), map);
                prisoner.guest.SetGuestStatus(faction, GuestStatus.Prisoner);
                if (restraintBed != null)
                {
                    prisoner.ownership.ClaimBedIfNonMedical(restraintBed);
                    prisoner.jobs.StartJob(
                        JobMaker.MakeJob(JobDefOf.LayDown, restraintBed),
                        JobCondition.InterruptForced,
                        resumeCurJobAfterwards: false,
                        cancelBusyStances: true);
                }
                // BF bondage beds apply their own restraint after the LayDown toil starts.
                // Applying MUGB_Restrained first downs the pawn and interrupts that native flow.
                if (restraintBed == null && restrained != null)
                {
                    prisoner.health.AddHediff(restrained);
                }
                restrainedPrisoners.Add(prisoner);
                TryMakePregnant(prisoner);
            }
        }

        private static bool IsPrisonerRestraint(Thing thing)
        {
            string defName = thing?.def?.defName;
            return defName == "DDJY_PrisonerPole"
                || defName == "DDJY_BindingCross"
                || defName == "DDJY_Pillory"
                || defName == "DDJY_BarCage"
                || defName == "DDJY_RoundCage"
                || defName == "DankPyon_LogColumn";
        }

        private void TryMakePregnant(Pawn mother)
        {
            if (!ModsConfig.BiotechActive || !Rand.Chance(0.10f))
            {
                return;
            }
            Pawn father = initialDefenders.Where(pawn => pawn.gender == Gender.Male && !pawn.Dead).RandomElementWithFallback();
            if (father == null || !PregnancyUtility.CanEverProduceChild(father, mother).Accepted)
            {
                return;
            }
            Hediff_Pregnant pregnancy = HediffMaker.MakeHediff(HediffDefOf.PregnantHuman, mother) as Hediff_Pregnant;
            if (pregnancy != null)
            {
                pregnancy.SetParents(mother, father, PregnancyUtility.GetInheritedGeneSet(father, mother));
                pregnancy.Severity = Rand.Range(0.05f, 0.35f);
                mother.health.AddHediff(pregnancy);
            }
        }

        private void ReleaseFreedPrisoners()
        {
            HediffDef restrained = DefDatabase<HediffDef>.GetNamedSilentFail("MUGB_Restrained");
            if (restrained == null)
            {
                return;
            }
            foreach (Pawn pawn in restrainedPrisoners.Where(pawn => pawn != null && !pawn.Dead))
            {
                if (pawn.IsPrisoner && pawn.guest?.Released != true)
                {
                    Building_Bed restraintBed = pawn.ownership?.OwnedBed;
                    if (restraintBed != null && IsPrisonerRestraint(restraintBed))
                    {
                        Hediff prematureRestraint = pawn.health.hediffSet.GetFirstHediffOfDef(restrained);
                        if (prematureRestraint != null)
                        {
                            pawn.health.RemoveHediff(prematureRestraint);
                        }
                        if (pawn.CurrentBed() != restraintBed && pawn.CurJobDef != JobDefOf.LayDown)
                        {
                            pawn.jobs.StartJob(
                                JobMaker.MakeJob(JobDefOf.LayDown, restraintBed),
                                JobCondition.InterruptForced,
                                resumeCurJobAfterwards: false,
                                cancelBusyStances: true);
                        }
                    }
                    if (pawn.needs?.food != null)
                    {
                        pawn.needs.food.CurLevelPercentage = 1f;
                    }
                    if (pawn.needs?.rest != null)
                    {
                        pawn.needs.rest.CurLevelPercentage = 1f;
                    }
                    continue;
                }
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(restrained);
                if (hediff != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
                HediffDef bondage = DefDatabase<HediffDef>.GetNamedSilentFail("DDJY_Hediff_BondageBed");
                if (bondage != null)
                {
                    foreach (Hediff bondageHediff in pawn.health.hediffSet.hediffs
                        .Where(existing => existing.def == bondage).ToList())
                    {
                        pawn.health.RemoveHediff(bondageHediff);
                    }
                }
                if (pawn.CurJobDef == JobDefOf.LayDown)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                pawn.ownership?.UnclaimBed();
            }
        }

        public bool IsManagedPrisoner(Pawn pawn)
        {
            return pawn != null && restrainedPrisoners?.Contains(pawn) == true;
        }

        public void NotifyPrisonerReleased(Pawn pawn)
        {
            if (pawn != null)
            {
                restrainedPrisoners?.Remove(pawn);
            }
        }

        private void AddBattlefieldScenery()
        {
            int ballistas = map.listerThings.ThingsOfDef(MUGBDefOf.MUGB_GoblinRepeaterBallista).Count;
            int corpseCount = ballistas >= 4 ? Rand.RangeInclusive(6, 8) : Rand.RangeInclusive(2, 4);
            int gibbetCount = ballistas >= 4 ? Rand.RangeInclusive(2, 3) : 2;
            for (int i = 0; i < corpseCount; i++)
            {
                PawnGenerationRequest request = new PawnGenerationRequest(PawnKindDefOf.Villager, forceGenerateNewPawn: true, allowDead: true, forceDead: true);
                Pawn dead = PawnGenerator.GeneratePawn(request);
                if (dead.Corpse != null)
                {
                    GenSpawn.Spawn(dead.Corpse, FindClearCellNear(baseCenter, 28), map);
                }
            }

            ThingDef gibbetDef = DefDatabase<ThingDef>.GetNamedSilentFail("GibbetCage");
            if (gibbetDef == null)
            {
                return;
            }
            for (int i = 0; i < gibbetCount; i++)
            {
                IntVec3 cell = FindClearCellNear(baseCenter, 30);
                Thing gibbet = ThingMaker.MakeThing(gibbetDef, GenStuff.DefaultStuffFor(gibbetDef));
                GenSpawn.Spawn(gibbet, cell, map);
                if (gibbet is Building_CorpseCasket casket)
                {
                    PawnGenerationRequest request = new PawnGenerationRequest(PawnKindDefOf.Villager, forceGenerateNewPawn: true, allowDead: true, forceDead: true);
                    Pawn dead = PawnGenerator.GeneratePawn(request);
                    if (dead.Corpse != null)
                    {
                        casket.TryAcceptThing(dead.Corpse);
                    }
                }
            }
        }

        private void PlaceReward()
        {
            if (rewardPlaced)
            {
                return;
            }
            ThingDef chestDef = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_RoyalChest");
            List<ThingDef> weaponDefs = MUGBSpecialWeaponUtility.EligibleDefNames
                .Select(DefDatabase<ThingDef>.GetNamedSilentFail)
                .Where(def => def != null)
                .ToList();
            if (chestDef == null || weaponDefs.Count == 0)
            {
                return;
            }

            IntVec3 cell = FindIndoorCell();
            Thing chest = ThingMaker.MakeThing(chestDef, GenStuff.DefaultStuffFor(chestDef));
            GenSpawn.Spawn(chest, cell, map);
            ThingDef weaponDef = weaponDefs.RandomElement();
            Thing weapon = ThingMaker.MakeThing(weaponDef, GenStuff.DefaultStuffFor(weaponDef));
            QualityCategory quality = Rand.Value < 0.70f ? QualityCategory.Good : Rand.Value < 0.833333f ? QualityCategory.Excellent : QualityCategory.Masterwork;
            weapon.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);
            MUGBSpecialWeaponUtility.Activate(weapon, 1, 2);
            GenSpawn.Spawn(weapon, cell, map);
            weapon.SetForbidden(false);
            rewardPlaced = true;
        }

        private IntVec3 FindIndoorCell()
        {
            return GenRadial.RadialCellsAround(baseCenter, 30f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map) && cell.GetEdifice(map) == null && map.roofGrid.Roofed(cell))
                .InRandomOrder()
                .DefaultIfEmpty(baseCenter)
                .First();
        }

        private IntVec3 FindClearCellNear(IntVec3 center, int radius)
        {
            return GenRadial.RadialCellsAround(center, radius, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map) && cell.GetEdifice(map) == null
                    && cell.GetFirstPawn(map) == null
                    && !cell.GetThingList(map).Any(thing => thing.def.IsDoor || thing is Building_TurretGun))
                .InRandomOrder()
                .DefaultIfEmpty(center)
                .First();
        }

        private static bool IsGoblinFaction(Faction other)
        {
            return other?.def?.defName?.StartsWith("MUGB_Goblin", StringComparison.Ordinal) == true;
        }

        private static void PlayDigging(Thing tunnel, bool large)
        {
            if (tunnel?.Spawned == true && ThingMaker.MakeThing(MUGBDefOf.MUGB_GoblinTunnelDiggingFX) is MUGB_TunnelDiggingFX fx)
            {
                GenSpawn.Spawn(fx, tunnel.Position, tunnel.Map);
                fx.Initialize(large);
            }
        }

        public bool IsDefenseTunnel(Building_GoblinTunnel tunnel)
        {
            return initialized && tunnel == defenseTunnel;
        }

        public string TunnelInspectString()
        {
            if (!tunnelActivated)
            {
                return "MUGB_BaseTunnelDormant".Translate();
            }
            int remaining = Mathf.Max(0, tunnelWaveBudgets.Count - nextTunnelWaveIndex);
            if (remaining == 0)
            {
                return "MUGB_BaseTunnelNoWaves".Translate();
            }
            return "MUGB_BaseTunnelStatus".Translate(
                Mathf.Max(0, nextTunnelWaveTick - Find.TickManager.TicksGame).ToStringTicksToPeriod(),
                remaining);
        }

        public bool HasPendingThreats(Faction checkedFaction)
        {
            if (!initialized || checkedFaction != faction)
            {
                return false;
            }
            bool tunnelPending = defenseTunnel?.Spawned == true
                && (!tunnelActivated || nextTunnelWaveIndex < tunnelWaveBudgets.Count);
            return tunnelPending || (externalScheduled && !externalArrived);
        }

        public void NotifyQuietRetreatPossible()
        {
            if (lullWarningSent || !HasPendingThreats(faction))
            {
                return;
            }
            bool active = map.mapPawns.SpawnedPawnsInFaction(faction).Any(pawn => GenHostility.IsActiveThreatToPlayer(pawn));
            if (active)
            {
                return;
            }
            Find.LetterStack.ReceiveLetter(
                "MUGB_BaseLullLetterLabel".Translate(),
                "MUGB_BaseLullLetterText".Translate(),
                LetterDefOf.NeutralEvent,
                defenseTunnel?.Spawned == true ? new LookTargets(defenseTunnel) : new LookTargets(baseCenter, map));
            lullWarningSent = true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref initialized, "initialized");
            Scribe_Values.Look(ref tunnelActivated, "tunnelActivated");
            Scribe_Values.Look(ref externalScheduled, "externalScheduled");
            Scribe_Values.Look(ref externalArrived, "externalArrived");
            Scribe_Values.Look(ref lullWarningSent, "lullWarningSent");
            Scribe_Values.Look(ref rewardPlaced, "rewardPlaced");
            Scribe_Values.Look(ref nextTunnelWaveTick, "nextTunnelWaveTick", -1);
            Scribe_Values.Look(ref tunnelWarningTick, "tunnelWarningTick", -1);
            Scribe_Values.Look(ref externalArrivalTick, "externalArrivalTick", -1);
            Scribe_Values.Look(ref nextTunnelWaveIndex, "nextTunnelWaveIndex");
            Scribe_Values.Look(ref baseCenter, "baseCenter");
            Scribe_References.Look(ref faction, "faction");
            Scribe_References.Look(ref defenseTunnel, "defenseTunnel");
            Scribe_References.Look(ref turretLord, "turretLord");
            Scribe_Collections.Look(ref initialDefenders, "initialDefenders", LookMode.Reference);
            Scribe_Collections.Look(ref restrainedPrisoners, "restrainedPrisoners", LookMode.Reference);
            Scribe_Collections.Look(ref tunnelWaveBudgets, "tunnelWaveBudgets", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                initialDefenders = initialDefenders ?? new List<Pawn>();
                restrainedPrisoners = restrainedPrisoners ?? new List<Pawn>();
                tunnelWaveBudgets = tunnelWaveBudgets ?? new List<float>();
            }
        }
    }

    public static class MUGB_MedievalBaseDebugActions
    {
        [DebugAction("MUGB", "Test medieval goblin base", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TestMedievalGoblinBase()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            AddOption(options, "Civil base A", MUGBDefOf.MUGB_GoblinCivilMedieval, "MUGB_MedievalBaseA");
            AddOption(options, "Civil base B", MUGBDefOf.MUGB_GoblinCivilMedieval, "MUGB_MedievalBaseB");
            AddOption(options, "Savage base A", MUGBDefOf.MUGB_GoblinSavageMedieval, "MUGB_MedievalBaseA");
            AddOption(options, "Savage base B", MUGBDefOf.MUGB_GoblinSavageMedieval, "MUGB_MedievalBaseB");
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void AddOption(List<FloatMenuOption> options, string label, FactionDef factionDef, string layoutDefName)
        {
            options.Add(new FloatMenuOption(label, delegate
            {
                Map map = Find.CurrentMap;
                Faction faction = Find.FactionManager.FirstFactionOfDef(factionDef);
                Type optionType = AccessTools.TypeByName("KCSG.CustomGenOption");
                Type layoutType = AccessTools.TypeByName("KCSG.StructureLayoutDef");
                if (map == null || faction == null || optionType == null || layoutType == null)
                {
                    Messages.Message("KCSG or the selected goblin faction is unavailable.", MessageTypeDefOf.RejectInput, false);
                    return;
                }

                object option = Activator.CreateInstance(optionType);
                IList layouts = AccessTools.Field(optionType, "chooseFromlayouts")?.GetValue(option) as IList;
                Def layout = GenDefDatabase.GetDef(layoutType, layoutDefName, false);
                if (layouts == null || layout == null)
                {
                    Messages.Message("The selected medieval base layout is unavailable.", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                layouts.Add(layout);
                AccessTools.Field(optionType, "tryFindFreeArea")?.SetValue(option, true);
                AccessTools.Field(optionType, "fullClear")?.SetValue(option, true);
                MethodInfo generate = AccessTools.Method(optionType, "Generate", new[] { typeof(IntVec3), typeof(Map) });
                try
                {
                    MUGB_KCSGMedievalSettlementPointsPatch.DebugFactionOverride = faction;
                    generate.Invoke(option, new object[] { map.Center, map });
                    map.GetComponent<MUGB_MedievalBaseManager>().InitializeDebug(faction);
                }
                finally
                {
                    MUGB_KCSGMedievalSettlementPointsPatch.DebugFactionOverride = null;
                }
            }));
        }
    }
}
