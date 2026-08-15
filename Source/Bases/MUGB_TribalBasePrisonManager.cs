using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MUGB
{
    public sealed class MUGB_TribalBasePrisonManager : MapComponent
    {
        private const int InitializationInterval = 250;
        private const int PrisonerMaintenanceInterval = 1250;
        private const int MaxInitializationAttempts = 8;

        private bool initialized;
        private bool disabled;
        private int initializationAttempts;
        private Faction captorFaction;
        private List<Pawn> restrainedPrisoners = new List<Pawn>();

        public MUGB_TribalBasePrisonManager(Map map) : base(map)
        {
        }

        public override void MapGenerated()
        {
            base.MapGenerated();
            if (!initialized && !disabled)
            {
                TryInitialize();
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int now = Find.TickManager.TicksGame;
            if (disabled)
            {
                return;
            }

            if (!initialized)
            {
                if (now % InitializationInterval == map.uniqueID % InitializationInterval)
                {
                    TryInitialize();
                }
                return;
            }

            if (now % PrisonerMaintenanceInterval == map.uniqueID % PrisonerMaintenanceInterval)
            {
                MaintainRestrainedPrisoners();
            }
        }

        private void TryInitialize()
        {
            captorFaction = captorFaction ?? map.ParentFaction;
            if (captorFaction == null)
            {
                return;
            }
            if (!(map.Parent is Settlement) || !IsTribalGoblinFaction(captorFaction))
            {
                disabled = true;
                return;
            }

            initializationAttempts++;
            List<Pawn> defenders = map.mapPawns.SpawnedPawnsInFaction(captorFaction)
                .Where(pawn => pawn.RaceProps.Humanlike && !pawn.IsPrisoner && !pawn.Dead)
                .ToList();
            Room room = FindPrisonRoom(defenders);
            if (defenders.Count == 0 || room == null)
            {
                if (initializationAttempts >= MaxInitializationAttempts)
                {
                    disabled = true;
                    Log.Warning("[MUGB] Could not find a suitable existing room for the tribal goblin base prisoner room.");
                }
                return;
            }

            BuildPrisonRoom(room, captorFaction, defenders);
            initialized = true;
        }

        private Room FindPrisonRoom(List<Pawn> defenders)
        {
            ThingDef restraintDef = GetRestraintDef();
            bool restraintIsBed = restraintDef != null && typeof(Building_Bed).IsAssignableFrom(restraintDef.thingClass);
            int minimumUsableCells = restraintIsBed ? 8 : 12;
            IntVec3 center = defenders.Count > 0
                ? new IntVec3(
                    Mathf.RoundToInt((float)defenders.Average(pawn => pawn.Position.x)),
                    0,
                    Mathf.RoundToInt((float)defenders.Average(pawn => pawn.Position.z)))
                : map.Center;

            return map.regionGrid.AllRooms
                .Where(room => room != null && !room.Dereferenced && room.ProperRoom
                    && !room.TouchesMapEdge && !room.PsychologicallyOutdoors
                    && room.CellCount >= 12 && room.CellCount <= 120)
                .Select(room => new
                {
                    Room = room,
                    Cells = GetUsableCells(room),
                    Center = GetRoomCenter(room)
                })
                .Where(candidate => candidate.Cells.Count >= minimumUsableCells)
                .OrderByDescending(candidate => Math.Min(candidate.Cells.Count, 10) * 10
                    - Math.Abs(candidate.Room.CellCount - 30)
                    - candidate.Center.DistanceTo(center))
                .Select(candidate => candidate.Room)
                .FirstOrDefault();
        }

        private List<IntVec3> GetUsableCells(Room room)
        {
            return room.Cells
                .Where(cell => cell.InBounds(map) && cell.Standable(map) && cell.GetEdifice(map) == null
                    && !map.thingGrid.ThingsListAtFast(cell).Any(thing => thing is Pawn
                        || thing.def.category == ThingCategory.Building))
                .ToList();
        }

        private static IntVec3 GetRoomCenter(Room room)
        {
            return new IntVec3(
                Mathf.RoundToInt((float)room.Cells.Average(cell => cell.x)),
                0,
                Mathf.RoundToInt((float)room.Cells.Average(cell => cell.z)));
        }

        private void BuildPrisonRoom(Room room, Faction captor, List<Pawn> defenders)
        {
            List<IntVec3> usableCells = GetUsableCells(room).InRandomOrder().ToList();
            ThingDef poleDef = GetRestraintDef();
            bool restraintIsBed = poleDef != null && typeof(Building_Bed).IsAssignableFrom(poleDef.thingClass);
            int availablePrisonerSlots = restraintIsBed
                ? usableCells.Count - 4
                : (usableCells.Count - 4) / 2;
            int prisonerCount = Math.Min(Rand.RangeInclusive(4, 6), availablePrisonerSlots);
            prisonerCount = Math.Max(4, prisonerCount);
            List<Faction> origins = Find.FactionManager.AllFactionsVisible
                .Where(faction => !faction.IsPlayer && !faction.defeated && faction.def.humanlikeFaction
                    && !IsAnyGoblinFaction(faction))
                .ToList();
            if (origins.Count == 0)
            {
                disabled = true;
                return;
            }

            HediffDef restrained = DefDatabase<HediffDef>.GetNamedSilentFail("MUGB_Restrained");
            for (int i = 0; i < prisonerCount; i++)
            {
                IntVec3 cell = usableCells[i];
                Building_Bed bondageBed = PlaceRestraint(poleDef, cell, captor) as Building_Bed;
                IntVec3 prisonerCell = bondageBed != null ? cell : usableCells[prisonerCount + i];
                Pawn prisoner = GeneratePrisoner(origins.RandomElement(), captor, prisonerCell);
                if (prisoner == null)
                {
                    continue;
                }

                if (bondageBed != null)
                {
                    prisoner.ownership.ClaimBedIfNonMedical(bondageBed);
                    prisoner.jobs.StartJob(
                        JobMaker.MakeJob(JobDefOf.LayDown, bondageBed),
                        JobCondition.InterruptForced,
                        resumeCurJobAfterwards: false,
                        cancelBusyStances: true);
                }
                // BF bondage beds must enter their own LayDown toil before movement is disabled.
                if (bondageBed == null && restrained != null && !prisoner.health.hediffSet.HasHediff(restrained))
                {
                    prisoner.health.AddHediff(restrained);
                }
                restrainedPrisoners.Add(prisoner);
                TryMakePregnant(prisoner, defenders);
            }

            SpawnCorpses(room, origins);
            ScatterFilth(room);
        }

        private Thing PlaceRestraint(ThingDef def, IntVec3 cell, Faction captor)
        {
            if (def == null)
            {
                return null;
            }

            ThingDef stuff = def.MadeFromStuff && def.stuffCategories?.Contains(StuffCategoryDefOf.Woody) == true
                ? ThingDefOf.WoodLog
                : null;
            Thing restraint = ThingMaker.MakeThing(def, stuff);
            GenSpawn.Spawn(restraint, cell, map);
            if (restraint.def.CanHaveFaction)
            {
                restraint.SetFaction(captor);
            }
            return restraint;
        }

        private static ThingDef GetRestraintDef()
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail("DDJY_PrisonerPole")
                ?? DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_LogColumn");
        }

        private Pawn GeneratePrisoner(Faction origin, Faction captor, IntVec3 cell)
        {
            PawnGenerationRequest request = new PawnGenerationRequest(
                PawnKindDefOf.Villager,
                origin,
                fixedBiologicalAge: Rand.Range(18f, 25.99f),
                fixedChronologicalAge: Rand.Range(18f, 25.99f),
                fixedGender: Gender.Female,
                developmentalStages: DevelopmentalStage.Adult,
                dontGiveWeapon: true,
                forceNoGear: true);
            Pawn prisoner = PawnGenerator.GeneratePawn(request);
            if (prisoner == null)
            {
                return null;
            }

            if (GoblinUtility.HasGoblinCoreMarker(prisoner))
            {
                prisoner.genes.SetXenotype(XenotypeDefOf.Baseliner);
            }

            GenSpawn.Spawn(prisoner, cell, map);
            prisoner.guest.SetGuestStatus(captor, GuestStatus.Prisoner);
            return prisoner;
        }

        private static void TryMakePregnant(Pawn mother, List<Pawn> defenders)
        {
            if (!ModsConfig.BiotechActive || !Rand.Chance(0.10f))
            {
                return;
            }

            Pawn father = defenders
                .Where(pawn => pawn.gender == Gender.Male && !pawn.Dead
                    && PregnancyUtility.CanEverProduceChild(pawn, mother).Accepted)
                .RandomElementWithFallback();
            if (father == null)
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

        private void SpawnCorpses(Room room, List<Faction> origins)
        {
            List<IntVec3> cells = GetUsableCells(room).InRandomOrder().ToList();
            int count = Math.Min(Rand.RangeInclusive(4, 6), cells.Count);
            int dessicatedCount = Rand.RangeInclusive(2, count - 2);
            for (int i = 0; i < count && cells.Count > 0; i++)
            {
                Faction origin = origins.RandomElement();
                PawnGenerationRequest request = new PawnGenerationRequest(
                    PawnKindDefOf.Villager,
                    origin,
                    fixedBiologicalAge: Rand.Range(18f, 60f),
                    fixedChronologicalAge: Rand.Range(18f, 60f),
                    forceGenerateNewPawn: true,
                    allowDead: true,
                    forceDead: true);
                Pawn dead = PawnGenerator.GeneratePawn(request);
                Corpse corpse = dead?.Corpse;
                if (corpse == null)
                {
                    continue;
                }

                CompRottable rottable = corpse.TryGetComp<CompRottable>();
                if (rottable != null)
                {
                    rottable.RotProgress = i < dessicatedCount
                        ? rottable.PropsRot.TicksToDessicated + Rand.Range(60000f, 300000f)
                        : Rand.Range(
                            rottable.PropsRot.TicksToRotStart + 60000f,
                            Math.Max(rottable.PropsRot.TicksToRotStart + 60001f,
                                rottable.PropsRot.TicksToDessicated - 60000f));
                }
                GenSpawn.Spawn(corpse, cells.Pop(), map);
            }
        }

        private void ScatterFilth(Room room)
        {
            List<ThingDef> filthDefs = new[]
            {
                "Filth_Vomit",
                "Filth_GestationFluid",
                "Filth_AmnioticFluid",
                "Filth_Blood"
            }
                .Select(DefDatabase<ThingDef>.GetNamedSilentFail)
                .Where(def => def != null)
                .ToList();
            if (filthDefs.Count == 0)
            {
                return;
            }

            List<IntVec3> cells = room.Cells.Where(cell => cell.InBounds(map) && cell.GetEdifice(map) == null).ToList();
            int filthCount = Mathf.Clamp(room.CellCount, 18, 32);
            for (int i = 0; i < filthCount && cells.Count > 0; i++)
            {
                FilthMaker.TryMakeFilth(cells.RandomElement(), map, filthDefs.RandomElement());
            }
        }

        private void MaintainRestrainedPrisoners()
        {
            HediffDef restrained = DefDatabase<HediffDef>.GetNamedSilentFail("MUGB_Restrained");
            foreach (Pawn pawn in restrainedPrisoners.Where(pawn => pawn != null && !pawn.Dead))
            {
                if (pawn.IsPrisoner && pawn.HostFaction == captorFaction && pawn.guest?.Released != true)
                {
                    Building_Bed bondageBed = pawn.ownership?.OwnedBed;
                    if (bondageBed != null && IsBondageBed(bondageBed))
                    {
                        Hediff prematureRestraint = restrained == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(restrained);
                        if (prematureRestraint != null)
                        {
                            pawn.health.RemoveHediff(prematureRestraint);
                        }
                        if (pawn.CurrentBed() != bondageBed && pawn.CurJobDef != JobDefOf.LayDown)
                        {
                            pawn.jobs.StartJob(
                                JobMaker.MakeJob(JobDefOf.LayDown, bondageBed),
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

                Hediff hediff = restrained == null ? null : pawn.health.hediffSet.GetFirstHediffOfDef(restrained);
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

        private static bool IsTribalGoblinFaction(Faction faction)
        {
            return faction?.def == MUGBDefOf.MUGB_GoblinTribe
                || faction?.def == MUGBDefOf.MUGB_GoblinCivilTribe;
        }

        private static bool IsBondageBed(Building_Bed bed)
        {
            return bed?.GetType().FullName == "DDJY_BED.Building_BondageBed";
        }

        private static bool IsAnyGoblinFaction(Faction faction)
        {
            string defName = faction?.def?.defName ?? string.Empty;
            return defName.StartsWith("MUGB_Goblin", StringComparison.Ordinal);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref initialized, "initialized");
            Scribe_Values.Look(ref disabled, "disabled");
            Scribe_Values.Look(ref initializationAttempts, "initializationAttempts");
            Scribe_References.Look(ref captorFaction, "captorFaction");
            Scribe_Collections.Look(ref restrainedPrisoners, "restrainedPrisoners", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                restrainedPrisoners = restrainedPrisoners ?? new List<Pawn>();
            }
        }
    }
}
