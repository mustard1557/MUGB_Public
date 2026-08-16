using RimWorld;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MUGB
{
    public enum GoblinBeaconSignalQuality
    {
        Poor = 1,
        Normal = 2,
        Good = 3,
        Excellent = 4
    }

    public class Building_GoblinBeacon : Building_Casket, IThingGlower, INotifyHauledTo
    {
        private const int BurnTicksTotal = 1750;
        private const int MinEventDelayTicks = 7500;
        private const int MaxEventDelayTicks = 60000;
        private const int MinCompositeFollowupDelayTicks = 2500;
        private const int MaxCompositeFollowupDelayTicks = 7500;
        private const string EmptyGraphicPath = "Things/Building/Production/MGB_goblinbeacon/MGB_goblinbeacon_empty";
        private const string LoadedGraphicPath = "Things/Building/Production/MGB_goblinbeacon/MGB_goblinbeacon_loaded";

        private int burnTicksLeft;
        private bool wantsCorpseLoaded;
        private bool ritualSignalPending;
        private Graphic emptyGraphic;
        private Graphic loadedGraphic;
        private Graphic fireGraphic;

        private bool Burning => burnTicksLeft > 0;
        private bool HasGoblinCorpse => ContainedThing is Corpse corpse && GoblinUtility.IsGoblin(corpse.InnerPawn);
        public bool WantsCorpseLoaded => wantsCorpseLoaded && !Burning && !HasGoblinCorpse && Spawned;

        // 한국어 의도: 봉화 의식은 고블린 시체가 들어 있고 아직 타지 않는 봉화만 대상으로 잡아야 한다.
        // 비어 있거나 이미 점화된 봉화를 의식 목록에 보여 주지 않아, 의식 완료 후 아무 일도 일어나지 않는 흐름을 막는다.
        public bool CanStartSignalRitual(out string failReason)
        {
            if (Burning)
            {
                failReason = "MUGB_GoblinBeaconRitualAlreadyBurning".Translate();
                return false;
            }

            if (!HasGoblinCorpse)
            {
                failReason = "MUGB_GoblinBeaconNeedCorpse".Translate();
                return false;
            }

            failReason = null;
            return true;
        }

        public bool ShouldBeLitNow()
        {
            return Burning;
        }

        public override Graphic Graphic
        {
            get
            {
                if (HasGoblinCorpse || Burning)
                {
                    loadedGraphic ??= GraphicDatabase.Get<Graphic_Single>(LoadedGraphicPath, ShaderDatabase.CutoutComplex, def.graphicData.drawSize, DrawColor);
                    return loadedGraphic;
                }

                emptyGraphic ??= GraphicDatabase.Get<Graphic_Single>(EmptyGraphicPath, ShaderDatabase.CutoutComplex, def.graphicData.drawSize, DrawColor);
                return emptyGraphic;
            }
        }

        public override bool CanOpen => !Burning && HasAnyContents;

        public override bool Accepts(Thing thing)
        {
            if (Burning || HasAnyContents)
            {
                return false;
            }

            return thing is Corpse corpse && GoblinUtility.IsGoblin(corpse.InnerPawn);
        }

        public override bool TryAcceptThing(Thing thing, bool allowSpecialEffects = true)
        {
            bool accepted = base.TryAcceptThing(thing, allowSpecialEffects);
            if (accepted)
            {
                NotifyContentsChanged();
            }

            return accepted;
        }

        public void Notify_HauledTo(Pawn hauler, Thing thing, int count)
        {
            NotifyContentsChanged();
        }

        private void NotifyContentsChanged()
        {
            wantsCorpseLoaded = false;
            if (Spawned)
            {
                DirtyMapMesh(Map);
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Burning)
            {
                return;
            }

            burnTicksLeft--;
            if (Spawned && burnTicksLeft % 12 == 0)
            {
                FleckMaker.ThrowFireGlow(DrawPos + new Vector3(0f, 0f, 0.05f), Map, Rand.Range(0.25f, 0.45f));
                if (burnTicksLeft % 24 == 0)
                {
                    FleckMaker.ThrowSmoke(DrawPos + new Vector3(Rand.Range(-0.35f, 0.35f), 0f, Rand.Range(-0.05f, 0.35f)), Map, Rand.Range(1.6f, 2.2f));
                }
            }

            if (burnTicksLeft <= 0)
            {
                if (ritualSignalPending)
                {
                    // Keep the final flame alive until the ritual outcome has safely queued its incident.
                    burnTicksLeft = 1;
                    return;
                }

                Messages.Message("MUGB_GoblinBeaconBurnedOut".Translate(), this, MessageTypeDefOf.NeutralEvent, historical: false);
                Destroy(DestroyMode.Vanish);
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            if (!Burning)
            {
                return;
            }

            fireGraphic ??= GraphicDatabase.Get<Graphic_GoblinFixedSizeFlicker>("Things/Special/Fire", ShaderDatabase.TransparentPostLight, new Vector2(2f, 2f), new Color(1f, 0.72f, 0.42f, 0.78f));
            Vector3 pos = drawLoc + new Vector3(0f, 0.05f, 0.08f);
            pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            fireGraphic.Draw(pos, Rot4.North, this);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                if (gizmo is Command_Action action && action.defaultLabel == "DEV: Open")
                {
                    continue;
                }

                yield return gizmo;
            }

            if (!Burning && !HasGoblinCorpse)
            {
                Command_Action load = new Command_Action
                {
                    defaultLabel = (wantsCorpseLoaded ? "MUGB_GoblinBeaconCancelLoad" : "MUGB_GoblinBeaconLoadCorpse").Translate(),
                    defaultDesc = "MUGB_GoblinBeaconLoadCorpseDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get(wantsCorpseLoaded ? "UI/Icons/MGB_LoadGcorpse_on" : "UI/Icons/MGB_LoadGcorpse_off", reportFailure: false),
                    action = ToggleCorpseLoadingRequest,
                    groupable = false,
                    groupKey = thingIDNumber
                };

                yield return load;
            }

        }

        public override string GetInspectString()
        {
            string text = base.GetInspectString();
            string state = Burning
                ? "MUGB_GoblinBeaconStateBurning".Translate()
                : (HasGoblinCorpse ? "MUGB_GoblinBeaconStateLoaded".Translate() : "MUGB_GoblinBeaconStateEmpty".Translate());
            if (!text.NullOrEmpty())
            {
                text += "\n";
            }

            text += "MUGB_GoblinBeaconInspectState".Translate(state);
            if (Burning)
            {
                text += "\n" + "MUGB_GoblinBeaconInspectBurnTime".Translate(burnTicksLeft.ToStringTicksToPeriod());
            }
            return text;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref burnTicksLeft, "burnTicksLeft", 0);
            Scribe_Values.Look(ref wantsCorpseLoaded, "wantsCorpseLoaded", defaultValue: false);
            Scribe_Values.Look(ref ritualSignalPending, "ritualSignalPending", defaultValue: false);
        }

        private void ToggleCorpseLoadingRequest()
        {
            if (wantsCorpseLoaded)
            {
                wantsCorpseLoaded = false;
                TryCancelLoadCorpse();
                GoblinSlaveMarriageUtility.PlayCommandCanceledSound();
                Messages.Message("MUGB_GoblinBeaconLoadCanceled".Translate(), this, MessageTypeDefOf.TaskCompletion, historical: false);
                return;
            }

            wantsCorpseLoaded = true;
            GoblinSlaveMarriageUtility.PlayCommandAcceptedSound();
            Messages.Message("MUGB_GoblinBeaconLoadEnabled".Translate(), this, MessageTypeDefOf.TaskCompletion, historical: false);
        }

        private bool TryCancelLoadCorpse()
        {
            Pawn pawn = AssignedLoadPawn();
            if (pawn == null)
            {
                return false;
            }

            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: true);
            return true;
        }

        private bool IsLoadJobAssigned()
        {
            return AssignedLoadPawn() != null;
        }

        private Pawn AssignedLoadPawn()
        {
            if (Map == null)
            {
                return null;
            }

            foreach (Pawn pawn in Map.mapPawns.FreeColonistsSpawned)
            {
                Job job = pawn.CurJob;
                if (job != null && JobTargetsBeacon(job, this))
                {
                    return pawn;
                }
            }

            return null;
        }

        private static bool JobTargetsBeacon(Job job, Building_GoblinBeacon beacon)
        {
            return job.GetTarget(TargetIndex.A).Thing == beacon
                || job.GetTarget(TargetIndex.B).Thing == beacon
                || job.GetTarget(TargetIndex.C).Thing == beacon;
        }

        public bool TryBeginRitualBurn(int ritualDurationTicks)
        {
            if (!CanStartSignalRitual(out string failReason))
            {
                Messages.Message(failReason, this, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            innerContainer.ClearAndDestroyContents();
            wantsCorpseLoaded = false;
            ritualSignalPending = true;
            burnTicksLeft = ritualDurationTicks > 0 ? ritualDurationTicks : BurnTicksTotal;
            this.TryGetComp<CompGlower>()?.UpdateLit(Map);
            GoblinSlaveMarriageUtility.PlayCommandAcceptedSound();
            DirtyMapMesh(Map);
            return true;
        }

        public bool TryCompleteRitualSignal(GoblinBeaconSignalQuality signalQuality)
        {
            if (!ritualSignalPending || Destroyed || Map == null)
            {
                return false;
            }

            ritualSignalPending = false;
            ScheduleBeaconEvents(signalQuality);
            burnTicksLeft = Mathf.Min(burnTicksLeft, 1);
            return true;
        }

        private void ScheduleBeaconEvents(GoblinBeaconSignalQuality signalQuality)
        {
            Map map = Map;
            if (map == null)
            {
                return;
            }

            int delay = RandomEventDelayTicks();
            if (Rand.Chance(CompositeChance(signalQuality)))
            {
                QueueFriendlyEvent(map, delay, signalQuality);
                QueueHostileEvent(map, delay + RandomCompositeFollowupDelayTicks());
                return;
            }

            float roll = Rand.Value;
            GetSingleEventWeights(signalQuality, out float friendlyWeight, out float goblinRaidWeight);
            if (roll < friendlyWeight)
            {
                QueueFriendlyEvent(map, delay, signalQuality);
            }
            else if (roll < friendlyWeight + goblinRaidWeight)
            {
                QueueGoblinRaid(map, delay);
            }
            else
            {
                QueueHumanHunterRaid(map, delay);
            }
        }

        private static int RandomEventDelayTicks()
        {
            return Rand.RangeInclusive(MinEventDelayTicks, MaxEventDelayTicks);
        }

        private static int RandomCompositeFollowupDelayTicks()
        {
            return Rand.RangeInclusive(MinCompositeFollowupDelayTicks, MaxCompositeFollowupDelayTicks);
        }

        private static float CompositeChance(GoblinBeaconSignalQuality quality)
        {
            return quality switch
            {
                GoblinBeaconSignalQuality.Poor => 0.20f,
                GoblinBeaconSignalQuality.Good => 0.12f,
                GoblinBeaconSignalQuality.Excellent => 0.10f,
                _ => 0.15f
            };
        }

        private static void GetSingleEventWeights(GoblinBeaconSignalQuality quality, out float friendlyWeight, out float goblinRaidWeight)
        {
            // 한국어 의도: 의식 품질이 좋을수록 우호 신호가 더 잘 닿고 인간 토벌대 비중은 낮아진다.
            // 복합 사건은 별도 확률로 먼저 판정하며, 여기 수치는 단독 사건의 편성 비율이다.
            switch (quality)
            {
                case GoblinBeaconSignalQuality.Poor:
                    friendlyWeight = 0.25f;
                    goblinRaidWeight = 0.40f;
                    break;
                case GoblinBeaconSignalQuality.Good:
                    friendlyWeight = 0.50f;
                    goblinRaidWeight = 0.32f;
                    break;
                case GoblinBeaconSignalQuality.Excellent:
                    friendlyWeight = 0.55f;
                    goblinRaidWeight = 0.30f;
                    break;
                default:
                    friendlyWeight = 0.40f;
                    goblinRaidWeight = 0.35f;
                    break;
            }
        }

        private static void QueueFriendlyEvent(Map map, int delayTicks, GoblinBeaconSignalQuality signalQuality)
        {
            float roll = Rand.Value;
            if (ModsConfig.RoyaltyActive)
            {
                if (roll < 0.35f)
                {
                    QueueIncident("MUGB_GoblinBeaconWanderer", map, delayTicks, pointsOverride: (float)signalQuality);
                }
                else if (roll < 0.70f)
                {
                    QueueIncident("MUGB_GoblinBeaconHospitalityRefugees", map, delayTicks);
                }
                else
                {
                    QueueIncident("TraderCaravanArrival", map, delayTicks, FirstFactionOfDef(MUGBDefOf.MUGB_GoblinCivilTribe));
                }
                return;
            }

            // Royalty가 없으면 임시 난민 퀘스트 몫을 나머지 두 우호 결과에 비례 재분배한다.
            if (roll < 0.54f)
            {
                QueueIncident("MUGB_GoblinBeaconWanderer", map, delayTicks, pointsOverride: (float)signalQuality);
            }
            else
            {
                QueueIncident("TraderCaravanArrival", map, delayTicks, FirstFactionOfDef(MUGBDefOf.MUGB_GoblinCivilTribe));
            }
        }

        private static void QueueHostileEvent(Map map, int delayTicks)
        {
            if (Rand.Chance(0.58f))
            {
                QueueGoblinRaid(map, delayTicks);
            }
            else
            {
                QueueHumanHunterRaid(map, delayTicks);
            }
        }

        private static void QueueGoblinRaid(Map map, int delayTicks)
        {
            QueueIncident("RaidEnemy", map, delayTicks, FirstFactionOfDef(MUGBDefOf.MUGB_GoblinTribe));
        }

        private static void QueueHumanHunterRaid(Map map, int delayTicks)
        {
            QueueIncident("MUGB_GoblinBeaconHumanHunters", map, delayTicks, FirstHumanHunterFaction());
        }

        private static Faction FirstFactionOfDef(FactionDef factionDef)
        {
            if (factionDef == null)
            {
                return null;
            }

            return Find.FactionManager?.FirstFactionOfDef(factionDef);
        }

        private static Faction FirstHumanHunterFaction()
        {
            FactionManager factionManager = Find.FactionManager;
            if (factionManager == null)
            {
                return null;
            }

            Faction dedicatedHunters = factionManager.FirstFactionOfDef(MUGBDefOf.MUGB_GoblinHunters);
            if (dedicatedHunters != null && !dedicatedHunters.defeated)
            {
                return dedicatedHunters;
            }

            return factionManager.AllFactionsVisible
                .Where(faction => faction != null && !faction.def.hidden && faction != Faction.OfPlayer)
                .Where(GoblinBeaconHumanHunterUtility.IsHumanHunterFactionCandidate)
                .Where(faction => faction.HostileTo(Faction.OfPlayer) && !faction.defeated)
                .RandomElementWithFallback();
        }

        private static void QueueIncident(string defName, Map map, int delayTicks, Faction faction = null, float? pointsOverride = null)
        {
            IncidentDef incident = DefDatabase<IncidentDef>.GetNamedSilentFail(defName);
            if (incident == null)
            {
                return;
            }

            IncidentParms parms = StorytellerUtility.DefaultParmsNow(incident.category, map);
            parms.target = map;
            parms.forced = true;
            if (faction != null)
            {
                parms.faction = faction;
            }
            if (pointsOverride.HasValue)
            {
                parms.points = pointsOverride.Value;
            }

            Find.Storyteller.incidentQueue.Add(incident, Find.TickManager.TicksGame + delayTicks, parms);
        }

        public static GoblinBeaconSignalQuality SignalQualityFromRitualQuality(float quality)
        {
            if (quality < 0.35f)
            {
                return GoblinBeaconSignalQuality.Poor;
            }
            if (quality < 0.65f)
            {
                return GoblinBeaconSignalQuality.Normal;
            }
            if (quality < 0.85f)
            {
                return GoblinBeaconSignalQuality.Good;
            }
            return GoblinBeaconSignalQuality.Excellent;
        }

        public static Job LoadCorpseJob(Pawn pawn, Corpse corpse, Building_GoblinBeacon beacon, out string failReason)
        {
            failReason = null;
            if (pawn == null || corpse == null || beacon == null)
            {
                failReason = "Invalid loading target.";
                return null;
            }

            if (!beacon.Accepts(corpse))
            {
                failReason = "This beacon can only accept one goblin corpse.";
                return null;
            }

            if (!pawn.CanReserveAndReach(corpse, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                failReason = "Cannot reach or reserve the goblin corpse.";
                return null;
            }

            if (!pawn.CanReserveAndReach(beacon, PathEndMode.Touch, Danger.Deadly))
            {
                failReason = "Cannot reach or reserve the goblin beacon.";
                return null;
            }

            return HaulAIUtility.HaulToContainerJob(pawn, corpse, beacon);
        }

        public Corpse FindCorpseFor(Pawn pawn)
        {
            if (pawn == null || Map == null || !WantsCorpseLoaded)
            {
                return null;
            }

            return GenClosest.ClosestThingReachable(
                Position,
                Map,
                ThingRequest.ForGroup(ThingRequestGroup.Corpse),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn, Danger.Deadly),
                9999f,
                t => t is Corpse c && Accepts(c) && !c.IsForbidden(pawn) && pawn.CanReserve(c)) as Corpse;
        }
    }

    public class RitualObligationTargetWorker_GoblinBeacon : RitualObligationTargetWorker_ThingDef
    {
        public RitualObligationTargetWorker_GoblinBeacon()
        {
        }

        public RitualObligationTargetWorker_GoblinBeacon(RitualObligationTargetFilterDef def)
            : base(def)
        {
        }

        protected override RitualTargetUseReport CanUseTargetInternal(TargetInfo target, RitualObligation obligation)
        {
            RitualTargetUseReport report = base.CanUseTargetInternal(target, obligation);
            if (!report.canUse)
            {
                return report;
            }

            string failReason = null;
            if (target.Thing is Building_GoblinBeacon beacon && beacon.CanStartSignalRitual(out failReason))
            {
                return true;
            }

            return failReason ?? "MUGB_GoblinBeaconNeedCorpse".Translate();
        }
    }

    public class RitualBehaviorWorker_GoblinBeaconSignal : RitualBehaviorWorker
    {
        public RitualBehaviorWorker_GoblinBeaconSignal()
        {
        }

        public RitualBehaviorWorker_GoblinBeaconSignal(RitualBehaviorDef def)
            : base(def)
        {
        }

        protected override LordJob CreateLordJob(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments)
        {
            Pawn caller = assignments.AssignedPawns("caller").FirstOrDefault();
            return new LordJob_GoblinBeaconSignal(target, caller, ritual, def.stages, assignments);
        }
    }

    public class LordJob_GoblinBeaconSignal : LordJob_Joinable_Speech
    {
        public LordJob_GoblinBeaconSignal()
        {
        }

        public LordJob_GoblinBeaconSignal(TargetInfo target, Pawn caller, Precept_Ritual ritual, List<RitualStage> stages, RitualRoleAssignments assignments)
            : base(target, caller, ritual, stages, assignments, titleSpeech: false)
        {
            (selectedTarget.Thing as Building_GoblinBeacon)?.TryBeginRitualBurn(DurationTicks);
        }

        public override void ApplyOutcome(float progress, bool showFinishedMessage = true, bool showFailedMessage = true, bool cancelled = false)
        {
            Building_GoblinBeacon beacon = selectedTarget.Thing as Building_GoblinBeacon;
            base.ApplyOutcome(progress, showFinishedMessage, showFailedMessage, cancelled);

            // A lit beacon cannot be unlit. Interrupted or failed ceremonies still emit a poor signal.
            beacon?.TryCompleteRitualSignal(GoblinBeaconSignalQuality.Poor);
        }
    }

    public class RitualRole_GoblinBeaconCaller : RitualRoleColonist
    {
        public override bool AppliesToPawn(Pawn pawn, out string reason, TargetInfo target, LordJob_Ritual ritual, RitualRoleAssignments assignments, Precept_Ritual precept, bool skipReason = false)
        {
            if (!base.AppliesToPawn(pawn, out reason, target, ritual, assignments, precept, skipReason))
            {
                return false;
            }

            if (!SlaveMarriageRitualUtility.IsAvailableFreeColonist(pawn)
                || (!SlaveMarriageRitualUtility.IsLeaderOrMoralGuide(pawn) && !IsSoleAvailableColonist(pawn)))
            {
                reason = "MUGB_GoblinBeaconRitualNeedCaller".Translate();
                return false;
            }

            reason = null;
            return true;
        }

        private static bool IsSoleAvailableColonist(Pawn pawn)
        {
            Map map = pawn?.Map;
            return map != null
                && map.mapPawns.FreeColonistsSpawned.Count(SlaveMarriageRitualUtility.IsAvailableFreeColonist) == 1;
        }
    }

    public class RitualOutcomeEffectWorker_GoblinBeaconSignal : RitualOutcomeEffectWorker_FromQuality
    {
        public RitualOutcomeEffectWorker_GoblinBeaconSignal()
        {
        }

        public RitualOutcomeEffectWorker_GoblinBeaconSignal(RitualOutcomeEffectDef def)
            : base(def)
        {
        }

        public override void Apply(float progress, Dictionary<Pawn, int> totalPresence, LordJob_Ritual jobRitual)
        {
            if (jobRitual.cancelled || !(jobRitual.selectedTarget.Thing is Building_GoblinBeacon beacon))
            {
                return;
            }

            float ritualQuality = GetQuality(jobRitual, progress);
            GoblinBeaconSignalQuality signalQuality = Building_GoblinBeacon.SignalQualityFromRitualQuality(ritualQuality);
            if (!beacon.TryCompleteRitualSignal(signalQuality))
            {
                return;
            }

            Find.LetterStack.ReceiveLetter(
                "MUGB_GoblinBeaconRitualOutcomeLabel".Translate(),
                "MUGB_GoblinBeaconRitualOutcomeText".Translate(
                    ("MUGB_GoblinBeaconSignalQuality" + signalQuality).Translate(),
                    ritualQuality.ToStringPercent()),
                LetterDefOf.RitualOutcomePositive,
                beacon);
        }
    }

    public class RitualOutcomeComp_GoblinSculptures : RitualOutcomeComp
    {
        public int maxDistance = 10;
        public int maxSmall = 6;
        public int maxGrand = 2;
        public float maxQualityOffset = 0.25f;

        public override bool DataRequired => false;

        public override bool Applies(LordJob_Ritual ritual)
        {
            return ritual != null && CalculateOffset(ritual.Map, ritual.selectedTarget.Cell) > 0f;
        }

        public override float QualityOffset(LordJob_Ritual ritual, RitualOutcomeComp_Data data)
        {
            return ritual == null ? 0f : CalculateOffset(ritual.Map, ritual.selectedTarget.Cell);
        }

        public override string GetDesc(LordJob_Ritual ritual = null, RitualOutcomeComp_Data data = null)
        {
            float offset = ritual == null ? maxQualityOffset : QualityOffset(ritual, data);
            return label + ": " + offset.ToStringWithSign("0.#%");
        }

        public override string GetBonusDescShort()
        {
            return maxQualityOffset.ToStringWithSign("0.#%");
        }

        public override QualityFactor GetQualityFactor(Precept_Ritual ritual, TargetInfo ritualTarget, RitualObligation obligation, RitualRoleAssignments assignments, RitualOutcomeComp_Data data)
        {
            float offset = CalculateOffset(ritualTarget.Map, ritualTarget.Cell);
            return new QualityFactor
            {
                label = label.CapitalizeFirst(),
                present = offset > 0f,
                qualityChange = offset.ToStringWithSign("0.#%"),
                quality = offset,
                positive = true,
                priority = 2f
            };
        }

        private float CalculateOffset(Map map, IntVec3 center)
        {
            if (map == null || !center.IsValid)
            {
                return 0f;
            }

            float small = BestNearbyBonuses(map, center, MUGBDefOf.MUGB_GoblinSculptureSmall, maxSmall, grand: false).Sum();
            float grand = BestNearbyBonuses(map, center, MUGBDefOf.MUGB_GoblinSculptureGrand, maxGrand, grand: true).Sum();
            return Mathf.Min(maxQualityOffset, small + grand);
        }

        private IEnumerable<float> BestNearbyBonuses(Map map, IntVec3 center, ThingDef def, int limit, bool grand)
        {
            if (def == null || limit <= 0)
            {
                return Enumerable.Empty<float>();
            }

            return map.listerThings.ThingsOfDef(def)
                .Where(thing => thing.Spawned
                    && thing.Faction == Faction.OfPlayer
                    && thing.Position.InHorDistOf(center, maxDistance))
                .Select(thing => SculptureBonus(thing, grand))
                .OrderByDescending(value => value)
                .Take(limit);
        }

        private static float SculptureBonus(Thing sculpture, bool grand)
        {
            QualityCategory quality = QualityCategory.Normal;
            sculpture.TryGetQuality(out quality);
            if (grand)
            {
                return quality switch
                {
                    QualityCategory.Awful => 0.05f,
                    QualityCategory.Poor => 0.05f,
                    QualityCategory.Normal => 0.10f,
                    QualityCategory.Good => 0.10f,
                    QualityCategory.Excellent => 0.15f,
                    QualityCategory.Masterwork => 0.15f,
                    QualityCategory.Legendary => 0.25f,
                    _ => 0.10f
                };
            }

            return quality switch
            {
                QualityCategory.Awful => 0.01f,
                QualityCategory.Poor => 0.01f,
                QualityCategory.Normal => 0.02f,
                QualityCategory.Good => 0.02f,
                QualityCategory.Excellent => 0.03f,
                QualityCategory.Masterwork => 0.03f,
                QualityCategory.Legendary => 0.05f,
                _ => 0.02f
            };
        }
    }

    public class IncidentWorker_GoblinBeaconJoiners : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms) || !(parms.target is Map map))
            {
                return false;
            }

            return TryFindEntryCell(map, out _);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map map))
            {
                return false;
            }

            LetterDef letterDef = DefDatabase<LetterDef>.GetNamedSilentFail("MUGB_GoblinBeaconJoinersChoice");
            if (letterDef == null)
            {
                return false;
            }

            GoblinBeaconSignalQuality signalQuality = SignalQualityFromIncidentPoints(parms.points);
            int targetCount = RefugeeCountFor(signalQuality);
            ChoiceLetter_GoblinBeaconJoiners letter = (ChoiceLetter_GoblinBeaconJoiners)LetterMaker.MakeLetter(
                "MUGB_GoblinBeaconJoinersLetterLabel".Translate(targetCount),
                "MUGB_GoblinBeaconJoinersLetterText".Translate(targetCount),
                letterDef,
                new LookTargets(map.Center, map));
            letter.map = map;
            letter.joinerCount = targetCount;
            Find.LetterStack.ReceiveLetter(letter);
            return true;
        }

        internal static bool TrySpawnJoiners(Map map, int targetCount, out List<Pawn> joiners)
        {
            joiners = new List<Pawn>();
            PawnKindDef refugeeKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_GoblinKind_BeaconRefugee");
            if (map == null || refugeeKind == null)
            {
                return false;
            }

            List<Pawn> refugees = new List<Pawn>();
            for (int i = 0; i < targetCount; i++)
            {
                if (!TryFindEntryCell(map, out IntVec3 spawnCell))
                {
                    break;
                }

                Pawn refugee = PawnGenerator.GeneratePawn(refugeeKind, Faction.OfPlayer);
                if (refugee == null)
                {
                    continue;
                }

                GenSpawn.Spawn(refugee, spawnCell, map, WipeMode.Vanish);
                refugees.Add(refugee);
            }

            joiners = refugees;
            return joiners.Count > 0;
        }

        internal static bool TryFindEntryCell(Map map, out IntVec3 cell)
        {
            return CellFinder.TryFindRandomEdgeCellWith(
                candidate => map.reachability.CanReachColony(candidate) && !candidate.Fogged(map),
                map,
                CellFinder.EdgeRoadChance_Neutral,
                out cell);
        }

        private static GoblinBeaconSignalQuality SignalQualityFromIncidentPoints(float points)
        {
            int value = Mathf.Clamp(Mathf.RoundToInt(points), (int)GoblinBeaconSignalQuality.Poor, (int)GoblinBeaconSignalQuality.Excellent);
            return (GoblinBeaconSignalQuality)value;
        }

        private static int RefugeeCountFor(GoblinBeaconSignalQuality quality)
        {
            return quality switch
            {
                GoblinBeaconSignalQuality.Poor => 1,
                GoblinBeaconSignalQuality.Good => 2,
                GoblinBeaconSignalQuality.Excellent => Rand.RangeInclusive(2, 3),
                _ => Rand.RangeInclusive(1, 2)
            };
        }
    }

    public class ChoiceLetter_GoblinBeaconJoiners : ChoiceLetter
    {
        public Map map;
        public int joinerCount;

        public override bool CanDismissWithRightClick => false;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (ArchivedOnly)
                {
                    yield return Option_Close;
                    yield break;
                }

                DiaOption accept = new DiaOption("AcceptButton".Translate());
                if (map == null || !map.IsPlayerHome)
                {
                    accept.Disable("MUGB_GoblinBeaconJoinersNoMap".Translate());
                }
                else
                {
                    accept.action = delegate
                    {
                        if (IncidentWorker_GoblinBeaconJoiners.TrySpawnJoiners(map, joinerCount, out List<Pawn> joiners))
                        {
                            Find.LetterStack.RemoveLetter(this);
                            Find.LetterStack.ReceiveLetter(
                                "MUGB_GoblinBeaconJoinersAcceptedLabel".Translate(joiners.Count),
                                "MUGB_GoblinBeaconJoinersAcceptedText".Translate(joiners.Count),
                                LetterDefOf.PositiveEvent,
                                new LookTargets(joiners));
                        }
                        else
                        {
                            Messages.Message("MUGB_GoblinBeaconJoinersSpawnFailed".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                        }
                    };
                }
                accept.resolveTree = true;

                DiaOption reject = new DiaOption("RejectLetter".Translate())
                {
                    action = delegate
                    {
                        Find.LetterStack.RemoveLetter(this);
                        Messages.Message("MUGB_GoblinBeaconJoinersRejected".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
                    },
                    resolveTree = true
                };

                yield return accept;
                yield return reject;
                if (lookTargets.IsValid())
                {
                    yield return Option_JumpToLocationAndPostpone;
                }
                yield return Option_Postpone;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref map, "map");
            Scribe_Values.Look(ref joinerCount, "joinerCount", 1);
        }
    }

    public class IncidentWorker_GoblinBeaconHospitalityRefugees : IncidentWorker
    {
        private static QuestScriptDef RefugeeQuestDef =>
            DefDatabase<QuestScriptDef>.GetNamedSilentFail("Hospitality_Refugee");

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!ModsConfig.RoyaltyActive || !base.CanFireNowSub(parms))
            {
                return false;
            }

            QuestScriptDef questDef = RefugeeQuestDef;
            return questDef != null
                && questDef.CanRun(parms.points, parms.target)
                && PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoSuspended.Any();
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            QuestScriptDef questDef = RefugeeQuestDef;
            if (questDef == null || !(parms.target is Map))
            {
                return false;
            }

            parms.questScriptDef = questDef;
            GiveQuest(parms, questDef);
            return true;
        }

        private static void GiveQuest(IncidentParms parms, QuestScriptDef questDef)
        {
            Slate slate = new Slate();
            slate.Set("points", parms.points);
            slate.Set("map", (Map)parms.target);

            Quest quest;
            GoblinBeaconRefugeeQuestContext.Enter();
            try
            {
                quest = QuestUtility.GenerateQuestAndMakeAvailable(questDef, slate);
            }
            finally
            {
                GoblinBeaconRefugeeQuestContext.Exit();
            }

            quest.name = "MUGB_GoblinBeaconHospitalityQuestName".Translate();
            quest.description += "\n\n" + "MUGB_GoblinBeaconHospitalityWarning".Translate();
            if (quest.root.sendAvailableLetter)
            {
                QuestUtility.SendLetterQuestAvailable(quest);
            }
        }
    }

    internal static class GoblinBeaconRefugeeQuestContext
    {
        [ThreadStatic]
        private static int depth;

        internal static bool Active => depth > 0;

        internal static void Enter()
        {
            depth++;
        }

        internal static void Exit()
        {
            depth = Mathf.Max(0, depth - 1);
        }
    }

    internal static class GoblinVanillaRefugeeQuestContext
    {
        [ThreadStatic]
        private static int depth;

        internal static bool Active => depth > 0;

        internal static void Enter()
        {
            depth++;
        }

        internal static void Exit()
        {
            depth = Mathf.Max(0, depth - 1);
        }
    }

    [HarmonyPatch(typeof(QuestNode_Root_Beggars), nameof(QuestNode_Root_Beggars.LodgerCountFromPopulation))]
    internal static class GoblinBeaconRefugeeCountPatch
    {
        private static void Postfix(ref int __result)
        {
            if (GoblinBeaconRefugeeQuestContext.Active)
            {
                __result = Mathf.Clamp(__result, 1, 4);
            }
        }
    }

    [HarmonyPatch(typeof(QuestGen_Pawns), nameof(QuestGen_Pawns.GeneratePawn), new Type[]
    {
        typeof(Quest), typeof(PawnKindDef), typeof(Faction), typeof(bool), typeof(IEnumerable<TraitDef>),
        typeof(float), typeof(bool), typeof(Pawn), typeof(float), typeof(float), typeof(bool), typeof(bool),
        typeof(DevelopmentalStage), typeof(bool)
    })]
    internal static class GoblinBeaconRefugeePawnPatch
    {
        private static void Prefix(ref PawnKindDef kindDef)
        {
            if ((!GoblinBeaconRefugeeQuestContext.Active && !GoblinVanillaRefugeeQuestContext.Active)
                || kindDef != PawnKindDefOf.Refugee)
            {
                return;
            }

            PawnKindDef goblinKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_GoblinKind_HospitalityRefugee");
            if (goblinKind != null)
            {
                kindDef = goblinKind;
            }
        }
    }

    [HarmonyPatch(typeof(QuestNode_Root_Hospitality_Refugee), "RunInt")]
    internal static class GoblinBeaconRefugeeQuestTuningPatch
    {
        private struct DurationState
        {
            internal bool changed;
            internal IntRange original;
            internal bool enteredVanillaGoblinContext;
        }

        private static readonly System.Reflection.FieldInfo DurationRangeField =
            AccessTools.Field(typeof(QuestNode_Root_Hospitality_Refugee), "QuestDurationDaysRange");

        private static void Prefix(out DurationState __state)
        {
            __state = default;
            if (!GoblinBeaconRefugeeQuestContext.Active && ShouldGenerateVanillaGoblinRefugees())
            {
                GoblinVanillaRefugeeQuestContext.Enter();
                __state.enteredVanillaGoblinContext = true;
            }

            if (!GoblinBeaconRefugeeQuestContext.Active || DurationRangeField == null)
            {
                return;
            }

            __state.changed = true;
            __state.original = (IntRange)DurationRangeField.GetValue(null);
            DurationRangeField.SetValue(null, new IntRange(5, 10));
        }

        private static void Postfix(DurationState __state)
        {
            bool beaconRefugees = GoblinBeaconRefugeeQuestContext.Active;
            bool vanillaGoblinRefugees = __state.enteredVanillaGoblinContext;
            try
            {
                if (beaconRefugees || vanillaGoblinRefugees)
                {
                    ConfigureBetrayal(beaconRefugees);
                }

                if (vanillaGoblinRefugees)
                {
                    AppendVanillaGoblinWarning();
                }
            }
            finally
            {
                RestoreDuration(__state);
                ExitVanillaContext(__state);
            }
        }

        private static Exception Finalizer(Exception __exception, DurationState __state)
        {
            if (__exception != null)
            {
                RestoreDuration(__state);
                ExitVanillaContext(__state);
            }
            return __exception;
        }

        private static bool ShouldGenerateVanillaGoblinRefugees()
        {
            const string playerGoblinFactionDefName = "MUGB_PlayerGoblinFaction";
            bool goblinPlayerFaction =
                Faction.OfPlayerSilentFail?.def?.defName == playerGoblinFactionDefName;
            return Rand.Chance(goblinPlayerFaction ? 0.80f : 0.20f);
        }

        private static void ExitVanillaContext(DurationState state)
        {
            if (state.enteredVanillaGoblinContext)
            {
                GoblinVanillaRefugeeQuestContext.Exit();
            }
        }

        private static void RestoreDuration(DurationState state)
        {
            if (state.changed && DurationRangeField != null)
            {
                DurationRangeField.SetValue(null, state.original);
            }
        }

        private static void AppendVanillaGoblinWarning()
        {
            Quest quest = RimWorld.QuestGen.QuestGen.quest;
            if (quest != null)
            {
                quest.description += "\n\n" + "MUGB_GoblinBeaconHospitalityWarning".Translate();
            }
        }

        private static void ConfigureBetrayal(bool beaconRefugees)
        {
            Quest quest = RimWorld.QuestGen.QuestGen.quest;
            Slate slate = RimWorld.QuestGen.QuestGen.slate;
            if (quest == null || slate == null)
            {
                return;
            }

            quest.PartsListForReading.RemoveAll(part =>
                part is QuestPart_Delay delay && delay.debugLabel != null && delay.debugLabel.StartsWith("Mutiny ("));

            if (!Find.Storyteller.difficulty.allowViolentQuests ||
                (!Find.Storyteller.difficulty.ChildRaidersAllowed && slate.Get("childCount", 0) > 0) ||
                !Rand.Chance(0.45f))
            {
                return;
            }

            quest.PartsListForReading.RemoveAll(part =>
                part is QuestPart_Delay delay && delay.debugLabel != null && delay.debugLabel.StartsWith("BetrayalOffer ("));

            QuestPart_RefugeeInteractions interactions = quest.PartsListForReading
                .OfType<QuestPart_RefugeeInteractions>()
                .FirstOrDefault();
            int durationTicks = slate.Get("questDurationTicks", 0);
            if (interactions == null || durationTicks <= 0)
            {
                return;
            }

            int betrayalDelay = Mathf.FloorToInt(Rand.Range(0.2f, 1f) * durationTicks);
            quest.Delay(betrayalDelay, delegate
            {
                quest.Letter(
                    LetterDefOf.ThreatBig,
                    label: (beaconRefugees
                        ? "MUGB_GoblinBeaconRefugeeBetrayalLabel"
                        : "MUGB_GoblinRefugeeBetrayalLabel").Translate(),
                    text: (beaconRefugees
                        ? "MUGB_GoblinBeaconRefugeeBetrayalText"
                        : "MUGB_GoblinRefugeeBetrayalText").Translate());
                quest.SignalPass(null, null, interactions.inSignalAssaultColony);
                QuestGen_End.End(quest, QuestEndOutcome.Unknown);
            }, debugLabel: (beaconRefugees ? "MUGB beacon" : "MUGB vanilla goblin")
                + " refugee betrayal (" + betrayalDelay.ToStringTicksToDays() + ")");
        }
    }

    public class IncidentWorker_GoblinBeaconHumanHunters : IncidentWorker_RaidEnemy
    {
        protected override bool TryResolveRaidFaction(IncidentParms parms)
        {
            if (GoblinBeaconHumanHunterUtility.IsUsableHumanHunterFaction(parms.faction, parms, this))
            {
                return true;
            }

            List<Faction> candidates = Find.FactionManager?.AllFactionsVisible
                .Where(faction => GoblinBeaconHumanHunterUtility.IsUsableHumanHunterFaction(faction, parms, this))
                .ToList();
            if (candidates.NullOrEmpty())
            {
                return false;
            }

            parms.faction = candidates.RandomElement();
            return true;
        }

        protected override string GetLetterLabel(IncidentParms parms)
        {
            return "MUGB_GoblinBeaconHumanHuntersLetterLabel".Translate(parms.faction?.Name ?? "unknown faction");
        }

        protected override string GetLetterText(IncidentParms parms, List<Pawn> pawns)
        {
            string factionName = parms.faction?.Name ?? "unknown faction";
            return "MUGB_GoblinBeaconHumanHuntersLetterText".Translate(factionName).Resolve();
        }

        protected override void PostProcessSpawnedPawns(IncidentParms parms, List<Pawn> pawns)
        {
            base.PostProcessSpawnedPawns(parms, pawns);
            GoblinBeaconHumanHunterUtility.PostProcessHunters(pawns);
        }
    }

    public static class GoblinBeaconHumanHunterUtility
    {
        private static readonly string[] HunterBackstories =
        {
            "MUGB_Backstory_Adult_GoblinHunter",
            "MUGB_Backstory_Adult_GoblinRaidSurvivor",
            "MUGB_Backstory_Adult_GoblinExterminator",
            "MUGB_Backstory_HumanAdult_GoblinPunitiveSoldier",
            "MUGB_Backstory_HumanAdult_GoblinSubjugator",
            "MUGB_Backstory_HumanAdult_GoblinHunter2",
            "MUGB_Backstory_HumanAdult_GoblinSubjugationCaptain"
        };

        public static bool IsHumanHunterFactionCandidate(Faction faction)
        {
            if (faction?.def == null || faction == Faction.OfPlayer || faction.defeated)
            {
                return false;
            }

            if (faction.def == MUGBDefOf.MUGB_GoblinHunters)
            {
                return true;
            }

            if (faction.def.hidden)
            {
                return false;
            }

            if (!faction.def.humanlikeFaction || faction.def.pawnGroupMakers.NullOrEmpty())
            {
                return false;
            }

            if (IsGoblinFaction(faction.def))
            {
                return false;
            }

            return faction.def.pawnGroupMakers.Any(maker => maker?.kindDef == PawnGroupKindDefOf.Combat && maker.options != null && maker.options.Any());
        }

        public static bool IsUsableHumanHunterFaction(Faction faction, IncidentParms parms, IncidentWorker_RaidEnemy worker)
        {
            if (!IsHumanHunterFactionCandidate(faction) || !faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            return worker?.FactionCanBeGroupSource(faction, parms, desperate: true) == true;
        }

        public static void PostProcessHunters(List<Pawn> pawns)
        {
            if (pawns.NullOrEmpty())
            {
                return;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.RaceProps?.Humanlike != true || pawn.story == null || GoblinUtility.IsGoblin(pawn))
                {
                    continue;
                }

                Rand.PushState(pawn.thingIDNumber ^ 0x6768756E);
                try
                {
                    // 한국어 의도: 봉화 냄새를 따라온 인간/비고블린 토벌대라는 분위기만 입힌다.
                    // 종족모드의 장비·몸·PawnKind는 그대로 두고, 백스토리와 최소 전투 능력만 살짝 보정한다.
                    BackstoryDef backstory = PickHunterBackstory();
                    if (backstory != null)
                    {
                        pawn.story.Adulthood = backstory;
                    }

                    ReinforceCombatSkill(pawn);
                }
                finally
                {
                    Rand.PopState();
                }
            }
        }

        private static bool IsGoblinFaction(FactionDef def)
        {
            return def == MUGBDefOf.MUGB_GoblinTribe
                || def == MUGBDefOf.MUGB_GoblinCivilTribe
                || def == MUGBDefOf.MUGB_GoblinCivilMedieval
                || def == MUGBDefOf.MUGB_GoblinSavageMedieval
                || def == MUGBDefOf.MUGB_GoblinCultists;
        }

        private static BackstoryDef PickHunterBackstory()
        {
            List<BackstoryDef> candidates = new List<BackstoryDef>();
            for (int i = 0; i < HunterBackstories.Length; i++)
            {
                BackstoryDef backstory = DefDatabase<BackstoryDef>.GetNamedSilentFail(HunterBackstories[i]);
                if (backstory != null)
                {
                    candidates.Add(backstory);
                }
            }

            return candidates.TryRandomElement(out BackstoryDef result) ? result : null;
        }

        private static void ReinforceCombatSkill(Pawn pawn)
        {
            if (pawn.skills == null)
            {
                return;
            }

            bool hasRangedWeapon = pawn.equipment?.Primary?.def?.IsRangedWeapon == true;
            EnsureSkillAtLeast(pawn, hasRangedWeapon ? SkillDefOf.Shooting : SkillDefOf.Melee, 6);
        }

        private static void EnsureSkillAtLeast(Pawn pawn, SkillDef skillDef, int level)
        {
            SkillRecord skill = pawn.skills?.GetSkill(skillDef);
            if (skill != null && !skill.TotallyDisabled && skill.Level < level)
            {
                skill.Level = level;
            }
        }
    }

    public class WorkGiver_LoadGoblinBeacon : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(MUGBDefOf.MUGB_goblinbeacon);

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobOnThing(pawn, t, forced) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_GoblinBeacon beacon) || t.Fogged() || t.IsForbidden(pawn) || t.Faction != pawn.Faction || !beacon.WantsCorpseLoaded)
            {
                return null;
            }

            if (!pawn.CanReserveAndReach(beacon, PathEndMode.Touch, Danger.Deadly, 1, -1, null, forced))
            {
                return null;
            }

            Corpse corpse = beacon.FindCorpseFor(pawn);
            if (corpse == null)
            {
                JobFailReason.Is("No reachable goblin corpse.");
                return null;
            }

            return Building_GoblinBeacon.LoadCorpseJob(pawn, corpse, beacon, out _);
        }
    }

    public class PlaceWorker_GoblinBeaconNoRoof : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            if (!ModsConfig.IdeologyActive
                || !MUGBGoblinIdeologyUtility.HasGoblinCoreMeme(Faction.OfPlayer?.ideos?.PrimaryIdeo))
            {
                return "MUGB_GoblinBeaconRequiresBlinia".Translate();
            }

            if (map == null)
            {
                return true;
            }

            foreach (IntVec3 cell in GenAdj.CellsOccupiedBy(loc, rot, checkingDef.Size))
            {
                if (cell.InBounds(map) && map.roofGrid.Roofed(cell))
                {
                    return "Cannot build a goblin beacon under a roof.";
                }
            }

            return true;
        }
    }
}
