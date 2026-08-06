using HarmonyLib;
using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MUGB.Patches
{
    [HarmonyPatch]
    public static class VefApparelShield_DrawShield_TuningPatch
    {
        private const float LayerToAltitude = 1f / 256f;
        private static readonly AccessTools.FieldRef<object, object> ShieldGraphicRef;
        private static FieldInfo offsetField;
        private static FieldInfo behindField;
        private static FieldInfo flipField;

        static VefApparelShield_DrawShield_TuningPatch()
        {
            System.Type shieldType = AccessTools.TypeByName("VEF.Apparels.Apparel_Shield");
            if (shieldType != null)
            {
                FieldInfo field = AccessTools.Field(shieldType, "shieldGraphic");
                if (field != null)
                {
                    ShieldGraphicRef = AccessTools.FieldRefAccess<object, object>(field);
                }
            }
        }

        public static bool Prepare()
        {
            return TargetMethod() != null;
        }

        public static MethodBase TargetMethod()
        {
            System.Type shieldType = AccessTools.TypeByName("VEF.Apparels.Apparel_Shield");
            System.Type compType = AccessTools.TypeByName("VEF.Apparels.CompShield");
            return shieldType != null && compType != null
                ? AccessTools.Method(shieldType, "DrawShield", new[] { compType, typeof(Vector3), typeof(Rot4) })
                : null;
        }

        public static bool Prefix(object __instance, object comp, Vector3 drawPos, Rot4 rot4)
        {
            if (!(__instance is Apparel apparel) || apparel.Wearer == null || !GoblinRenderNodeUtility.IsShieldApparelDef(apparel.def))
            {
                return true;
            }

            object props = AccessTools.Property(comp.GetType(), "Props")?.GetValue(comp);
            object holdOffsets = AccessTools.Field(props?.GetType(), "offHandHoldOffset")?.GetValue(props);
            object holdOffset = holdOffsets?.GetType().GetMethod("Pick")?.Invoke(holdOffsets, new object[] { rot4 });
            if (holdOffset == null)
            {
                return true;
            }

            Vector3 baseOffset = HoldOffset(holdOffset);
            bool behind = HoldBehind(holdOffset);
            bool flip = HoldFlip(holdOffset);

            MUGBSettings settings = MUGBMod.Settings ?? new MUGBSettings();
            string formKey = GoblinUtility.TuningProfileKey(apparel.Wearer);
            if (formKey.NullOrEmpty())
            {
                formKey = GoblinUtility.IsHobgoblin(apparel.Wearer) ? "Hobgoblin" : "Goblin";
            }

            Vector3 finalDrawLoc = AimingVector(apparel.Wearer, drawPos, rot4)
                + baseOffset
                + new Vector3(0f, behind ? -0.0390625f : 0.0390625f, 0f);
            finalDrawLoc.x += settings.GetRenderTargetOffsetX("Shield", apparel.def, formKey, rot4);
            finalDrawLoc.z += settings.GetRenderTargetOffsetY("Shield", apparel.def, formKey, rot4);
            finalDrawLoc.y += settings.GetRenderTargetLayerOffset("Shield", apparel.def, formKey, rot4) * LayerToAltitude;

            Rot4 drawRot = flip ? rot4.Opposite : rot4;
            Graphic graphic = ShieldGraphic(apparel, props);
            if (graphic == null)
            {
                return true;
            }

            float scale = settings.GetRenderTargetScale("Shield", apparel.def, formKey, rot4);
            Graphic drawGraphic = ScaledGraphic(graphic, scale);
            drawGraphic.Draw(finalDrawLoc, drawRot, apparel);
            return false;
        }

        private static Graphic ScaledGraphic(Graphic graphic, float scale)
        {
            if (graphic == null || Mathf.Abs(scale - 1f) < 0.001f)
            {
                return graphic;
            }

            Vector2 drawSize = graphic.drawSize * scale;
            return GraphicDatabase.Get(
                graphic.GetType(),
                graphic.path,
                graphic.Shader,
                drawSize,
                graphic.Color,
                graphic.ColorTwo,
                graphic.data,
                null,
                graphic.maskPath);
        }

        private static Graphic ShieldGraphic(Apparel apparel, object props)
        {
            if (ShieldGraphicRef != null)
            {
                object cached = ShieldGraphicRef(apparel);
                if (cached is Graphic graphic)
                {
                    return graphic;
                }
            }

            GraphicData data = AccessTools.Field(props?.GetType(), "offHandGraphicData")?.GetValue(props) as GraphicData;
            Graphic generated = data?.GraphicColoredFor(apparel);
            if (generated != null && ShieldGraphicRef != null)
            {
                ShieldGraphicRef(apparel) = generated;
            }
            return generated;
        }

        private static Vector3 HoldOffset(object holdOffset)
        {
            offsetField ??= AccessTools.Field(holdOffset.GetType(), "offset");
            return offsetField != null ? (Vector3)offsetField.GetValue(holdOffset) : Vector3.zero;
        }

        private static bool HoldBehind(object holdOffset)
        {
            behindField ??= AccessTools.Field(holdOffset.GetType(), "behind");
            return behindField != null && (bool)behindField.GetValue(holdOffset);
        }

        private static bool HoldFlip(object holdOffset)
        {
            flipField ??= AccessTools.Field(holdOffset.GetType(), "flip");
            return flipField != null && (bool)flipField.GetValue(holdOffset);
        }

        private static Vector3 AimingVector(Pawn wearer, Vector3 rootLoc, Rot4 rot4)
        {
            Stance_Busy stance = wearer?.stances?.curStance as Stance_Busy;
            if (stance != null && !stance.neverAimWeapon && stance.focusTarg.IsValid)
            {
                Vector3 target = stance.focusTarg.HasThing ? stance.focusTarg.Thing.DrawPos : stance.focusTarg.Cell.ToVector3Shifted();
                float angle = (target - wearer.DrawPos).MagnitudeHorizontalSquared() > 0.001f
                    ? (target - wearer.DrawPos).AngleFlat()
                    : 0f;
                Vector3 drawLoc = rootLoc + new Vector3(0f, 0f, 0.4f).RotatedBy(angle);
                drawLoc.y += 9f / 245f;
                return drawLoc;
            }

            if (wearer == null || CarryWeaponOpenly(wearer))
            {
                if (rot4 == Rot4.South)
                {
                    Vector3 drawLoc = rootLoc + new Vector3(0f, 0f, -0.22f);
                    drawLoc.y += 9f / 245f;
                    return drawLoc;
                }
                if (rot4 == Rot4.North)
                {
                    return rootLoc + new Vector3(0f, 0f, -0.11f);
                }
                if (rot4 == Rot4.East)
                {
                    Vector3 drawLoc = rootLoc + new Vector3(0.2f, 0f, -0.22f);
                    drawLoc.y += 9f / 245f;
                    return drawLoc;
                }
                if (rot4 == Rot4.West)
                {
                    Vector3 drawLoc = rootLoc + new Vector3(-0.2f, 0f, -0.22f);
                    drawLoc.y += 9f / 245f;
                    return drawLoc;
                }
            }

            return Vector3.zero;
        }

        private static bool CarryWeaponOpenly(Pawn wearer)
        {
            if (wearer?.carryTracker?.CarriedThing != null)
            {
                return false;
            }
            if (wearer?.Drafted == true)
            {
                return true;
            }
            if (wearer?.CurJob?.def?.alwaysShowWeapon == true)
            {
                return true;
            }
            if (wearer?.mindState?.duty?.def?.alwaysShowWeapon == true)
            {
                return true;
            }
            Lord lord = wearer?.GetLord();
            return lord?.LordJob?.AlwaysShowWeapon == true;
        }

    }
}
