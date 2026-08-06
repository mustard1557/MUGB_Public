using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MUGB.Patches;
using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace MUGB
{
    public class GameComponent_GoblinSlaveMarriage : GameComponent
    {
        private List<string> slaveMarriagePairs = new List<string>();

        public GameComponent_GoblinSlaveMarriage(Game game)
        {
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref slaveMarriagePairs, "slaveMarriagePairs", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && slaveMarriagePairs == null)
            {
                slaveMarriagePairs = new List<string>();
            }
        }

        public bool IsSlaveMarriage(Pawn first, Pawn second)
        {
            return first != null
                && second != null
                && slaveMarriagePairs.Contains(PairKey(first, second));
        }

        public void Register(Pawn first, Pawn second)
        {
            if (first == null || second == null)
            {
                return;
            }

            string key = PairKey(first, second);
            if (!slaveMarriagePairs.Contains(key))
            {
                slaveMarriagePairs.Add(key);
            }
        }

        public void Unregister(Pawn first, Pawn second)
        {
            if (first == null || second == null)
            {
                return;
            }

            slaveMarriagePairs.Remove(PairKey(first, second));
        }

        private static string PairKey(Pawn first, Pawn second)
        {
            int a = first.thingIDNumber;
            int b = second.thingIDNumber;
            return a < b ? $"{a}:{b}" : $"{b}:{a}";
        }
    }

    public static class GoblinSlaveMarriageUtility
    {
        private const int InteractionTicks = 180;
        private const int SlaveMarriageLovinCooldownTicks = 7500;
        private const float SlaveMarriageLovinMtbHours = 3f;
        private const int LovinThoughtDedupeTicks = 500;
        private static readonly Dictionary<string, int> lastLovinThoughtTicksByPair = new Dictionary<string, int>();
        private static readonly HashSet<string> SpouseDeathThoughtDefNames = new HashSet<string>
        {
            "MyHusbandDied",
            "MyWifeDied",
            "MyLoverDied",
            "MyFianceDied",
            "MyFianceeDied"
        };

        public static int JobInteractionTicks => InteractionTicks;

        public static bool IsSlaveMarriage(Pawn first, Pawn second)
        {
            return Current.Game?.GetComponent<GameComponent_GoblinSlaveMarriage>()?.IsSlaveMarriage(first, second) == true;
        }

        public static void TryGiveFreeMarriageUnderSlaveMarriageIdeoThought(Pawn pawn, Pawn spouse)
        {
            // Korean intent: "노예혼 중요시함"은 정착민끼리의 정식결혼만 -3으로 보며,
            // 노예혼으로 등록된 관계와 그 당사자에게는 절대 적용하지 않는다.
            if (pawn == null
                || spouse == null
                || pawn.Dead
                || spouse.Dead
                || pawn.IsSlave
                || spouse.IsSlave
                || IsSlaveMarriage(pawn, spouse)
                || MUGBDefOf.MUGB_FreeMarriageUnderSlaveMarriageIdeo == null)
            {
                return;
            }

            PreceptDef importantDef = DefDatabase<PreceptDef>.GetNamedSilentFail("MUGB_SlaveMarriage_Important");
            if (importantDef == null || pawn.Ideo?.HasPrecept(importantDef) != true)
            {
                return;
            }

            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGBDefOf.MUGB_FreeMarriageUnderSlaveMarriageIdeo, spouse);
        }

        public static bool HasAnySlaveMarriage(Pawn pawn)
        {
            if (pawn?.relations?.DirectRelations == null)
            {
                return false;
            }

            List<DirectPawnRelation> relations = pawn.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                if (relations[i].def == PawnRelationDefOf.Spouse && IsSlaveMarriage(pawn, relations[i].otherPawn))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasSlaveMarriageWithGoblinMaster(Pawn pawn)
        {
            if (pawn?.relations?.DirectRelations == null)
            {
                return false;
            }

            return pawn.relations.DirectRelations.Any(relation =>
                relation.def == PawnRelationDefOf.Spouse
                && relation.otherPawn != null
                && !relation.otherPawn.Dead
                && GoblinUtility.IsGoblin(relation.otherPawn)
                && IsSlaveMarriage(pawn, relation.otherPawn));
        }

        public static bool HasLivingSlaveConcubine(Pawn pawn)
        {
            if (pawn?.relations?.DirectRelations == null)
            {
                return false;
            }

            return pawn.relations.DirectRelations.Any(relation =>
                relation.def == PawnRelationDefOf.Spouse
                && relation.otherPawn?.IsSlave == true
                && !relation.otherPawn.Dead
                && IsSlaveMarriage(pawn, relation.otherPawn));
        }

        public static bool IsAdultForSlaveMarriage(Pawn pawn)
        {
            return GoblinRomanceAgeUtility.IsRomanceAdult(pawn);
        }

        public static bool IsMonogamousByIdeology(Pawn pawn)
        {
            if (!ModsConfig.IdeologyActive || pawn?.Ideo == null)
            {
                return false;
            }

            HistoryEvent twoSpouses = new HistoryEvent(
                HistoryEventDefOf.GotMarried_SpouseCount_Two,
                pawn.Named(HistoryEventArgsNames.Doer));
            return !twoSpouses.DoerWillingToDo();
        }

        public static bool TryGetSlaveMarriageBedPartner(Building_Bed bed, Pawn pawn, out Pawn partner)
        {
            partner = null;
            if (bed == null || pawn == null || bed.ForPrisoners || bed.SleepingSlotsCount <= 1)
            {
                return false;
            }

            List<Pawn> owners = bed.OwnersForReading;
            if (owners == null || owners.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < owners.Count; i++)
            {
                Pawn owner = owners[i];
                if (owner == null || owner == pawn || owner.Dead)
                {
                    continue;
                }

                if (!IsSlaveMarriage(owner, pawn))
                {
                    continue;
                }

                if (owner.IsSlave == pawn.IsSlave)
                {
                    continue;
                }

                partner = owner;
                return true;
            }

            return false;
        }

        public static bool CanCrossAssignSlaveMarriageBed(CompAssignableToPawn_Bed comp, Pawn pawn)
        {
            Building_Bed bed = comp?.parent as Building_Bed;
            if (bed == null || pawn == null || pawn.Dead)
            {
                return false;
            }

            if (bed.Medical || bed.ForPrisoners || bed.SleepingSlotsCount <= 1)
            {
                return false;
            }

            if (!TryGetSlaveMarriageBedPartner(bed, pawn, out Pawn partner) || partner == null)
            {
                return false;
            }

            if (pawn.BodySize > bed.def.building.bed_maxBodySize)
            {
                return false;
            }

            return true;
        }

        public static bool CanUseSlaveMarriageBedNow(Building_Bed bed, Pawn sleeper, bool checkSocialProperness, bool allowMedBedEvenIfSetToNoCare, GuestStatus? guestStatusOverride)
        {
            if (bed == null || sleeper == null || !TryGetSlaveMarriageBedPartner(bed, sleeper, out Pawn partner))
            {
                return false;
            }

            if (!bed.Spawned || bed.Map != sleeper.MapHeld || bed.IsBurning())
            {
                return false;
            }

            if (sleeper.HarmedByVacuum && bed.Position.GetVacuum(bed.Map) >= 0.5f)
            {
                return false;
            }

            if (!RestUtility.CanUseBedEver(sleeper, bed.def))
            {
                return false;
            }

            int? assignedSleepingSlot;
            bool isOwner = bed.IsOwner(sleeper, out assignedSleepingSlot);
            int? sleepingSlot;
            bool alreadyInBed = sleeper.CurrentBed(out sleepingSlot) == bed;
            if (!bed.AnyUnoccupiedSleepingSlot && !isOwner && !alreadyInBed)
            {
                return false;
            }

            GuestStatus? guestStatus = guestStatusOverride ?? sleeper.GuestStatus;
            bool forPrisoner = guestStatus == GuestStatus.Prisoner;
            bool forSlave = guestStatus == GuestStatus.Slave;
            if (checkSocialProperness && !bed.IsSociallyProper(sleeper, forPrisoner))
            {
                return false;
            }

            if (bed.ForPrisoners != forPrisoner)
            {
                return false;
            }

            if (!bed.ForSlaves && forSlave && partner.IsSlave)
            {
                return false;
            }

            if (bed.ForSlaves && !forSlave && !partner.IsSlave)
            {
                return false;
            }

            if (bed.ForPrisoners && !bed.Position.IsInPrisonCell(bed.Map))
            {
                return false;
            }

            if (bed.Medical)
            {
                if (!allowMedBedEvenIfSetToNoCare && !HealthAIUtility.ShouldEverReceiveMedicalCareFromPlayer(sleeper))
                {
                    return false;
                }

                if (!HealthAIUtility.ShouldSeekMedicalRest(sleeper))
                {
                    return false;
                }
            }
            else
            {
                if (!isOwner && !RestUtility.BedOwnerWillShare(bed, sleeper, guestStatusOverride))
                {
                    return false;
                }

                if (alreadyInBed && sleepingSlot != assignedSleepingSlot)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool CanProclaimSlaveMarriage(Pawn master, Pawn slave, out string reason)
        {
            if (!CanJoinSlaveMarriageCeremony(master, slave, out reason))
            {
                return false;
            }

            if (master.relations?.DirectRelationExists(PawnRelationDefOf.Spouse, slave) == true)
            {
                reason = "MUGB_SlaveMarriageAlreadyMarried".Translate(master.Named("MASTER"), slave.Named("SLAVE"));
                return false;
            }

            return true;
        }

        public static bool CanJoinSlaveMarriageCeremony(Pawn master, Pawn slave, out string reason)
        {
            reason = null;
            if (!CanActorUseSlaveMarriageCommand(master, out reason))
            {
                return false;
            }

            if (slave?.RaceProps?.Humanlike != true || slave.Dead)
            {
                reason = "MUGB_SlaveMarriageNeedLivingHumanlike".Translate();
                return false;
            }

            if (!slave.IsSlaveOfColony)
            {
                reason = "MUGB_SlaveMarriageNeedColonySlave".Translate(slave.Named("SLAVE"));
                return false;
            }

            if (!IsAdultForSlaveMarriage(slave))
            {
                reason = "MUGB_SlaveMarriageNeedAdult".Translate(slave.Named("PAWN"));
                return false;
            }

            if (slave.Downed || slave.InMentalState)
            {
                reason = "MUGB_SlaveMarriageUnavailableNow".Translate(slave.Named("PAWN"));
                return false;
            }

            bool alreadySpouse = master.relations?.DirectRelationExists(PawnRelationDefOf.Spouse, slave) == true;
            if (alreadySpouse && !IsSlaveMarriage(master, slave))
            {
                reason = "MUGB_SlaveMarriageAlreadyRegularSpouse".Translate(master.Named("MASTER"), slave.Named("SLAVE"));
                return false;
            }

            return CanUseSlaveMarriageRules(master, slave, GoblinUtility.IsGoblin(master), GoblinUtility.IsGoblin(slave), out reason);
        }

        public static bool CanNotifySlaveDivorce(Pawn master, Pawn slave, out string reason)
        {
            reason = null;
            if (!CanActorUseSlaveMarriageCommand(master, out reason))
            {
                return false;
            }

            if (slave?.RaceProps?.Humanlike != true || slave.Dead)
            {
                reason = "MUGB_SlaveMarriageTargetNotLivingHumanlike".Translate();
                return false;
            }

            if (!slave.IsSlaveOfColony)
            {
                reason = "MUGB_SlaveMarriageTargetNotColonySlave".Translate();
                return false;
            }

            if (master.relations?.DirectRelationExists(PawnRelationDefOf.Spouse, slave) != true || !IsSlaveMarriage(master, slave))
            {
                reason = "MUGB_SlaveMarriageNotSlaveMarriage".Translate();
                return false;
            }

            return true;
        }

        public static void ProclaimSlaveMarriage(Pawn master, Pawn slave)
        {
            if (!CanProclaimSlaveMarriage(master, slave, out string _))
            {
                return;
            }

            RegisterSlaveMarriagePair(master, slave, assignBed: true, showMessage: true);
        }

        public static bool RegisterSlaveMarriagePair(Pawn master, Pawn slave, bool assignBed, bool showMessage)
        {
            if (master == null || slave == null || IsSlaveMarriage(master, slave))
            {
                return false;
            }

            master.relations.TryRemoveDirectRelation(PawnRelationDefOf.ExSpouse, slave);
            master.relations.TryRemoveDirectRelation(PawnRelationDefOf.ExLover, slave);
            master.relations.TryRemoveDirectRelation(PawnRelationDefOf.Lover, slave);
            master.relations.TryRemoveDirectRelation(PawnRelationDefOf.Fiance, slave);
            master.relations.AddDirectRelation(PawnRelationDefOf.Spouse, slave);
            Current.Game?.GetComponent<GameComponent_GoblinSlaveMarriage>()?.Register(master, slave);
            if (assignBed)
            {
                LovePartnerRelationUtility.TryToShareBed(master, slave);
            }
            RecordSlaveMarriagePlayLog(master, slave);
            if (showMessage)
            {
                Messages.Message(
                    "MUGB_SlaveMarriageProclaimed".Translate(master.Named("MASTER"), slave.Named("SLAVE")),
                    new LookTargets(master, slave),
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
            }
            return true;
        }

        public static void NotifySlaveDivorce(Pawn master, Pawn slave)
        {
            if (!CanNotifySlaveDivorce(master, slave, out string _))
            {
                return;
            }

            Current.Game?.GetComponent<GameComponent_GoblinSlaveMarriage>()?.Unregister(master, slave);
            master.relations.TryRemoveDirectRelation(PawnRelationDefOf.Spouse, slave);
            RemoveMarriageMemoriesWithoutDivorceThought(master, slave);
            if (master.ownership?.OwnedBed != null && master.ownership.OwnedBed == slave.ownership?.OwnedBed)
            {
                slave.ownership.UnclaimBed();
            }
            if (slave.needs?.mood != null && MUGBDefOf.MUGB_RejectedByMaster != null)
            {
                slave.needs.mood.thoughts.memories.TryGainMemory(MUGBDefOf.MUGB_RejectedByMaster, master);
            }
        }

        public static bool TryGiveSlaveMarriageJob(Pawn master, Pawn slave, bool divorce)
        {
            JobDef jobDef = divorce ? MUGBDefOf.MUGB_NotifySlaveDivorce : MUGBDefOf.MUGB_ProclaimSlaveMarriage;
            if (jobDef == null || master == null || slave == null)
            {
                return false;
            }

            if (!slave.Downed && slave.Awake() == false && slave.jobs?.curDriver != null)
            {
                slave.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: true);
            }

            Job job = JobMaker.MakeJob(jobDef, slave);
            bool started = master.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            if (started)
            {
                PlayCommandAcceptedSound();
            }
            return started;
        }

        public static int AdjustSlaveMarriageLovinCooldown(Pawn pawn, int ticks)
        {
            Pawn partner = pawn?.CurJob?.GetTarget(TargetIndex.A).Pawn;
            if (partner != null && IsSlaveMarriage(pawn, partner))
            {
                return System.Math.Min(ticks, SlaveMarriageLovinCooldownTicks);
            }

            return ticks;
        }

        public static void TryAddSlaveMarriageLovinThought(Pawn pawn)
        {
            Pawn partner = pawn?.CurJob?.GetTarget(TargetIndex.A).Pawn;
            if (partner == null || !IsSlaveMarriage(pawn, partner))
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            string key = PairKey(pawn, partner);
            lastLovinThoughtTicksByPair[key] = now;
            if (lastLovinThoughtTicksByPair.Count > 128)
            {
                lastLovinThoughtTicksByPair.Clear();
            }
        }

        public static bool TryReplaceSlaveMarriageLovinThought(Pawn pawn, ThoughtDef thought, Pawn otherPawn)
        {
            if (pawn == null
                || otherPawn == null
                || thought?.defName != "GotSomeLovin"
                || !pawn.IsSlave
                || !IsSlaveMarriage(pawn, otherPawn))
            {
                return false;
            }

            if (!GoblinUtility.IsGoblin(pawn) && GoblinUtility.IsGoblin(otherPawn) && MUGBDefOf.MUGB_LovinWithGoblinSlaveSpouse != null)
            {
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGBDefOf.MUGB_LovinWithGoblinSlaveSpouse, otherPawn);
            }

            return true;
        }

        public static bool TryReplaceSlaveMarriageSpouseDeathThought(Pawn pawn, ThoughtDef thought, Pawn otherPawn)
        {
            if (pawn == null
                || thought == null
                || otherPawn == null
                || !SpouseDeathThoughtDefNames.Contains(thought.defName)
                || !IsSlaveMarriage(pawn, otherPawn))
            {
                return false;
            }

            if (pawn.IsSlave && MUGBDefOf.MUGB_SlaveSpouseDied != null)
            {
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGBDefOf.MUGB_SlaveSpouseDied, otherPawn);
            }
            else if (otherPawn.IsSlave && MUGBDefOf.MUGB_BrokenToySlaveSpouseDied != null)
            {
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(MUGBDefOf.MUGB_BrokenToySlaveSpouseDied, otherPawn);
            }

            return true;
        }

        public static float AdjustSlaveMarriageLovinMtb(Pawn pawn, Pawn partner, float hours)
        {
            if (hours > SlaveMarriageLovinMtbHours && IsSlaveMarriage(pawn, partner))
            {
                return SlaveMarriageLovinMtbHours;
            }

            return hours;
        }

        public static int NonSlaveMarriageSpouseCount(Pawn pawn, bool includeDead)
        {
            if (pawn?.RaceProps?.IsFlesh != true || pawn.relations == null)
            {
                return 0;
            }

            int count = 0;
            List<DirectPawnRelation> relations = pawn.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                Pawn other = relations[i].otherPawn;
                if (relations[i].def == PawnRelationDefOf.Spouse
                    && other != null
                    && (includeDead || !other.Dead)
                    && !IsSlaveMarriage(pawn, other))
                {
                    count++;
                }
            }

            return count;
        }

        public static DirectPawnRelation MostLikedNonSlaveMarriageSpouse(Pawn pawn)
        {
            if (pawn?.RaceProps?.IsFlesh != true || pawn.relations == null)
            {
                return null;
            }

            DirectPawnRelation best = null;
            int bestOpinion = int.MinValue;
            List<DirectPawnRelation> relations = pawn.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                Pawn other = relations[i].otherPawn;
                if (relations[i].def != PawnRelationDefOf.Spouse || other == null || other.Dead || IsSlaveMarriage(pawn, other))
                {
                    continue;
                }

                int opinion = pawn.relations.OpinionOf(other);
                if (best == null || opinion > bestOpinion)
                {
                    best = relations[i];
                    bestOpinion = opinion;
                }
            }

            return best;
        }

        public static bool HasNonSlaveMarriageLovePartner(Pawn pawn)
        {
            if (pawn?.relations == null)
            {
                return false;
            }

            List<DirectPawnRelation> relations = pawn.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                Pawn other = relation.otherPawn;
                if (other == null || other.Dead || IsSlaveMarriage(pawn, other))
                {
                    continue;
                }

                if (relation.def == PawnRelationDefOf.Spouse
                    || relation.def == PawnRelationDefOf.Lover
                    || relation.def == PawnRelationDefOf.Fiance)
                {
                    return true;
                }
            }

            return false;
        }

        public static void ChangeNonSlaveSpousesToExSpouses(Pawn pawn)
        {
            if (pawn?.relations == null)
            {
                return;
            }

            List<Pawn> spouses = pawn.GetSpouses(includeDead: true);
            for (int i = spouses.Count - 1; i >= 0; i--)
            {
                Pawn spouse = spouses[i];
                if (spouse == null || IsSlaveMarriage(pawn, spouse))
                {
                    continue;
                }

                HistoryEvent ev = new HistoryEvent(pawn.GetHistoryEventForSpouseCountPlusOne(), pawn.Named(HistoryEventArgsNames.Doer));
                if (spouse.Dead || !ev.DoerWillingToDo())
                {
                    pawn.relations.RemoveDirectRelation(PawnRelationDefOf.Spouse, spouse);
                    pawn.relations.AddDirectRelation(PawnRelationDefOf.ExSpouse, spouse);
                }
            }
        }

        public static void PlayCommandAcceptedSound()
        {
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        public static void PlayCommandCanceledSound()
        {
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        public static string OverrideLovinReport(JobDriver_Lovin driver)
        {
            Pawn pawn = driver?.pawn;
            Pawn partner = pawn?.CurJob?.GetTarget(TargetIndex.A).Pawn;
            if (pawn?.IsSlave == true && partner != null && IsSlaveMarriage(pawn, partner))
            {
                // ko-KR 의도: "주인님께 봉사하는 중."
                return "주인님께 봉사하는 중.";
            }

            return null;
        }

        public static string OverrideIngestReport(JobDriver driver)
        {
            Job job = driver?.job;
            if (driver?.pawn == null || job == null || job.def != JobDefOf.Ingest)
            {
                return null;
            }

            Thing targetThing = job.GetTarget(TargetIndex.A).Thing;
            if (targetThing is Building_GoblinStewpot || targetThing?.def == MUGBDefOf.MUGB_gutstew)
            {
                return "MUGB_ConsumingGutStew".Translate();
            }

            Thing targetBThing = job.GetTarget(TargetIndex.B).Thing;
            if (targetBThing is Building_GoblinStewpot || targetBThing?.def == MUGBDefOf.MUGB_gutstew)
            {
                return "MUGB_ConsumingGutStew".Translate();
            }

            return null;
        }

        private static bool CanActorUseSlaveMarriageCommand(Pawn pawn, out string reason)
        {
            reason = null;
            if (pawn?.RaceProps?.Humanlike != true || pawn.Dead)
            {
                reason = "MUGB_SlaveMarriageActorNotLivingHumanlike".Translate();
                return false;
            }

            if (!pawn.IsColonistPlayerControlled || pawn.IsSlave || pawn.IsPrisoner)
            {
                reason = "MUGB_SlaveMarriageActorMustBeFreeColonist".Translate();
                return false;
            }

            if (!IsAdultForSlaveMarriage(pawn))
            {
                reason = "MUGB_SlaveMarriageNeedAdult".Translate(pawn.Named("PAWN"));
                return false;
            }

            if (pawn.Downed || pawn.InMentalState || pawn.Drafted)
            {
                reason = "MUGB_SlaveMarriageActorUnavailable".Translate();
                return false;
            }

            return true;
        }

        private static bool CanUseSlaveMarriageRules(Pawn master, Pawn slave, bool masterIsGoblin, bool slaveIsGoblin, out string reason)
        {
            reason = null;
            bool pheromonePreferenceRequired = MUGBMod.Settings?.requireSlaveMarriagePheromonePreference ?? true;

            // Goblin-side rules always stay available, regardless of Ideology setup.
            if (masterIsGoblin || slaveIsGoblin)
            {
                if (masterIsGoblin && slaveIsGoblin)
                {
                    return true;
                }

                if (masterIsGoblin)
                {
                    if (pheromonePreferenceRequired && !GoblinPheromonePreferenceUtility.HasPreference(slave))
                    {
                        reason = "MUGB_SlaveMarriageNeedPheromonePreference".Translate(slave.Named("PAWN"));
                        return false;
                    }

                    return true;
                }

                if (pheromonePreferenceRequired && !GoblinPheromonePreferenceUtility.HasPreference(master))
                {
                    reason = "MUGB_SlaveMarriageNeedPheromonePreference".Translate(master.Named("PAWN"));
                    return false;
                }

                return true;
            }

            // Without Ideology, keep the old MUGB flow:
            // only goblin colonists can proclaim slave marriage.
            if (!ModsConfig.IdeologyActive)
            {
                reason = "MUGB_SlaveMarriageRequiresGoblin".Translate();
                return false;
            }

            // With Ideology active, non-goblin slave marriage is allowed only
            // when the acting colonist's ideoligion approves of slavery.
            if (!IdeologyAllowsSlaveMarriage(master?.Ideo))
            {
                reason = "MUGB_SlaveMarriageIdeologyRequired".Translate(master.Named("MASTER"));
                return false;
            }

            return true;
        }

        public static bool IdeologyAllowsSlaveMarriage(Ideo ideo)
        {
            if (!ModsConfig.IdeologyActive || ideo == null || !ideo.IdeoApprovesOfSlavery())
            {
                return false;
            }

            string[] allowedPrecepts =
            {
                "MUGB_SlaveMarriage_Acceptable",
                "MUGB_SlaveMarriage_Preferred",
                "MUGB_SlaveMarriage_Important"
            };
            for (int i = 0; i < allowedPrecepts.Length; i++)
            {
                PreceptDef precept = DefDatabase<PreceptDef>.GetNamedSilentFail(allowedPrecepts[i]);
                if (precept != null && ideo.HasPrecept(precept))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveMarriageMemoriesWithoutDivorceThought(Pawn first, Pawn second)
        {
            first.needs?.mood?.thoughts?.memories?.RemoveMemoriesOfDef(ThoughtDefOf.GotMarried);
            first.needs?.mood?.thoughts?.memories?.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.HoneymoonPhase, second);
            second.needs?.mood?.thoughts?.memories?.RemoveMemoriesOfDef(ThoughtDefOf.GotMarried);
            second.needs?.mood?.thoughts?.memories?.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.HoneymoonPhase, first);
        }

        private static string PairKey(Pawn first, Pawn second)
        {
            int a = first.thingIDNumber;
            int b = second.thingIDNumber;
            return a < b ? $"{a}:{b}" : $"{b}:{a}";
        }

        private static void RecordSlaveMarriagePlayLog(Pawn master, Pawn slave)
        {
            if (master == null || slave == null || MUGBDefOf.MUGB_SlaveMarriageProclamation == null || Find.PlayLog == null)
            {
                return;
            }

            Find.PlayLog.Add(new PlayLogEntry_Interaction(MUGBDefOf.MUGB_SlaveMarriageProclamation, master, slave, null));
        }
    }

    public class ThoughtWorker_SpecialSlaveMarriageMood : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || !p.IsSlave || !GoblinSlaveMarriageUtility.HasAnySlaveMarriage(p))
            {
                return ThoughtState.Inactive;
            }

            return GoblinSlaveMarriageUtility.HasSlaveMarriageWithGoblinMaster(p)
                ? ThoughtState.ActiveAtStage(0)
                : ThoughtState.ActiveAtStage(1);
        }
    }

    public class ThoughtWorker_ViewedAsSpecialSlave : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (p == null || other == null || p == other || p.Dead || other.Dead)
            {
                return ThoughtState.Inactive;
            }

            if (other.Faction != p.Faction || !other.IsSlave || !GoblinSlaveMarriageUtility.HasAnySlaveMarriage(other))
            {
                return ThoughtState.Inactive;
            }

            if (GoblinSlaveMarriageUtility.IsSlaveMarriage(p, other))
            {
                return ThoughtState.Inactive;
            }

            return ThoughtState.ActiveDefault;
        }
    }

    public class ThoughtWorker_OpinionOfGoblin : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (p == null || other == null || p == other || p.Dead || other.Dead)
            {
                return ThoughtState.Inactive;
            }

            if (!p.RaceProps.Humanlike || GoblinUtility.IsGoblin(p) || !GoblinUtility.IsGoblin(other))
            {
                return ThoughtState.Inactive;
            }

            if (GoblinPheromonePreferenceUtility.HasPreference(p))
            {
                return ThoughtState.Inactive;
            }

            return ThoughtState.ActiveDefault;
        }
    }

    public class ThoughtWorker_GoblinSupremacyNonGoblinOpinion : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (!ModsConfig.IdeologyActive || p == null || other == null || p == other || p.Dead || other.Dead)
            {
                return ThoughtState.Inactive;
            }

            if (p.RaceProps?.Humanlike != true || other.RaceProps?.Humanlike != true)
            {
                return ThoughtState.Inactive;
            }

            if (MUGBDefOf.MUGB_GoblinSupremacy == null || p.Ideo?.HasMeme(MUGBDefOf.MUGB_GoblinSupremacy) != true)
            {
                return ThoughtState.Inactive;
            }

            if (GoblinUtility.HasGoblinCoreMarker(other))
            {
                return ThoughtState.Inactive;
            }

            if (GoblinUtility.HasHalfGoblinAncestry(other))
            {
                return ThoughtState.ActiveAtStage(0);
            }

            return ThoughtState.ActiveAtStage(1);
        }
    }

    public class ThoughtWorker_GoblinSlaveSpouseOpinion : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (p == null || other == null || p == other || p.Dead || other.Dead)
            {
                return ThoughtState.Inactive;
            }

            if (GoblinUtility.IsGoblin(p) || !GoblinUtility.IsGoblin(other))
            {
                return ThoughtState.Inactive;
            }

            if (!p.IsSlave || !GoblinSlaveMarriageUtility.IsSlaveMarriage(p, other))
            {
                return ThoughtState.Inactive;
            }

            if (p.relations?.DirectRelationExists(PawnRelationDefOf.Spouse, other) != true)
            {
                return ThoughtState.Inactive;
            }

            return ThoughtState.ActiveDefault;
        }
    }

    public class ThoughtWorker_NonGoblinSlaveSpouseOpinion : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (p?.IsSlave != true || other == null || p == other || p.Dead || other.Dead)
            {
                return ThoughtState.Inactive;
            }

            return !GoblinUtility.IsGoblin(other)
                && GoblinSlaveMarriageUtility.IsSlaveMarriage(p, other)
                && p.relations?.DirectRelationExists(PawnRelationDefOf.Spouse, other) == true
                    ? ThoughtState.ActiveDefault
                    : ThoughtState.Inactive;
        }
    }

    public class ThoughtWorker_RegularSpouseHasConcubineMood : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p?.relations?.DirectRelations == null || !GoblinSlaveMarriageUtility.IsMonogamousByIdeology(p))
            {
                return ThoughtState.Inactive;
            }

            foreach (DirectPawnRelation relation in p.relations.DirectRelations)
            {
                Pawn spouse = relation.otherPawn;
                if (relation.def == PawnRelationDefOf.Spouse
                    && spouse != null
                    && !spouse.Dead
                    && !GoblinSlaveMarriageUtility.IsSlaveMarriage(p, spouse)
                    && GoblinSlaveMarriageUtility.HasLivingSlaveConcubine(spouse))
                {
                    return ThoughtState.ActiveDefault;
                }
            }

            return ThoughtState.Inactive;
        }
    }

    public class ThoughtWorker_RegularSpouseTookConcubineOpinion : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (p == null || other == null || p == other || p.Dead || other.Dead
                || !GoblinSlaveMarriageUtility.IsMonogamousByIdeology(p))
            {
                return ThoughtState.Inactive;
            }

            bool regularSpouses = p.relations?.DirectRelationExists(PawnRelationDefOf.Spouse, other) == true
                && !GoblinSlaveMarriageUtility.IsSlaveMarriage(p, other);
            return regularSpouses && GoblinSlaveMarriageUtility.HasLivingSlaveConcubine(other)
                ? ThoughtState.ActiveDefault
                : ThoughtState.Inactive;
        }
    }

    public class ThoughtWorker_OpinionOfSpousesConcubine : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (p?.relations?.DirectRelations == null || other?.IsSlave != true || p == other || p.Dead || other.Dead
                || !GoblinSlaveMarriageUtility.IsMonogamousByIdeology(p))
            {
                return ThoughtState.Inactive;
            }

            foreach (DirectPawnRelation relation in p.relations.DirectRelations)
            {
                Pawn spouse = relation.otherPawn;
                if (relation.def == PawnRelationDefOf.Spouse
                    && spouse != null
                    && !spouse.Dead
                    && !GoblinSlaveMarriageUtility.IsSlaveMarriage(p, spouse)
                    && GoblinSlaveMarriageUtility.IsSlaveMarriage(spouse, other))
                {
                    return ThoughtState.ActiveDefault;
                }
            }

            return ThoughtState.Inactive;
        }
    }

    public class InteractionWorker_SlaveMarriageProclamation : InteractionWorker
    {
    }

    public class JobDriver_ProclaimSlaveMarriage : JobDriver
    {
        private Pawn Slave => job.GetTarget(TargetIndex.A).Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Slave, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !GoblinSlaveMarriageUtility.CanProclaimSlaveMarriage(pawn, Slave, out string _));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch).FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_General.WaitWith(TargetIndex.A, GoblinSlaveMarriageUtility.JobInteractionTicks, useProgressBar: true);
            yield return Toils_General.Do(delegate
            {
                GoblinSlaveMarriageUtility.ProclaimSlaveMarriage(pawn, Slave);
            });
        }
    }

    public class JobDriver_NotifySlaveDivorce : JobDriver
    {
        private Pawn Slave => job.GetTarget(TargetIndex.A).Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Slave, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !GoblinSlaveMarriageUtility.CanNotifySlaveDivorce(pawn, Slave, out string _));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch).FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_General.WaitWith(TargetIndex.A, GoblinSlaveMarriageUtility.JobInteractionTicks, useProgressBar: true);
            yield return Toils_General.Do(delegate
            {
                GoblinSlaveMarriageUtility.NotifySlaveDivorce(pawn, Slave);
            });
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetFloatMenuOptions))]
    public static class Pawn_GetFloatMenuOptions_GoblinSlaveMarriagePatch
    {
        public static void Postfix(Pawn __instance, Pawn selPawn, ref IEnumerable<FloatMenuOption> __result)
        {
            if (__instance == null || selPawn == null || __instance == selPawn)
            {
                return;
            }

            List<FloatMenuOption> options = __result?.ToList() ?? new List<FloatMenuOption>();
            if (GoblinSlaveMarriageUtility.CanProclaimSlaveMarriage(selPawn, __instance, out string marriageReason))
            {
                Pawn target = __instance;
                options.Add(new FloatMenuOption("MUGB_TakeAsSlaveSpouse".Translate(target.LabelShortCap), delegate
                {
                    GoblinSlaveMarriageUtility.TryGiveSlaveMarriageJob(selPawn, target, divorce: false);
                }));
            }
            else if (!marriageReason.NullOrEmpty() && __instance.IsSlaveOfColony && (GoblinUtility.IsGoblin(selPawn) || GoblinUtility.IsGoblin(__instance)))
            {
                options.Add(new FloatMenuOption("MUGB_CannotTakeAsSlaveSpouse".Translate(marriageReason), null));
            }

            if (GoblinSlaveMarriageUtility.CanNotifySlaveDivorce(selPawn, __instance, out string divorceReason))
            {
                Pawn target = __instance;
                options.Add(new FloatMenuOption("MUGB_InformSlaveDivorce".Translate(target.LabelShortCap), delegate
                {
                    GoblinSlaveMarriageUtility.TryGiveSlaveMarriageJob(selPawn, target, divorce: true);
                }));
            }
            else if (!divorceReason.NullOrEmpty() && GoblinSlaveMarriageUtility.IsSlaveMarriage(selPawn, __instance))
            {
                options.Add(new FloatMenuOption("MUGB_CannotInformSlaveDivorce".Translate(divorceReason), null));
            }

            __result = options;
        }
    }

    [HarmonyPatch(typeof(InteractionWorker_Breakup), nameof(InteractionWorker_Breakup.RandomSelectionWeight))]
    public static class InteractionWorker_Breakup_GoblinSlaveMarriagePatch
    {
        public static void Postfix(Pawn initiator, Pawn recipient, ref float __result)
        {
            if (__result > 0f && GoblinSlaveMarriageUtility.IsSlaveMarriage(initiator, recipient))
            {
                __result = 0f;
            }
        }
    }

    [HarmonyPatch(typeof(SpouseRelationUtility), nameof(SpouseRelationUtility.DoDivorce))]
    public static class SpouseRelationUtility_DoDivorce_GoblinSlaveMarriagePatch
    {
        public static bool Prefix(Pawn initiator, Pawn recipient)
        {
            return !GoblinSlaveMarriageUtility.IsSlaveMarriage(initiator, recipient);
        }
    }

    [HarmonyPatch(typeof(MarriageCeremonyUtility), nameof(MarriageCeremonyUtility.Married))]
    public static class MarriageCeremonyUtility_Married_FreeMarriageThoughtPatch
    {
        public static void Postfix(Pawn firstPawn, Pawn secondPawn)
        {
            GoblinSlaveMarriageUtility.TryGiveFreeMarriageUnderSlaveMarriageIdeoThought(firstPawn, secondPawn);
            GoblinSlaveMarriageUtility.TryGiveFreeMarriageUnderSlaveMarriageIdeoThought(secondPawn, firstPawn);
        }
    }

    [HarmonyPatch(typeof(SpouseRelationUtility), nameof(SpouseRelationUtility.GetSpouseCount))]
    public static class SpouseRelationUtility_GetSpouseCount_GoblinSlaveMarriagePatch
    {
        public static void Postfix(Pawn pawn, bool includeDead, ref int __result)
        {
            __result = GoblinSlaveMarriageUtility.NonSlaveMarriageSpouseCount(pawn, includeDead);
        }
    }

    [HarmonyPatch(typeof(SpouseRelationUtility), nameof(SpouseRelationUtility.GetMostLikedSpouseRelation))]
    public static class SpouseRelationUtility_GetMostLikedSpouseRelation_GoblinSlaveMarriagePatch
    {
        public static void Postfix(Pawn pawn, ref DirectPawnRelation __result)
        {
            if (__result != null && GoblinSlaveMarriageUtility.IsSlaveMarriage(pawn, __result.otherPawn))
            {
                __result = GoblinSlaveMarriageUtility.MostLikedNonSlaveMarriageSpouse(pawn);
            }
        }
    }

    [HarmonyPatch(typeof(LovePartnerRelationUtility), nameof(LovePartnerRelationUtility.ChangeSpouseRelationsToExSpouse))]
    public static class LovePartnerRelationUtility_ChangeSpouseRelationsToExSpouse_GoblinSlaveMarriagePatch
    {
        public static bool Prefix(Pawn pawn)
        {
            GoblinSlaveMarriageUtility.ChangeNonSlaveSpousesToExSpouses(pawn);
            return false;
        }
    }

    [HarmonyPatch(typeof(LovePartnerRelationUtility), nameof(LovePartnerRelationUtility.GetLovinMtbHours))]
    public static class LovePartnerRelationUtility_GetLovinMtbHours_GoblinSlaveMarriagePatch
    {
        public static void Postfix(Pawn pawn, Pawn partner, ref float __result)
        {
            if (__result > 0f)
            {
                __result = GoblinSlaveMarriageUtility.AdjustSlaveMarriageLovinMtb(pawn, partner, __result);
            }
        }
    }

    public static class GoblinRomanceAgeUtility
    {
        private const float VanillaRomanceMinAge = 16f;
        private const float HumanEquivalentAdultAge = 18f;

        public static bool UsesGoblinRomanceAge(Pawn pawn)
        {
            return GoblinUtility.IsGoblin(pawn);
        }

        public static bool IsRomanceAdult(Pawn pawn)
        {
            if (pawn?.ageTracker == null)
            {
                return false;
            }

            return pawn.ageTracker.AgeBiologicalYearsFloat >= VanillaRomanceMinAge;
        }

        public static float HumanEquivalentAge(Pawn pawn)
        {
            float age = pawn?.ageTracker?.AgeBiologicalYearsFloat ?? 0f;
            if (GoblinUtility.IsGoblin(pawn))
            {
                if (age <= GoblinAgeUtility.AdultAgeYears)
                {
                    return age;
                }

                if (age <= GoblinAgeUtility.ElderAgeYears)
                {
                    return GenMath.LerpDouble(
                        GoblinAgeUtility.AdultAgeYears,
                        GoblinAgeUtility.ElderAgeYears,
                        HumanEquivalentAdultAge,
                        60f,
                        age);
                }

                return GenMath.LerpDouble(
                    GoblinAgeUtility.ElderAgeYears,
                    GoblinAgeUtility.LifeExpectancyYears,
                    60f,
                    80f,
                    age);
            }

            return age;
        }

        public static float LovinAgeFactor(Pawn pawn, Pawn otherPawn)
        {
            float pawnAge = HumanEquivalentAge(pawn);
            float otherAge = HumanEquivalentAge(otherPawn);
            float ageGapFactor = 1f;
            if (pawn.gender == Gender.Male)
            {
                ageGapFactor = GenMath.FlatHill(0.2f, pawnAge - 30f, pawnAge - 10f, pawnAge + 3f, pawnAge + 10f, 0.2f, otherAge);
            }
            else if (pawn.gender == Gender.Female)
            {
                ageGapFactor = GenMath.FlatHill(0.2f, pawnAge - 10f, pawnAge - 3f, pawnAge + 10f, pawnAge + 30f, 0.2f, otherAge);
            }

            return ageGapFactor
                * Mathf.InverseLerp(VanillaRomanceMinAge, HumanEquivalentAdultAge, pawnAge)
                * Mathf.InverseLerp(VanillaRomanceMinAge, HumanEquivalentAdultAge, otherAge);
        }

        public static float LovinMtbSinglePawnFactor(Pawn pawn)
        {
            float factor = 1f;
            factor /= 1f - pawn.health.hediffSet.PainTotal;
            float consciousness = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
            if (consciousness < 0.5f)
            {
                factor /= consciousness * 2f;
            }

            float equivalentAge = HumanEquivalentAge(pawn);
            return factor / GenMath.FlatHill(0f, 14f, 16f, 25f, 80f, 0.2f, equivalentAge);
        }
    }

    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.SecondaryLovinChanceFactor))]
    public static class PawnRelationsTracker_SecondaryLovinChanceFactor_GoblinAgePatch
    {
        public static bool Prefix(Pawn_RelationsTracker __instance, Pawn ___pawn, Pawn otherPawn, ref float __result)
        {
            if (!GoblinRomanceAgeUtility.UsesGoblinRomanceAge(___pawn)
                && !GoblinRomanceAgeUtility.UsesGoblinRomanceAge(otherPawn))
            {
                return true;
            }

            if (___pawn?.def != otherPawn?.def || ___pawn == otherPawn
                || !GoblinRomanceAgeUtility.IsRomanceAdult(___pawn)
                || !GoblinRomanceAgeUtility.IsRomanceAdult(otherPawn))
            {
                __result = 0f;
                return false;
            }

            if (___pawn.story?.traits != null)
            {
                if (___pawn.story.traits.HasTrait(TraitDefOf.Asexual))
                {
                    __result = 0f;
                    return false;
                }

                if (!___pawn.story.traits.HasTrait(TraitDefOf.Bisexual))
                {
                    if (___pawn.story.traits.HasTrait(TraitDefOf.Gay))
                    {
                        if (otherPawn.gender != ___pawn.gender)
                        {
                            __result = 0f;
                            return false;
                        }
                    }
                    else if (otherPawn.gender == ___pawn.gender)
                    {
                        __result = 0f;
                        return false;
                    }
                }
            }

            __result = GoblinRomanceAgeUtility.LovinAgeFactor(___pawn, otherPawn)
                * __instance.PrettinessFactor(otherPawn);
            return false;
        }
    }

    [HarmonyPatch(typeof(LovePartnerRelationUtility), nameof(LovePartnerRelationUtility.GetLovinMtbHours))]
    public static class LovePartnerRelationUtility_GetLovinMtbHours_GoblinAgePatch
    {
        public static bool Prefix(Pawn pawn, Pawn partner, ref float __result)
        {
            if (!GoblinRomanceAgeUtility.UsesGoblinRomanceAge(pawn)
                && !GoblinRomanceAgeUtility.UsesGoblinRomanceAge(partner))
            {
                return true;
            }

            if (pawn == null || partner == null || pawn.Dead || partner.Dead)
            {
                __result = -1f;
                return false;
            }

            if (DebugSettings.alwaysDoLovin)
            {
                __result = 0.1f;
                return false;
            }

            if (!GoblinRomanceAgeUtility.IsRomanceAdult(pawn)
                || !GoblinRomanceAgeUtility.IsRomanceAdult(partner)
                || (pawn.needs?.food?.Starving).GetValueOrDefault()
                || (partner.needs?.food?.Starving).GetValueOrDefault()
                || pawn.health.hediffSet.BleedRateTotal > 0f
                || partner.health.hediffSet.BleedRateTotal > 0f
                || pawn.health.hediffSet.InLabor()
                || partner.health.hediffSet.InLabor())
            {
                __result = -1f;
                return false;
            }

            float pawnFactor = GoblinRomanceAgeUtility.LovinMtbSinglePawnFactor(pawn);
            float partnerFactor = GoblinRomanceAgeUtility.LovinMtbSinglePawnFactor(partner);
            if (pawnFactor <= 0f || partnerFactor <= 0f)
            {
                __result = -1f;
                return false;
            }

            float result = 12f * pawnFactor * partnerFactor;
            result /= Mathf.Max(pawn.relations.SecondaryLovinChanceFactor(partner), 0.1f);
            result /= Mathf.Max(partner.relations.SecondaryLovinChanceFactor(pawn), 0.1f);
            result *= GenMath.LerpDouble(-100f, 100f, 1.3f, 0.7f, pawn.relations.OpinionOf(partner));
            result *= GenMath.LerpDouble(-100f, 100f, 1.3f, 0.7f, partner.relations.OpinionOf(pawn));
            if (pawn.health.hediffSet.HasHediff(HediffDefOf.PsychicLove))
            {
                result /= 4f;
            }

            __result = result;
            return false;
        }
    }

    [HarmonyPatch(typeof(BedUtility), nameof(BedUtility.WillingToShareBed))]
    public static class BedUtility_WillingToShareBed_GoblinSlaveMarriagePatch
    {
        public static void Postfix(Pawn pawn1, Pawn pawn2, ref bool __result)
        {
            if (!__result && GoblinSlaveMarriageUtility.IsSlaveMarriage(pawn1, pawn2))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(CompAssignableToPawn_Bed), nameof(CompAssignableToPawn_Bed.AssigningCandidates), MethodType.Getter)]
    public static class CompAssignableToPawn_Bed_AssigningCandidates_GoblinSlaveMarriagePatch
    {
        public static void Postfix(CompAssignableToPawn_Bed __instance, ref IEnumerable<Pawn> __result)
        {
            Building_Bed bed = __instance?.parent as Building_Bed;
            if (bed?.Spawned != true
                || !bed.def.building.bed_humanlike
                || bed.Faction != Faction.OfPlayer
                || bed.ForPrisoners)
            {
                return;
            }

            List<Pawn> candidates = __result?.ToList() ?? new List<Pawn>();
            List<Pawn> slaves = bed.Map.mapPawns.SlavesOfColonySpawned;
            for (int i = 0; i < slaves.Count; i++)
            {
                Pawn slave = slaves[i];
                if (!candidates.Contains(slave)
                    && GoblinSlaveMarriageUtility.CanCrossAssignSlaveMarriageBed(__instance, slave))
                {
                    candidates.Add(slave);
                }
            }

            __result = candidates;
        }
    }

    [HarmonyPatch(typeof(CompAssignableToPawn_Bed), nameof(CompAssignableToPawn_Bed.CanAssignTo))]
    public static class CompAssignableToPawn_Bed_CanAssignTo_GoblinSlaveMarriagePatch
    {
        public static void Postfix(CompAssignableToPawn_Bed __instance, Pawn pawn, ref AcceptanceReport __result)
        {
            if (!__result.Accepted && GoblinSlaveMarriageUtility.CanCrossAssignSlaveMarriageBed(__instance, pawn))
            {
                __result = AcceptanceReport.WasAccepted;
            }
        }
    }

    [HarmonyPatch(typeof(CompAssignableToPawn_Bed), nameof(CompAssignableToPawn_Bed.IdeoligionForbids))]
    public static class CompAssignableToPawn_Bed_IdeoligionForbids_GoblinSlaveMarriagePatch
    {
        public static void Postfix(CompAssignableToPawn_Bed __instance, Pawn pawn, ref bool __result)
        {
            Building_Bed bed = __instance?.parent as Building_Bed;
            if (!__result || bed == null || pawn == null)
            {
                return;
            }

            if (GoblinSlaveMarriageUtility.TryGetSlaveMarriageBedPartner(bed, pawn, out Pawn _))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(RestUtility), nameof(RestUtility.CanUseBedNow))]
    public static class RestUtility_CanUseBedNow_GoblinSlaveMarriagePatch
    {
        public static void Postfix(Thing bedThing, Pawn sleeper, bool checkSocialProperness, bool allowMedBedEvenIfSetToNoCare, GuestStatus? guestStatusOverride, ref bool __result)
        {
            if (!__result && bedThing is Building_Bed bed && GoblinSlaveMarriageUtility.CanUseSlaveMarriageBedNow(bed, sleeper, checkSocialProperness, allowMedBedEvenIfSetToNoCare, guestStatusOverride))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(JobDriver_Lovin), "GenerateRandomMinTicksToNextLovin")]
    public static class JobDriver_Lovin_GenerateRandomMinTicksToNextLovin_GoblinSlaveMarriagePatch
    {
        public static void Postfix(JobDriver_Lovin __instance, Pawn pawn, ref int __result)
        {
            GoblinSlaveMarriageUtility.TryAddSlaveMarriageLovinThought(pawn);
            __result = GoblinSlaveMarriageUtility.AdjustSlaveMarriageLovinCooldown(pawn, __result);
        }
    }

    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.GetReport))]
    public static class JobDriver_GetReport_MUGBOverridePatch
    {
        public static void Postfix(JobDriver __instance, ref string __result)
        {
            if (__instance is JobDriver_Lovin lovin)
            {
                string lovinReport = GoblinSlaveMarriageUtility.OverrideLovinReport(lovin);
                if (!lovinReport.NullOrEmpty())
                {
                    __result = lovinReport;
                    return;
                }
            }

            if (__instance is JobDriver_Ingest ingest)
            {
                string ingestReport = GoblinSlaveMarriageUtility.OverrideIngestReport(ingest);
                if (!ingestReport.NullOrEmpty())
                {
                    __result = ingestReport;
                }
            }
        }
    }

    [HarmonyPatch(typeof(ThoughtWorker_OpinionOfMyLover), "CurrentStateInternal")]
    public static class ThoughtWorker_OpinionOfMyLover_GoblinSlaveMarriagePatch
    {
        public static void Postfix(Pawn p, ref ThoughtState __result)
        {
            if (__result.Active && !GoblinSlaveMarriageUtility.HasNonSlaveMarriageLovePartner(p))
            {
                __result = ThoughtState.Inactive;
            }
        }
    }

    [HarmonyPatch(typeof(SocialCardUtility), "GetRelationsString")]
    public static class SocialCardUtility_GetRelationsString_GoblinSlaveMarriagePatch
    {
        private static readonly FieldInfo OtherPawnField = AccessTools.Field("RimWorld.SocialCardUtility+CachedSocialTabEntry:otherPawn");

        public static void Postfix(object entry, Pawn selPawnForSocialInfo, ref string __result)
        {
            Pawn other = OtherPawnField?.GetValue(entry) as Pawn;
            Pawn selPawn = selPawnForSocialInfo;
            if (selPawn == null || other == null || !GoblinSlaveMarriageUtility.IsSlaveMarriage(selPawn, other))
            {
                return;
            }

            if (selPawn.IsSlave && !other.IsSlave)
            {
                __result = "MUGB_SlaveMarriageRelationMaster".Translate();
            }
            else if (!selPawn.IsSlave && other.IsSlave)
            {
                __result = "MUGB_SlaveMarriageRelationConcubine".Translate();
            }
            else
            {
                __result = "MUGB_SlaveMarriageRelationGeneric".Translate();
            }
        }
    }
}
