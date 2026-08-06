using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MUGB
{
    // 한국어 의도: 한두 명짜리 바닐라 통행자 대신 3~5명의 방랑 거지무리가 맵 가장자리에서 들어와 반대편으로 빠져나간다.
    // 바닐라 이데올로기 거지처럼 방문마다 숨겨진 임시 세력을 만들며, 월드에 영구 세력을 남기지 않는다.
    public class IncidentWorker_BeggarTravelerGroup : IncidentWorker_TravelerGroup
    {
        public static bool TryFire(Map map)
        {
            IncidentDef incident = MUGBDefOf.MUGB_BeggarTravelerGroup;
            if (map == null || incident?.Worker == null)
            {
                return false;
            }

            Faction faction = MUGBBeggarFactionUtility.CreateTemporaryBeggarFaction();
            if (faction == null)
            {
                return false;
            }

            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = faction,
                points = 100f
            };
            bool result = incident.Worker.CanFireNow(parms) && incident.Worker.TryExecute(parms);
            if (!result)
            {
                // 폰을 한 명도 만들지 못한 임시 세력은 다음 일일 정리 때 제거된다.
                MUGBBeggarFactionUtility.CleanupUnusedTemporaryFactions();
            }
            return result;
        }

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms))
            {
                return false;
            }

            Faction faction = parms.faction;
            return faction != null && !NeutralGroupIncidentUtility.AnyBlockingHostileLord((Map)parms.target, faction);
        }

        protected override bool TryResolveParmsGeneral(IncidentParms parms)
        {
            Faction faction = parms.faction;
            if (faction == null || faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }

            parms.faction = faction;
            return base.TryResolveParmsGeneral(parms);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (!TryResolveParms(parms) || !RCellFinder.TryFindTravelDestFrom(parms.spawnCenter, map, out IntVec3 travelDest))
            {
                return false;
            }

            List<Pawn> pawns = SpawnBeggarPawns(parms, Rand.RangeInclusive(3, 5));
            if (pawns.Count == 0)
            {
                return false;
            }

            // 한국어 의도: 토스트 메시지가 아니라 중립 편지로 알린다. 놓치면 그대로 공급 기회를 잃기 때문이다.
            Find.LetterStack.ReceiveLetter(
                "MUGB_BeggarTravelersPassingLabel".Translate(),
                "MUGB_BeggarTravelersPassingText".Translate(),
                LetterDefOf.NeutralEvent,
                new LookTargets(pawns));
            // 바닐라 방문객·상단처럼 기지 바로 바깥의 접근 가능한 곳까지 들어와 머문다.
            // 적당한 장소가 없으면 기존처럼 진입 지점 근처를 사용한다.
            IntVec3 lingerSpot;
            if (!RCellFinder.TryFindRandomSpotJustOutsideColony(pawns[0], out lingerSpot))
            {
                lingerSpot = CellFinder.RandomClosewalkCellNear(parms.spawnCenter, map, 7);
            }

            // 초반 고블린은 느리고 수가 적어 4~6시간으로는 따라잡지 못하므로 15일까지는 창을 넓혀 준다.
            int lingerTicks = GenDate.DaysPassed < 15
                ? Rand.RangeInclusive(8 * GenDate.TicksPerHour, 10 * GenDate.TicksPerHour)
                : Rand.RangeInclusive(4 * GenDate.TicksPerHour, 6 * GenDate.TicksPerHour);
            LordMaker.MakeNewLord(parms.faction, new LordJob_BeggarLingerAndExit(lingerSpot, lingerTicks), map, pawns);
            PawnRelationUtility.Notify_PawnsSeenByPlayer_Letter_Send(pawns, "LetterRelatedPawnsNeutralGroup".Translate(Faction.OfPlayer.def.pawnsPlural), LetterDefOf.NeutralEvent, informEvenIfSeenBefore: true);
            return true;
        }

        private static List<Pawn> SpawnBeggarPawns(IncidentParms parms, int count)
        {
            List<Pawn> pawns = new List<Pawn>(count);
            Map map = (Map)parms.target;
            for (int i = 0; i < count; i++)
            {
                PawnKindDef kind = RandomBeggarKind();
                if (kind == null)
                {
                    continue;
                }

                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind,
                    parms.faction,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    colonistRelationChanceFactor: 0f);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(parms.spawnCenter, map, 5);
                GenSpawn.Spawn(pawn, cell, map);
                pawns.Add(pawn);
                parms.storeGeneratedNeutralPawns?.Add(pawn);
            }
            return pawns;
        }

        private static PawnKindDef RandomBeggarKind()
        {
            int roll = Rand.RangeInclusive(1, 19);
            string defName = roll <= 12 ? "MUGB_BeggarDrifter" : roll <= 17 ? "MUGB_BeggarScavenger" : "MUGB_BeggarLookout";
            return DefDatabase<PawnKindDef>.GetNamedSilentFail(defName);
        }
    }

    public class LordJob_BeggarLingerAndExit : LordJob
    {
        private IntVec3 lingerSpot;
        private int lingerTicks;
        private bool playerHostilityPending;

        public LordJob_BeggarLingerAndExit()
        {
        }

        public LordJob_BeggarLingerAndExit(IntVec3 lingerSpot, int lingerTicks)
        {
            this.lingerSpot = lingerSpot;
            this.lingerTicks = lingerTicks;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            StateGraph approachGraph = new LordJob_Travel(lingerSpot).CreateGraph();
            ReplaceTravelHarmTriggers(approachGraph);
            LordToil travelToEdge = graph.AttachSubgraph(approachGraph).StartingToil;
            LordToil_DefendPoint defendLocalGroup = (LordToil_DefendPoint)approachGraph.lordToils[1];
            LordToil_DefendPoint linger = new LordToil_DefendPoint(lingerSpot);
            graph.AddToil(linger);

            StateGraph exitGraph = new LordJob_TravelAndExit(IntVec3.Invalid).CreateGraph();
            ReplaceTravelHarmTriggers(exitGraph);
            LordToil exitStart = graph.AttachSubgraph(exitGraph).StartingToil;

            // 접근 중 공격받아 방어했다면 오래 머무르지 않고 떠난다.
            // target만 바꾸므로 기존 저장의 transition/trigger 순서와 상태 인덱스는 그대로 유지된다.
            Transition approachCalmed = approachGraph.transitions.FirstOrDefault(transition =>
                transition.triggers.Any(trigger => trigger is Trigger_TicksPassedWithoutHarm));
            if (approachCalmed != null)
            {
                approachCalmed.target = exitStart;
            }

            Transition arrived = new Transition(travelToEdge, linger);
            arrived.AddTrigger(new Trigger_Memo("TravelArrived"));
            graph.AddTransition(arrived);

            Transition leaveAfterLinger = new Transition(linger, exitStart);
            leaveAfterLinger.AddTrigger(new Trigger_TicksPassed(lingerTicks));
            leaveAfterLinger.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            leaveAfterLinger.AddPostAction(new TransitionAction_WakeAll());
            leaveAfterLinger.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(leaveAfterLinger);

            // 기존 Trigger_BecamePlayerEnemy의 순서는 유지하되, 도주 대신 바닐라 방문객식 국지 방어로 전환한다.
            Transition defendWhenHostile = new Transition(linger, defendLocalGroup, canMoveToSameState: true);
            foreach (LordToil toil in approachGraph.lordToils)
            {
                if (toil != defendLocalGroup)
                {
                    defendWhenHostile.AddSource(toil);
                }
            }
            defendWhenHostile.AddTrigger(new Trigger_BecamePlayerEnemy());
            defendWhenHostile.AddPreAction(new TransitionAction_SetDefendLocalGroup());
            defendWhenHostile.AddPostAction(new TransitionAction_WakeAll());
            defendWhenHostile.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(defendWhenHostile);

            // 이동 서브그래프의 harm 전환이 없는 대기/방어 상태에서도 체포 시도를 놓치지 않는다.
            // 기존 transition 뒤에 붙여 구형 세이브의 trigger data 인덱스를 보존한다.
            Transition defendWhenBetrayed = new Transition(linger, defendLocalGroup, canMoveToSameState: true);
            defendWhenBetrayed.AddSources(approachGraph.lordToils);
            defendWhenBetrayed.AddSources(exitGraph.lordToils);
            defendWhenBetrayed.AddTrigger(new Trigger_BeggarHarmed(playerBetrayalOnly: true));
            defendWhenBetrayed.AddPreAction(new TransitionAction_SetDefendLocalGroup());
            defendWhenBetrayed.AddPostAction(new TransitionAction_Custom(transition =>
                ApplyPendingPlayerHostility(transition.target?.lord)));
            defendWhenBetrayed.AddPostAction(new TransitionAction_WakeAll());
            defendWhenBetrayed.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(defendWhenBetrayed);
            return graph;
        }

        private static void ReplaceTravelHarmTriggers(StateGraph travelGraph)
        {
            foreach (Transition transition in travelGraph.transitions)
            {
                for (int i = 0; i < transition.triggers.Count; i++)
                {
                    if (transition.triggers[i].GetType() == typeof(Trigger_PawnHarmed))
                    {
                        transition.triggers[i] = new Trigger_BeggarHarmed(playerBetrayalOnly: false);
                        transition.AddPostAction(new TransitionAction_Custom(current =>
                            ApplyPendingPlayerHostility(current.target?.lord)));
                    }
                }
            }
        }

        internal void MarkPlayerHostilityPending()
        {
            playerHostilityPending = true;
        }

        private static void ApplyPendingPlayerHostility(Lord currentLord)
        {
            if (!(currentLord?.LordJob is LordJob_BeggarLingerAndExit beggarJob) || !beggarJob.playerHostilityPending)
            {
                return;
            }

            beggarJob.playerHostilityPending = false;
            MUGBBeggarFactionUtility.MakeHostileToPlayer(currentLord.faction);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref lingerSpot, "lingerSpot");
            Scribe_Values.Look(ref lingerTicks, "lingerTicks", 4 * GenDate.TicksPerHour);
        }
    }

    public class Trigger_BeggarHarmed : Trigger_PawnHarmed
    {
        private readonly bool playerBetrayalOnly;

        public Trigger_BeggarHarmed(bool playerBetrayalOnly)
        {
            this.playerBetrayalOnly = playerBetrayalOnly;
        }

        public override bool ActivateOn(Lord lord, TriggerSignal signal)
        {
            bool playerBetrayal = signal.type == TriggerSignalType.PawnArrestAttempted
                || signal.type == TriggerSignalType.PawnDamaged
                && signal.dinfo.Instigator?.Faction == Faction.OfPlayer;
            if (playerBetrayalOnly && !playerBetrayal)
            {
                return false;
            }
            if (!base.ActivateOn(lord, signal))
            {
                return false;
            }

            if (playerBetrayal && lord?.LordJob is LordJob_BeggarLingerAndExit beggarJob)
            {
                beggarJob.MarkPlayerHostilityPending();
            }
            return true;
        }
    }

    public static class MUGBBeggarFactionUtility
    {
        public static Faction CreateTemporaryBeggarFaction()
        {
            FactionDef factionDef = MUGBDefOf.MUGB_NeutralBeggarBand;
            if (factionDef == null || Find.FactionManager == null || Find.UniqueIDsManager == null)
            {
                return null;
            }

            // 이전 버전에서 빠진 관계만 보충한다. 이미 적대화된 방문 무리를 다시 중립으로 덮어쓰지 않는다.
            foreach (Faction existing in Find.FactionManager.AllFactionsListForReading
                .Where(candidate => candidate?.def == factionDef && candidate.temporary)
                .ToList())
            {
                RepairMissingBeggarRelations(existing);
            }

            Faction faction = new Faction
            {
                def = factionDef,
                loadID = Find.UniqueIDsManager.GetNextFactionID(),
                colorFromSpectrum = -999f,
                hidden = true,
                temporary = true,
                defeated = false,
                Name = "MUGB_NeutralBeggarBandFixedName".Translate()
            };

            // 바닐라 순서: 먼저 FactionManager에 등록하고 양쪽 FactionRelation 객체를 만든 뒤 관계 종류를 바꾼다.
            // 등록/초기화 전에 SetRelationDirect를 호출하면 상대 세력의 관계 목록이 null이라 오류가 세력 수만큼 반복된다.
            Find.FactionManager.Add(faction);

            InitializeBeggarRelations(faction);
            return faction;
        }

        private static void InitializeBeggarRelations(Faction faction)
        {
            // 한국어 의도: 플레이어에게는 중립, 고블린 세력에게는 적대. 다른 세력과의 관계는 이 방문에만 존재한다.
            //
            // 관계 객체를 Faction.relations 목록에 직접 넣습니다. 바닐라 API(TryMakeInitialRelationsWith,
            // SetRelationDirect)를 쓰지 않는 이유는 그 안에서 Faction.RelationWith를 부르기 때문입니다.
            // 갓 만든 임시 세력은 모든 세력과 관계가 비어 있어, Rim War가 걸어 둔 RelationWith
            // 프리픽스의 무한 재귀에 정확히 걸립니다. (자세한 설명은 MUGBFactionRelationSafety 참고)
            if (!MUGBFactionRelationSafety.Available)
            {
                InitializeBeggarRelationsFallback(faction);
                return;
            }

            foreach (Faction other in Find.FactionManager.AllFactionsListForReading.ToList())
            {
                if (other == null || other == faction)
                {
                    continue;
                }

                bool hostile = MGBFactionInjectionComponent.IsGoblinFaction(other);
                MUGBFactionRelationSafety.SetPair(
                    faction,
                    other,
                    hostile ? FactionRelationKind.Hostile : FactionRelationKind.Neutral,
                    hostile ? -100 : 0);
            }
        }

        private static void RepairMissingBeggarRelations(Faction faction)
        {
            if (!MUGBFactionRelationSafety.Available)
            {
                return;
            }

            foreach (Faction other in Find.FactionManager.AllFactionsListForReading.ToList())
            {
                if (other == null || other == faction)
                {
                    continue;
                }

                bool hostile = MGBFactionInjectionComponent.IsGoblinFaction(other);
                MUGBFactionRelationSafety.EnsurePair(
                    faction,
                    other,
                    hostile ? FactionRelationKind.Hostile : FactionRelationKind.Neutral,
                    hostile ? -100 : 0);
            }
        }

        public static void MakeHostileToPlayer(Faction faction)
        {
            Faction player = Faction.OfPlayer;
            if (faction == null || player == null)
            {
                return;
            }

            // 임시 세력에는 goodwill이 없으며 관계 객체도 생성 때 준비된다. 누락된 구형 세이브만 먼저 보충한다.
            if (MUGBFactionRelationSafety.Available)
            {
                MUGBFactionRelationSafety.EnsurePair(faction, player, FactionRelationKind.Neutral, 0);
            }
            else
            {
                faction.TryMakeInitialRelationsWith(player);
            }

            if (faction.HostileTo(player))
            {
                return;
            }

            faction.SetRelationDirect(player, FactionRelationKind.Hostile, canSendHostilityLetter: false);
        }

        /// <summary>
        /// 한국어 의도: 리플렉션이 실패할 때만 쓰는 예비 경로입니다.
        /// 바닐라 단독이라면 문제없이 동작하지만, Rim War가 있으면 위의 재귀에 걸릴 수 있습니다.
        /// </summary>
        private static void InitializeBeggarRelationsFallback(Faction faction)
        {
            foreach (Faction other in Find.FactionManager.AllFactionsListForReading.ToList())
            {
                if (other == null || other == faction)
                {
                    continue;
                }

                FactionRelationKind relation = MGBFactionInjectionComponent.IsGoblinFaction(other)
                    ? FactionRelationKind.Hostile
                    : FactionRelationKind.Neutral;
                faction.TryMakeInitialRelationsWith(other);
                faction.SetRelationDirect(other, relation, canSendHostilityLetter: false);
            }
        }

        public static void CleanupUnusedTemporaryFactions()
        {
            // FactionManager는 퀘스트 종료 때 임시 세력을 정리한다. 통과 사건은 퀘스트가 아니므로 같은 정리를 하루에 한 번만 호출한다.
            Find.FactionManager?.Notify_QuestCleanedUp(null);
        }
    }
}
