using RimWorld;
using Verse;

namespace MUGB.Livestock
{
    // 한국어 의도: 인간가축 시스템이 쓰는 Def 참조 모음입니다.
    // 이데올로기 전용 Def는 MayRequire로 감싸서 DLC가 없을 때 로그 오염을 막습니다.
    [DefOf]
    public static class MUGB_LivestockDefOf
    {
        public static DesignationDef MUGB_SlaughterHumanlike;

        public static DesignationDef MUGB_ProtectFromSlaughter;

        public static JobDef MUGB_TakeLivestockToButcher;

        public static ThoughtDef MUGB_WitnessedLivestockButchery;

        public static ThoughtDef MUGB_WitnessedLivestockResigned;

        public static ThoughtDef MUGB_KnowLivestockSlaughter;

        public static ThoughtDef MUGB_LivestockSlaughterAtonement;

        public static PawnTableDef MUGB_HumanLivestock_Prisoners;

        public static PawnTableDef MUGB_HumanLivestock_Slaves;

        public static PawnTableDef MUGB_HumanLivestock_Marked;

        [MayRequire("Ludeon.RimWorld.Ideology")]
        public static PrisonerInteractionModeDef MUGB_MeatLivestock;

        [MayRequire("Ludeon.RimWorld.Ideology")]
        public static PreceptDef MUGB_LivestockSlaves_Acceptable;

        [MayRequire("Ludeon.RimWorld.Ideology")]
        public static PreceptDef MUGB_LivestockSlaves_Encouraged;

        static MUGB_LivestockDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MUGB_LivestockDefOf));
        }
    }
}
