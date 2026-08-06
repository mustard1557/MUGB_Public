using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MUGB.Patches
{
    public static class HumanlikeBoneGearUtility
    {
        private static readonly Dictionary<ThingDef, bool> FixedHumanlikeBoneCache = new Dictionary<ThingDef, bool>();

        public static bool ContainsHumanlikeBone(Thing thing)
        {
            if (thing?.def == null)
            {
                return false;
            }

            if (thing.Stuff == MUGBDefOf.MUGB_Bone)
            {
                return true;
            }

            if (FixedHumanlikeBoneCache.TryGetValue(thing.def, out bool cached))
            {
                return cached;
            }

            bool containsHumanlikeBone = false;
            List<ThingDefCountClass> costs = thing.def.costList;
            if (costs != null)
            {
                for (int i = 0; i < costs.Count; i++)
                {
                    if (costs[i]?.thingDef == MUGBDefOf.MUGB_Bone)
                    {
                        containsHumanlikeBone = true;
                        break;
                    }
                }
            }

            FixedHumanlikeBoneCache[thing.def] = containsHumanlikeBone;
            return containsHumanlikeBone;
        }
    }

    [HarmonyPatch(typeof(ThoughtWorker_HumanLeatherApparel), nameof(ThoughtWorker_HumanLeatherApparel.CurrentThoughtState))]
    public static class ThoughtWorker_HumanLeatherApparel_HumanlikeBonePatch
    {
        public static void Postfix(Pawn p, ref ThoughtState __result)
        {
            string reason = __result.Reason;
            int count = __result.StageIndex < 0 ? 0 : __result.StageIndex + 1;
            ThingDef humanLeather = ThingDefOf.Human?.race?.leatherDef;

            List<Apparel> wornApparel = p?.apparel?.WornApparel;
            if (wornApparel != null)
            {
                for (int i = 0; i < wornApparel.Count; i++)
                {
                    // Vanilla already counted apparel made from human leather; do not count the same apparel twice.
                    if (wornApparel[i].Stuff == humanLeather || !HumanlikeBoneGearUtility.ContainsHumanlikeBone(wornApparel[i]))
                    {
                        continue;
                    }

                    reason = reason ?? wornApparel[i].def.label;
                    count++;
                }
            }

            // 한국어 의도: 인골이 들어간 무기도 인간가죽 장비와 같은 거부감/이념 판정을 받습니다.
            ThingWithComps primary = p?.equipment?.Primary;
            if (HumanlikeBoneGearUtility.ContainsHumanlikeBone(primary))
            {
                reason = reason ?? primary.def.label;
                count++;
            }

            if (count == 0)
            {
                __result = ThoughtState.Inactive;
            }
            else if (count >= 5)
            {
                __result = ThoughtState.ActiveAtStage(4, reason);
            }
            else
            {
                __result = ThoughtState.ActiveAtStage(count - 1, reason);
            }

        }
    }
}
