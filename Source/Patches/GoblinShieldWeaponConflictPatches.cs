using HarmonyLib;
using RimWorld;
using System.Reflection;
using Verse;
using Verse.AI;

namespace MUGB.Patches
{
    internal static class ShieldConflictMessageUtility
    {
        private static readonly System.Collections.Generic.Dictionary<int, int> LastWarnTickByPawnId = new System.Collections.Generic.Dictionary<int, int>();

        public static void WarnOnce(Pawn pawn, string key, string arg0, string arg1)
        {
            if (pawn?.Faction != Faction.OfPlayer)
            {
                return;
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (LastWarnTickByPawnId.TryGetValue(pawn.thingIDNumber, out int lastTick) && currentTick - lastTick < 60)
            {
                return;
            }

            LastWarnTickByPawnId[pawn.thingIDNumber] = currentTick;
            Messages.Message(key.Translate(arg0, arg1), pawn, MessageTypeDefOf.RejectInput, historical: false);
        }
    }

    [HarmonyPatch]
    public static class VefEquipShieldFloatMenu_ShieldForbiddenWeaponPatch
    {
        public static MethodBase TargetMethod()
        {
            System.Type providerType = AccessTools.TypeByName("VEF.Apparels.FloatMenuOptionProvider_EquipShield");
            return providerType == null ? null : AccessTools.Method(providerType, "AddShieldFloatMenuOption");
        }

        public static bool Prepare()
        {
            return TargetMethod() != null;
        }

        public static bool Prefix(object[] __args)
        {
            Pawn pawn = null;
            Apparel shield = null;

            for (int i = 0; i < __args.Length; i++)
            {
                if (pawn == null && __args[i] is Pawn argPawn)
                {
                    pawn = argPawn;
                }
                if (shield == null && __args[i] is Apparel argApparel && GoblinRenderNodeUtility.IsShieldApparelDef(argApparel.def))
                {
                    shield = argApparel;
                }
            }

            if (pawn?.equipment?.Primary?.def?.weaponTags?.Contains("MUGB_ShieldForbidden") == true && shield != null)
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Wear), typeof(Apparel), typeof(bool), typeof(bool))]
    public static class PawnApparelTracker_Wear_ShieldForbiddenWeaponPatch
    {
        private static readonly AccessTools.FieldRef<Pawn_ApparelTracker, Pawn> PawnRef =
            AccessTools.FieldRefAccess<Pawn_ApparelTracker, Pawn>("pawn");

        public static bool Prefix(Pawn_ApparelTracker __instance, Apparel newApparel)
        {
            if (!GoblinRenderNodeUtility.IsShieldApparelDef(newApparel?.def))
            {
                return true;
            }

            Pawn pawn = PawnRef(__instance);
            ThingWithComps weapon = pawn?.equipment?.Primary;
            if (weapon?.def?.weaponTags?.Contains("MUGB_ShieldForbidden") != true)
            {
                return true;
            }

            ShieldConflictMessageUtility.WarnOnce(pawn, "MUGB_CannotWearShieldWithWeapon", newApparel.LabelCap, weapon.LabelCap);
            return false;
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_Equip), "GetSingleOptionFor")]
    public static class FloatMenuOptionProvider_Equip_ShieldForbiddenWeaponPatch
    {
        public static void Postfix(ref FloatMenuOption __result, Thing clickedThing, FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            if (__result == null || pawn == null || !(clickedThing is ThingWithComps eq) || eq.def?.weaponTags?.Contains("MUGB_ShieldForbidden") != true)
            {
                return;
            }

            Apparel shield = ShieldConflictUtility.EquippedShield(pawn);
            if (shield == null)
            {
                return;
            }

            string label = "CannotEquip".Translate(eq.LabelShort) + " (" + "MUGB_CannotEquipWithShield".Translate(eq.LabelCap, shield.LabelCap) + ")";
            __result = new FloatMenuOption(label, null);
        }
    }

    [HarmonyPatch(typeof(JobDriver_Equip), nameof(JobDriver_Equip.TryMakePreToilReservations))]
    public static class JobDriver_Equip_ShieldForbiddenWeaponPatch
    {
        public static bool Prefix(JobDriver_Equip __instance, bool errorOnFailed, ref bool __result)
        {
            Pawn pawn = __instance.pawn;
            ThingWithComps eq = __instance.job?.targetA.Thing as ThingWithComps;
            if (pawn == null || eq?.def?.weaponTags?.Contains("MUGB_ShieldForbidden") != true)
            {
                return true;
            }

            Apparel shield = ShieldConflictUtility.EquippedShield(pawn);
            if (shield == null)
            {
                return true;
            }

            ShieldConflictMessageUtility.WarnOnce(pawn, "MUGB_CannotEquipWithShield", eq.LabelCap, shield.LabelCap);
            __result = false;
            return false;
        }
    }

    internal static class ShieldConflictUtility
    {
        public static Apparel EquippedShield(Pawn pawn)
        {
            if (pawn?.apparel?.WornApparel == null)
            {
                return null;
            }

            for (int i = 0; i < pawn.apparel.WornApparel.Count; i++)
            {
                Apparel apparel = pawn.apparel.WornApparel[i];
                if (GoblinRenderNodeUtility.IsShieldApparelDef(apparel?.def))
                {
                    return apparel;
                }
            }

            return null;
        }
    }
}
