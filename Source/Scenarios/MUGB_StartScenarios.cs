using System;
using System.Collections.Generic;
using System.Linq;
using KCSG;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using KCSGLayoutDef = KCSG.StructureLayoutDef;

namespace MUGB
{
    public enum MUGBStartScenarioKind
    {
        Exile,
        StrandedSquad,
        GoldenCaravan
    }

    public sealed class ScenPart_MUGB_PlayerPawnsArriveAtEdge : ScenPart
    {
        private const int WalkInDistance = 10;

        private Map arrivalMap;
        private IntVec3 walkInCenter = IntVec3.Invalid;
        private readonly List<Pawn> arrivingPawns = new List<Pawn>();
        private readonly List<Thing> pendingStartingThings = new List<Thing>();
        private int expectedStartingThingGroups;

        public override void GenerateIntoMap(Map map)
        {
            GameInitData initData = Find.GameInitData;
            if (initData?.startingAndOptionalPawns.NullOrEmpty() != false)
            {
                Log.Error("[MUGB] The goblin starting party could not be placed because no starting pawns were configured.");
                return;
            }

            List<Thing> party = initData.startingAndOptionalPawns.Cast<Thing>().ToList();
            List<Thing> startingThings = Find.Scenario.AllParts
                .SelectMany(part => part.PlayerStartingThings())
                .ToList();
            for (int i = 0; i < startingThings.Count; i++)
            {
                Thing thing = startingThings[i];
                if (thing.def.CanHaveFaction)
                {
                    thing.SetFactionDirect(Faction.OfPlayer);
                }
                party.Add(thing);
            }

            int pawnCount = party.Count(thing => thing is Pawn);
            IntVec3 arrivalCell;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out arrivalCell, map, 1f, false)
                || NearbyStandableCells(arrivalCell, map, pawnCount).Count < pawnCount)
            {
                arrivalCell = FindClosestStandableCellToEdge(map, pawnCount);
            }
            if (!arrivalCell.IsValid)
            {
                Log.Error("[MUGB] The goblin starting party could not find any standable edge approach on the starting map.");
                return;
            }

            List<IntVec3> spawnCells = NearbyStandableCells(arrivalCell, map, pawnCount);
            if (spawnCells.Count < pawnCount)
            {
                Log.Error("[MUGB] The goblin starting party could not find enough distinct cells near one map edge.");
                return;
            }

