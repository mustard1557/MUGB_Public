using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace MUGB.Patches
{
    [StaticConstructorOnStartup]
    public static class YayoCombatAmmoCompatibility
    {
        private const string YayoCombatPackageId = "Mlie.YayosCombat3";
        private static readonly Dictionary<string, int> MinimumAmmoByWeapon = new Dictionary<string, int>
        {
            { "MUGB_GoblinStaffSling", 45 },
            { "MUGB_GoblinArquebus", 45 },
            { "MUGB_GoblinMusket", 45 },
            { "MUGB_GoblinHandgonne", 45 },
            { "MUGB_GoblinWarbow", 30 },
            { "MUGB_GoblinCrossbow", 30 },
            { "MUGB_GoblinRepeatingCrossbow", 30 }
        };

        static YayoCombatAmmoCompatibility()
        {
            LongEventHandler.ExecuteWhenFinished(Apply);
        }

        private static void Apply()
        {
            if (!ModsConfig.IsActive(YayoCombatPackageId))
            {
                return;
            }

            foreach (KeyValuePair<string, int> pair in MinimumAmmoByWeapon)
            {
                ThingDef weapon = DefDatabase<ThingDef>.GetNamedSilentFail(pair.Key);
                if (weapon == null || weapon.comps == null)
                {
                    continue;
                }

                foreach (CompProperties comp in weapon.comps)
                {
                    if (comp == null || !LooksLikeAmmoComp(comp))
                    {
                        continue;
                    }

                    TryRaiseNumericMember(comp, "maxAmmo", pair.Value);
                    TryRaiseNumericMember(comp, "maxAmmoAmount", pair.Value);
                    TryRaiseNumericMember(comp, "MaxAmmoAmount", pair.Value);
                    TryRaiseNumericMember(comp, "maxCharges", pair.Value);
                    TryRaiseNumericMember(comp, "MaxCharges", pair.Value);
                }
            }
        }

        private static bool LooksLikeAmmoComp(CompProperties comp)
        {
            string name = comp.GetType().FullName ?? comp.GetType().Name;
            return name.IndexOf("Ammo", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Reload", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void TryRaiseNumericMember(object target, string memberName, int minimum)
        {
            Type type = target.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                TryRaiseField(target, field, minimum);
                return;
            }

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.CanRead && property.CanWrite)
            {
                TryRaiseProperty(target, property, minimum);
            }
        }

        private static void TryRaiseField(object target, FieldInfo field, int minimum)
        {
            object current = field.GetValue(target);
            if (TryConvert(current, out float value) && value >= minimum)
            {
                return;
            }

            SetNumericValue(target, field.FieldType, minimum, value => field.SetValue(target, value));
        }

        private static void TryRaiseProperty(object target, PropertyInfo property, int minimum)
        {
            object current = property.GetValue(target, null);
            if (TryConvert(current, out float value) && value >= minimum)
            {
                return;
            }

            SetNumericValue(target, property.PropertyType, minimum, value => property.SetValue(target, value, null));
        }

        private static bool TryConvert(object value, out float result)
        {
            if (value is int i)
            {
                result = i;
                return true;
            }

            if (value is float f)
            {
                result = f;
                return true;
            }

            if (value is double d)
            {
                result = (float)d;
                return true;
            }

            result = 0f;
            return value == null;
        }

        private static void SetNumericValue(object target, Type type, int minimum, Action<object> setter)
        {
            if (type == typeof(int))
            {
                setter(minimum);
            }
            else if (type == typeof(float))
            {
                setter((float)minimum);
            }
            else if (type == typeof(double))
            {
                setter((double)minimum);
            }
        }
    }
}
