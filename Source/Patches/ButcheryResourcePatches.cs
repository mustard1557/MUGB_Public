using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(Corpse), nameof(Corpse.ButcherProducts))]
    public static class Corpse_ButcheryProducts_Patch
    {
        public static void Postfix(Corpse __instance, Pawn butcher, float efficiency, ref IEnumerable<Thing> __result)
        {
            __result = AddBoneProduct(__result, __instance, butcher, efficiency);
        }

        private static IEnumerable<Thing> AddBoneProduct(IEnumerable<Thing> originalProducts, Corpse corpse, Pawn butcher, float efficiency)
        {
            Pawn innerPawn = corpse?.InnerPawn;
            bool humanlike = innerPawn?.RaceProps?.Humanlike == true;
            bool goblinCookstationButchery = IsGoblinCookstationButchery(butcher);
            bool savorDisassembly = IsSavorHumanDisassembly(butcher);
            foreach (Thing product in originalProducts)
            {
                // Humanlike corpses use MUGB humanlike bone instead of Medieval Overhaul's generic bone.
                // Other Medieval Overhaul bone resources and all animal butchery products remain untouched.
                if (humanlike && product?.def?.defName == "DankPyon_Bone")
                {
                    continue;
                }

                Thing result;
                if (ShouldConvertGoblinMeat(innerPawn, product))
                {
                    Thing converted = ThingMaker.MakeThing(MUGBDefOf.Meat_Goblin);
                    converted.stackCount = product.stackCount;
                    result = converted;
                }
                else if (ShouldConvertGoblinLeather(innerPawn, product))
                {
                    Thing converted = ThingMaker.MakeThing(MUGBDefOf.MUGB_Gskin);
                    converted.stackCount = product.stackCount;
                    result = converted;
                }
                else
                {
                    result = product;
                }

                if (savorDisassembly && result?.def != null && result.def.stackLimit > 1)
                {
                    result.stackCount = GenMath.RoundRandom(result.stackCount * 1.12f);
                }

                yield return result;
            }

            if (goblinCookstationButchery || savorDisassembly)
            {
                Thing guts = MakeGutProduct(innerPawn, efficiency, savorDisassembly);
                if (guts != null)
                {
                    yield return guts;
                }
            }

            if (savorDisassembly && CanExtractSkull(innerPawn))
            {
                Thing skull = ThingMaker.MakeThing(ThingDefOf.Skull);
                skull.stackCount = 1;
                yield return skull;
            }

            // 한국어 의도: 인간형 시체는 미디블 일반 뼈 대신 인골만 얻습니다.
            // 일반 도축 < 고블린 조리대 도축 < 인간해체음미 순으로 인골 산출량이 증가합니다.
            float humanlikeBoneYieldFactor = savorDisassembly ? 0.26f : goblinCookstationButchery ? 0.2f : 0.12f;
            int amount = MUGBResourceUtility.HumanlikeBoneAmountFor(innerPawn, efficiency, humanlikeBoneYieldFactor);
            if (amount <= 0 || MUGBDefOf.MUGB_Bone == null)
            {
                yield break;
            }

            Thing bones = ThingMaker.MakeThing(MUGBDefOf.MUGB_Bone);
            bones.stackCount = amount;
            yield return bones;
        }

        private static bool ShouldConvertGoblinMeat(Pawn pawn, Thing product)
        {
            if (!GoblinUtility.IsGoblin(pawn) || product?.def == null || MUGBDefOf.Meat_Goblin == null)
            {
                return false;
            }

            ThingDef sourceMeat = pawn.RaceProps?.meatDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("Meat_Human");
            return product.def == sourceMeat || product.def.defName == "Meat_Human";
        }

        private static bool ShouldConvertGoblinLeather(Pawn pawn, Thing product)
        {
            if (!GoblinUtility.IsGoblin(pawn) || product?.def == null || MUGBDefOf.MUGB_Gskin == null)
            {
                return false;
            }

            ThingDef humanLeather = DefDatabase<ThingDef>.GetNamedSilentFail("Leather_Human");
            ThingDef sourceLeather = pawn.RaceProps?.leatherDef ?? humanLeather;
            return product.def == sourceLeather || product.def == humanLeather || product.def.defName == "Leather_Human";
        }

        private static bool IsGoblinCookstationButchery(Pawn butcher)
        {
            Job job = butcher?.CurJob;
            if (job?.RecipeDef == null)
            {
                return false;
            }

            Thing billGiver = job.GetTarget(TargetIndex.A).Thing;
            if (billGiver?.def != MUGBDefOf.MUGB_cookstation && billGiver?.def?.defName != "MUGB_cookstation")
            {
                return false;
            }

            string recipeName = job.RecipeDef.defName;
            return recipeName == "ButcherCorpseFlesh" || recipeName == "MUGB_ButcherCorpseGoblinCookstation";
        }

        private static bool IsSavorHumanDisassembly(Pawn butcher)
        {
            Job job = butcher?.CurJob;
            if (job?.RecipeDef == null || job.RecipeDef.defName != "MUGB_SavorHumanDisassembly")
            {
                return false;
            }

            Thing billGiver = job.GetTarget(TargetIndex.A).Thing;
            return billGiver?.def == MUGBDefOf.MUGB_cookstation || billGiver?.def?.defName == "MUGB_cookstation";
        }

        private static Thing MakeGutProduct(Pawn pawn, float efficiency, bool savorDisassembly)
        {
            if (pawn?.RaceProps?.Humanlike != true)
            {
                return null;
            }

            ThingDef gutDef = GoblinUtility.IsGoblin(pawn) ? MUGBDefOf.MUGB_Ggut : MUGBDefOf.MUGB_Hgut;
            if (gutDef == null)
            {
                return null;
            }

            float factor = savorDisassembly ? 0.16f : 0.1f;
            int amount = GenMath.RoundRandom(pawn.GetStatValue(StatDefOf.MeatAmount) * efficiency * factor);
            if (amount <= 0)
            {
                return null;
            }

            Thing guts = ThingMaker.MakeThing(gutDef);
            guts.stackCount = amount;
            return guts;
        }

        private static bool CanExtractSkull(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true
                && pawn.health?.hediffSet?.GetNotMissingParts()?.Any(part => part.def == BodyPartDefOf.Head) == true;
        }
    }
}
