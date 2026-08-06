using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MUGB.Squads
{
    public class MUGB_SquadTemplateDef : Def
    {
        public float weight = 1f;
        public int maxSquadsPerRaid = int.MaxValue;
        public SimpleCurve selectionWeightFactorByRaidPoints;
        public IntRange sizeRange = new IntRange(3, 6);
        public float minBudget;
        public List<FactionDef> allowedFactions = new List<FactionDef>();
        public List<MUGB_SquadLeaderOption> leaderOptions = new List<MUGB_SquadLeaderOption>();
        public List<MUGB_SquadMemberOption> memberOptions = new List<MUGB_SquadMemberOption>();
        public List<MUGB_SquadUpgradePair> upgradePairs = new List<MUGB_SquadUpgradePair>();
        public List<RaidStrategyDef> allowedRaidStrategies = new List<RaidStrategyDef>();
        public List<PawnsArrivalModeDef> allowedArrivalModes = new List<PawnsArrivalModeDef>();
        public float maxNonFighterRatio;
        public string lordJobKind = "Normal";
        public bool sapperSquad;
        public PawnKindDef sapperKind;
        public bool caravanAmbushOnly;
        public bool weakCaravanAmbush;

        public bool AllowsFaction(FactionDef factionDef)
        {
            return factionDef != null && (allowedFactions.NullOrEmpty() || allowedFactions.Contains(factionDef));
        }

        public bool AllowsRaidStrategy(RaidStrategyDef strategy)
        {
            if (allowedRaidStrategies.NullOrEmpty() || (strategy != null && allowedRaidStrategies.Contains(strategy)))
            {
                return true;
            }

            return strategy == MUGBDefOf.MUGB_GoblinCompositeSapperRaid
                && allowedRaidStrategies.Contains(MUGBDefOf.MUGB_GoblinSapperRaid);
        }

        public bool AllowsArrivalMode(PawnsArrivalModeDef arrivalMode)
        {
            if (allowedArrivalModes.NullOrEmpty() || (arrivalMode != null && allowedArrivalModes.Contains(arrivalMode)))
            {
                return true;
            }

            return arrivalMode == MUGBDefOf.MUGB_GoblinCompositeTwoDirections
                && allowedArrivalModes.Contains(PawnsArrivalModeDefOf.EdgeWalkIn);
        }

        public float SelectionWeightAt(float raidPoints)
        {
            float factor = selectionWeightFactorByRaidPoints != null && raidPoints >= 0f
                ? selectionWeightFactorByRaidPoints.Evaluate(raidPoints)
                : 1f;
            return weight * Mathf.Max(0f, factor);
        }
    }

    public class MUGB_SquadLeaderOption
    {
        public PawnKindDef kind;
        public float budgetThreshold;
        public float weight = 1f;
    }

    public class MUGB_SquadMemberOption
    {
        public PawnKindDef kind;
        public float weight = 1f;
        public int maxCount = int.MaxValue;
        public float budgetThreshold;
        public bool isAnimal;
    }

    public class MUGB_SquadUpgradePair
    {
        public PawnKindDef from;
        public PawnKindDef to;
    }
}
