using HarmonyLib;
using LudeonTK;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MUGB
{
    [HarmonyPatch(typeof(PreceptWorker), nameof(PreceptWorker.ThingDefsForIdeo), new System.Type[] { typeof(Ideo), typeof(FactionDef) })]
    public static class MUGB_ChildrenOfBliniaRelicChoicesPatch
    {
        private static readonly HashSet<string> GoblinRelicDefNames = new HashSet<string>
        {
            "MUGB_GoblinShamanStaffRelicA",
            "MUGB_GoblinShamanStaffRelicB"
        };

        public static void Postfix(PreceptWorker __instance, Ideo ideo, ref IEnumerable<PreceptThingChance> __result)
        {
            if (!(__instance is PreceptWorker_Relic)
                || !MUGBGoblinIdeologyUtility.HasGoblinCoreMeme(ideo)
                || __result == null)
            {
                return;
            }

            List<PreceptThingChance> goblinRelics = __result
                .Where(choice => choice.def?.relicChance > 0f
                    && (choice.def.weaponTags?.Contains("MUGB_GoblinWeapon") == true
                        || GoblinRelicDefNames.Contains(choice.def.defName)))
                .ToList();
            if (goblinRelics.Count > 0)
            {
                __result = goblinRelics;
            }
        }
    }

    [HarmonyPatch(typeof(Precept_Relic), nameof(Precept_Relic.GenerateRelic))]
    public static class MUGB_RelicSpecialWeaponPatch
    {
        public static void Postfix(ref Thing __result)
        {
            if (MUGBSpecialWeaponUtility.IsEligible(__result?.def))
            {
                MUGBSpecialWeaponUtility.Activate(__result, 2, 3);
            }
        }
    }

    [HarmonyPatch(typeof(Reward_Items), nameof(Reward_Items.InitFromValue))]
    public static class MUGB_QuestRewardSpecialWeaponPatch
    {
        private const float MinimumRewardValue = 700f;
        private const float SpecialRewardChance = 0.06f;

        public static void Postfix(Reward_Items __instance, float rewardValue, RewardsGeneratorParams parms, ref float valueActuallyUsed)
        {
            if (rewardValue < MinimumRewardValue
                || __instance.items.Any(x => x.def == ThingDefOf.PsychicAmplifier)
                || !Rand.Chance(SpecialRewardChance))
            {
                return;
            }

            List<ThingDef> candidates = MUGBSpecialWeaponUtility.EligibleDefNames
                .Select(DefDatabase<ThingDef>.GetNamedSilentFail)
                .Where(def => def != null
                    && (parms.disallowedThingDefs == null || !parms.disallowedThingDefs.Contains(def))
                    && def.BaseMarketValue <= rewardValue * 1.2f)
                .ToList();
            if (candidates.Count == 0)
            {
                return;
            }

            ThingDef weaponDef = candidates.RandomElement();
            ThingDef stuff = weaponDef.MadeFromStuff ? GenStuff.DefaultStuffFor(weaponDef) : null;
            Thing weapon = ThingMaker.MakeThing(weaponDef, stuff);
            MUGBSpecialWeaponUtility.Activate(weapon, 1, 3);
            if (weapon.TryGetComp<CompQuality>() is CompQuality quality)
            {
                quality.SetQuality(QualityUtility.GenerateFromGaussian(1f, QualityCategory.Masterwork, QualityCategory.Good, QualityCategory.Normal), ArtGenerationContext.Outsider);
            }

            float maxValue = rewardValue * 1.2f;
            if (weapon.MarketValue > maxValue)
            {
                weapon.Destroy();
                return;
            }

            __instance.items.Clear();
            __instance.items.Add(weapon);
            int silverCount = UnityEngine.Mathf.FloorToInt(maxValue - weapon.MarketValue);
            while (silverCount > 0)
            {
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = UnityEngine.Mathf.Min(silverCount, ThingDefOf.Silver.stackLimit);
                __instance.items.Add(silver);
                silverCount -= silver.stackCount;
            }
            valueActuallyUsed = __instance.TotalMarketValue;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class MUGB_SquadLeaderSpecialWeaponPatch
    {
        private static int lastGrantedTick = -1;

        public static void Postfix(ref Pawn __result)
        {
            Pawn pawn = __result;
            Faction playerFaction = Faction.OfPlayerSilentFail;
            string kindName = pawn?.kindDef?.defName;
            if (playerFaction == null
                || pawn?.Faction == null
                || pawn.Faction == playerFaction
                || !pawn.Faction.HostileTo(playerFaction)
                || kindName.NullOrEmpty()
                || kindName.IndexOf("SquadLeader", System.StringComparison.OrdinalIgnoreCase) < 0
                || !MUGBSpecialWeaponUtility.IsEligible(pawn.equipment?.Primary?.def)
                || Find.TickManager == null
                || lastGrantedTick == Find.TickManager.TicksGame)
            {
                return;
            }

            float chance = kindName.IndexOf("Cultist", System.StringComparison.OrdinalIgnoreCase) >= 0 ? 0.01f : 0.005f;
            if (Rand.Chance(chance))
            {
                MUGBSpecialWeaponUtility.Activate(pawn.equipment.Primary, 1, 3);
                lastGrantedTick = Find.TickManager.TicksGame;
            }
        }
    }

    public static class MUGB_SpecialWeaponDebugActions
    {
        [DebugAction("MUGB", "Spawn all special weapons", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnAllSpecialWeapons()
        {
            IntVec3 cell = UI.MouseCell();
            foreach (string defName in MUGBSpecialWeaponUtility.EligibleDefNames)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null) continue;
                ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
                Thing thing = ThingMaker.MakeThing(def, stuff);
                MUGBSpecialWeaponUtility.Activate(thing, 3, 3);
                GenPlace.TryPlaceThing(thing, cell, Find.CurrentMap, ThingPlaceMode.Near);
            }
        }
    }
}
