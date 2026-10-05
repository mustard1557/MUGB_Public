using HarmonyLib;
using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;

namespace MUGB.Patches
{
    /*
      [파일 개요] 비고블린 세력이 고블린 무기를 들고 나오는 문제를 다룹니다.

      배경:
      고블린 무기 대부분이 바닐라 무기군 태그(MedievalMeleeDecent, NeolithicMeleeBasic,
      Spear 등)를 함께 달고 있습니다. 바닐라와 다른 모드의 부족민/중세 PawnKind가 바로 그
      태그로 무기를 뽑기 때문에, MUGB를 켜면 남의 습격대가 고블린 무기를 들고 나옵니다.
      태그별 후보 수를 세어보면 MedievalMeleeDecent 풀은 바닐라 4종 대 고블린 9종이라
      고블린 무기가 약 69%를 차지합니다.

      왜 XML만으로는 못 고치는가:
      - 태그를 떼면 남의 세력이 아예 안 들게 되어 "가끔은 노획해서 쓴다"는 그림이 사라집니다.
      - ThingDef.generateCommonality를 낮추면 "누가 뽑는지"를 구분하지 못합니다. 고블린
        중세보병(MUGB_GoblinKind_MedievalFootman/MedievalAdvancedFootman)은 주석에 적힌 대로
        고블린 장비와 미디블 장비를 일부러 섞어 쓰는데, 전역 값을 낮추면 이 혼합비가
        69:31에서 21:79로 뒤집혀 고블린 보병이 글라디우스를 듭니다.

      그래서 무게 계산 지점을 직접 잡습니다:
          PawnWeaponGenerator.GetCommonality(Pawn pawn, ThingStuffPair pair)
              => pair.Commonality * pair.Price * 이데올로기보정 * 제노타입보정
      이 메서드는 pawn을 인자로 받으므로 "누가 뽑는지"를 알 수 있습니다. 여기에 Postfix를
      붙여 비고블린이 고블린 무기를 뽑을 때만 무게를 깎습니다. 고블린 폰카인드와
      고블린 토벌대(MUGB_HumanKind_*)는 조건에 걸리지 않아 기존 확률이 그대로 유지됩니다.

      호환 참고:
      VEF(MVCF/PatchSets/PatchSet_Reloading.cs)가 PawnWeaponGenerator.TryGenerateWeaponFor에
      Postfix를 걸어 재장전 무기의 탄약을 지급합니다. 여기서 TryGenerateWeaponFor를 잡아
      무기를 재추첨하면 그 Postfix와 순서가 얽혀 탄약 없는 총병이 나올 수 있습니다.
      GetCommonality만 건드리는 이유가 이것입니다.
    */
    internal static class MUGBGoblinWeaponLeakUtility
    {
        private const string GoblinWeaponTag = "MUGB_GoblinWeapon";
        private const string MUGBDefNamePrefix = "MUGB_";

        // 설정이 0%여도 무게를 정확히 0으로 만들지는 않습니다.
        // TryRandomElementByWeight는 후보 무게 합이 0이면 무기를 못 고르는데,
        // 그 시점에는 이미 DestroyAllEquipment가 끝나 있어서 폰이 맨손으로 나옵니다.
        // MedievalRangedDecent처럼 바닐라 무기가 하나도 없는 태그를 쓰는 PawnKind가
        // 실제로 존재하므로(고블린 석궁만 후보인 경우), 바닥값을 남겨 둡니다.
        private const float MinimumWeightMultiplier = 0.001f;

        public static bool IsGoblinWeapon(ThingDef def)
        {
            return def?.weaponTags != null && def.weaponTags.Contains(GoblinWeaponTag);
        }

        public static bool IsExemptFromLeakReduction(Pawn pawn)
        {
            if (pawn == null)
            {
                return true;
            }

            // MUGB가 정의한 PawnKind는 전부 예외입니다. 고블린 전투 PawnKind는 물론이고
            // 고블린 토벌대/사냥꾼(MUGB_HumanKind_*)도 여기 포함됩니다. 이들은 비고블린
            // 인간이지만 고블린 장비를 노획해 쓰는 것이 설계 의도입니다.
            if (pawn.kindDef?.defName != null && pawn.kindDef.defName.StartsWith(MUGBDefNamePrefix))
            {
                return true;
            }

            // PawnKind가 무엇이든 고블린 본인은 예외입니다. 다른 모드나 시나리오가 바닐라
            // PawnKind로 고블린을 만들어내는 경우를 위한 것입니다.
            return GoblinUtility.HasGoblinCoreMarker(pawn);
        }

        public static float WeightMultiplier()
        {
            MUGBSettings settings = MUGBMod.Settings;
            int percent = settings?.nonGoblinGoblinWeaponWeightPercent ?? 15;
            return Mathf.Max(percent / 100f, MinimumWeightMultiplier);
        }
    }

    [HarmonyPatch]
    public static class MUGB_NonGoblinGoblinWeaponCommonalityPatch
    {
        public static MethodBase TargetMethod()
        {
            // private static 메서드입니다. 바닐라 업데이트로 사라지면 Prepare가 패치를
            // 조용히 건너뜁니다.
            return AccessTools.Method(typeof(PawnWeaponGenerator), "GetCommonality");
        }

        public static bool Prepare()
        {
            return TargetMethod() != null;
        }

        // 인자 이름 대신 __0/__1을 씁니다. 바닐라 인자명이 바뀌면 이름 바인딩은
        // 패치 하나가 아니라 PatchAll 전체를 죽입니다.
        public static void Postfix(Pawn __0, ThingStuffPair __1, ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }

            if (!MUGBGoblinWeaponLeakUtility.IsGoblinWeapon(__1.thing))
            {
                return;
            }

            if (MUGBGoblinWeaponLeakUtility.IsExemptFromLeakReduction(__0))
            {
                return;
            }

            __result *= MUGBGoblinWeaponLeakUtility.WeightMultiplier();
        }
    }
}
