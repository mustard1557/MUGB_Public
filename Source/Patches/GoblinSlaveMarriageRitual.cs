using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MUGB
{
    public static class SlaveMarriageRitualUtility
    {
        public const string OfficiantRoleId = "officiant";
        public const string MastersRoleId = "masters";
        public const string SlavesRoleId = "slaves";
        public const int CooldownTicks = 2 * GenDate.TicksPerDay;

        public static bool IsAvailableFreeColonist(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true
                && !pawn.Dead
                && pawn.IsColonistPlayerControlled
                && !pawn.IsSlave
                && !pawn.IsPrisoner
                && GoblinSlaveMarriageUtility.IsAdultForSlaveMarriage(pawn)
                && !pawn.Downed
                && !pawn.InMentalState;
        }

        public static bool IsAvailableColonySlave(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true
                && !pawn.Dead
                && pawn.Faction == Faction.OfPlayer
                && pawn.IsSlave
                && !pawn.IsPrisoner
                && GoblinSlaveMarriageUtility.IsAdultForSlaveMarriage(pawn)
                && !pawn.Downed
                && !pawn.InMentalState;
        }

        public static bool IsLeaderOrMoralGuide(Pawn pawn)
        {
            Precept_Role role = pawn?.Ideo?.GetRole(pawn);
            string defName = role?.def?.defName;
            return defName == "IdeoRole_Leader" || defName == "IdeoRole_Moralist";
        }

        public static bool ValidateAssignments(RitualRoleAssignments assignments, out string reason, out List<Pawn> masters, out List<Pawn> slaves)
        {
            masters = assignments?.AssignedPawns(MastersRoleId).Where(p => p != null).Distinct().ToList() ?? new List<Pawn>();
            slaves = assignments?.AssignedPawns(SlavesRoleId).Where(p => p != null).Distinct().ToList() ?? new List<Pawn>();
            Pawn officiant = assignments?.FirstAssignedPawn(OfficiantRoleId);

            if (officiant == null || !IsAvailableFreeColonist(officiant) || !IsLeaderOrMoralGuide(officiant))
            {
                reason = "MUGB_SlaveMarriageRitualNeedOfficiant".Translate();
                return false;
            }

            if (masters.Count < 1 || masters.Count > 3 || slaves.Count < 1 || slaves.Count > 3)
            {
                reason = "MUGB_SlaveMarriageRitualNeedGroups".Translate();
                return false;
            }

            bool hasNewPair = false;
            for (int masterIndex = 0; masterIndex < masters.Count; masterIndex++)
            {
                Pawn master = masters[masterIndex];
                if (!IsAvailableFreeColonist(master))
                {
                    reason = "MUGB_SlaveMarriageUnavailableNow".Translate(master.Named("PAWN"));
                    return false;
                }

                for (int slaveIndex = 0; slaveIndex < slaves.Count; slaveIndex++)
                {
                    Pawn slave = slaves[slaveIndex];
                    if (!IsAvailableColonySlave(slave))
                    {
                        reason = "MUGB_SlaveMarriageUnavailableNow".Translate(slave.Named("PAWN"));
                        return false;
                    }

                    if (!GoblinSlaveMarriageUtility.CanJoinSlaveMarriageCeremony(master, slave, out string pairReason))
                    {
                        reason = "MUGB_SlaveMarriageRitualInvalidPair".Translate(master.LabelShortCap, slave.LabelShortCap, pairReason);
                        return false;
                    }

                    hasNewPair |= !GoblinSlaveMarriageUtility.IsSlaveMarriage(master, slave);
                }
            }

            if (!hasNewPair)
            {
                reason = "MUGB_SlaveMarriageRitualNeedNewPair".Translate();
                return false;
            }

            reason = null;
            return true;
        }

        public static bool TryGetPawnPosition(Pawn pawn, LordJob_Ritual ritual, out PawnStagePosition position)
        {
            position = default;
            if (pawn == null
                || !(ritual is LordJob_Joinable_SlaveMarriage slaveMarriage)
                || ritual.assignments == null
                || ritual.Map == null)
            {
                return false;
            }

            return slaveMarriage.TryGetPlannedPosition(pawn, out position);
        }

        public static bool TryFindStageAnchor(
            TargetInfo target,
            RitualRoleAssignments assignments,
            out IntVec3 anchorCell,
            out Rot4 forward,
            out Thing focusThing)
        {
            anchorCell = IntVec3.Invalid;
            forward = Rot4.Invalid;
            focusThing = null;
            if (!target.IsValid || target.Map == null || assignments == null)
            {
                return false;
            }

            Pawn officiant = assignments.FirstAssignedPawn(OfficiantRoleId);
            List<Pawn> masters = assignments.AssignedPawns(MastersRoleId).Where(p => p != null).ToList();
            List<Pawn> slaves = assignments.AssignedPawns(SlavesRoleId).Where(p => p != null).ToList();
            foreach (SlaveMarriageStageAnchor anchor in CandidateStageAnchors(target, officiant))
            {
                Dictionary<Pawn, PawnStagePosition> layout = BuildLayout(masters, slaves, officiant, anchor);
                if (layout.Count == masters.Count + slaves.Count + (officiant != null ? 1 : 0)
                    && LayoutIsUsable(layout, target.Map, target))
                {
                    anchorCell = anchor.Cell;
                    forward = anchor.Forward;
                    focusThing = anchor.OfficiantThing;
                    return true;
                }
            }

            return false;
        }

        public static Dictionary<Pawn, PawnStagePosition> BuildPlannedLayout(
            TargetInfo target,
            RitualRoleAssignments assignments,
            IntVec3 anchorCell,
            Rot4 forward,
            Thing focusThing)
        {
            List<Pawn> masters = assignments.AssignedPawns(MastersRoleId).Where(p => p != null).ToList();
            List<Pawn> slaves = assignments.AssignedPawns(SlavesRoleId).Where(p => p != null).ToList();
            Pawn officiant = assignments.FirstAssignedPawn(OfficiantRoleId);
            bool usesLectern = focusThing?.def == ThingDefOf.Lectern;
            IntVec3 officiantCell = usesLectern
                ? new RitualPosition_Lectern { maxDistanceToFocus = 5 }.PositionForThing(focusThing)
                : anchorCell;
            Dictionary<Pawn, PawnStagePosition> desired = BuildLayout(
                masters,
                slaves,
                officiant,
                new SlaveMarriageStageAnchor(
                    anchorCell,
                    forward,
                    target.Thing,
                    officiantCell,
                    usesLectern ? focusThing : target.Thing,
                    usesLectern ? focusThing.Rotation : forward));
            return desired;
        }

        private static Dictionary<Pawn, PawnStagePosition> BuildLayout(
            List<Pawn> masters,
            List<Pawn> slaves,
            Pawn officiant,
            SlaveMarriageStageAnchor anchor)
        {
            Dictionary<Pawn, PawnStagePosition> result = new Dictionary<Pawn, PawnStagePosition>();
            IntVec3 forwardCell = anchor.Forward.FacingCell;
            IntVec3 leftCell = new IntVec3(forwardCell.z, 0, -forwardCell.x);

            AddMeetingLine(result, masters, anchor.Cell, forwardCell, leftCell, masterSide: true);
            AddMeetingLine(result, slaves, anchor.Cell, forwardCell, leftCell, masterSide: false);

            if (officiant != null)
            {
                result[officiant] = new PawnStagePosition(
                    anchor.OfficiantCell,
                    anchor.OfficiantThing,
                    anchor.OfficiantFacing,
                    highlight: true);
            }

            return result;
        }

        private static void AddMeetingLine(
            Dictionary<Pawn, PawnStagePosition> result,
            List<Pawn> pawns,
            IntVec3 officiantCell,
            IntVec3 forward,
            IntVec3 left,
            bool masterSide)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                IntVec3 aisleCell = officiantCell + forward * i;
                IntVec3 side = masterSide ? left : new IntVec3(-left.x, 0, -left.z);
                IntVec3 facingVector = new IntVec3(-side.x, 0, -side.z);
                result[pawns[i]] = new PawnStagePosition(
                    aisleCell + side,
                    null,
                    Rot4.FromIntVec3(facingVector),
                    highlight: true);
            }
        }

        private static IntVec3 OfficiantCell(IntVec3 center, Thing targetThing, Rot4 forward)
        {
            if (targetThing == null)
            {
                return center;
            }

            if (targetThing.def.passability == Traversability.Standable)
            {
                IntVec3 centerCell = targetThing.OccupiedRect().CenterCell;
                if (centerCell.Standable(targetThing.Map))
                {
                    return centerCell;
                }
            }

            if (targetThing?.def?.hasInteractionCell == true)
            {
                return targetThing.InteractionCell;
            }

            CellRect occupied = targetThing.OccupiedRect();
            IntVec3 edge = occupied.ClosestCellTo(center + forward.FacingCell * 100);
            return edge + forward.FacingCell;
        }

        private static IEnumerable<SlaveMarriageStageAnchor> CandidateStageAnchors(TargetInfo target, Pawn officiant)
        {
            Thing lectern = FindUsableLectern(target, officiant);
            RitualPosition_Lectern lecternPosition = lectern == null
                ? null
                : new RitualPosition_Lectern { maxDistanceToFocus = 5 };
            foreach (Rot4 forward in CandidateAudienceDirections(target))
            {
                IntVec3 formationCell = OfficiantCell(target.Cell, target.Thing, forward);
                yield return new SlaveMarriageStageAnchor(
                    formationCell,
                    forward,
                    target.Thing,
                    lecternPosition?.PositionForThing(lectern) ?? formationCell,
                    lectern,
                    lectern?.Rotation ?? forward);
            }
        }

        private static Thing FindUsableLectern(TargetInfo target, Pawn officiant)
        {
            if (target.Map == null || officiant == null || ThingDefOf.Lectern == null)
            {
                return null;
            }

            Room ritualRoom = GetRitualRoom(target);
            RitualPosition_Lectern position = new RitualPosition_Lectern { maxDistanceToFocus = 5 };
            return target.Map.listerThings.ThingsOfDef(ThingDefOf.Lectern)
                .Where(lectern => lectern?.Spawned == true)
                .OrderBy(lectern => lectern.Position.DistanceToSquared(target.Cell))
                .FirstOrDefault(lectern =>
                {
                    IntVec3 cell = position.PositionForThing(lectern);
                    return position.IsUsableThing(lectern, target.Cell, target)
                        && cell.GetRoom(target.Map) == ritualRoom
                        && officiant.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Deadly);
                });
        }

        private static IEnumerable<Rot4> CandidateAudienceDirections(TargetInfo target)
        {
            Thing targetThing = target.Thing;
            List<Rot4> directions = new List<Rot4>();
            RitualFocusProperties ritualFocus = targetThing?.def?.ritualFocus;
            if (ritualFocus != null)
            {
                SpectateRectSide allowedSides = ritualFocus.allowedSpectateSides.Rotated(targetThing.Rotation);
                SpectateRectSide[] orderedSides =
                {
                    SpectateRectSide.Down,
                    SpectateRectSide.Up,
                    SpectateRectSide.Right,
                    SpectateRectSide.Left
                };
                for (int i = 0; i < orderedSides.Length; i++)
                {
                    if ((allowedSides & orderedSides[i]) == orderedSides[i])
                    {
                        directions.Add(orderedSides[i].AsRot4());
                    }
                }
            }

            if (directions.Count == 0 && targetThing?.def?.hasInteractionCell == true)
            {
                IntVec3 delta = targetThing.InteractionCell - target.Cell;
                if (delta.x != 0 || delta.z != 0)
                {
                    directions.Add(Math.Abs(delta.x) > Math.Abs(delta.z)
                        ? (delta.x > 0 ? Rot4.East : Rot4.West)
                        : (delta.z > 0 ? Rot4.North : Rot4.South));
                }
            }

            if (directions.Count == 0)
            {
                directions.Add(Rot4.South);
            }

            if (ritualFocus == null)
            {
                Rot4 first = directions[0];
                Rot4[] fallbackOrder =
                {
                    first,
                    new Rot4((first.AsInt + 1) % 4),
                    new Rot4((first.AsInt + 3) % 4),
                    first.Opposite
                };
                for (int i = 0; i < fallbackOrder.Length; i++)
                {
                    if (!directions.Contains(fallbackOrder[i]))
                    {
                        directions.Add(fallbackOrder[i]);
                    }
                }
            }

            for (int i = 0; i < directions.Count; i++)
            {
                yield return directions[i];
            }
        }

        public static Room GetRitualRoom(TargetInfo target)
        {
            Map map = target.Map;
            Thing thing = target.Thing;
            if (thing != null)
            {
                Room indoorRoom = thing.OccupiedRect().Cells
                    .Select(cell => cell.GetRoom(map))
                    .FirstOrDefault(room => room != null && !room.PsychologicallyOutdoors);
                if (indoorRoom != null)
                {
                    return indoorRoom;
                }
            }

            if (thing?.def?.hasInteractionCell == true && thing.InteractionCell.InBounds(map))
            {
                Room interactionRoom = thing.InteractionCell.GetRoom(map);
                if (interactionRoom != null)
                {
                    return interactionRoom;
                }
            }

            return target.Cell.GetRoom(map);
        }

        private static bool LayoutIsUsable(
            Dictionary<Pawn, PawnStagePosition> layout,
            Map map,
            TargetInfo target)
        {
            Room ritualRoom = GetRitualRoom(target);
            HashSet<IntVec3> usedCells = new HashSet<IntVec3>();
            foreach (KeyValuePair<Pawn, PawnStagePosition> entry in layout)
            {
                IntVec3 cell = entry.Value.cell;
                if (!usedCells.Add(cell)
                    || !IsUsableCell(cell, map)
                    || cell.GetRoom(map) != ritualRoom
                    || CommonRitualCellPredicates.InDoor(map, cell)
                    || entry.Key?.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Deadly) != true)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsUsableCell(IntVec3 cell, Map map)
        {
            return cell.InBounds(map) && cell.Standable(map) && !cell.Fogged(map);
        }

        private sealed class SlaveMarriageStageAnchor
        {
            public readonly IntVec3 Cell;
            public readonly Rot4 Forward;
            public readonly Thing FocusThing;
            public readonly IntVec3 OfficiantCell;
            public readonly Thing OfficiantThing;
            public readonly Rot4 OfficiantFacing;

            public SlaveMarriageStageAnchor(
                IntVec3 cell,
                Rot4 forward,
                Thing focusThing,
                IntVec3 officiantCell,
                Thing officiantThing,
                Rot4 officiantFacing)
            {
                Cell = cell;
                Forward = forward;
                FocusThing = focusThing;
                OfficiantCell = officiantCell.IsValid ? officiantCell : cell;
                OfficiantThing = officiantThing ?? focusThing;
                OfficiantFacing = officiantFacing.IsValid ? officiantFacing : forward;
            }
        }
    }

    public class RitualRole_SlaveMarriageMaster : RitualRoleColonist
    {
        public override bool AppliesToPawn(Pawn pawn, out string reason, TargetInfo target, LordJob_Ritual ritual, RitualRoleAssignments assignments, Precept_Ritual precept, bool skipReason = false)
        {
            if (!base.AppliesToPawn(pawn, out reason, target, ritual, assignments, precept, skipReason))
            {
                return false;
            }

            if (!SlaveMarriageRitualUtility.IsAvailableFreeColonist(pawn))
            {
                reason = "MUGB_SlaveMarriageNeedAdultFreeColonist".Translate(pawn.Named("PAWN"));
                return false;
            }

            foreach (Pawn slave in assignments?.AssignedPawns(SlaveMarriageRitualUtility.SlavesRoleId) ?? Enumerable.Empty<Pawn>())
            {
                if (!GoblinSlaveMarriageUtility.CanJoinSlaveMarriageCeremony(pawn, slave, out reason))
                {
                    return false;
                }
            }

            reason = null;
            return true;
        }
    }

    public class RitualRole_SlaveMarriageSlave : RitualRolePrisonerOrSlave
    {
        public override bool AppliesToPawn(Pawn pawn, out string reason, TargetInfo target, LordJob_Ritual ritual, RitualRoleAssignments assignments, Precept_Ritual precept, bool skipReason = false)
        {
            if (!base.AppliesToPawn(pawn, out reason, target, ritual, assignments, precept, skipReason))
            {
                return false;
            }

            if (!SlaveMarriageRitualUtility.IsAvailableColonySlave(pawn))
            {
                reason = "MUGB_SlaveMarriageNeedAdultColonySlave".Translate(pawn.Named("PAWN"));
                return false;
            }

            foreach (Pawn master in assignments?.AssignedPawns(SlaveMarriageRitualUtility.MastersRoleId) ?? Enumerable.Empty<Pawn>())
            {
                if (!GoblinSlaveMarriageUtility.CanJoinSlaveMarriageCeremony(master, pawn, out reason))
                {
                    return false;
                }
            }

            reason = null;
            return true;
        }
    }

    public class RitualRole_SlaveMarriageOfficiant : RitualRoleColonist
    {
        public override bool AppliesToPawn(Pawn pawn, out string reason, TargetInfo target, LordJob_Ritual ritual, RitualRoleAssignments assignments, Precept_Ritual precept, bool skipReason = false)
        {
            if (!base.AppliesToPawn(pawn, out reason, target, ritual, assignments, precept, skipReason))
            {
                return false;
            }

            if (!SlaveMarriageRitualUtility.IsAvailableFreeColonist(pawn) || !SlaveMarriageRitualUtility.IsLeaderOrMoralGuide(pawn))
            {
                reason = "MUGB_SlaveMarriageRitualNeedOfficiant".Translate();
                return false;
            }

            reason = null;
            return true;
        }
    }

    public class RitualPosition_SlaveMarriageLine : RitualPosition
    {
        public override PawnStagePosition GetCell(IntVec3 spot, Pawn pawn, LordJob_Ritual ritual)
        {
            return SlaveMarriageRitualUtility.TryGetPawnPosition(pawn, ritual, out PawnStagePosition position)
                ? position
                : new PawnStagePosition(spot, null, Rot4.South, highlight);
        }
    }

    public class LordJob_Joinable_SlaveMarriage : LordJob_Ritual
    {
        public override bool AllowStartNewGatherings => false;
        public override bool OrganizerIsStartingPawn => true;

        private bool stageAnchorInitialized;
        private IntVec3 stageAnchorCell = IntVec3.Invalid;
        private Rot4 stageForward = Rot4.Invalid;
        private Thing stageFocusThing;
        private Dictionary<Pawn, PawnStagePosition> plannedPositions;

        public LordJob_Joinable_SlaveMarriage()
        {
        }

        public LordJob_Joinable_SlaveMarriage(
            TargetInfo target,
            Pawn organizer,
            Precept_Ritual ritual,
            List<RitualStage> stages,
            RitualRoleAssignments assignments)
            : base(target, ritual, null, stages, assignments, organizer)
        {
            InitializeStageAnchor();
        }

        public bool TryGetPlannedPosition(Pawn pawn, out PawnStagePosition position)
        {
            if (!stageAnchorInitialized || plannedPositions == null)
            {
                InitializeStageAnchor();
            }

            position = null;
            return plannedPositions != null && plannedPositions.TryGetValue(pawn, out position);
        }

        public override bool BlocksSocialInteraction(Pawn pawn)
        {
            return assignments?.PawnParticipating(pawn) == true || base.BlocksSocialInteraction(pawn);
        }

        public CellRect SpectatorStageRect()
        {
            if (!stageAnchorInitialized || plannedPositions == null)
            {
                InitializeStageAnchor();
            }

            CellRect targetRect = selectedTarget.Thing?.OccupiedRect()
                ?? CellRect.SingleCell(selectedTarget.Cell);
            if (plannedPositions == null || plannedPositions.Count == 0)
            {
                return targetRect.ExpandedBy(3);
            }

            int minX = targetRect.minX;
            int maxX = targetRect.maxX;
            int minZ = targetRect.minZ;
            int maxZ = targetRect.maxZ;
            foreach (PawnStagePosition position in plannedPositions.Values)
            {
                minX = Math.Min(minX, position.cell.x);
                maxX = Math.Max(maxX, position.cell.x);
                minZ = Math.Min(minZ, position.cell.z);
                maxZ = Math.Max(maxZ, position.cell.z);
            }

            return new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
        }

        private void InitializeStageAnchor()
        {
            if (!stageAnchorInitialized
                && SlaveMarriageRitualUtility.TryFindStageAnchor(
                    selectedTarget,
                    assignments,
                    out IntVec3 cell,
                    out Rot4 forward,
                    out Thing focusThing))
            {
                stageAnchorCell = cell;
                stageForward = forward;
                stageFocusThing = focusThing;
                stageAnchorInitialized = true;
            }

            if (stageAnchorInitialized && plannedPositions == null)
            {
                plannedPositions = SlaveMarriageRitualUtility.BuildPlannedLayout(
                    selectedTarget,
                    assignments,
                    stageAnchorCell,
                    stageForward,
                    stageFocusThing);
            }
        }

        protected override LordToil_Ritual MakeToil(RitualStage stage)
        {
            return new LordToil_Ritual_SlaveMarriage(spot, this, stage, organizer);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref stageAnchorInitialized, "stageAnchorInitialized", defaultValue: false);
            Scribe_Values.Look(ref stageAnchorCell, "stageAnchorCell");
            Scribe_Values.Look(ref stageForward, "stageForward");
            Scribe_References.Look(ref stageFocusThing, "stageFocusThing");
        }
    }

    public class LordToil_Ritual_SlaveMarriage : LordToil_Ritual
    {
        public LordToil_Ritual_SlaveMarriage(
            IntVec3 spot,
            LordJob_Ritual ritual,
            RitualStage stage,
            Pawn organizer)
            : base(spot, ritual, stage, organizer)
        {
        }

        public override void UpdateAllDuties()
        {
            base.UpdateAllDuties();
            if (!(ritual is LordJob_Joinable_SlaveMarriage slaveMarriage))
            {
                return;
            }

            CellRect stageRect = slaveMarriage.SpectatorStageRect();
            foreach (Pawn spectator in ritual.assignments.SpectatorsForReading)
            {
                if (spectator?.mindState?.duty == null || spectator.GetLord() != lord)
                {
                    continue;
                }

                spectator.mindState.duty.spectateRect = stageRect;
                spectator.mindState.duty.spectateRectAllowedSides = SpectateRectSide.All;
                spectator.mindState.duty.spectateRectPreferredSide = SpectateRectSide.None;
                spectator.mindState.duty.spectateDistance = new IntRange(1, 4);
                spectator.jobs?.CheckForJobOverride();
            }
        }

        public override void LordToilTick()
        {
            base.LordToilTick();
            if (!(ritual is LordJob_Joinable_SlaveMarriage slaveMarriage))
            {
                return;
            }

            Room ritualRoom = SlaveMarriageRitualUtility.GetRitualRoom(slaveMarriage.selectedTarget);
            List<Pawn> ownedPawns = lord.ownedPawns;
            for (int i = 0; i < ownedPawns.Count; i++)
            {
                Pawn pawn = ownedPawns[i];
                if (pawn?.Spawned == true && pawn.Position.GetRoom(pawn.Map) == ritualRoom)
                {
                    continue;
                }

                // LordToil_Gathering just counted this pawn by radius; only undo that tick.
                if (pawn?.MapHeld == null
                    || !GatheringsUtility.InGatheringArea(pawn.Position, spot, pawn.MapHeld))
                {
                    continue;
                }

                if (Data.presentForTicks.TryGetValue(pawn, out int ticks))
                {
                    if (ticks <= 1)
                    {
                        Data.presentForTicks.Remove(pawn);
                    }
                    else
                    {
                        Data.presentForTicks[pawn] = ticks - 1;
                    }
                }
            }
        }
    }

    public class JobGiver_SlaveMarriageStand : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            Job job = JobMaker.MakeJob(MUGBDefOf.MUGB_StandAtSlaveMarriage);
            job.expiryInterval = 60;
            job.overrideFacing = pawn.mindState?.duty?.overrideFacing ?? Rot4.Invalid;
            return job;
        }
    }

    [HarmonyPatch(typeof(JobGiver_SpectateDutySpectateRect), "TryFindSpot")]
    public static class JobGiver_SpectateDutySpectateRect_TryFindSpot_SlaveMarriagePatch
    {
        public static bool Prefix(Pawn pawn, PawnDuty duty, ref IntVec3 spot, ref bool __result)
        {
            if (!(pawn?.GetLord()?.LordJob is LordJob_Joinable_SlaveMarriage ritual))
            {
                return true;
            }

            Map map = pawn.Map;
            Room ritualRoom = SlaveMarriageRitualUtility.GetRitualRoom(ritual.selectedTarget);
            Func<IntVec3, Pawn, Map, bool> validator = (cell, spectator, spectatorMap) =>
                RitualUtility.GoodSpectateCellForRitual(cell, spectator, spectatorMap)
                && cell.GetRoom(spectatorMap) == ritualRoom
                && !CommonRitualCellPredicates.InDoor(spectatorMap, cell);

            Precept_Ritual precept = ritual.Ritual;
            bool found = duty.spectateRectPreferredSide != SpectateRectSide.None
                && SpectatorCellFinder.TryFindSpectatorCellFor(
                    pawn,
                    duty.spectateRect,
                    map,
                    out spot,
                    duty.spectateRectPreferredSide,
                    1,
                    null,
                    precept,
                    validator);

            if (!found)
            {
                found = SpectatorCellFinder.TryFindSpectatorCellFor(
                    pawn,
                    duty.spectateRect,
                    map,
                    out spot,
                    duty.spectateRectAllowedSides,
                    1,
                    null,
                    precept,
                    validator);
            }

            if (!found)
            {
                IntVec3 target = duty.spectateRect.CenterCell;
                found = CellFinder.TryFindRandomReachableNearbyCell(
                    target,
                    pawn.MapHeld,
                    5f,
                    TraverseParms.For(pawn),
                    cell => cell.GetRoom(pawn.MapHeld) == ritualRoom
                        && pawn.CanReserveSittableOrSpot(cell)
                        && !duty.spectateRect.Contains(cell)
                        && !CommonRitualCellPredicates.InDoor(pawn.MapHeld, cell),
                    null,
                    out spot);
            }

            if (!found)
            {
                spot = IntVec3.Invalid;
                Log.Warning($"[MUGB] Failed to find an indoor slave-marriage spectator spot for {pawn}.");
            }

            __result = found;
            return false;
        }
    }

    public class RitualBehaviorWorker_SlaveMarriage : RitualBehaviorWorker
    {
        public RitualBehaviorWorker_SlaveMarriage()
        {
        }

        public RitualBehaviorWorker_SlaveMarriage(RitualBehaviorDef def) : base(def)
        {
        }

        public override string CanStartRitualNow(TargetInfo target, Precept_Ritual ritual, Pawn selectedPawn, Dictionary<string, Pawn> forcedForRole)
        {
            string baseReason = base.CanStartRitualNow(target, ritual, selectedPawn, forcedForRole);
            if (!baseReason.NullOrEmpty())
            {
                return baseReason;
            }

            if (!GoblinSlaveMarriageUtility.IdeologyAllowsSlaveMarriage(ritual?.ideo))
            {
                return "MUGB_SlaveMarriageRitualIdeologyRequired".Translate();
            }

            int elapsed = Find.TickManager.TicksGame - ritual.lastFinishedTick;
            if (ritual.lastFinishedTick > 0 && elapsed < SlaveMarriageRitualUtility.CooldownTicks)
            {
                return "MUGB_SlaveMarriageRitualCooldown".Translate((SlaveMarriageRitualUtility.CooldownTicks - elapsed).ToStringTicksToPeriod());
            }

            return null;
        }

        public override void TryExecuteOn(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments, bool playerForced = false)
        {
            if (!SlaveMarriageRitualUtility.ValidateAssignments(assignments, out string reason, out _, out _))
            {
                Messages.Message(reason, target, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            if (!SlaveMarriageRitualUtility.TryFindStageAnchor(target, assignments, out _, out _, out _))
            {
                Messages.Message("MUGB_SlaveMarriageRitualNeedSpace".Translate(), target, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            base.TryExecuteOn(target, organizer, ritual, obligation, assignments, playerForced);
        }

        protected override LordJob CreateLordJob(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments)
        {
            Pawn officiant = assignments.FirstAssignedPawn(SlaveMarriageRitualUtility.OfficiantRoleId);
            return new LordJob_Joinable_SlaveMarriage(target, officiant, ritual, def.stages, assignments);
        }
    }

    public class RitualOutcomeEffectWorker_SlaveMarriage : RitualOutcomeEffectWorker_FromQuality
    {
        public RitualOutcomeEffectWorker_SlaveMarriage()
        {
        }

        public RitualOutcomeEffectWorker_SlaveMarriage(RitualOutcomeEffectDef def) : base(def)
        {
        }

        public override void Apply(float progress, Dictionary<Pawn, int> totalPresence, LordJob_Ritual jobRitual)
        {
            if (jobRitual.cancelled)
            {
                return;
            }

            if (!SlaveMarriageRitualUtility.ValidateAssignments(jobRitual.assignments, out _, out List<Pawn> masters, out List<Pawn> slaves))
            {
                Messages.Message("MUGB_SlaveMarriageRitualFailed".Translate(), jobRitual.selectedTarget, MessageTypeDefOf.NegativeEvent, historical: false);
                return;
            }

            List<Tuple<Pawn, Pawn>> newPairs = new List<Tuple<Pawn, Pawn>>();
            for (int masterIndex = 0; masterIndex < masters.Count; masterIndex++)
            {
                for (int slaveIndex = 0; slaveIndex < slaves.Count; slaveIndex++)
                {
                    if (!GoblinSlaveMarriageUtility.IsSlaveMarriage(masters[masterIndex], slaves[slaveIndex]))
                    {
                        newPairs.Add(Tuple.Create(masters[masterIndex], slaves[slaveIndex]));
                    }
                }
            }

            int registered = 0;
            for (int i = 0; i < newPairs.Count; i++)
            {
                if (GoblinSlaveMarriageUtility.RegisterSlaveMarriagePair(newPairs[i].Item1, newPairs[i].Item2, assignBed: false, showMessage: false))
                {
                    registered++;
                }
            }

            float quality = GetQuality(jobRitual, progress);
            int thoughtStage = QualityThoughtStage(quality);
            if (MUGBDefOf.MUGB_AttendedSlaveMarriageCeremony != null)
            {
                foreach (Pawn pawn in totalPresence.Keys)
                {
                    pawn?.needs?.mood?.thoughts?.memories?.TryGainMemory(
                        ThoughtMaker.MakeThought(MUGBDefOf.MUGB_AttendedSlaveMarriageCeremony, thoughtStage) as Thought_Memory);
                }
            }

            LookTargets letterLookTargets = new LookTargets(masters.Concat(slaves));
            string attachedOutcomeText = null;
            RitualOutcomePossibility outcome = def.outcomeChances.FirstOrDefault();
            if (outcome != null)
            {
                ApplyAttachableOutcome(totalPresence, jobRitual, outcome, out attachedOutcomeText, ref letterLookTargets);
            }

            TaggedString letterText = "MUGB_SlaveMarriageRitualCompletedText".Translate(
                masters.Count,
                slaves.Count,
                registered,
                quality.ToStringPercent());
            if (!attachedOutcomeText.NullOrEmpty())
            {
                letterText += "\n\n" + attachedOutcomeText;
            }

            Find.LetterStack.ReceiveLetter(
                "MUGB_SlaveMarriageRitualCompletedLabel".Translate(),
                letterText,
                quality < 0.2f ? LetterDefOf.RitualOutcomeNegative : LetterDefOf.RitualOutcomePositive,
                letterLookTargets);
        }

        private static int QualityThoughtStage(float quality)
        {
            if (quality < 0.2f) return 0;
            if (quality < 0.4f) return 1;
            if (quality < 0.6f) return 2;
            if (quality < 0.75f) return 3;
            if (quality < 0.9f) return 4;
            return 5;
        }
    }
}
