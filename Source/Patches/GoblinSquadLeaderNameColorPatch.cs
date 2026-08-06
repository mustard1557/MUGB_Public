using HarmonyLib;
using MUGB.Squads;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MUGB.Patches
{
    [StaticConstructorOnStartup]
    public static class GoblinSquadLeaderNameColorUtility
    {
        private static readonly Color LeaderNameColor = new Color32(190, 2, 227, 255);
        private static HashSet<PawnKindDef> leaderKinds = new HashSet<PawnKindDef>();

        static GoblinSquadLeaderNameColorUtility()
        {
            LongEventHandler.ExecuteWhenFinished(InitializeLeaderKinds);
        }

        public static bool TryGetColor(Pawn pawn, Color original, out Color color)
        {
            color = original;
            if (!IsActiveHostileSquadLeader(pawn))
            {
                return false;
            }

            color = LeaderNameColor;
            color.a = original.a;
            return true;
        }

        private static void InitializeLeaderKinds()
        {
            HashSet<PawnKindDef> kinds = new HashSet<PawnKindDef>();
            foreach (MUGB_SquadTemplateDef template in DefDatabase<MUGB_SquadTemplateDef>.AllDefsListForReading)
            {
                if (template?.leaderOptions == null)
                {
                    continue;
                }

                foreach (MUGB_SquadLeaderOption option in template.leaderOptions)
                {
                    if (option?.kind != null)
                    {
                        kinds.Add(option.kind);
                    }
                }
            }

            leaderKinds = kinds;
        }

        private static bool IsActiveHostileSquadLeader(Pawn pawn)
        {
            if (pawn?.Spawned != true
                || pawn.Dead
                || pawn.IsPrisoner
                || pawn.IsSlave
                || pawn.MentalStateDef != null
                || pawn.kindDef == null
                || !leaderKinds.Contains(pawn.kindDef))
            {
                return false;
            }

            Faction playerFaction = Faction.OfPlayerSilentFail;
            if (playerFaction == null || pawn.Faction?.HostileTo(playerFaction) != true)
            {
                return false;
            }

            Lord lord = pawn.GetLord();
            if (lord?.CurLordToil == null || IsLeavingMap(lord, pawn))
            {
                return false;
            }

            return true;
        }

        private static bool IsLeavingMap(Lord lord, Pawn pawn)
        {
            string toilName = lord.CurLordToil.GetType().Name;
            if (toilName.IndexOf("ExitMap", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            DutyDef duty = pawn.mindState?.duty?.def;
            return duty == DutyDefOf.ExitMapBest
                || duty == DutyDefOf.ExitMapBestAndDefendSelf
                || duty == DutyDefOf.ExitMapNearDutyTarget
                || duty == DutyDefOf.ExitMapRandom
                || duty == DutyDefOf.Kidnap
                || duty == DutyDefOf.Steal
                || duty == DutyDefOf.TakeWoundedGuest
                || duty == DutyDefOf.TravelOrLeave;
        }
    }

    [HarmonyPatch(typeof(PawnNameColorUtility), nameof(PawnNameColorUtility.PawnNameColorOf))]
    public static class PawnNameColorUtility_PawnNameColorOf_MUGBSquadLeaderPatch
    {
        public static void Postfix(Pawn pawn, ref Color __result)
        {
            if (GoblinSquadLeaderNameColorUtility.TryGetColor(pawn, __result, out Color color))
            {
                __result = color;
            }
        }
    }
}
