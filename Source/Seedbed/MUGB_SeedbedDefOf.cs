using RimWorld;
using Verse;

namespace MUGB.Seedbed
{
    // 한국어 의도: 종양 모판 시스템이 쓰는 Def 참조 모음입니다.
    //
    // 기존 MUGBDefOf에 얹지 않고 따로 둔 이유: 이 기능만 통째로 되돌릴 수 있게 하려는
    // 것입니다. 파일 하나와 Defs/*_TumorSeedbed*.xml만 지우면 나머지에 흔적이 남지 않습니다.
    [DefOf]
    public static class MUGB_SeedbedDefOf
    {
        public static ThingDef MUGB_FleshTumor;

        public static HediffDef MUGB_TumorSeedbed;

        public static HediffDef MUGB_TumorGestation;

        public static HediffDef MUGB_SeedbedDegeneration;

        public static HediffDef MUGB_ActivatorDrain;

        public static RecipeDef MUGB_InduceTumorSeedbed;

        public static RecipeDef MUGB_InjectTumorActivator;

        public static ThoughtDef MUGB_TumorSeedbedInstalled;

        public static ThoughtDef MUGB_InjectedWithActivator;

        public static ThoughtDef MUGB_ActivatorAccepted;

        public static ThoughtDef MUGB_GaveBirthToTumor;

        public static ThoughtDef MUGB_SeedbedEuphoria;

        public static PawnTableDef MUGB_HumanLivestock_Seedbeds;

        static MUGB_SeedbedDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MUGB_SeedbedDefOf));
        }
    }
}
