using HarmonyLib;
using System;
using System.Reflection;
using Verse;

namespace MUGB.Patches
{
    [HarmonyPatch(typeof(ThingDef), "get_LabelAsStuff")]
    public static class ThingDef_LabelAsStuff_GoblinAlloyKoreanPatch
    {
        private static readonly FieldInfo ActiveLanguageField =
            AccessTools.Field(typeof(LanguageDatabase), "activeLanguage");

        public static void Postfix(ThingDef __instance, ref string __result)
        {
            if (__instance == null || !IsKoreanActive())
            {
                return;
            }

            switch (__instance.defName)
            {
                case "MUGB_CrudeGoblinAlloy":
                    __result = "하급고블린합금";
                    break;
                case "MUGB_GoblinAlloy":
                    __result = "고블린합금";
                    break;
                case "MUGB_FineGoblinAlloy":
                    __result = "상급고블린합금";
                    break;
                case "MUGB_Bone":
                    __result = "뼈";
                    break;
            }
        }

        private static bool IsKoreanActive()
        {
            try
            {
                LoadedLanguage language = ActiveLanguageField?.GetValue(null) as LoadedLanguage;
                if (language == null)
                {
                    return false;
                }

                if (string.Equals(language.LegacyFolderName, "Korean", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!language.FriendlyNameNative.NullOrEmpty() && language.FriendlyNameNative.Contains("한국어"))
                {
                    return true;
                }

                if (!language.FriendlyNameEnglish.NullOrEmpty() && language.FriendlyNameEnglish.IndexOf("Korean", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
