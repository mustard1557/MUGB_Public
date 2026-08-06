using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace MUGB
{
    // 한국어 의도: 이데올로기 작업장(WorkSite)에 잡부 4~5명을 얹는다.
    // 고블린은 초반 인육 공급처가 사실상 없다시피 한데, 바닐라 작업장은 자원 전리품 위주로 짜여 있어
    // 사람 수가 적다. 자원 대신 사람을 늘려서 작업장을 "식량 사냥터"로 만드는 것이 목적이다.
    // 전투 난이도를 올리려는 게 아니므로 이들은 위협 예산(threatPoints)에 잡히지 않고,
    // 무장은 맨손~나무 곤봉, 전투 스킬은 아래에서 0~2로 깎는다.
    public static class MUGBWorkSiteStragglerUtility
    {
        public const int MinStragglers = 4;
        public const int MaxStragglers = 5;

        // 사이트 설명에 뜨는 인원수와 실제 맵에 생성되는 인원수가 어긋나지 않도록,
        // 사이트 고유 시드에서 결정론적으로 뽑는다. 같은 사이트면 항상 같은 값이 나온다.
        public static int StragglerCountFor(SitePartParams parms)
        {
            if (parms == null)
            {
                return 0;
            }

            Rand.PushState(OutpostSitePartUtility.GetPawnGroupMakerSeed(parms) ^ 0x5B17E1);
            try
            {
                return Rand.RangeInclusive(MinStragglers, MaxStragglers);
            }
            finally
            {
                Rand.PopState();
            }
        }

        public static PawnKindDef RandomStragglerKind()
        {
            // 맨손 잡부가 다수, 나무 곤봉을 든 쪽이 소수.
            string defName = Rand.Chance(0.7f) ? "MUGB_WorkSiteLaborer" : "MUGB_WorkSiteHand";
            return DefDatabase<PawnKindDef>.GetNamedSilentFail(defName);
        }

        // 한국어 의도: 배후 이야기에서 굴러온 전투 스킬을 깎아 확실히 형편없게 만든다.
        // PawnKindDef에는 스킬 필드가 없어 생성 후에 손대는 수밖에 없다.
        public static void MakeCombatIncompetent(Pawn pawn)
        {
            if (pawn?.skills == null)
            {
                return;
            }

            foreach (SkillDef skillDef in new[] { SkillDefOf.Melee, SkillDefOf.Shooting })
            {
                SkillRecord skill = pawn.skills.GetSkill(skillDef);
                if (skill == null || skill.TotallyDisabled)
                {
                    continue;
                }

                skill.Level = Rand.RangeInclusive(0, 2);
                skill.passion = Passion.None;
                skill.xpSinceLastLevel = 0f;
            }
        }
    }

    public class GenStep_MUGBWorkSiteStragglers : GenStep
    {
        public override int SeedPart => 604187733;

        public override void Generate(Map map, GenStepParams parms)
        {
            Faction faction = parms.sitePart?.site?.Faction;
            if (faction == null)
            {
                return;
            }

            int count = MUGBWorkSiteStragglerUtility.StragglerCountFor(parms.sitePart.parms);
            if (count <= 0)
            {
                return;
            }

            // 바닐라 GenStep_WorkSitePawns(order 406)가 이미 만들어 둔 방어 Lord에 합류시킨다.
            // 별도 Lord를 만들면 잡부들이 기지를 지키지 않고 따로 논다.
            Lord lord = map.lordManager.lords.FirstOrDefault(candidate =>
                candidate.faction == faction && candidate.LordJob is LordJob_DefendBase);
            if (lord == null)
            {
                return;
            }

            IntVec3 center = MapGenerator.TryGetVar<CellRect>("RectOfInterest", out CellRect rect)
                ? rect.CenterCell
                : map.Center;
            TraverseParms traverseParms = TraverseParms.For(TraverseMode.PassDoors);

            for (int i = 0; i < count; i++)
            {
                PawnKindDef kind = MUGBWorkSiteStragglerUtility.RandomStragglerKind();
                if (kind == null)
                {
                    continue;
                }

                if (!CellFinder.TryFindRandomCellNear(center, map, 12,
                        cell => cell.Standable(map) && map.reachability.CanReachMapEdge(cell, traverseParms),
                        out IntVec3 cell))
                {
                    continue;
                }

                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction,
                    PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, mustBeCapableOfViolence: false));
                MUGBWorkSiteStragglerUtility.MakeCombatIncompetent(pawn);
                GenSpawn.Spawn(pawn, cell, map);
                lord.AddPawn(pawn);
            }
        }
    }
}
