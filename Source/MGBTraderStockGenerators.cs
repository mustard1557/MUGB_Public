using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MUGB
{
    public class StockGenerator_MUGB_SpecialWeapon : StockGenerator
    {
        public float chance = 0.03f;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            if (!Rand.Chance(chance))
            {
                yield break;
            }

            ThingDef def = MUGBSpecialWeaponUtility.EligibleDefNames
                .Select(DefDatabase<ThingDef>.GetNamedSilentFail)
                .Where(x => x != null)
                .RandomElement();
            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            Thing weapon = ThingMaker.MakeThing(def, stuff);
            MUGBSpecialWeaponUtility.Activate(weapon, 1, 3);
            if (weapon.TryGetComp<CompQuality>() is CompQuality quality)
            {
                quality.SetQuality(QualityUtility.GenerateFromGaussian(1f, QualityCategory.Masterwork, QualityCategory.Good, QualityCategory.Normal), ArtGenerationContext.Outsider);
            }
            yield return weapon;
        }

        public override bool HandlesThingDef(ThingDef thingDef) => MUGBSpecialWeaponUtility.IsEligible(thingDef);
    }

    // KO intent: 고블린 상단은 노예를 팔되, 원자재/암시장/가축상 쪽은 비고블린 노예가 안정적으로 섞여야 한다.
    // 바닐라 StockGenerator_Slaves에는 "고블린 제외" 필터가 없어 고블린 상단 전용 생성기로 보장한다.
    public class StockGenerator_MUGB_NonGoblinSlaves : StockGenerator
    {
        public PawnKindDef slaveKindDef;
        public bool forceFemale;
        public float minAge = 18f;
        public float maxAge = 999f;
        public int maxGenerationAttempts = 12;
        public bool includeHARFemalePool;
        public float harSlaveChance = 0.45f;
        public bool forceBeautyTrait;
        public int beautyTraitDegree = 2;

        private static List<PawnKindDef> cachedHARHumanlikeSlaveKinds;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            int targetCount = countRange.RandomInRange;
            for (int i = 0; i < targetCount; i++)
            {
                Pawn pawn = TryGenerateNonGoblinSlave(forTile, faction);
                if (pawn != null)
                {
                    yield return pawn;
                }
            }
        }

        public override bool HandlesThingDef(ThingDef thingDef)
        {
            return thingDef == ThingDefOf.Human || GoblinUtility.IsHARAlienRaceDef(thingDef);
        }

        private Pawn TryGenerateNonGoblinSlave(PlanetTile forTile, Faction faction)
        {
            for (int attempt = 0; attempt < maxGenerationAttempts; attempt++)
            {
                PawnKindDef kindDef = PawnKindForAttempt();
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kindDef,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    forTile,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: false,
                    colonistRelationChanceFactor: 0f,
                    developmentalStages: DevelopmentalStage.Adult,
                    fixedGender: forceFemale ? Gender.Female : (Gender?)null);

                // The trader faction's xenotypeSet is goblin-only. Vanilla human slaves
                // must explicitly remain baseliners or the faction assignment gives them
                // an inactive female goblin body-plan gene while they still look human.
                if (kindDef?.race == ThingDefOf.Human)
                {
                    request.ForcedXenotype = XenotypeDefOf.Baseliner;
                }

                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    continue;
                }

                if (!GoblinUtility.HasGoblinCoreMarker(pawn) && PassesSlaveFilters(pawn))
                {
                    if (forceBeautyTrait)
                    {
                        ForceBeautyTrait(pawn);
                    }
                    MUGBTraderPawnUtility.MarkPawnAsTraderSlave(pawn);
                    DressNonGoblinSlaveForSale(pawn);
                    return pawn;
                }

                pawn.Destroy();
            }

            return null;
        }

        private PawnKindDef PawnKindForAttempt()
        {
            if (includeHARFemalePool && Rand.Chance(Mathf.Clamp01(harSlaveChance)))
            {
                PawnKindDef harKind = RandomHARHumanlikeSlaveKind();
                if (harKind != null)
                {
                    return harKind;
                }
            }

            return slaveKindDef ?? PawnKindDefOf.Slave;
        }

        private static PawnKindDef RandomHARHumanlikeSlaveKind()
        {
            List<PawnKindDef> candidates = HARHumanlikeSlaveKinds();
            return candidates.NullOrEmpty() ? null : candidates.RandomElement();
        }

        private static List<PawnKindDef> HARHumanlikeSlaveKinds()
        {
            if (cachedHARHumanlikeSlaveKinds != null)
            {
                return cachedHARHumanlikeSlaveKinds;
            }

            cachedHARHumanlikeSlaveKinds = DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(kind => kind?.race?.race?.Humanlike == true
                    && kind.race != ThingDefOf.Human
                    && !kind.defName.StartsWith("MUGB_")
                    && GoblinUtility.IsHARAlienRaceDef(kind.race))
                .ToList();

            return cachedHARHumanlikeSlaveKinds;
        }

        private bool PassesSlaveFilters(Pawn pawn)
        {
            if (forceFemale && pawn.gender != Gender.Female)
            {
                return false;
            }

            float age = pawn.ageTracker?.AgeBiologicalYearsFloat ?? 999f;
            return age >= minAge && age <= maxAge;
        }

        private void ForceBeautyTrait(Pawn pawn)
        {
            if (pawn?.story?.traits == null)
            {
                return;
            }

            TraitDef beauty = DefDatabase<TraitDef>.GetNamedSilentFail("Beauty");
            if (beauty == null)
            {
                return;
            }

            for (int i = pawn.story.traits.allTraits.Count - 1; i >= 0; i--)
            {
                if (pawn.story.traits.allTraits[i]?.def == beauty)
                {
                    pawn.story.traits.allTraits.RemoveAt(i);
                }
            }

            pawn.story.traits.GainTrait(new Trait(beauty, beautyTraitDegree));
        }

        private static void DressNonGoblinSlaveForSale(Pawn pawn)
        {
            // KO intent: 고블린 상단의 비고블린 노예는 고블린 몸싸개가 아니라 바닐라 노예 몸싸개를 입힌다.
            // HAR 종족은 바닐라 몸싸개가 종족별 의류 그래픽과 어긋날 수 있어 PawnKind가 입혀온 옷을 존중한다.
            if (!GoblinUtility.IsHARAlienRace(pawn))
            {
                TryWearSlaveApparel(pawn, "Apparel_BodyStrap", QualityCategory.Poor);
            }

            TryWearSlaveApparel(pawn, "Apparel_Collar", QualityCategory.Normal);
        }

        private static void TryWearSlaveApparel(Pawn pawn, string defName, QualityCategory quality)
        {
            if (pawn?.apparel == null)
            {
                return;
            }

            ThingDef apparelDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (apparelDef == null || !pawn.apparel.CanWearWithoutDroppingAnything(apparelDef))
            {
                return;
            }

            ThingDef stuff = null;
            if (apparelDef.MadeFromStuff)
            {
                stuff = apparelDef.stuffCategories?.Contains(StuffCategoryDefOf.Fabric) == true ? ThingDefOf.Cloth : GenStuff.RandomStuffFor(apparelDef);
                if (stuff == null || !AllowsStuff(apparelDef, stuff))
                {
                    stuff = GenStuff.RandomStuffFor(apparelDef);
                }
            }

            Apparel apparel = (Apparel)ThingMaker.MakeThing(apparelDef, stuff);
            apparel.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);
            pawn.apparel.Wear(apparel, dropReplacedApparel: false);
        }

        private static bool AllowsStuff(ThingDef apparelDef, ThingDef stuff)
        {
            if (apparelDef?.stuffCategories == null || stuff?.stuffProps?.categories == null)
            {
                return false;
            }

            return apparelDef.stuffCategories.Any(category => stuff.stuffProps.categories.Contains(category));
        }
    }

    // KO intent: 고블린 상단은 비고블린 노예만이 아니라 고블린 노예도 상품으로 끌고 와야 한다.
    // 특히 가축 상단은 고블린 노예 3~4마리를 보장한다.
    public class StockGenerator_MUGB_GoblinSlaves : StockGenerator
    {
        public PawnKindDef slaveKindDef;
        public bool forceFemale;
        public float minAge = 0f;
        public float maxAge = 999f;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            int targetCount = countRange.RandomInRange;
            for (int i = 0; i < targetCount; i++)
            {
                Pawn pawn = TryGenerateGoblinSlave(forTile, faction);
                if (pawn != null)
                {
                    yield return pawn;
                }
            }
        }

        public override bool HandlesThingDef(ThingDef thingDef)
        {
            return thingDef == ThingDefOf.Human;
        }

        private Pawn TryGenerateGoblinSlave(PlanetTile forTile, Faction faction)
        {
            PawnKindDef kindDef = slaveKindDef ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_GoblinKind_Slave") ?? PawnKindDefOf.Slave;

            for (int attempt = 0; attempt < 24; attempt++)
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kindDef,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    forTile,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: false,
                    colonistRelationChanceFactor: 0f,
                    developmentalStages: DevelopmentalStage.Adult);

                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    continue;
                }

                if (GoblinUtility.IsGoblin(pawn) && PassesSlaveFilters(pawn))
                {
                    MUGBTraderPawnUtility.MarkPawnAsTraderSlave(pawn);
                    return pawn;
                }

                pawn.Destroy();
            }

            return null;
        }

        private bool PassesSlaveFilters(Pawn pawn)
        {
            if (forceFemale && pawn.gender != Gender.Female)
            {
                return false;
            }

            float age = pawn.ageTracker?.AgeBiologicalYearsFloat ?? 999f;
            return age >= minAge && age <= maxAge;
        }
    }

    // KO intent: 고블린 상단의 고블린 상품은 노예로 팔리는 경우도 있고, 정착민으로 합류하는 경우도 있어야 한다.
    // 비고블린은 반드시 노예 상품으로만 유지하고, 정착민 합류형은 고블린 전용 생성기로 분리한다.
    public class StockGenerator_MUGB_GoblinRecruits : StockGenerator
    {
        public PawnKindDef recruitKindDef;
        public bool forceFemale;
        public float minAge = 0f;
        public float maxAge = 999f;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            int targetCount = countRange.RandomInRange;
            for (int i = 0; i < targetCount; i++)
            {
                Pawn pawn = TryGenerateGoblinRecruit(forTile, faction);
                if (pawn != null)
                {
                    yield return pawn;
                }
            }
        }

        public override bool HandlesThingDef(ThingDef thingDef)
        {
            return thingDef == ThingDefOf.Human;
        }

        private Pawn TryGenerateGoblinRecruit(PlanetTile forTile, Faction faction)
        {
            PawnKindDef kindDef = recruitKindDef ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_GoblinKind_Beggar") ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("MUGB_GoblinBareBrawler") ?? PawnKindDefOf.Slave;

            for (int attempt = 0; attempt < 24; attempt++)
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kindDef,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    forTile,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: false,
                    colonistRelationChanceFactor: 0f,
                    developmentalStages: DevelopmentalStage.Adult);

                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    continue;
                }

                if (GoblinUtility.IsGoblin(pawn) && PassesRecruitFilters(pawn))
                {
                    // 노예 guest 상태를 주지 않는다. 거래로 산 뒤 정착민 합류형으로 처리되게 하는 고블린 전용 상품이다.
                    return pawn;
                }

                pawn.Destroy();
            }

            return null;
        }

        private bool PassesRecruitFilters(Pawn pawn)
        {
            if (forceFemale && pawn.gender != Gender.Female)
            {
                return false;
            }

            float age = pawn.ageTracker?.AgeBiologicalYearsFloat ?? 999f;
            return age >= minAge && age <= maxAge;
        }
    }

    internal static class MUGBTraderPawnUtility
    {
        public static void MarkPawnAsTraderSlave(Pawn pawn)
        {
            // KO intent: 고블린 상단이 파는 비고블린은 정착민 합류 상품이 아니라 노예 상품이어야 한다.
            // 정착민 합류형 상품은 추후 고블린 전용 생성기로 따로 만들 때만 허용한다.
            if (pawn?.guest != null && pawn.Faction != null)
            {
                pawn.guest.SetGuestStatus(pawn.Faction, GuestStatus.Slave);
            }
        }
    }
}
