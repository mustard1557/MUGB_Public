using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace MUGB.Patches
{
    /*
        [파일 개요]
        RJW / RJW Menstruation 임신 경로로 태어나는 고블린 아기를 MUGB 규격으로 되돌립니다.

        [문제]
        MUGB의 출산 파이프라인은 바닐라 Biotech 임신 헤디프에만 붙어 있습니다.
        (Patches/CoreGoblinPregnancyPlan.xml -> PregnantHuman / PregnancyLabor / PregnancyLaborPushing)

        그런데 RJW Menstruation의 임신 방식 기본값은 MultiplePregnancy이고, 이 경로와 Base RJW 경로는
        PregnancyUtility.ApplyBirthOutcome을 아예 거치지 않습니다. 아기는 수태 시점에 RJW가
        PregnancyCommon.BabyPawnKindDecider로 직접 만들고, 그 결과는 대개 어미의 PawnKind입니다.
        어미가 HAR 외계 종족이면 아기도 그 종족으로 태어나 머리카락과 꼬리를 물려받고,
        제노타입 이름표만 고블린으로 붙는 일이 생깁니다. 얼굴 부속은 고블린 유전자에 딸린
        렌더 노드에서 나오므로, 그 유전자가 상속되지 않으면 하나도 나오지 않습니다.

        [해결]
        1) 수태 시점에 RJW가 채운 아기 목록을 MUGB가 만든 아기로 교체합니다.
           GeneratePregeneratedChild가 종족(인간 뼈대) / 제노타입 / 성별 / 나이 / 유전자를
           전부 확정하므로, 교체 시점에 이미 완성된 아기가 됩니다.
        2) 출산 직후에 어미만 남기는 관계 정리, 사상 계승, 노예/포로 신분 계승을 붙입니다.
           RJW의 MultiplePregnancy 경로는 부모 관계를 아예 걸어주지 않으므로 이 단계가 필요합니다.

        [의도적으로 하지 않은 것]
        산자수는 RJW가 정한 수를 그대로 씁니다. 마리 수까지 MUGB 규격으로 바꾸면
        기존 RJW 경로 플레이어의 출산 규모가 갑자기 달라지므로, 이번에는 외형 문제만 고칩니다.
        같은 이유로 임신 기간 가속과 출산 부담 헤디프도 RJW 경로에 넣지 않았습니다.

        [기존 세이브]
        전부 새 수태부터 적용됩니다. 이미 태어난 폰의 종족은 사후에 바꿀 수 없으므로 그대로 남습니다.
        진행 중인 임신은 아기가 이미 만들어져 있어 예전 그대로 태어납니다.

        [틱 비용]
        수태 1회와 아기 1마리 출산당 1회만 실행됩니다. 매 틱 도는 코드는 없습니다.

        [반사 접근을 쓰는 이유]
        MUGB는 RJW와 Menstruation을 참조하지 않고 빌드됩니다. 두 모드가 없어도 동작해야 하므로
        타입과 필드를 전부 이름으로 찾고, 못 찾으면 경고만 남기고 조용히 빠집니다.
    */
    public static class GoblinRJWBirthCompat
    {
        private const string BasePregnancyTypeName = "rjw.Hediff_BasePregnancy";
        private const string PregeneratedBabiesTypeName = "RJW_Menstruation.HediffComp_PregeneratedBabies";

        private static FieldInfo pregnancyFatherField;
        private static bool warnedAboutBabiesField;

        public static void Patch(Harmony harmony)
        {
            try
            {
                PatchRJWPregnancyInitialize(harmony);
                PatchMenstruationExtraBaby(harmony);
            }
            catch (Exception e)
            {
                // 여기서 예외가 새어 나가면 모드 초기화 전체가 깨집니다.
                // 호환 패치가 안 걸리는 것보다 게임이 뜨는 것이 우선입니다.
                Log.Error("[MUGB] Failed to apply the RJW goblin birth compatibility patches: " + e);
            }
        }

        // Hediff_BasePregnancy.Initialize는 비가상 public이고 그 안에서 가상 GenerateBabies를 부릅니다.
        // 여기 하나만 잡으면 Hediff_HumanlikePregnancy(Base RJW)와
        // Hediff_MultiplePregnancy(Menstruation 기본값)를 포함한 모든 하위 임신이 함께 처리됩니다.
        private static void PatchRJWPregnancyInitialize(Harmony harmony)
        {
            Type pregnancyType = AccessTools.TypeByName(BasePregnancyTypeName);
            if (pregnancyType == null)
            {
                return;
            }

            pregnancyFatherField = AccessTools.Field(pregnancyType, "father");
            MethodInfo initialize = FindSingleMethod(pregnancyType, "Initialize", parameterCount: 3);
            if (pregnancyFatherField == null || initialize == null)
            {
                Log.Warning(
                    "[MUGB] RJW is loaded but Hediff_BasePregnancy.Initialize/father was not found. "
                    + "Goblin babies conceived through RJW pregnancies may keep the mother's race.");
                return;
            }

            harmony.Patch(
                initialize,
                postfix: new HarmonyMethod(typeof(GoblinRJWBirthCompat), nameof(RJWPregnancyInitializePostfix)));
        }

        // 이란성 쌍둥이로 두 번째 난자가 수정되면 Menstruation이 이미 만들어진 목록 뒤에
        // 아기를 덧붙입니다. 그 아기만 어미 종족으로 남는 구멍을 막습니다.
        private static void PatchMenstruationExtraBaby(Harmony harmony)
        {
            Type compType = AccessTools.TypeByName(PregeneratedBabiesTypeName);
            if (compType == null)
            {
                return;
            }

            MethodInfo addNewBaby = AccessTools.Method(compType, "AddNewBaby", new[] { typeof(Pawn), typeof(Pawn) });
            ParameterInfo[] parameters = addNewBaby?.GetParameters();

            // 매개변수를 이름으로 받아오므로 이름이 바뀌면 패치 시점에 예외가 납니다.
            // 미리 확인해서, 다르면 걸지 않고 경고만 남깁니다.
            if (addNewBaby == null
                || parameters.Length != 2
                || parameters[0].Name != "mother"
                || parameters[1].Name != "father")
            {
                Log.Warning(
                    "[MUGB] RJW Menstruation is loaded but HediffComp_PregeneratedBabies.AddNewBaby(mother, father) "
                    + "was not found in the expected shape. Extra twins added to a goblin pregnancy may keep the mother's race.");
                return;
            }

            harmony.Patch(
                addNewBaby,
                prefix: new HarmonyMethod(typeof(GoblinRJWBirthCompat), nameof(AddNewBabyPrefix)),
                postfix: new HarmonyMethod(typeof(GoblinRJWBirthCompat), nameof(AddNewBabyPostfix)));
        }

        // 매개변수를 하나도 받지 않고 인스턴스 필드만 읽습니다.
        // RJW가 인자 이름을 바꾸어도 이 패치는 그대로 동작합니다.
        public static void RJWPregnancyInitializePostfix(object __instance)
        {
            try
            {
                Pawn mother = ReadPregnancyMother(__instance);
                Pawn father = ReadPregnancyFather(__instance);
                GoblinPregnancyPlanInitializer.ClearPendingFather(mother);
                if (!ShouldRebuildLitter(mother, father))
                {
                    return;
                }

                List<Pawn> babies = ReadBabies(__instance);
                if (babies.NullOrEmpty())
                {
                    return;
                }

                if (ReplaceBabiesInPlace(babies, mother, father, plan: null))
                {
                    ClearEnzygoticSiblings(__instance);
                }
            }
            catch (Exception e)
            {
                Log.Error("[MUGB] Failed to rebuild an RJW goblin litter: " + e);
            }
        }

        public static void AddNewBabyPrefix(HediffComp __instance, out int __state)
        {
            __state = ReadBabies(__instance)?.Count ?? 0;
        }

        public static void AddNewBabyPostfix(HediffComp __instance, Pawn mother, Pawn father, int __state)
        {
            try
            {
                if (!ShouldRebuildLitter(mother, father))
                {
                    return;
                }

                // 계획이 아직 없으면(첫 수태 시점) 곧이어 계획 초기화가 목록 전체를 교체합니다.
                // 그쪽에 맡기는 편이 안전하므로 여기서는 손대지 않습니다.
                HediffComp_MUGBGoblinPregnancyPlan plan = (__instance?.parent as HediffWithComps)
                    ?.TryGetComp<HediffComp_MUGBGoblinPregnancyPlan>();
                if (plan?.Initialized != true)
                {
                    return;
                }

                List<Pawn> babies = ReadBabies(__instance);
                if (babies == null || babies.Count <= __state)
                {
                    return;
                }

                if (ReplaceBabiesInPlace(babies, mother, father, plan, startIndex: __state))
                {
                    ClearEnzygoticSiblings(__instance);
                }
            }
            catch (Exception e)
            {
                Log.Error("[MUGB] Failed to rebuild an extra RJW Menstruation goblin baby: " + e);
            }
        }

        private static bool ShouldRebuildLitter(Pawn mother, Pawn father)
        {
            return mother?.RaceProps?.Humanlike == true && GoblinUtility.IsGoblin(father);
        }

        /// <summary>
        /// 아기 목록의 startIndex부터를 MUGB가 만든 아기로 바꿉니다. 마리 수는 그대로입니다.
        /// plan이 있으면(바닐라 Biotech + Menstruation 경로) 출산 시 다시 적용될 결과를
        /// 계획에 같이 덧붙여, 생성 시점과 출산 시점의 판정이 어긋나지 않게 맞춥니다.
        /// </summary>
        private static bool ReplaceBabiesInPlace(
            List<Pawn> babies,
            Pawn mother,
            Pawn father,
            HediffComp_MUGBGoblinPregnancyPlan plan,
            int startIndex = 0)
        {
            bool hobgoblinFather = GoblinUtility.IsHobgoblin(father);
            bool replacedAny = false;

            for (int i = startIndex; i < babies.Count; i++)
            {
                Pawn original = babies[i];

                // 동물 새끼가 섞이는 임신(수간/곤충 등)은 건드리지 않습니다.
                // 위 ShouldRebuildLitter에서 대부분 걸러지지만 한 겹 더 둡니다.
                if (original != null && original.RaceProps?.Humanlike != true)
                {
                    continue;
                }

                GoblinBirthUtility.GoblinBirthResult roll = plan != null
                    ? plan.AppendRolledResult(hobgoblinFather)
                    : GoblinBirthUtility.RollGoblinBirthResult(hobgoblinFather);

                Pawn replacement = GoblinBirthUtility.GeneratePregeneratedChild(mother, father, roll);
                if (replacement == null || replacement == original)
                {
                    continue;
                }

                babies[i] = replacement;
                replacedAny = true;
                DiscardUnusedBaby(original);
            }

            return replacedAny;
        }

        // RJW가 만들어 두었다가 쓰이지 않게 된 아기를 정리합니다.
        // 관계를 먼저 끊는 이유는 일부 경로가 생성 직후 SetMother/SetFather를 걸어두기 때문입니다.
        // 그대로 파괴하면 어미의 관계 목록에 사라진 자식이 남습니다.
        private static void DiscardUnusedBaby(Pawn baby)
        {
            if (baby == null || baby.Destroyed)
            {
                return;
            }

            try
            {
                baby.relations?.ClearAllRelations();
                baby.Destroy(DestroyMode.Vanish);
            }
            catch (Exception e)
            {
                Log.Warning("[MUGB] Failed to discard an unused RJW newborn: " + e);
            }
        }

        // 일란성 쌍둥이 표는 버려진 아기를 키로 들고 있게 되고, 그 표는 폰 참조로 저장됩니다.
        // 파괴된 폰 참조가 세이브에 남으면 불러올 때 해석 실패 경고가 뜨므로 비웁니다.
        // 교체된 아기는 전부 MUGB가 만든 것이라 외형 복사 대상도 아닙니다.
        private static void ClearEnzygoticSiblings(object instance)
        {
            try
            {
                FieldInfo field = AccessTools.Field(instance.GetType(), "enzygoticSiblings");
                if (field?.GetValue(instance) is IDictionary siblings)
                {
                    siblings.Clear();
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MUGB] Failed to clear the RJW enzygotic twin table: " + e);
            }
        }

        public static Pawn ReadPregnancyMother(object instance)
        {
            // 임신 헤디프를 들고 있는 폰이 어미입니다.
            return (instance as Hediff)?.pawn;
        }

        public static Pawn ReadPregnancyFather(object instance)
        {
            if (instance == null)
            {
                return null;
            }

            FieldInfo field = pregnancyFatherField;
            if (field == null || !field.DeclaringType.IsInstanceOfType(instance))
            {
                field = AccessTools.Field(instance.GetType(), "father");
            }

            return field?.GetValue(instance) as Pawn;
        }

        private static List<Pawn> ReadBabies(object instance)
        {
            if (instance == null)
            {
                return null;
            }

            // Hediff_BasePregnancy와 HediffComp_PregeneratedBabies가 각자 같은 이름의 목록을 갖고 있어
            // 인스턴스 타입에서 바로 찾습니다.
            FieldInfo field = AccessTools.Field(instance.GetType(), "babies");
            if (field == null || !typeof(List<Pawn>).IsAssignableFrom(field.FieldType))
            {
                if (!warnedAboutBabiesField)
                {
                    warnedAboutBabiesField = true;
                    Log.Warning("[MUGB] A pregnancy baby list was not found on " + instance.GetType().FullName + ".");
                }
                return null;
            }

            return field.GetValue(instance) as List<Pawn>;
        }

        private static MethodInfo FindSingleMethod(Type type, string name, int parameterCount)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo match = null;
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != name || methods[i].GetParameters().Length != parameterCount)
                {
                    continue;
                }

                // 이름과 인자 개수가 같은 것이 둘 이상이면 어느 쪽인지 확신할 수 없으므로 포기합니다.
                if (match != null)
                {
                    return null;
                }
                match = methods[i];
            }

            return match;
        }
    }

    public static class RJW_HediffBasePregnancy_PostBirth_GoblinNewbornPatch
    {
        // RJW 경로는 출산을 자체 처리하므로 MUGB의 출생 후처리가 돌지 않습니다.
        // 아기 한 마리가 스폰된 직후 호출되는 이 지점에서 어미만 남기는 관계 정리,
        // 사상 계승, 신분 계승을 붙입니다.
        //
        // MultiplePregnancy 경로는 아비를 아기의 관계에서 역추적하는데 MUGB 아기에는 그 관계가
        // 없어 null이 넘어옵니다. 그래서 인자가 비면 임신 헤디프에서 직접 읽습니다.
        public static void Postfix(object __instance, Pawn mother, Pawn father, Pawn baby)
        {
            try
            {
                GoblinBirthUtility.ApplyRJWNewbornFollowUp(
                    mother ?? GoblinRJWBirthCompat.ReadPregnancyMother(__instance),
                    father ?? GoblinRJWBirthCompat.ReadPregnancyFather(__instance),
                    baby);
            }
            catch (Exception e)
            {
                Log.Error("[MUGB] Failed to finalize an RJW goblin newborn: " + e);
            }
        }
    }
}