            arrivalMap = map;
            walkInCenter = FindWalkInCenter(arrivalCell, map);
            arrivingPawns.Clear();
            pendingStartingThings.Clear();
            expectedStartingThingGroups = startingThings.Count;
            int spawnCellIndex = 0;
            foreach (Thing thing in party)
            {
                IntVec3 spawnCell = spawnCells[Math.Min(spawnCellIndex, spawnCells.Count - 1)];
                if (thing is Pawn pawn)
                {
                    GenSpawn.Spawn(pawn, spawnCell, map, Rot4.Random);
                    arrivingPawns.Add(pawn);
                    spawnCellIndex++;
                }
                else if (!GenPlace.TryPlaceThing(thing, arrivalCell, map, ThingPlaceMode.Near))
                {
                    pendingStartingThings.Add(thing);
                }
            }
        }

        public override void PostGameStart()
        {
            if (arrivalMap == null || !walkInCenter.IsValid)
            {
                return;
            }

            for (int i = pendingStartingThings.Count - 1; i >= 0; i--)
            {
                Thing thing = pendingStartingThings[i];
                if (thing.Spawned || GenPlace.TryPlaceThing(thing, walkInCenter, arrivalMap, ThingPlaceMode.Near))
                {
                    pendingStartingThings.RemoveAt(i);
                }
            }

            foreach (Thing thing in pendingStartingThings)
            {
                Log.Error($"[MUGB] Failed to place starting item {thing.def?.defName ?? "<unknown>"} after retrying on the completed map.");
            }

            for (int i = 0; i < arrivingPawns.Count; i++)
            {
                Pawn pawn = arrivingPawns[i];
                if (!pawn.Spawned || pawn.Map != arrivalMap)
                {
                    continue;
                }

                IntVec3 destination = FindReachableWalkInCell(pawn, walkInCenter, arrivalMap, i);
                if (!destination.IsValid || destination == pawn.Position)
                {
                    continue;
                }

                Job job = JobMaker.MakeJob(JobDefOf.Goto, destination);
                job.locomotionUrgency = LocomotionUrgency.Walk;
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            }

            int placedStartingThingGroups = expectedStartingThingGroups - pendingStartingThings.Count;
            Log.Message($"[MUGB] Starting party placed with {arrivingPawns.Count} pawns and {placedStartingThingGroups}/{expectedStartingThingGroups} starting item groups.");
            arrivingPawns.Clear();
            pendingStartingThings.Clear();
            arrivalMap = null;
            walkInCenter = IntVec3.Invalid;
        }

        private static List<IntVec3> NearbyStandableCells(IntVec3 center, Map map, int required)
        {
            List<IntVec3> cells = GenRadial.RadialCellsAround(center, 5f, true)
                .Where(cell => cell.InBounds(map) && cell.Standable(map))
                .OrderBy(cell => cell.DistanceToSquared(center))
                .ToList();
            if (cells.Count == 0)
            {
                cells.Add(center);
            }
            return cells.Take(Math.Max(required, 1)).ToList();
        }

        private static IntVec3 FindWalkInCenter(IntVec3 edgeCell, Map map)
        {
            Vector3 direction = (map.Center.ToVector3Shifted() - edgeCell.ToVector3Shifted()).normalized;
            IntVec3 desired = edgeCell + new IntVec3(
                Mathf.RoundToInt(direction.x * WalkInDistance),
                0,
                Mathf.RoundToInt(direction.z * WalkInDistance));
            return desired.ClampInsideMap(map);
        }

        private static IntVec3 FindReachableWalkInCell(Pawn pawn, IntVec3 center, Map map, int offset)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, 5f, true).Skip(offset % 8))
            {
                if (cell.InBounds(map)
                    && cell.Standable(map)
                    && map.reachability.CanReach(pawn.Position, cell, PathEndMode.OnCell, TraverseParms.For(pawn)))
                {
                    return cell;
                }
            }
            return IntVec3.Invalid;
        }

        private static IntVec3 FindClosestStandableCellToEdge(Map map, int pawnCount)
        {
            IntVec3 best = IntVec3.Invalid;
            int bestEdgeDistance = int.MaxValue;
            float bestCenterDistance = float.MaxValue;
            for (int z = 0; z < map.Size.z; z++)
            {
                for (int x = 0; x < map.Size.x; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.Standable(map))
                    {
                        continue;
                    }

                    int nearbyStandable = 0;
                    foreach (IntVec3 nearby in CellRect.CenteredOn(cell, 9, 9).ClipInsideMap(map))
                    {
                        if (nearby.Standable(map))
                        {
                            nearbyStandable++;
                        }
                    }
                    if (nearbyStandable < pawnCount + 8)
                    {
                        continue;
                    }

                    int edgeDistance = Math.Min(Math.Min(x, map.Size.x - 1 - x), Math.Min(z, map.Size.z - 1 - z));
                    float centerDistance = cell.DistanceToSquared(map.Center);
                    if (edgeDistance < bestEdgeDistance || (edgeDistance == bestEdgeDistance && centerDistance < bestCenterDistance))
                    {
                        best = cell;
                        bestEdgeDistance = edgeDistance;
                        bestCenterDistance = centerDistance;
                    }
                }
            }
            return best;
        }
    }

    public sealed class ScenPart_MUGB_StartScenarioController : ScenPart
    {
        // 미디블 오버홀은 권장 선행이지 필수가 아닙니다. 마을 레이아웃처럼 그 모드의
        // 물건에 기대는 부분은 이 값으로 켜져 있는지 확인하고 갈라집니다.
        private const string MedievalOverhaulPackageId = "DankPyon.Medieval.Overhaul";

        public MUGBStartScenarioKind scenarioKind;

        private bool initialMapHandled;
        private bool startingLoadoutApplied;
        private bool startingResearchApplied;
        private bool startDialogShown;
        private bool scheduledThreatExecuted;
        private bool villageNeutralized;
        private bool villageLetterSent;
        private bool returnWarningSent;
        private bool returnRaidExecuted;
        private bool villageInteriorFogApplied;
        private int villageFogVersion;
        private int scheduledThreatTick = -1;
        private int forcedReturnTick = -1;
        private int returnRaidTick = -1;
        private int nextVillageCheckTick = -1;
        private Map villageMap;
        private IntVec3 villageCenter = IntVec3.Invalid;
        private int villageRectMinX = -1;
        private int villageRectMinZ = -1;
        private int villageRectMaxX = -1;
        private int villageRectMaxZ = -1;
        private List<Pawn> villageResidents = new List<Pawn>();

        private const int VillageCheckInterval = 250;
        private const int CurrentVillageFogVersion = 2;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref scenarioKind, "scenarioKind");
            Scribe_Values.Look(ref initialMapHandled, "initialMapHandled");
            Scribe_Values.Look(ref startingLoadoutApplied, "startingLoadoutApplied");
            Scribe_Values.Look(ref startingResearchApplied, "startingResearchApplied");
            Scribe_Values.Look(ref startDialogShown, "startDialogShown");
            Scribe_Values.Look(ref scheduledThreatExecuted, "scheduledThreatExecuted");
            Scribe_Values.Look(ref villageNeutralized, "villageNeutralized");
            Scribe_Values.Look(ref villageLetterSent, "villageLetterSent");
            Scribe_Values.Look(ref returnWarningSent, "returnWarningSent");
            Scribe_Values.Look(ref returnRaidExecuted, "returnRaidExecuted");
            Scribe_Values.Look(ref villageInteriorFogApplied, "villageInteriorFogApplied");
            Scribe_Values.Look(ref villageFogVersion, "villageFogVersion");
            Scribe_Values.Look(ref scheduledThreatTick, "scheduledThreatTick", -1);
            Scribe_Values.Look(ref forcedReturnTick, "forcedReturnTick", -1);
            Scribe_Values.Look(ref returnRaidTick, "returnRaidTick", -1);
            Scribe_Values.Look(ref nextVillageCheckTick, "nextVillageCheckTick", -1);
            Scribe_References.Look(ref villageMap, "villageMap");
            Scribe_Values.Look(ref villageCenter, "villageCenter", IntVec3.Invalid);
            Scribe_Values.Look(ref villageRectMinX, "villageRectMinX", -1);
            Scribe_Values.Look(ref villageRectMinZ, "villageRectMinZ", -1);
            Scribe_Values.Look(ref villageRectMaxX, "villageRectMaxX", -1);
            Scribe_Values.Look(ref villageRectMaxZ, "villageRectMaxZ", -1);
            Scribe_Collections.Look(ref villageResidents, "villageResidents", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                villageResidents ??= new List<Pawn>();
            }
        }

        public override string Summary(Scenario scen)
        {
            return "MUGB_StartScenarioSummary".Translate();
        }

        public override void Notify_PawnGenerated(Pawn pawn, PawnGenerationContext context, bool redressed)
        {
            if (scenarioKind == MUGBStartScenarioKind.StrandedSquad
                && context == PawnGenerationContext.PlayerStarter
                && GoblinUtility.IsGoblin(pawn))
            {
                EquipStrandedCandidate(pawn);
            }
        }

        public override void PostMapGenerate(Map map)
        {
            if (initialMapHandled)
            {
                return;
            }

            if (Find.Maps.Any(existing => existing != map && existing.IsPlayerHome))
            {
                initialMapHandled = true;
                Log.Warning("[MUGB] Starting village generation was skipped because this is not the first player home map.");
                return;
            }

            initialMapHandled = true;
            if (scenarioKind == MUGBStartScenarioKind.StrandedSquad)
            {
                TryGenerateStartingVillage(map);
            }
        }

        public override void PostGameStart()
        {
            int now = Find.TickManager.TicksGame;
            TryApplyStartingVillageInteriorFog();
            if (!startingLoadoutApplied)
            {
                ApplyStartingLoadout();
                startingLoadoutApplied = true;
            }
            if (!startingResearchApplied)
            {
                ApplyStartingResearch();
                startingResearchApplied = true;
            }

            if (scheduledThreatTick < 0)
            {
                scheduledThreatTick = scenarioKind switch
                {
                    MUGBStartScenarioKind.Exile => now + 3 * GenDate.TicksPerDay,
                    MUGBStartScenarioKind.GoldenCaravan => now + Rand.RangeInclusive(4, 6) * GenDate.TicksPerDay,
                    _ => -1
                };
            }

            if (scenarioKind == MUGBStartScenarioKind.StrandedSquad && forcedReturnTick < 0)
            {
                forcedReturnTick = now + Rand.RangeInclusive(10, 15) * GenDate.TicksPerDay;
                nextVillageCheckTick = now + VillageCheckInterval;
            }

            if (!startDialogShown)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(StartTextForScenario(), "Close".Translate()));
                startDialogShown = true;
            }
        }

        public override void Tick()
        {
            int now = Find.TickManager.TicksGame;
            if (scenarioKind == MUGBStartScenarioKind.StrandedSquad
                && villageFogVersion < CurrentVillageFogVersion)
            {
                TryApplyStartingVillageInteriorFog();
            }
            if (!scheduledThreatExecuted && scheduledThreatTick >= 0 && now >= scheduledThreatTick)
            {
                scheduledThreatExecuted = true;
                if (scenarioKind == MUGBStartScenarioKind.Exile)
                {
                    SpawnExileTrackers();
                }
                else if (scenarioKind == MUGBStartScenarioKind.GoldenCaravan)
                {
                    ExecuteGoldenRaid();
                }
            }

            if (scenarioKind != MUGBStartScenarioKind.StrandedSquad || returnRaidExecuted)
            {
                return;
            }

            if (!returnWarningSent && forcedReturnTick > 0 && now >= forcedReturnTick - GenDate.TicksPerDay)
            {
                SendReturnWarning();
                returnWarningSent = true;
            }

            if (!villageNeutralized && nextVillageCheckTick > 0 && now >= nextVillageCheckTick)
            {
                nextVillageCheckTick = now + VillageCheckInterval;
                if (!VillageHasActiveDefender())
                {
                    villageNeutralized = true;
                    returnRaidTick = now + 2 * GenDate.TicksPerDay;
                    SendVillageNeutralizedLetter();
                }
            }

            int dueTick = villageNeutralized ? returnRaidTick : forcedReturnTick;
            if (dueTick > 0 && now >= dueTick)
            {
                returnRaidExecuted = true;
                SpawnVillageReturners();
            }
        }

        private TaggedString StartTextForScenario()
        {
            return scenarioKind switch
            {
                MUGBStartScenarioKind.Exile => "MUGB_ExileStartText".Translate(),
                MUGBStartScenarioKind.StrandedSquad => "MUGB_StrandedStartText".Translate(),
                _ => "MUGB_GoldenStartText".Translate()
            };
        }

        private void ApplyStartingResearch()
        {
            if (scenarioKind != MUGBStartScenarioKind.StrandedSquad
                || !ModsConfig.IsActive(MedievalOverhaulPackageId))
            {
                return;
            }

            FinishStartingResearch("DankPyon_Lumber");
            FinishStartingResearch("Stonecutting");
        }

        private static void FinishStartingResearch(string defName)
        {
            ResearchProjectDef project = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);
            if (project != null && !project.IsFinished)
            {
                Find.ResearchManager.FinishProject(project, false, null, false);
            }
        }

        private void ApplyStartingLoadout()
        {
            List<Pawn> pawns = Find.Maps
                .SelectMany(map => map.mapPawns.FreeColonistsSpawned)
                .Where(GoblinUtility.IsGoblin)
                .OrderByDescending(GoblinUtility.IsHobgoblin)
                .ToList();
            if (pawns.Count == 0)
            {
                Log.Warning("[MUGB] No spawned starting goblins were found for scenario loadout.");
                return;
            }

            switch (scenarioKind)
            {
                case MUGBStartScenarioKind.Exile:
                    EquipExile(pawns[0]);
                    break;
                case MUGBStartScenarioKind.StrandedSquad:
                    EquipStrandedSquad(pawns);
                    break;
                case MUGBStartScenarioKind.GoldenCaravan:
                    EquipGoldenCaravan(pawns);
                    break;
            }
        }

        private static void EquipExile(Pawn pawn)
        {
            ResetCombatLoadout(pawn);
            Wear(pawn, RandomDef("MUGB_Apparel_GoblinBodyWrap", "MUGB_Apparel_GoblinLoincloth"), QualityCategory.Poor, 0.75f);
            Wear(pawn, DefDatabase<ThingDef>.GetNamedSilentFail("MUGB_Apparel_HumanHideMantle"), QualityCategory.Poor, 0.70f);
            Equip(pawn, DefDatabase<ThingDef>.GetNamedSilentFail("MUGB_GoblinRepeatingCrossbow"), QualityCategory.Good);

            ThingDef melee = RandomDef("MUGB_GoblinDagger", "MUGB_GoblinCleaver", "MUGB_GoblinCurvedBlade", "MUGB_GoblinBoneAxe");
            SpawnNear(pawn, MakeThing(melee, QualityCategory.Poor));

            ThingDef specialDef = MUGBSpecialWeaponUtility.EligibleDefNames
                .Select(DefDatabase<ThingDef>.GetNamedSilentFail)
                .Where(def => def != null)
                .RandomElementWithFallback();
            Thing special = MakeThing(specialDef, QualityCategory.Normal);
            if (special != null)
            {
                MUGBSpecialWeaponUtility.Activate(special, 1, 2);
                SpawnNear(pawn, special);
            }
        }

        private static void EquipStrandedSquad(List<Pawn> pawns)
        {
            string[] reserveWeapons =
            {
                "MUGB_GoblinDagger", "MUGB_GoblinChainSnare", "MUGB_GoblinBlowdart"
            };

            foreach (Pawn pawn in pawns)
            {
                if (pawn.equipment?.Primary == null || pawn.apparel?.WornApparelCount < 2)
                {
                    EquipStrandedCandidate(pawn);
                }
            }

            Pawn anchor = pawns[0];
            foreach (string defName in reserveWeapons)
            {
                SpawnNear(anchor, MakeThing(DefDatabase<ThingDef>.GetNamedSilentFail(defName), QualityCategory.Normal));
            }
        }

        private static void EquipStrandedCandidate(Pawn pawn)
        {
            bool hob = GoblinUtility.IsHobgoblin(pawn);
            ResetCombatLoadout(pawn);
            WearGoblinUnderlayer(pawn, hob ? QualityCategory.Normal : QualityCategory.Poor);
            Wear(pawn,
                hob
                    ? RandomDef("MUGB_Apparel_GoblinScaleArmor", "MUGB_Apparel_GoblinChestplate", "MUGB_Apparel_GoblinTorsoArmorA")
                    : RandomDef("MUGB_Apparel_GoblinChainArmor", "MUGB_Apparel_WoodArmorA", "MUGB_Apparel_GoblinChestplate"),
                hob ? QualityCategory.Normal : QualityCategory.Poor,
                Rand.Range(0.55f, 0.90f));

            if (hob || Rand.Chance(0.75f))
            {
                Wear(pawn,
                    hob
                        ? RandomDef("MUGB_Apparel_KettleHelmetA", "MUGB_Apparel_WarHelmetA", "MUGB_Apparel_HammerHelmet")
                        : RandomDef("MUGB_Apparel_CrudeHelmetA", "MUGB_Apparel_BirdHelmetA", "MUGB_Apparel_KettleHelmetA"),
                    hob ? QualityCategory.Normal : QualityCategory.Poor,
                    Rand.Range(0.55f, 0.90f));
            }

            ThingDef weapon = RandomDef(
                "MUGB_GoblinMusket",
                "MUGB_GoblinCrossbow",
                "MUGB_GoblinSpear",
                "MUGB_GoblinCurvedBlade",
                "MUGB_GoblinMachete");
            Equip(pawn, weapon, hob ? QualityCategory.Good : QualityCategory.Normal);
        }

        private static void EquipGoldenCaravan(List<Pawn> pawns)
        {
            string[] weapons =
            {
                "MUGB_GoblinMusket", "MUGB_GoblinCurvedBlade", "MUGB_GoblinCurvedBlade",
                "MUGB_GoblinSpear", "MUGB_GoblinMachete", "MUGB_GoblinDagger"
            };
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                bool hob = GoblinUtility.IsHobgoblin(pawn);
                ResetCombatLoadout(pawn);
                WearGoblinUnderlayer(pawn, hob ? QualityCategory.Normal : QualityCategory.Poor);
                if (hob)
                {
                    Wear(pawn, RandomDef("MUGB_Apparel_GoblinScaleArmor", "MUGB_Apparel_GoblinChestplate", "MUGB_Apparel_GoblinTorsoArmorA"), QualityCategory.Normal, Rand.Range(0.75f, 1f));
                    Wear(pawn, RandomDef("MUGB_Apparel_KettleHelmetA", "MUGB_Apparel_WarHelmetA", "MUGB_Apparel_HammerHelmet"), QualityCategory.Normal, Rand.Range(0.75f, 1f));
                }
                Equip(pawn, DefDatabase<ThingDef>.GetNamedSilentFail(weapons[Math.Min(i, weapons.Length - 1)]), hob ? QualityCategory.Good : QualityCategory.Normal);
            }
        }

        private void TryGenerateStartingVillage(Map map)
        {
            // 한국어 의도: 원래 마을 레이아웃은 미디블 오버홀 물건으로 채워져 있습니다.
            // 주의할 점은, 그 모드가 없어도 레이아웃 Def 자체는 그대로 남는다는 것입니다.
            // KCSG의 modRequirements는 Def를 지우는 게 아니라 "쓸 수 있음" 표시만 끕니다
            // (StructureLayoutDef.RequiredModLoaded). 그 표시는 internal이라 여기서 읽을 수 없어,
            // 미디블이 켜져 있는지를 직접 확인합니다.
            // 예전에는 Def 존재 여부만 보고 판단해서, 미디블이 없어도 미디블 마을을 골랐고
            // 건물이 전부 빠진 빈 터가 생겼습니다.
            List<string> layoutDefNames = new List<string>
            {
                "MUGB_StartVillageSmall1", "MUGB_StartVillageSmall2"
            };
            if (ModsConfig.IsActive(MedievalOverhaulPackageId))
            {
                layoutDefNames.AddRange(new[] { "MUGB_StartVillage1", "MUGB_StartVillage2", "MUGB_StartVillage3" });
            }
            List<KCSGLayoutDef> layouts = layoutDefNames
                .Select(DefDatabase<KCSGLayoutDef>.GetNamedSilentFail)
                .Where(layout => layout != null)
                .ToList();
            KCSGLayoutDef fallback = DefDatabase<KCSGLayoutDef>.GetNamedSilentFail("MUGB_StartVillageVanilla1");
            KCSGLayoutDef compactFallback = DefDatabase<KCSGLayoutDef>.GetNamedSilentFail("MUGB_StartVillageVanillaCompact");
            if (layouts.Count == 0 && fallback == null && compactFallback == null)
            {
                Log.Error("[MUGB] Starting village could not be generated because no village layout is available.");
                return;
            }

            // Try the authored villages in random order, then the compact core-only village.
            // This keeps unusual landforms intact while avoiding a missing village when one
            // large layout happens not to fit on the selected starting map.
            layouts = layouts.InRandomOrder().ToList();
            if (fallback != null && !layouts.Contains(fallback))
            {
                layouts.Add(fallback);
            }
            if (compactFallback != null && !layouts.Contains(compactFallback))
            {
                layouts.Add(compactFallback);
            }

            Faction villageFaction = GetOrCreateVillageFaction();
            if (villageFaction == null)
            {
                Log.Error("[MUGB] Starting village could not create its scenario-only faction.");
                return;
            }

            EnsureHostile(villageFaction);
            KCSGLayoutDef layout = null;
            CellRect rect = default;
            bool fullClean = false;
            foreach (KCSGLayoutDef candidate in layouts)
            {
                if (TryFindVillageRect(map, candidate, out rect, out fullClean))
                {
                    layout = candidate;
                    break;
                }
            }

            if (layout == null)
            {
                Log.Warning("[MUGB] No safe starting-village rectangle was found. The scenario will continue without a village.");
                return;
            }

            try
            {
                LayoutUtils.CleanRect(layout, map, rect, fullClean);
                GenOption.GetAllMineableIn(rect, map);
                List<Thing> spawned = new List<Thing>();
                layout.Generate(rect, map, spawned, villageFaction);
                villageCenter = rect.CenterCell;
                villageMap = map;
                villageRectMinX = rect.minX;
                villageRectMinZ = rect.minZ;
                villageRectMaxX = rect.maxX;
                villageRectMaxZ = rect.maxZ;
                SpawnVillageFoodAndField(map, rect);
                SpawnVillageResidents(map, villageFaction, rect);
                Log.Message($"[MUGB] Starting village generated with layout {layout.defName} at {rect.CenterCell}.");
            }
            catch (Exception ex)
            {
                Log.Error("[MUGB] Starting village generation failed. The scenario will continue without retrying on later maps.\n" + ex);
            }
        }

        private void TryApplyStartingVillageInteriorFog()
        {
            if (villageFogVersion >= CurrentVillageFogVersion
                || scenarioKind != MUGBStartScenarioKind.StrandedSquad)
            {
                return;
            }
            if (villageMap == null
                || villageRectMinX < 0 || villageRectMinZ < 0 || villageRectMaxX < villageRectMinX || villageRectMaxZ < villageRectMinZ)
            {
                villageInteriorFogApplied = true;
                villageFogVersion = CurrentVillageFogVersion;
                return;
            }

            CellRect villageRect = CellRect.FromLimits(villageRectMinX, villageRectMinZ, villageRectMaxX, villageRectMaxZ)
                .ClipInsideMap(villageMap);
            CellRect outerRing = CellRect.FromLimits(
                    villageRect.minX - 1,
                    villageRect.minZ - 1,
                    villageRect.maxX + 1,
                    villageRect.maxZ + 1)
                .ClipInsideMap(villageMap);
            List<IntVec3> revealedOuterCells = outerRing.Cells
                .Where(cell => !villageRect.Contains(cell)
                    && !cell.Fogged(villageMap)
                    && (cell.GetEdifice(villageMap) == null || !cell.GetEdifice(villageMap).def.MakeFog))
                .ToList();

            villageMap.fogGrid.Refog(villageRect);
            foreach (IntVec3 cell in revealedOuterCells)
            {
                bool bordersFog = false;
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 adjacent = cell + GenAdj.CardinalDirections[i];
                    if (adjacent.InBounds(villageMap) && adjacent.Fogged(villageMap))
                    {
                        bordersFog = true;
                        break;
                    }
                }
                if (bordersFog)
                {
                    villageMap.fogGrid.FloodUnfogAdjacent(cell, sendLetters: false);
                }
            }

            foreach (Pawn pawn in villageMap.mapPawns.FreeColonistsSpawned)
            {
                if (villageRect.Contains(pawn.Position) && pawn.Position.Fogged(villageMap))
                {
                    villageMap.fogGrid.FloodUnfogAdjacent(pawn.Position, sendLetters: false);
                }
            }

            villageInteriorFogApplied = true;
            villageFogVersion = CurrentVillageFogVersion;
        }

        private static bool TryFindVillageRect(Map map, KCSGLayoutDef layout, out CellRect result, out bool fullClean)
        {
            result = default;
            fullClean = false;
            int width = layout.Sizes.x;
            int height = layout.Sizes.z;
            List<IntVec3> playerCells = map.mapPawns.FreeColonistsSpawned.Select(pawn => pawn.Position).ToList();

            int mapWidth = map.Size.x;
            int mapHeight = map.Size.z;
            int[,] waterPrefix = new int[mapWidth + 1, mapHeight + 1];
            int[,] impassablePrefix = new int[mapWidth + 1, mapHeight + 1];
            for (int z = 0; z < mapHeight; z++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    TerrainDef terrain = cell.GetTerrain(map);
                    int water = terrain.IsWater || terrain.IsRiver ? 1 : 0;
                    int impassable = cell.Impassable(map) ? 1 : 0;
                    waterPrefix[x + 1, z + 1] = water + waterPrefix[x, z + 1] + waterPrefix[x + 1, z] - waterPrefix[x, z];
                    impassablePrefix[x + 1, z + 1] = impassable + impassablePrefix[x, z + 1] + impassablePrefix[x + 1, z] - impassablePrefix[x, z];
                }
            }

            // First preserve passable terrain, then permit KCSG to clear rock and other
            // impassable obstacles. Water and rivers are never overwritten.
            for (int cleanPass = 0; cleanPass < 2; cleanPass++)
            {
                bool allowFullClean = cleanPass > 0;
                if (TryFindVillageRectPass(map, width, height, playerCells, allowFullClean,
                    waterPrefix, impassablePrefix, out result))
                {
                    fullClean = allowFullClean;
                    return true;
                }
            }
            return false;
        }

        private static bool TryFindVillageRectPass(
            Map map,
            int width,
            int height,
            List<IntVec3> playerCells,
            bool allowFullClean,
            int[,] waterPrefix,
            int[,] impassablePrefix,
            out CellRect result)
        {
            result = default;
            int edgeMargin = Math.Max(width, height) / 2 + 6;
            float bestDistance = float.MaxValue;
            int minX = edgeMargin;
            int maxX = map.Size.x - edgeMargin - 1;
            int minZ = edgeMargin;
            int maxZ = map.Size.z - edgeMargin - 1;
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    IntVec3 center = new IntVec3(x, 0, z);
                    float distance = center.DistanceToSquared(map.Center);
                    if (distance >= bestDistance || (playerCells.Count > 0 && playerCells.Any(p => p.DistanceTo(center) < 35f)))
                    {
                        continue;
                    }

                    CellRect rect = CellRect.CenteredOn(center, width, height);
                    if (rect.Width != width || rect.Height != height || !rect.FullyContainedWithin(new CellRect(0, 0, map.Size.x, map.Size.z)))
                    {
                        continue;
                    }

                    if (PrefixRectSum(waterPrefix, rect) != 0 || (!allowFullClean && PrefixRectSum(impassablePrefix, rect) != 0))
                    {
                        continue;
                    }

                    result = rect;
                    bestDistance = distance;
                }
            }

            return result.Area > 0;
        }

        private static int PrefixRectSum(int[,] prefix, CellRect rect)
        {
            int minX = rect.minX;
            int minZ = rect.minZ;
            int maxX = rect.maxX + 1;
            int maxZ = rect.maxZ + 1;
            return prefix[maxX, maxZ] - prefix[minX, maxZ] - prefix[maxX, minZ] + prefix[minX, minZ];
        }

        private static void SpawnVillageFoodAndField(Map map, CellRect rect)
        {
            Thing pemmican = ThingMaker.MakeThing(ThingDefOf.Pemmican);
            pemmican.stackCount = 200;
            GenPlace.TryPlaceThing(pemmican, rect.CenterCell, map, ThingPlaceMode.Near);

            ThingDef potato = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Potato");
            CellRect field = CellRect.CenteredOn(new IntVec3(rect.minX + 5, 0, rect.minZ + 5), 5, 6).ClipInsideMap(map);
            foreach (IntVec3 cell in field.Cells)
            {
                if (!cell.Standable(map) || cell.GetEdifice(map) != null)
                {
                    continue;
                }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                if (potato != null && cell.GetPlant(map) == null && Rand.Chance(0.8f))
                {
                    Plant plant = (Plant)ThingMaker.MakeThing(potato);
                    plant.Growth = Rand.Range(0.25f, 0.85f);
                    GenSpawn.Spawn(plant, cell, map);
                }
            }
        }

        private void SpawnVillageResidents(Map map, Faction faction, CellRect rect)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_StartVillageWoman");
            TraitDef feminine = DefDatabase<TraitDef>.GetNamedSilentFail("MGB_Feminine");
            if (kind == null)
            {
                Log.Error("[MUGB] Starting village resident PawnKind is missing.");
                return;
            }

            int count = Rand.RangeInclusive(4, 6);
            for (int i = 0; i < count; i++)
            {
                float age = i == 0 ? 35f : i == 1 ? 16f : Rand.RangeInclusive(16, 35);
                IEnumerable<TraitDef> forcedTraits = feminine != null && Rand.Chance(0.7f)
                    ? new[] { feminine }
                    : null;
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    map.Tile,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    fixedBiologicalAge: age,
                    fixedChronologicalAge: age,
                    fixedGender: Gender.Female,
                    forcedTraits: forcedTraits,
                    developmentalStages: DevelopmentalStage.Adult,
                    dontGiveWeapon: true);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (Rand.Chance(0.2f))
                {
                    Wear(pawn, DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Headgear_coif"), QualityCategory.Poor, Rand.Range(0.65f, 1f));
                }
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(rect.CenterCell, map, Math.Min(12, Math.Max(rect.Width, rect.Height) / 2));
                GenSpawn.Spawn(pawn, cell, map);
                villageResidents.Add(pawn);
            }

            if (villageResidents.Count >= 2)
            {
                villageResidents[1].relations.AddDirectRelation(PawnRelationDefOf.Parent, villageResidents[0]);
            }

            GiveVillageImprovisedWeapons();
            LordMaker.MakeNewLord(
                faction,
                new LordJob_DefendPoint(rect.CenterCell, wanderRadius: 12f, defendRadius: 18f),
                map,
                villageResidents);
        }

        private void GiveVillageImprovisedWeapons()
        {
            int armedCount = Math.Min(villageResidents.Count, Rand.Chance(0.35f) ? 2 : 1);
            foreach (Pawn pawn in villageResidents.InRandomOrder().Take(armedCount))
            {
                ThingDef weapon = Rand.Chance(0.6f)
                    ? DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_MeleeWeapon_Pitchfork")
                    : DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_MeleeWeapon_ButcherCleaver");
                Equip(pawn, weapon, Rand.Chance(0.25f) ? QualityCategory.Normal : QualityCategory.Poor);
            }
        }

        private bool VillageHasActiveDefender()
        {
            return villageResidents.Any(pawn => pawn != null
                && !pawn.Dead
                && pawn.Spawned
                && !pawn.Downed
                && !pawn.IsPrisonerOfColony
                && pawn.Faction != Faction.OfPlayer);
        }

        private void SendVillageNeutralizedLetter()
        {
            if (villageLetterSent)
            {
                return;
            }
            Find.LetterStack.ReceiveLetter(
                "MUGB_VillageNeutralizedLabel".Translate(),
                "MUGB_VillageNeutralizedText".Translate(),
                LetterDefOf.ThreatSmall,
                villageCenter.IsValid && villageMap != null ? new TargetInfo(villageCenter, villageMap) : TargetInfo.Invalid);
            villageLetterSent = true;
        }

        private void SendReturnWarning()
        {
            Find.LetterStack.ReceiveLetter(
                "MUGB_VillageReturnWarningLabel".Translate(),
                "MUGB_VillageReturnWarningText".Translate(),
                LetterDefOf.ThreatSmall,
                villageCenter.IsValid && villageMap != null ? new TargetInfo(villageCenter, villageMap) : TargetInfo.Invalid);
        }

        private void SpawnVillageReturners()
        {
            Map map = villageMap ?? Find.AnyPlayerHomeMap;
            Faction faction = GetOrCreateVillageFaction();
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_StartVillageReturner");
            if (map == null || faction == null || kind == null)
            {
                Log.Warning("[MUGB] Village return raid could not find its map, faction, or PawnKind.");
                return;
            }
            EnsureHostile(faction);
            int count = Rand.RangeInclusive(3, 4);
            List<Pawn> raiders = SpawnAssaultGroup(map, faction, Enumerable.Repeat(kind, count));
            List<Pawn> wives = villageResidents.Where(p => p != null).InRandomOrder().ToList();
            for (int i = 0; i < raiders.Count && i < wives.Count; i++)
            {
                Pawn husband = raiders[i];
                Pawn wife = wives[i];
                if (!husband.relations.DirectRelationExists(PawnRelationDefOf.Spouse, wife))
                {
                    husband.relations.AddDirectRelation(PawnRelationDefOf.Spouse, wife);
                }
            }
            Find.LetterStack.ReceiveLetter("MUGB_VillageReturnRaidLabel".Translate(), "MUGB_VillageReturnRaidText".Translate(), LetterDefOf.ThreatBig, raiders.FirstOrDefault());
        }

        private void SpawnExileTrackers()
        {
            Map map = Find.AnyPlayerHomeMap;
            Faction faction = Find.FactionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinTribe)
                ?? Find.FactionManager.AllFactionsVisible.FirstOrDefault(f => f.def == MUGBDefOf.MUGB_GoblinCivilTribe);
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_GoblinKind_PrimitiveMelee");
            if (map == null || faction == null || kind == null)
            {
                Log.Warning("[MUGB] Exile trackers could not be spawned.");
                return;
            }
            EnsureHostile(faction);
            List<Pawn> trackers = SpawnAssaultGroup(map, faction, Enumerable.Repeat(kind, 4));
            Find.LetterStack.ReceiveLetter("MUGB_ExileTrackersLabel".Translate(), "MUGB_ExileTrackersText".Translate(), LetterDefOf.ThreatSmall, trackers.FirstOrDefault());
        }

        private static void ExecuteGoldenRaid()
        {
            Map map = Find.AnyPlayerHomeMap;
            if (map == null)
            {
                return;
            }
            Faction faction = Find.FactionManager.AllFactionsVisible
                .Where(f => f != null
                    && f.HostileTo(Faction.OfPlayer)
                    && f.def.humanlikeFaction
                    && !f.def.hidden
                    && f.def.pawnGroupMakers?.Any(maker => maker.kindDef == PawnGroupKindDefOf.Combat) == true)
                .RandomElementWithFallback();
            if (faction == null)
            {
                Log.Warning("[MUGB] Golden caravan raid found no eligible humanlike hostile faction.");
                return;
            }

            HashSet<Pawn> before = map.mapPawns.AllPawnsSpawned.ToHashSet();
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = faction;
            parms.points = Mathf.Max(350f, parms.points * 0.8f);
            parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            if (!IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
            {
                Log.Warning("[MUGB] Golden caravan raid incident failed to execute.");
                return;
            }

            Pawn carrier = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => !before.Contains(p) && p.Faction == faction && p.equipment != null);
            if (carrier == null)
            {
                return;
            }
            ThingDef specialDef = MUGBSpecialWeaponUtility.EligibleDefNames.Select(DefDatabase<ThingDef>.GetNamedSilentFail).Where(def => def != null).RandomElementWithFallback();
            Thing special = MakeThing(specialDef, QualityCategory.Good);
            if (special is ThingWithComps equipment)
            {
                MUGBSpecialWeaponUtility.Activate(equipment, 1, 2);
                carrier.equipment.DestroyAllEquipment();
                carrier.equipment.AddEquipment(equipment);
            }
            Messages.Message("MUGB_GoldenRaidMessage".Translate(), carrier, MessageTypeDefOf.ThreatBig);
        }

        private static List<Pawn> SpawnAssaultGroup(Map map, Faction faction, IEnumerable<PawnKindDef> kinds)
        {
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, CellFinder.EdgeRoadChance_Hostile, false, null))
            {
                entry = CellFinder.RandomEdgeCell(map);
            }
            List<Pawn> pawns = new List<Pawn>();
            foreach (PawnKindDef kind in kinds)
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    map.Tile,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true,
                    developmentalStages: DevelopmentalStage.Adult);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(entry, map, 5), map, Rot4.Random);
                pawns.Add(pawn);
            }
            if (pawns.Count > 0)
            {
                LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: true, canTimeoutOrFlee: true), map, pawns);
            }
            return pawns;
        }

        private static void EnsureHostile(Faction faction)
        {
            Faction player = Faction.OfPlayer;
            if (faction == null || player == null || faction.HostileTo(player))
            {
                return;
            }
            faction.SetRelationDirect(player, FactionRelationKind.Hostile, canSendHostilityLetter: false);
        }

        private static Faction GetOrCreateVillageFaction()
        {
            FactionManager manager = Find.FactionManager;
            FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail("MUGB_StartVillageFaction");
            if (manager == null || def == null)
            {
                return null;
            }

            Faction faction = manager.FirstFactionOfDef(def);
            if (faction != null)
            {
                return faction;
            }

            try
            {
                faction = FactionGenerator.NewGeneratedFaction(
                    new FactionGeneratorParms(def, default(IdeoGenerationParms), hidden: true));
                manager.Add(faction);
                // 관계는 FactionGenerator.NewGeneratedFaction이 이미 모든 세력과 만들어 둡니다.
                // (내부에서 TryMakeInitialRelationsWith를 전부 돌림) 그래서 여기서 따로 채울 필요가 없습니다.
                return faction;
            }
            catch (Exception ex)
            {
                Log.Error("[MUGB] Failed to create the starting-village faction.\n" + ex);
                return null;
            }
        }

        private static void ResetCombatLoadout(Pawn pawn)
        {
            pawn.equipment?.DestroyAllEquipment();
            if (pawn.apparel != null)
            {
                foreach (Apparel apparel in pawn.apparel.WornApparel.ToList())
                {
                    pawn.apparel.Remove(apparel);
                    apparel.Destroy();
                }
            }
        }

        private static void WearGoblinUnderlayer(Pawn pawn, QualityCategory quality)
        {
            Wear(pawn, RandomDef("MUGB_Apparel_GoblinBodyWrap", "MUGB_Apparel_GoblinLoincloth"), quality, Rand.Range(0.65f, 1f));
        }

        private static void Wear(Pawn pawn, ThingDef def, QualityCategory quality, float hitPointFactor)
        {
            if (pawn?.apparel == null || def == null)
            {
                return;
            }
            Apparel apparel = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def)) as Apparel;
            if (apparel == null || !ApparelUtility.HasPartsToWear(pawn, def))
            {
                apparel?.Destroy();
                return;
            }
            apparel.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Colony);
            apparel.HitPoints = Mathf.Clamp(Mathf.RoundToInt(apparel.MaxHitPoints * hitPointFactor), 1, apparel.MaxHitPoints);
            pawn.apparel.Wear(apparel, dropReplacedApparel: true);
        }

        private static void Equip(Pawn pawn, ThingDef def, QualityCategory quality)
        {
            if (pawn?.equipment == null || def == null)
            {
                return;
            }
            Thing thing = MakeThing(def, quality);
            if (thing is ThingWithComps equipment)
            {
                pawn.equipment.DestroyAllEquipment();
                pawn.equipment.AddEquipment(equipment);
            }
        }

        private static Thing MakeThing(ThingDef def, QualityCategory quality)
        {
            if (def == null)
            {
                return null;
            }
            Thing thing = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
            thing.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Colony);
            return thing;
        }

        private static void SpawnNear(Pawn pawn, Thing thing)
        {
            if (pawn?.Map != null && thing != null)
            {
                GenPlace.TryPlaceThing(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            }
        }

        private static ThingDef RandomDef(params string[] defNames)
        {
            return defNames.Select(DefDatabase<ThingDef>.GetNamedSilentFail).Where(def => def != null).RandomElementWithFallback();
        }
    }
}
