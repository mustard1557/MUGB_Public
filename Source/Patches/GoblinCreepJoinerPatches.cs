using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(CreepJoinerUtility), nameof(CreepJoinerUtility.GenerateAndSpawn),
        new Type[]
        {
            typeof(CreepJoinerFormKindDef),
            typeof(CreepJoinerBenefitDef),
            typeof(CreepJoinerDownsideDef),
            typeof(CreepJoinerAggressiveDef),
            typeof(CreepJoinerRejectionDef),
            typeof(Map)
        })]
    public static class CreepJoinerUtility_GoblinVariantPatch
    {
        private const string PlayerGoblinFactionDefName = "MUGB_PlayerGoblinFaction";
        private const float GoblinPlayerFactionVariantChance = 0.80f;
        private const float OtherPlayerFactionVariantChance = 0.10f;
        private const float HobgoblinChance = 0.20f;

        public static bool Prepare()
        {
            return ModsConfig.AnomalyActive;
        }

        public static void Postfix(CreepJoinerFormKindDef form, CreepJoinerBenefitDef benefit, Map map, ref Pawn __result)
        {
            Pawn pawn = __result;
            float goblinVariantChance = IsGoblinPlayerFaction()
                ? GoblinPlayerFactionVariantChance
                : OtherPlayerFactionVariantChance;
            if (pawn == null
                || pawn.genes == null
                || GoblinUtility.IsGoblin(pawn)
                || !Rand.ChanceSeeded(goblinVariantChance, pawn.thingIDNumber ^ 0x43524545))
            {
                return;
            }

            XenotypeDef xenotype = Rand.ChanceSeeded(HobgoblinChance, pawn.thingIDNumber ^ 0x484F4247)
                ? MUGBDefOf.MUGB_Hobgoblin
                : MUGBDefOf.MUGB_Goblin;
            if (xenotype == null)
            {
                return;
            }

            pawn.gender = Gender.Male;
            pawn.genes.SetXenotype(xenotype);
            TryAddCrossEyedGene(pawn);
            ApplyFormAppropriateAge(pawn, form);
            GoblinAgeUtility.RemovePrematureAgeHediffs(pawn);
            GoblinUtility.EnforceGoblinStoryGraphics(pawn);
            GoblinPersonalNameUtility.TryApplyKoreanGoblinName(pawn, enforceGeneratedFormat: true);
            ApplyCreepJoinerBackstories(pawn, form, benefit);
            MarkGeneratedCreepJoinerAsMature(pawn);
            ReplaceGear(pawn, map);
            pawn.Notify_DisabledWorkTypesChanged();
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        private static bool IsGoblinPlayerFaction()
        {
            return Faction.OfPlayerSilentFail?.def?.defName == PlayerGoblinFactionDefName;
        }

        private static void TryAddCrossEyedGene(Pawn pawn)
        {
            if (MUGBDefOf.MUGB_Gene_CrossEyed != null
                && pawn.genes.GetGene(MUGBDefOf.MUGB_Gene_CrossEyed) == null
                && Rand.ChanceSeeded(0.25f, pawn.thingIDNumber ^ 0x4D554742))
            {
                pawn.genes.AddGene(MUGBDefOf.MUGB_Gene_CrossEyed, xenogene: false);
            }
        }

        private static void ApplyFormAppropriateAge(Pawn pawn, CreepJoinerFormKindDef form)
        {
            int seed = pawn.thingIDNumber ^ 0x41474543;
            FloatRange range;
            switch (form?.defName)
            {
                case "TimelessOne":
                    range = new FloatRange(18f, 19.5f);
                    break;
                case "DealMaker":
                    range = new FloatRange(18f, 28f);
                    break;
                case "CreepDrifter":
                    range = new FloatRange(18f, 32f);
                    break;
                case "CultEscapee":
                    range = new FloatRange(18f, 36f);
                    break;
                case "LoneGenius":
                    range = new FloatRange(24f, 38f);
                    break;
                case "Blindhealer":
                    range = new FloatRange(34f, 39.5f);
                    break;
                case "LeatheryStranger":
                case "DarkScholar":
                    range = new FloatRange(30f, 39.5f);
                    break;
                default:
                    range = new FloatRange(18f, 36f);
                    break;
            }

            float biologicalYears = Rand.RangeSeeded(range.min, range.max, seed);
            float chronologicalYears = Mathf.Min(
                biologicalYears + Rand.RangeSeeded(0f, 2f, seed ^ 0x4348524F),
                GoblinAgeUtility.LifeExpectancyYears - 0.1f);
            pawn.ageTracker.AgeBiologicalTicks = GoblinAgeUtility.TicksForYears(biologicalYears);
            pawn.ageTracker.AgeChronologicalTicks = GoblinAgeUtility.TicksForYears(chronologicalYears);
        }

        private static void ApplyCreepJoinerBackstories(Pawn pawn, CreepJoinerFormKindDef form, CreepJoinerBenefitDef benefit)
        {
            BackstoryDef childhood = PickChildhood(pawn, form, benefit);
            if (childhood != null)
            {
                pawn.story.Childhood = childhood;
            }

            if (pawn.ageTracker.AgeBiologicalYearsFloat < GoblinAgeUtility.AdultAgeYears)
            {
                pawn.story.Adulthood = null;
                return;
            }

            BackstoryDef adulthood = PickAdulthood(pawn, form, benefit);
            if (adulthood != null)
            {
                pawn.story.Adulthood = adulthood;
            }
        }

        private static void MarkGeneratedCreepJoinerAsMature(Pawn pawn)
        {
            GoblinRapidMaturationComponent component = Current.Game?.GetComponent<GoblinRapidMaturationComponent>();
            if (component == null)
            {
                return;
            }
            component.MarkTeenMatured(pawn);
            component.MarkMatured(pawn);
        }

        private static BackstoryDef PickChildhood(Pawn pawn, CreepJoinerFormKindDef form, CreepJoinerBenefitDef benefit)
        {
            Dictionary<string, float> weights = new Dictionary<string, float>
            {
                ["MUGB_Backstory_Child_CreepMachineMotherWhelp"] = 0.65f,
                ["MUGB_Backstory_Child_CreepDampCaveOrphan"] = 1f,
                ["MUGB_Backstory_Child_CreepRabbitShrineMaidenChild"] = 0.65f,
                ["MUGB_Backstory_Child_CreepFleshbeastChild"] = 0.85f,
                ["MUGB_Backstory_Child_CreepDiscardedTestSubject"] = 1f,
                ["MUGB_Backstory_Child_CreepMegaspider"] = 0.8f
            };

            string benefitName = benefit?.defName;
            string formName = form?.defName;
            if (benefitName == "PerfectHuman" || benefitName == "BodyMastery" || formName == "LoneGenius")
            {
                AddWeight(weights, "MUGB_Backstory_Child_CreepMachineMotherWhelp", 1.6f);
                AddWeight(weights, "MUGB_Backstory_Child_CreepDiscardedTestSubject", 1.4f);
            }
            if (benefitName == "UnnaturalHealing" || benefitName == "Occultist" || benefitName == "Joybringer" || formName == "Blindhealer")
            {
                AddWeight(weights, "MUGB_Backstory_Child_CreepRabbitShrineMaidenChild", 1.8f);
            }
            if (benefitName == "Fleshcrafter" || benefitName == "ShamblerOverlord" || benefitName == "PsychicButcher" || formName == "CultEscapee")
            {
                AddWeight(weights, "MUGB_Backstory_Child_CreepFleshbeastChild", 2f);
            }
            if (benefitName == "Alchemist" || formName == "LeatheryStranger" || formName == "CreepDrifter")
            {
                AddWeight(weights, "MUGB_Backstory_Child_CreepDampCaveOrphan", 1.3f);
            }

            return PickWeightedBackstory(weights, pawn.thingIDNumber ^ 0x4348494C);
        }

        private static BackstoryDef PickAdulthood(Pawn pawn, CreepJoinerFormKindDef form, CreepJoinerBenefitDef benefit)
        {
            Dictionary<string, float> weights = new Dictionary<string, float>
            {
                ["MUGB_Backstory_Adult_CreepWanderingGoblinShaman"] = 0.8f,
                ["MUGB_Backstory_Adult_CreepTaciturnGoblin"] = 1.4f,
                ["MUGB_Backstory_Adult_CreepWrigglingLoincloth"] = 0.6f,
                ["MUGB_Backstory_Adult_CreepEerieGoblinWanderer"] = 1f,
                ["MUGB_Backstory_Adult_CreepFleshWhisperer"] = 0.05f
            };

            string benefitName = benefit?.defName;
            string formName = form?.defName;
            if (benefitName == "Occultist" || benefitName == "UnnaturalHealing" || benefitName == "Alchemist")
            {
                AddWeight(weights, "MUGB_Backstory_Adult_CreepWanderingGoblinShaman", 2.2f);
            }
            if (benefitName == "Fleshcrafter" || benefitName == "ShamblerOverlord" || benefitName == "PsychicButcher")
            {
                AddWeight(weights, "MUGB_Backstory_Adult_CreepWanderingGoblinShaman", 1.2f);
                AddWeight(weights, "MUGB_Backstory_Adult_CreepFleshWhisperer", 4f);
            }
            if (benefitName == "BodyMastery" || benefitName == "DeathRefusal" || formName == "CultEscapee")
            {
                AddWeight(weights, "MUGB_Backstory_Adult_CreepWrigglingLoincloth", 1.8f);
            }
            if (formName == "LeatheryStranger" || formName == "DarkScholar" || formName == "Blindhealer")
            {
                AddWeight(weights, "MUGB_Backstory_Adult_CreepEerieGoblinWanderer", 2.2f);
                AddWeight(weights, "MUGB_Backstory_Adult_CreepTaciturnGoblin", 1f);
            }

            return PickWeightedBackstory(weights, pawn.thingIDNumber ^ 0x4144554C);
        }

        private static void AddWeight(Dictionary<string, float> weights, string defName, float amount)
        {
            weights[defName] = weights.TryGetValue(defName, out float current) ? current + amount : amount;
        }

        private static BackstoryDef PickWeightedBackstory(Dictionary<string, float> weights, int seed)
        {
            List<WeightedBackstory> candidates = new List<WeightedBackstory>();
            foreach (KeyValuePair<string, float> pair in weights)
            {
                BackstoryDef backstory = DefDatabase<BackstoryDef>.GetNamedSilentFail(pair.Key);
                if (backstory != null && pair.Value > 0f)
                {
                    candidates.Add(new WeightedBackstory(backstory, pair.Value));
                }
            }
            if (candidates.Count == 0)
            {
                return null;
            }

            Rand.PushState(seed);
            try
            {
                return candidates.RandomElementByWeight(candidate => candidate.weight).backstory;
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static void ReplaceGear(Pawn pawn, Map map)
        {
            pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
            if (pawn.apparel != null)
            {
                foreach (Apparel apparel in pawn.apparel.WornApparel.ToList())
                {
                    pawn.apparel.Remove(apparel);
                    apparel.Destroy(DestroyMode.Vanish);
                }
            }

            if (pawn.inventory?.innerContainer != null)
            {
                for (int i = pawn.inventory.innerContainer.Count - 1; i >= 0; i--)
                {
                    Thing thing = pawn.inventory.innerContainer[i];
                    if (thing?.def?.IsWeapon == true || thing?.def?.IsApparel == true)
                    {
                        pawn.inventory.innerContainer.Remove(thing);
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
            }

            ThingDef humanLeather = DefDatabase<ThingDef>.GetNamedSilentFail("Leather_Human");
            Wear(pawn, "MUGB_Apparel_GoblinLoincloth", QualityCategory.Poor, humanLeather);
            string cloakDefName = ChooseCloakDefName(pawn, map);
            Wear(pawn, cloakDefName, QualityCategory.Poor, humanLeather);
            if (Rand.ChanceSeeded(0.5f, pawn.thingIDNumber ^ 0x4D41534B))
            {
                Wear(pawn, "MUGB_Apparel_CultSkinMask", QualityCategory.Poor, null);
            }

            ThingDef staffDef = DefDatabase<ThingDef>.GetNamedSilentFail("MUGB_GoblinShamanStaff");
            ThingWithComps staff = MakeThing(staffDef, QualityCategory.Normal, null) as ThingWithComps;
            if (staff != null)
            {
                if (pawn.equipment != null)
                {
                    pawn.equipment.AddEquipment(staff);
                }
                else
                {
                    staff.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static string ChooseCloakDefName(Pawn pawn, Map map)
        {
            if (map?.mapTemperature != null && map.mapTemperature.OutdoorTemp < 5f)
            {
                return "MUGB_Apparel_HumanHideMantle";
            }
            return Rand.ChanceSeeded(0.5f, pawn.thingIDNumber ^ 0x434C4F41)
                ? "MUGB_Apparel_HumanHideMantle"
                : "MUGB_Apparel_HumanHideCapeB";
        }

        private static void Wear(Pawn pawn, string defName, QualityCategory quality, ThingDef preferredStuff)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            Apparel apparel = MakeThing(def, quality, preferredStuff) as Apparel;
            if (apparel == null)
            {
                return;
            }
            if (pawn.apparel == null || !ApparelUtility.HasPartsToWear(pawn, def))
            {
                apparel.Destroy(DestroyMode.Vanish);
                return;
            }
            pawn.apparel.Wear(apparel, dropReplacedApparel: true);
        }

        private static Thing MakeThing(ThingDef def, QualityCategory quality, ThingDef preferredStuff)
        {
            if (def == null)
            {
                return null;
            }
            ThingDef stuff = def.MadeFromStuff
                ? (preferredStuff?.stuffProps?.CanMake(def) == true
                    ? preferredStuff
                    : GenStuff.DefaultStuffFor(def))
                : null;
            Thing thing = ThingMaker.MakeThing(def, stuff);
            thing.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Colony);
            return thing;
        }

        private readonly struct WeightedBackstory
        {
            public readonly BackstoryDef backstory;
            public readonly float weight;

            public WeightedBackstory(BackstoryDef backstory, float weight)
            {
                this.backstory = backstory;
                this.weight = weight;
            }
        }
    }
}
