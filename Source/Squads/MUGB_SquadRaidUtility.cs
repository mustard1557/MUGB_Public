using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace MUGB.Squads
{
    public static class MUGB_SquadRaidUtility
    {
        private const float MortarSiegeEscortBudgetFactor = 0.70f;
        public const float SquadBaseBudget = 260f;
        public const float MinSystemPoints = 0f;
        public const float LateLeaderBudget = 300f;
        public const float DiscardLimit = 30f;
        public const float SmallTunnelBudgetCap = 450f;
        private const float SapperSquadBudget = 320f;
        private const float FullSapperCompositionChance = 0.10f;
        private const int NormalSapperSquadCap = 2;
        private const float SuicideSapperSquadBudget = 450f;
        private const float RaidAnimalChance = 0.125f;
        private const float RaidAnimalPointFactor = 0.70f;
        private const float RaidAnimalMinimumPoints = 700f;
        private const float SecondRaidAnimalMinimumPoints = 2500f;
        private const float SecondRaidAnimalChance = 0.30f;

        private static readonly HashSet<string> GoblinRaidFactionDefNames = new HashSet<string>
        {
            "MUGB_GoblinTribe",
            "MUGB_GoblinCivilTribe",
            "MUGB_GoblinCivilMedieval",
            "MUGB_GoblinSavageMedieval",
            "MUGB_GoblinCultists"
        };

        private static readonly HashSet<string> GoblinTunnelWarfareFactionDefNames = new HashSet<string>
        {
            "MUGB_GoblinCivilMedieval",
            "MUGB_GoblinSavageMedieval",
            "MUGB_GoblinCultists"
        };

        private static readonly Dictionary<IncidentParms, string> PendingSummaries = new Dictionary<IncidentParms, string>();
        private static readonly Dictionary<IncidentParms, List<int>> PendingSquadLayouts =
            new Dictionary<IncidentParms, List<int>>();

        public static bool IsGoblinRaidFaction(Faction faction)
        {
            return faction?.def != null && GoblinRaidFactionDefNames.Contains(faction.def.defName);
        }

        public static bool CanUseGoblinTunnelWarfare(Faction faction)
        {
            return faction?.def != null && GoblinTunnelWarfareFactionDefNames.Contains(faction.def.defName);
        }

        public static bool ShouldProcess(PawnGroupMakerParms groupParms, IncidentParms raidParms, bool debugTest)
        {
            if (MUGBMod.Settings == null || !MUGBMod.Settings.enableGoblinSquadSystem)
            {
                return false;
            }

            if (groupParms == null
                || raidParms == null
                || groupParms.groupKind != PawnGroupKindDefOf.Combat
                || groupParms.points <= MinSystemPoints
                || !IsGoblinRaidFaction(groupParms.faction))
            {
                return false;
            }

            if (raidParms.quest != null || raidParms.questScriptDef != null || !raidParms.questTag.NullOrEmpty())
            {
                return false;
            }

            RaidStrategyDef strategy = raidParms.raidStrategy;
            if (strategy == null
                || groupParms.raidStrategy != strategy
                || !IsSupportedGoblinSquadStrategy(strategy))
            {
                return false;
            }
            return true;
        }

        public static bool IsGoblinSapperStrategy(RaidStrategyDef strategy)
        {
            return strategy != null
                && (strategy.defName == "MUGB_GoblinSapperRaid"
                    || strategy.defName == "MUGB_GoblinCompositeSapperRaid");
        }

        public static bool IsGoblinCompositeSapperStrategy(RaidStrategyDef strategy)
        {
            return strategy != null && strategy.defName == "MUGB_GoblinCompositeSapperRaid";
        }

        public static bool IsGoblinSuicideSapperStrategy(RaidStrategyDef strategy)
        {
            return strategy != null && strategy.defName == "MUGB_GoblinSuicideSapperRaid";
        }

        public static bool IsSupportedGoblinSquadStrategy(RaidStrategyDef strategy)
        {
            string defName = strategy?.defName;
            return defName == "ImmediateAttack"
                || defName == "ImmediateAttackSmart"
                || defName == "StageThenAttack"
                || defName == "MUGB_GoblinSapperRaid"
                || defName == "MUGB_GoblinCompositeSapperRaid"
                || defName == "MUGB_GoblinSuicideSapperRaid"
                || defName == "MUGB_GoblinMortarTunnelSiege";
        }

        public static bool TryMakeSquadOptions(
            float pointsTotal,
            PawnGroupMakerParms groupParms,
            out List<PawnGenOptionWithXenotype> options,
            out string summary,
            out List<int> squadSizes)
        {
            options = null;
            summary = null;
            squadSizes = null;
            Faction faction = groupParms?.faction;
            FactionDef factionDef = faction?.def;
            if (factionDef == null)
            {
                return false;
            }

            IncidentParms raidParms = MUGB_SquadRaidContext.CurrentParms;
            if (raidParms?.raidArrivalMode == MUGBDefOf.MUGB_GoblinMortarTunnelArrival)
            {
                return TryMakeTunnelSquadOptions(pointsTotal * MortarSiegeEscortBudgetFactor, faction, out options, out summary, out squadSizes);
            }

            if (raidParms?.raidArrivalMode == MUGBDefOf.MUGB_GoblinTunnelArrivalCenter)
            {
                return TryMakeTunnelSquadOptions(pointsTotal, faction, out options, out summary, out squadSizes, maxSquadSize: 5);
            }

            if (raidParms?.raidArrivalMode == MUGBDefOf.MUGB_GoblinTunnelArrival)
            {
                return TryMakeTunnelSquadOptions(pointsTotal, faction, out options, out summary, out squadSizes);
            }

            RaidStrategyDef raidStrategy = raidParms?.raidStrategy ?? groupParms.raidStrategy;
            PawnsArrivalModeDef arrivalMode = raidParms?.raidArrivalMode;
            bool caravanAmbush = MUGB_SquadRaidContext.CaravanAmbush;
            bool weakCaravanAmbush = MUGB_SquadRaidContext.WeakCaravanAmbush;
            List<MUGB_SquadTemplateDef> validTemplates = DefDatabase<MUGB_SquadTemplateDef>.AllDefsListForReading
                .Where(t => t != null
                    && t.weight > 0f
                    && (caravanAmbush && weakCaravanAmbush
                        ? t.weakCaravanAmbush
                        : !t.caravanAmbushOnly)
                    && t.AllowsFaction(factionDef)
                    && t.AllowsRaidStrategy(raidStrategy)
                    && t.AllowsArrivalMode(arrivalMode)
                    && t.leaderOptions != null
                    && t.leaderOptions.Any(o => o?.kind != null)
                    && t.memberOptions != null
                    && t.memberOptions.Any(o => o?.kind != null))
                .ToList();

            if (IsGoblinSuicideSapperStrategy(raidStrategy))
            {
                return TryMakeSuicideSapperOptions(pointsTotal, factionDef, validTemplates, out options, out summary, out squadSizes);
            }

            bool sapperRaid = IsGoblinSapperStrategy(raidStrategy);
            List<MUGB_SquadTemplateDef> normalTemplates = validTemplates
                .Where(t => !t.sapperSquad
                    && (!sapperRaid || string.Equals(t.lordJobKind, "SapperEscort", System.StringComparison.OrdinalIgnoreCase)))
                .ToList();
            List<MUGB_SquadTemplateDef> sapperTemplates = validTemplates.Where(t => t.sapperSquad && t.sapperKind != null).ToList();
            if (normalTemplates.Count == 0 || (sapperRaid && sapperTemplates.Count == 0))
            {
                return false;
            }

            if (!caravanAmbush
                && IsStandardAssaultStrategy(raidStrategy)
                && TryMakeUnderstrengthSquadOptions(
                    pointsTotal,
                    normalTemplates,
                    out options,
                    out summary,
                    out squadSizes))
            {
                return true;
            }

            List<RaidAnimalOption> raidAnimals = PlanRaidAnimals(
                raidStrategy,
                pointsTotal,
                caravanAmbush);
            float squadPointsTotal = Mathf.Max(
                0f,
                pointsTotal - raidAnimals.Sum(animal => animal.EffectiveCost));

            int softCap = Mathf.Clamp(MUGBMod.Settings?.goblinSquadSoftCap ?? 6, 1, 9);
            int hardCap = Mathf.Clamp(MUGBMod.Settings?.goblinSquadHardCap ?? 9, softCap, 12);
            float squadBudgetBasis = sapperRaid ? SapperSquadBudget : SquadBaseBudget;
            int squadCount = Mathf.Clamp(Mathf.FloorToInt(squadPointsTotal / squadBudgetBasis), 1, softCap);
            float sapperRatio = sapperRaid ? Rand.Range(0.20f, 0.30f) : 0f;
            bool fullSapperComposition = sapperRaid
                && !IsGoblinCompositeSapperStrategy(raidStrategy)
                && Rand.Chance(FullSapperCompositionChance);
            int sapperSquadCount = sapperRaid
                ? (fullSapperComposition
                    ? squadCount
                    : Mathf.Clamp(Mathf.RoundToInt(squadCount * sapperRatio), 1, Mathf.Min(NormalSapperSquadCap, squadCount)))
                : 0;
            HashSet<int> sapperSlots = new HashSet<int>();
            while (sapperSlots.Count < sapperSquadCount)
            {
                sapperSlots.Add(Rand.Range(0, squadCount));
            }
            List<float> randomWeights = new List<float>();
            float weightTotal = 0f;
            for (int i = 0; i < squadCount; i++)
            {
                float weight = Rand.Range(0.8f, 1.2f);
                randomWeights.Add(weight);
                weightTotal += weight;
            }

            List<SquadBuild> squads = new List<SquadBuild>();
            float leftover = 0f;
            for (int i = 0; i < squadCount; i++)
            {
                float budget = squadPointsTotal * randomWeights[i] / weightTotal;
                List<MUGB_SquadTemplateDef> templatePool = ApplyTemplateRaidLimits(
                    sapperSlots.Contains(i) ? sapperTemplates : normalTemplates,
                    squads);
                if (TryBuildSquad(factionDef, templatePool, budget, out SquadBuild squad, out float spent, pointsTotal))
                {
                    squads.Add(squad);
                    leftover += Mathf.Max(0f, budget - spent);
                }
                else
                {
                    leftover += budget;
                }
            }

            if (squads.Count == 0)
            {
                return false;
            }

            leftover += AttachRaidAnimals(squads, raidAnimals);
            SpendLeftoverOnUpgrades(squads, ref leftover);
            SpendLeftoverOnFillers(squads, ref leftover);
            while (leftover >= squadBudgetBasis && squads.Count < hardCap)
            {
                int existingSappers = squads.Count(s => s.Template.sapperSquad);
                bool addSapper = sapperRaid && (fullSapperComposition
                    || (existingSappers < NormalSapperSquadCap
                        && (float)existingSappers / (squads.Count + 1) < sapperRatio));
                List<MUGB_SquadTemplateDef> templatePool = ApplyTemplateRaidLimits(
                    addSapper ? sapperTemplates : normalTemplates,
                    squads);
                if (!TryBuildSquad(factionDef, templatePool, squadBudgetBasis, out SquadBuild squad, out float spent, pointsTotal))
                {
                    break;
                }
                squads.Add(squad);
                leftover = Mathf.Max(0f, leftover - spent);
                SpendLeftoverOnUpgrades(squads, ref leftover);
                SpendLeftoverOnFillers(squads, ref leftover);
                if (leftover <= DiscardLimit)
                {
                    break;
                }
            }

            List<PawnGenOptionWithXenotype> generated = new List<PawnGenOptionWithXenotype>();
            foreach (SquadBuild squad in squads)
            {
                foreach (PawnKindDef kind in squad.Kinds)
                {
                    generated.Add(MakeOption(kind));
                }
            }

            if (generated.Count == 0)
            {
                return false;
            }

            options = generated;
            summary = MakeSummary(squads);
            squadSizes = squads.Select(squad => squad.Kinds.Count).ToList();
            float spentTotal = squads.Sum(s => s.Cost);
            if (Prefs.DevMode && Mathf.Abs(spentTotal - pointsTotal) > pointsTotal * 0.1f)
            {
                Log.Message($"[MUGB] Goblin squad raid budget check: requested={pointsTotal:F0}, bought={spentTotal:F0}, squads={squads.Count}, leftover={leftover:F0}");
            }
            return true;
        }

        private static bool TryMakeUnderstrengthSquadOptions(
            float pointsTotal,
            List<MUGB_SquadTemplateDef> normalTemplates,
            out List<PawnGenOptionWithXenotype> options,
            out string summary,
            out List<int> squadSizes)
        {
            options = null;
            summary = null;
            squadSizes = null;

            PawnKindDef cheapFighter = MUGBDefOf.MUGB_GoblinBareBrawler;
            float cheapFighterCost = CostOf(cheapFighter);
            if (cheapFighter == null || cheapFighterCost <= 0f || normalTemplates.NullOrEmpty())
            {
                return false;
            }

            float normalSquadCost = normalTemplates
                .Select(EstimateMinimumRegularSquadCost)
                .DefaultIfEmpty(float.MaxValue)
                .Min();
            if (normalSquadCost == float.MaxValue || pointsTotal >= normalSquadCost)
            {
                return false;
            }

            int pawnCount = pointsTotal >= cheapFighterCost * 2f
                ? 3
                : pointsTotal >= cheapFighterCost
                    ? 2
                    : 1;
            options = Enumerable.Range(0, pawnCount)
                .Select(_ => MakeOption(cheapFighter))
                .ToList();
            summary = "MUGB_UnderstrengthGoblinSquad".Translate(pawnCount);
            squadSizes = new List<int> { pawnCount };
            return true;
        }

        private static float EstimateMinimumRegularSquadCost(MUGB_SquadTemplateDef template)
        {
            if (template?.leaderOptions.NullOrEmpty() != false || template.memberOptions.NullOrEmpty())
            {
                return float.MaxValue;
            }

            float leaderCost = template.leaderOptions
                .Where(option => option?.kind != null)
                .Select(option => CostOf(option.kind))
                .DefaultIfEmpty(float.MaxValue)
                .Min();
            if (leaderCost == float.MaxValue)
            {
                return float.MaxValue;
            }

            List<float> memberCosts = new List<float>();
            foreach (MUGB_SquadMemberOption option in template.memberOptions
                .Where(option => option?.kind != null
                    && option.kind.isFighter
                    && !option.isAnimal
                    && option.budgetThreshold <= 0f))
            {
                int copies = option.maxCount > 0 && option.maxCount < int.MaxValue
                    ? Mathf.Min(2, option.maxCount)
                    : 2;
                for (int i = 0; i < copies; i++)
                {
                    memberCosts.Add(CostOf(option.kind));
                }
            }

            if (memberCosts.Count < 2)
            {
                return float.MaxValue;
            }
            memberCosts.Sort();
            return leaderCost + memberCosts[0] + memberCosts[1];
        }

        private static bool IsStandardAssaultStrategy(RaidStrategyDef strategy)
        {
            string defName = strategy?.defName;
            return defName == "ImmediateAttack"
                || defName == "ImmediateAttackSmart"
                || defName == "StageThenAttack";
        }

        private static List<RaidAnimalOption> PlanRaidAnimals(
            RaidStrategyDef raidStrategy,
            float pointsTotal,
            bool caravanAmbush)
        {
            List<RaidAnimalOption> result = new List<RaidAnimalOption>();
            if (caravanAmbush
                || pointsTotal < RaidAnimalMinimumPoints
                || !IsStandardAnimalRaidStrategy(raidStrategy)
                || !Rand.Chance(RaidAnimalChance))
            {
                return result;
            }

            int count = 1;
            if (pointsTotal >= SecondRaidAnimalMinimumPoints && Rand.Chance(SecondRaidAnimalChance))
            {
                count++;
            }

            PawnKindDef warg = DefDatabase<PawnKindDef>.GetNamedSilentFail("Warg");
            PawnKindDef wildBoar = DefDatabase<PawnKindDef>.GetNamedSilentFail("WildBoar");
            PawnKindDef dromedary = DefDatabase<PawnKindDef>.GetNamedSilentFail("Dromedary");
            List<PawnKindDef> animalKinds = new[] { warg, wildBoar, dromedary }
                .Where(kind => kind != null)
                .ToList();
            if (animalKinds.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < count; i++)
            {
                animalKinds.TryRandomElementByWeight(
                    kind => kind == warg ? 0.50f : kind == wildBoar ? 0.35f : 0.15f,
                    out PawnKindDef kind);

                result.Add(new RaidAnimalOption(kind, CostOf(kind) * RaidAnimalPointFactor));
            }
            return result;
        }

        private static bool IsStandardAnimalRaidStrategy(RaidStrategyDef strategy)
        {
            string defName = strategy?.defName;
            return defName == "ImmediateAttack"
                || defName == "ImmediateAttackSmart"
                || defName == "StageThenAttack";
        }

        private static float AttachRaidAnimals(List<SquadBuild> squads, List<RaidAnimalOption> animals)
        {
            float refundedPoints = 0f;
            HashSet<SquadBuild> usedSquads = new HashSet<SquadBuild>();
            foreach (RaidAnimalOption animal in animals)
            {
                List<SquadBuild> candidates = squads
                    .Where(squad => !usedSquads.Contains(squad)
                        && squad.Kinds.Count < Mathf.Min(6, squad.Template.sizeRange.max))
                    .ToList();
                if (candidates.Count == 0)
                {
                    candidates = squads
                        .Where(squad => squad.Kinds.Count < Mathf.Min(6, squad.Template.sizeRange.max))
                        .ToList();
                }

                if (!candidates.TryRandomElement(out SquadBuild targetSquad))
                {
                    refundedPoints += animal.EffectiveCost;
                    continue;
                }

                targetSquad.Add(animal.Kind, animal.EffectiveCost);
                usedSquads.Add(targetSquad);
            }
            return refundedPoints;
        }

        private static bool TryMakeSuicideSapperOptions(
            float pointsTotal,
            FactionDef factionDef,
            List<MUGB_SquadTemplateDef> validTemplates,
            out List<PawnGenOptionWithXenotype> options,
            out string summary,
            out List<int> squadSizes)
        {
            options = null;
            summary = null;
            squadSizes = null;
            List<MUGB_SquadTemplateDef> bomberTemplates = validTemplates
                .Where(t => t.sapperSquad
                    && t.sapperKind != null
                    && string.Equals(t.lordJobKind, "SuicideSapper", System.StringComparison.OrdinalIgnoreCase))
                .ToList();
            List<MUGB_SquadTemplateDef> escortTemplates = validTemplates
                .Where(t => !t.sapperSquad
                    && string.Equals(t.lordJobKind, "SuicideEscort", System.StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (bomberTemplates.Count == 0 || escortTemplates.Count == 0)
            {
                return false;
            }

            int bomberSquadCount = CalculateSuicideSapperSquadCount(pointsTotal);
            int softCap = Mathf.Clamp(MUGBMod.Settings?.goblinSquadSoftCap ?? 6, bomberSquadCount, 9);
            int hardCap = Mathf.Clamp(MUGBMod.Settings?.goblinSquadHardCap ?? 9, softCap, 12);
            int targetSquadCount = Mathf.Clamp(Mathf.FloorToInt(pointsTotal / 300f), bomberSquadCount + 1, softCap);
            List<SquadBuild> squads = new List<SquadBuild>();
            float remaining = pointsTotal;
            float bomberBudget = Mathf.Clamp(pointsTotal * 0.45f / bomberSquadCount, 300f, SuicideSapperSquadBudget);
            for (int i = 0; i < bomberSquadCount; i++)
            {
                float budget = Mathf.Min(bomberBudget, remaining);
                if (TryBuildSquad(factionDef, bomberTemplates, budget, out SquadBuild squad, out float spent))
                {
                    squads.Add(squad);
                    remaining = Mathf.Max(0f, remaining - spent);
                }
            }

            if (squads.Count == 0)
            {
                return false;
            }

            int escortCount = Mathf.Max(1, targetSquadCount - squads.Count);
            for (int i = 0; i < escortCount && remaining > 0f; i++)
            {
                float budget = remaining / (escortCount - i);
                if (TryBuildSquad(factionDef, escortTemplates, budget, out SquadBuild squad, out float spent))
                {
                    squads.Add(squad);
                    remaining = Mathf.Max(0f, remaining - spent);
                }
            }

            SpendLeftoverOnUpgrades(squads, ref remaining);
            SpendLeftoverOnFillers(squads, ref remaining);
            while (remaining >= SquadBaseBudget && squads.Count < hardCap)
            {
                if (!TryBuildSquad(factionDef, escortTemplates, SquadBaseBudget, out SquadBuild squad, out float spent))
                {
                    break;
                }
                squads.Add(squad);
                remaining = Mathf.Max(0f, remaining - spent);
                SpendLeftoverOnUpgrades(squads, ref remaining);
                SpendLeftoverOnFillers(squads, ref remaining);
            }

            options = squads.SelectMany(squad => squad.Kinds).Select(MakeOption).ToList();
            summary = MakeSummary(squads);
            squadSizes = squads.Select(squad => squad.Kinds.Count).ToList();
            return options.Count > 0;
        }

        private static int CalculateSuicideSapperSquadCount(float points)
        {
            int count = 1;
            if (points >= 1800f)
            {
                count = 2;
            }
            else if (points >= 1200f)
            {
                float chance = Mathf.Lerp(0.30f, 0.80f, Mathf.InverseLerp(1200f, 1800f, points));
                if (Rand.Chance(chance))
                {
                    count = 2;
                }
            }

            if (points >= 5000f)
            {
                count = 3;
            }
            else if (points >= 3000f)
            {
                float chance = Mathf.Lerp(0.20f, 0.80f, Mathf.InverseLerp(3000f, 5000f, points));
                if (Rand.Chance(chance))
                {
                    count = 3;
                }
            }
            return count;
        }

        public static List<PawnKindDef> GenerateTunnelSquadKinds(Faction faction, float points, int maxSquadSize = 6)
        {
            if (!TryBuildTunnelSquads(faction, points, forceSingleSquad: true, out List<SquadBuild> squads)
                || squads.Count == 0)
            {
                return new List<PawnKindDef>();
            }

            int cappedSize = Mathf.Clamp(maxSquadSize, 3, 6);
            return squads[0].Kinds.Take(cappedSize).ToList();
        }

        private static bool TryMakeTunnelSquadOptions(
            float pointsTotal,
            Faction faction,
            out List<PawnGenOptionWithXenotype> options,
            out string summary,
            out List<int> squadSizes,
            int maxSquadSize = 6)
        {
            options = null;
            summary = null;
            squadSizes = null;
            if (!TryBuildTunnelSquads(faction, pointsTotal, forceSingleSquad: false, out List<SquadBuild> squads))
            {
                return false;
            }

            int cappedSize = Mathf.Clamp(maxSquadSize, 3, 6);
            foreach (SquadBuild squad in squads)
            {
                squad.TrimToSize(cappedSize);
            }

            options = squads.SelectMany(squad => squad.Kinds).Select(MakeOption).ToList();
            summary = MakeSummary(squads);
            squadSizes = squads.Select(squad => squad.Kinds.Count).ToList();
            return options.Count > 0;
        }

        private static bool TryBuildTunnelSquads(
            Faction faction,
            float pointsTotal,
            bool forceSingleSquad,
            out List<SquadBuild> squads)
        {
            squads = new List<SquadBuild>();
            FactionDef factionDef = faction?.def;
            if (factionDef == null || pointsTotal <= 0f)
            {
                return false;
            }

            IncidentParms raidParms = MUGB_SquadRaidContext.CurrentParms;
            RaidStrategyDef raidStrategy = raidParms?.raidStrategy;
            PawnsArrivalModeDef arrivalMode = raidParms?.raidArrivalMode;
            List<MUGB_SquadTemplateDef> templates = DefDatabase<MUGB_SquadTemplateDef>.AllDefsListForReading
                .Where(t => t != null
                    && t.weight > 0f
                    && !t.sapperSquad
                    && t.AllowsFaction(factionDef)
                    && (forceSingleSquad
                        ? t.allowedRaidStrategies.NullOrEmpty() && t.allowedArrivalModes.NullOrEmpty()
                        : t.AllowsRaidStrategy(raidStrategy) && t.AllowsArrivalMode(arrivalMode))
                    && t.leaderOptions != null
                    && t.leaderOptions.Any(o => o?.kind != null)
                    && t.memberOptions != null
                    && t.memberOptions.Any(o => o?.kind != null))
                .ToList();
            if (templates.Count == 0)
            {
                return false;
            }

            if (forceSingleSquad)
            {
                float budget = Mathf.Max(pointsTotal, EstimateFourPawnSquadCost(templates));
                if (!TryBuildSquad(factionDef, templates, budget, out SquadBuild squad, out float spent))
                {
                    return false;
                }
                squads.Add(squad);
                float leftover = Mathf.Max(0f, pointsTotal - spent);
                SpendLeftoverOnUpgrades(squads, ref leftover);
                SpendLeftoverOnFillers(squads, ref leftover);
                return true;
            }

            float minimumNewTunnelBudget = EstimateFourPawnSquadCost(templates);
            int fullSlots = Mathf.FloorToInt(pointsTotal / SmallTunnelBudgetCap);
            if (fullSlots == 0)
            {
                fullSlots = 1;
            }
            float firstPassBudget = Mathf.Min(pointsTotal, fullSlots * SmallTunnelBudgetCap);
            float remainder = Mathf.Max(0f, pointsTotal - firstPassBudget);
            int slotCount = fullSlots + (remainder >= minimumNewTunnelBudget ? 1 : 0);
            List<float> budgets = new List<float>(slotCount);
            for (int i = 0; i < fullSlots; i++)
            {
                budgets.Add(Mathf.Min(SmallTunnelBudgetCap, pointsTotal));
            }
            if (slotCount > fullSlots)
            {
                budgets.Add(remainder);
                remainder = 0f;
            }

            float leftoverTotal = remainder;
            bool centerSuicideSquad = !forceSingleSquad
                && arrivalMode == MUGBDefOf.MUGB_GoblinTunnelArrivalCenter
                && RollCenterTunnelSuicideSquad(pointsTotal);
            int suicideSlot = centerSuicideSquad ? budgets.IndexOf(budgets.Max()) : -1;
            List<MUGB_SquadTemplateDef> centerSuicideTemplates = centerSuicideSquad
                ? DefDatabase<MUGB_SquadTemplateDef>.AllDefsListForReading
                    .Where(t => t != null
                        && t.weight > 0f
                        && t.sapperSquad
                        && t.sapperKind != null
                        && string.Equals(t.lordJobKind, "SuicideSapper", System.StringComparison.OrdinalIgnoreCase)
                        && t.AllowsFaction(factionDef))
                    .ToList()
                : null;
            for (int i = 0; i < budgets.Count; i++)
            {
                float budget = budgets[i];
                List<MUGB_SquadTemplateDef> pool = i == suicideSlot && !centerSuicideTemplates.NullOrEmpty()
                    ? centerSuicideTemplates
                    : templates;
                pool = ApplyTemplateRaidLimits(pool, squads);
                if (TryBuildSquad(factionDef, pool, budget, out SquadBuild squad, out float spent))
                {
                    squads.Add(squad);
                    leftoverTotal += Mathf.Max(0f, budget - spent);
                }
                else
                {
                    leftoverTotal += budget;
                }
            }

            if (squads.Count == 0)
            {
                return false;
            }
            SpendLeftoverOnUpgrades(squads, ref leftoverTotal);
            SpendLeftoverOnFillers(squads, ref leftoverTotal);
            return true;
        }

        private static bool RollCenterTunnelSuicideSquad(float points)
        {
            float chance = points < 1200f ? 0f
                : points < 2500f ? 0.05f
                : points < 4000f ? 0.10f
                : 0.15f;
            return chance > 0f && Rand.Chance(chance);
        }

        private static float EstimateFourPawnSquadCost(List<MUGB_SquadTemplateDef> templates)
        {
            float best = float.MaxValue;
            foreach (MUGB_SquadTemplateDef template in templates)
            {
                float leader = template.leaderOptions
                    .Where(option => option?.kind != null)
                    .Select(option => CostOf(option.kind))
                    .DefaultIfEmpty(float.MaxValue)
                    .Min();
                List<float> members = template.memberOptions
                    .Where(option => option?.kind != null && option.kind.isFighter)
                    .Select(option => CostOf(option.kind))
                    .OrderBy(cost => cost)
                    .ToList();
                if (leader < float.MaxValue && members.Count > 0)
                {
                    best = Mathf.Min(best, leader + members[0] * 3f);
                }
            }

            return best < float.MaxValue ? Mathf.Clamp(best, 120f, SmallTunnelBudgetCap) : 200f;
        }

        public static void SetPendingSummary(IncidentParms parms, string summary)
        {
            if (parms == null || summary.NullOrEmpty())
            {
                return;
            }
            PendingSummaries[parms] = summary;
        }

        public static bool TryConsumeSummary(IncidentParms parms, out string summary)
        {
            summary = null;
            if (parms == null || !PendingSummaries.TryGetValue(parms, out summary))
            {
                return false;
            }
            PendingSummaries.Remove(parms);
            return !summary.NullOrEmpty();
        }

        public static void SetPendingSquadLayout(IncidentParms parms, List<int> squadSizes)
        {
            if (parms != null && !squadSizes.NullOrEmpty())
            {
                PendingSquadLayouts[parms] = new List<int>(squadSizes);
            }
        }

        public static bool TryConsumeSquadLayout(IncidentParms parms, out List<int> squadSizes)
        {
            squadSizes = null;
            if (parms == null || !PendingSquadLayouts.TryGetValue(parms, out squadSizes))
            {
                return false;
            }

            PendingSquadLayouts.Remove(parms);
            return !squadSizes.NullOrEmpty();
        }

        public static bool TryGetSquadLayout(IncidentParms parms, out List<int> squadSizes)
        {
            squadSizes = null;
            return parms != null
                && PendingSquadLayouts.TryGetValue(parms, out squadSizes)
                && !squadSizes.NullOrEmpty();
        }

        public static void ClearPendingSquadLayout(IncidentParms parms)
        {
            if (parms != null)
            {
                PendingSquadLayouts.Remove(parms);
            }
        }

        private static bool TryBuildSquad(
            FactionDef factionDef,
            List<MUGB_SquadTemplateDef> templates,
            float budget,
            out SquadBuild squad,
            out float spent,
            float raidPointsForWeight = -1f)
        {
            squad = null;
            spent = 0f;
            if (templates.NullOrEmpty())
            {
                return false;
            }
            List<MUGB_SquadTemplateDef> candidates = templates.Where(t => t.minBudget <= budget && t.AllowsFaction(factionDef)).ToList();
            if (candidates.Count == 0)
            {
                float minimumBudget = templates.Where(t => t.AllowsFaction(factionDef)).Min(t => t.minBudget);
                candidates = templates
                    .Where(t => t.AllowsFaction(factionDef) && Mathf.Approximately(t.minBudget, minimumBudget))
                    .ToList();
            }
            if (!candidates.TryRandomElementByWeight(t => t.SelectionWeightAt(raidPointsForWeight), out MUGB_SquadTemplateDef template))
            {
                return false;
            }

            if (!TryChooseLeader(template, budget, out PawnKindDef leaderKind))
            {
                return false;
            }

            squad = new SquadBuild(template);
            squad.Add(leaderKind);
            spent += CostOf(leaderKind);
            float remaining = budget - spent;
            int targetMembers = Mathf.Max(2, template.sizeRange.RandomInRange - 1);
            int membersAdded = 0;
            if (template.sapperSquad && template.sapperKind != null)
            {
                squad.Add(template.sapperKind);
                float sapperCost = CostOf(template.sapperKind);
                spent += sapperCost;
                remaining -= sapperCost;
                membersAdded++;
            }

            for (int i = membersAdded; i < targetMembers; i++)
            {
                if (!TryChooseMember(template, squad, budget, remaining, mustBeAffordable: true, out PawnKindDef memberKind))
                {
                    if (i < 2 && TryChooseMember(template, squad, budget, remaining, mustBeAffordable: false, kind: out memberKind, forceFighter: true))
                    {
                        // Force the minimum squad body count with fighters only. This may slightly overspend tiny budgets, but avoids non-combat filler spam.
                    }
                    else
                    {
                        break;
                    }
                }

                squad.Add(memberKind);
                float cost = CostOf(memberKind);
                spent += cost;
                remaining -= cost;
            }

            return squad.Kinds.Count >= 3;
        }

        private static List<MUGB_SquadTemplateDef> ApplyTemplateRaidLimits(
            List<MUGB_SquadTemplateDef> templates,
            List<SquadBuild> existingSquads)
        {
            if (templates.NullOrEmpty())
            {
                return new List<MUGB_SquadTemplateDef>();
            }

            return templates
                .Where(template => template.maxSquadsPerRaid <= 0
                    || template.maxSquadsPerRaid == int.MaxValue
                    || existingSquads.Count(squad => squad.Template == template) < template.maxSquadsPerRaid)
                .ToList();
        }

        private static bool TryChooseLeader(MUGB_SquadTemplateDef template, float budget, out PawnKindDef kind)
        {
            kind = null;
            List<MUGB_SquadLeaderOption> eligible = template.leaderOptions
                .Where(o => o?.kind != null && o.budgetThreshold <= budget)
                .ToList();
            if (eligible.Count == 0)
            {
                eligible = template.leaderOptions.Where(o => o?.kind != null).OrderBy(o => o.budgetThreshold).Take(1).ToList();
            }

            float bestThreshold = eligible.Max(o => o.budgetThreshold);
            List<MUGB_SquadLeaderOption> best = eligible.Where(o => Mathf.Approximately(o.budgetThreshold, bestThreshold)).ToList();
            if (!best.TryRandomElementByWeight(o => Mathf.Max(0.01f, o.weight), out MUGB_SquadLeaderOption chosen))
            {
                return false;
            }
            kind = chosen.kind;
            return kind != null;
        }

        private static bool TryChooseMember(MUGB_SquadTemplateDef template, SquadBuild squad, float squadBudget, float remaining, bool mustBeAffordable, out PawnKindDef kind, bool forceFighter = false)
        {
            kind = null;
            List<MUGB_SquadMemberOption> candidates = template.memberOptions
                .Where(o => CanUseMemberOption(template, squad, o, squadBudget, remaining, mustBeAffordable, forceFighter))
                .ToList();

            if (!candidates.TryRandomElementByWeight(o => Mathf.Max(0.01f, o.weight), out MUGB_SquadMemberOption chosen))
            {
                return false;
            }

            kind = chosen.kind;
            return kind != null;
        }

        private static bool CanUseMemberOption(MUGB_SquadTemplateDef template, SquadBuild squad, MUGB_SquadMemberOption option, float squadBudget, float remaining, bool mustBeAffordable, bool forceFighter)
        {
            if (option?.kind == null)
            {
                return false;
            }

            if (option.budgetThreshold > 0f && squadBudget < option.budgetThreshold)
            {
                return false;
            }

            if (mustBeAffordable && CostOf(option.kind) > remaining)
            {
                return false;
            }

            if (option.maxCount > 0 && option.maxCount < int.MaxValue && squad.CountKind(option.kind) >= option.maxCount)
            {
                return false;
            }

            bool nonFighter = !option.kind.isFighter;
            if (forceFighter && nonFighter)
            {
                return false;
            }

            if (nonFighter)
            {
                int nextTotal = squad.Kinds.Count + 1;
                int nextNonFighters = squad.NonFighterCount + 1;
                if (template.maxNonFighterRatio <= 0f || (float)nextNonFighters / nextTotal > template.maxNonFighterRatio)
                {
                    return false;
                }
            }

            return true;
        }

        private static void SpendLeftoverOnUpgrades(List<SquadBuild> squads, ref float leftover)
        {
            int guard = 0;
            while (leftover > 0f && guard++ < 250)
            {
                List<UpgradeCandidate> candidates = new List<UpgradeCandidate>();
                for (int i = 0; i < squads.Count; i++)
                {
                    SquadBuild squad = squads[i];
                    if (squad.Template.upgradePairs.NullOrEmpty())
                    {
                        continue;
                    }
                    for (int j = 1; j < squad.Kinds.Count; j++)
                    {
                        PawnKindDef current = squad.Kinds[j];
                        foreach (MUGB_SquadUpgradePair pair in squad.Template.upgradePairs)
                        {
                            if (pair?.from == current && pair.to != null)
                            {
                                float delta = CostOf(pair.to) - CostOf(pair.from);
                                if (delta > 0f && delta <= leftover)
                                {
                                    candidates.Add(new UpgradeCandidate(i, j, pair.to, delta));
                                }
                            }
                        }
                    }
                }

                if (!candidates.TryRandomElement(out UpgradeCandidate chosen))
                {
                    break;
                }

                squads[chosen.SquadIndex].Replace(chosen.MemberIndex, chosen.ToKind);
                leftover -= chosen.CostDelta;
            }
        }

        private static void SpendLeftoverOnFillers(List<SquadBuild> squads, ref float leftover)
        {
            int guard = 0;
            while (leftover > 0f && guard++ < 100)
            {
                List<SquadBuild> candidates = squads
                    .Where(s => s.Kinds.Count < s.Template.sizeRange.max)
                    .InRandomOrder()
                    .ToList();
                bool spent = false;
                foreach (SquadBuild squad in candidates)
                {
                    if (TryChooseMember(squad.Template, squad, squad.Cost + leftover, leftover, mustBeAffordable: true, out PawnKindDef kind))
                    {
                        squad.Add(kind);
                        leftover -= CostOf(kind);
                        spent = true;
                        break;
                    }
                }

                if (!spent)
                {
                    break;
                }
            }
        }

        private static PawnGenOptionWithXenotype MakeOption(PawnKindDef kind)
        {
            PawnGenOption option = new PawnGenOption
            {
                kind = kind,
                selectionWeight = 1f
            };
            return new PawnGenOptionWithXenotype(option, null, 1f);
        }

        private static string MakeSummary(List<SquadBuild> squads)
        {
            return string.Join(", ", squads
                .GroupBy(s => s.Template)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.label)
                .Select(g => $"{g.Key.LabelCap} x{g.Count()}"));
        }

        private static float CostOf(PawnKindDef kind)
        {
            return kind?.combatPower ?? 0f;
        }

        private class SquadBuild
        {
            public readonly MUGB_SquadTemplateDef Template;
            public readonly List<PawnKindDef> Kinds = new List<PawnKindDef>();
            public int NonFighterCount;
            public float Cost;

            public SquadBuild(MUGB_SquadTemplateDef template)
            {
                Template = template;
            }

            public void Add(PawnKindDef kind, float? costOverride = null)
            {
                Kinds.Add(kind);
                Cost += costOverride ?? CostOf(kind);
                if (kind != null && !kind.isFighter)
                {
                    NonFighterCount++;
                }
            }

            public void Replace(int index, PawnKindDef toKind)
            {
                PawnKindDef fromKind = Kinds[index];
                if (fromKind != null && !fromKind.isFighter)
                {
                    NonFighterCount--;
                }
                Kinds[index] = toKind;
                if (toKind != null && !toKind.isFighter)
                {
                    NonFighterCount++;
                }
                Cost += CostOf(toKind) - CostOf(fromKind);
            }

            public void TrimToSize(int maxSize)
            {
                if (Kinds.Count <= maxSize)
                {
                    return;
                }

                Kinds.RemoveRange(maxSize, Kinds.Count - maxSize);
                Cost = Kinds.Sum(CostOf);
                NonFighterCount = Kinds.Count(kind => kind != null && !kind.isFighter);
            }

            public int CountKind(PawnKindDef kind)
            {
                int count = 0;
                for (int i = 0; i < Kinds.Count; i++)
                {
                    if (Kinds[i] == kind)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        private readonly struct RaidAnimalOption
        {
            public readonly PawnKindDef Kind;
            public readonly float EffectiveCost;

            public RaidAnimalOption(PawnKindDef kind, float effectiveCost)
            {
                Kind = kind;
                EffectiveCost = effectiveCost;
            }
        }

        private readonly struct UpgradeCandidate
        {
            public readonly int SquadIndex;
            public readonly int MemberIndex;
            public readonly PawnKindDef ToKind;
            public readonly float CostDelta;

            public UpgradeCandidate(int squadIndex, int memberIndex, PawnKindDef toKind, float costDelta)
            {
                SquadIndex = squadIndex;
                MemberIndex = memberIndex;
                ToKind = toKind;
                CostDelta = costDelta;
            }
        }
    }
}
