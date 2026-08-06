using HarmonyLib;
using RimWorld;
using System.Linq;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(GasUtility), nameof(GasUtility.PawnGasEffectsTickInterval))]
    public static class GasUtility_PawnGasEffectsTickInterval_StinkGasPatch
    {
        public static bool Prefix(Pawn pawn, int delta)
        {
            if (!pawn.Spawned)
            {
                return false;
            }

            bool vanillaGasTick = pawn.IsHashIntervalTick(50, delta);
            bool stinkGasTick = pawn.IsHashIntervalTick(90, delta);
            if (vanillaGasTick)
            {
                HandleRotStink(pawn);
                HandleDeadlifeDust(pawn);
            }

            if (vanillaGasTick || stinkGasTick)
            {
                HandleToxOrStinkGas(pawn, vanillaGasTick, stinkGasTick);
            }
            return false;
        }

        private static void HandleRotStink(Pawn pawn)
        {
            if (pawn.Position.GasDensity(pawn.Map, GasType.RotStink) <= 0)
            {
                return;
            }

            if (!(pawn.RaceProps.Animal || pawn.RaceProps.Humanlike))
            {
                return;
            }

            if ((pawn.IsMutant && pawn.mutant.Def.isImmuneToInfections) || pawn.RaceProps.isImmuneToInfections)
            {
                return;
            }

            if (pawn.health.hediffSet.HasHediff(HediffDefOf.LungRotExposure))
            {
                return;
            }

            bool hasAffectedLung = pawn.health.hediffSet.GetNotMissingParts()
                .Any(part => part.def == BodyPartDefOf.Lung
                    && !pawn.health.hediffSet.hediffs.Any(h => h.Part == part && h.def.preventsLungRot));
            if (hasAffectedLung)
            {
                pawn.health.AddHediff(HediffDefOf.LungRotExposure);
            }
        }

        private static void HandleToxOrStinkGas(Pawn pawn, bool vanillaGasTick, bool stinkGasTick)
        {
            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            byte density = pawn.Position.GasDensity(pawn.Map, GasType.ToxGas);
            if (density <= 0)
            {
                return;
            }

            if (GoblinStinkGasUtility.TryGetActiveCloudAt(pawn, out GoblinStinkGasCloud cloud) && cloud.IsPawnAffected(pawn))
            {
                if (!stinkGasTick)
                {
                    return;
                }

                float intensity = density / 255f;
                float scale = GoblinUtility.IsGoblin(pawn) ? 0.2f : 1f;
                float effectPower = intensity * cloud.CurrentIntensity * cloud.GasPower;
                GoblinStinkGasUtility.RefreshClouded(pawn, scale);
                GoblinStinkGasUtility.ApplyLingeringExposure(pawn, 0.045f * scale * effectPower);
                if (MUGBDefOf.MUGB_StinkGasDamage != null)
                {
                    pawn.TakeDamage(new DamageInfo(MUGBDefOf.MUGB_StinkGasDamage, 1f * scale * effectPower, 0f, -1f, cloud));
                }

                Hediff vanillaExposure = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.ToxGasExposure);
                if (vanillaExposure != null)
                {
                    pawn.health.RemoveHediff(vanillaExposure);
                }
                return;
            }

            if (!vanillaGasTick)
            {
                return;
            }

            float toxIntensity = density / 255f;
            Hediff toxicBuildup = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup);
            if (toxicBuildup != null && toxicBuildup.CurStageIndex == toxicBuildup.def.stages.Count - 1)
            {
                toxIntensity *= 0.25f;
            }

            if (GasUtility.IsAffectedByExposure(pawn) && !pawn.health.hediffSet.HasHediff(HediffDefOf.ToxGasExposure))
            {
                pawn.health.AddHediff(HediffDefOf.ToxGasExposure);
            }
            ToxicUtility.DoPawnToxicDamage(pawn, toxIntensity);
        }

        private static void HandleDeadlifeDust(Pawn pawn)
        {
            if (!ModsConfig.AnomalyActive || !pawn.Spawned || pawn.Position.GasDensity(pawn.Map, GasType.DeadlifeDust) <= 0 || !pawn.IsShambler)
            {
                return;
            }

            HediffComp_DisappearsAndKills disappears = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Shambler)?.TryGetComp<HediffComp_DisappearsAndKills>();
            if (disappears != null)
            {
                disappears.ticksToDisappear = System.Math.Max(disappears.ticksToDisappear, 15000);
            }
        }
    }
}
