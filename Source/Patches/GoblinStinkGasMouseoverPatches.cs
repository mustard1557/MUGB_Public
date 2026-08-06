using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(MouseoverReadout), "DrawGas")]
    public static class MouseoverReadout_DrawGas_GoblinStinkGasPatch
    {
        private const float BotLeftX = 15f;
        private const float BotLeftY = 65f;
        private const float YInterval = 19f;

        public static bool Prefix(GasType gasType, byte density, ref float curYOffset)
        {
            if (gasType != GasType.ToxGas || density <= 0)
            {
                return true;
            }

            Map map = Find.CurrentMap;
            if (map == null)
            {
                return true;
            }

            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map) || !GoblinStinkGasUtility.TryGetActiveCloudAt(map, cell, out _))
            {
                return true;
            }

            Widgets.Label(
                new Rect(BotLeftX, UI.screenHeight - BotLeftY - curYOffset, 999f, 999f),
                "MUGB_StinkGasCloudMouseover".Translate(Mathf.RoundToInt(density / 255f * 100f)));
            curYOffset += YInterval;
            return false;
        }
    }
}
