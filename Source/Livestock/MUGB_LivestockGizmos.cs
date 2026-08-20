using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MUGB.Livestock
{
    // 한국어 의도: 노예/죄수 개별 기즈모입니다. P1은 이 버튼 하나로 기능이 완결됩니다.
    // 인간가축 탭(P3)이 붙으면 탭의 체크박스도 같은 Designation을 토글하므로 자동으로
    // 동기화됩니다. 따로 상태를 들지 않는 이유가 그것입니다.
    //
    // 성능 주의(설계지침 6.9): GetGizmos는 폰이 선택된 동안 매 프레임 불립니다. 그래서
    // 여기서는 비싼 검사를 하지 않습니다.
    //   - 도축대 유무 검사는 '지정하는 순간'으로 옮겼습니다. 툴팁에 넣으면 초당 60번 작업대
    //     목록을 훑게 되고, 툴팁은 마우스를 올려야 보이므로 전달력도 떨어집니다.
    //   - 규율 판정은 틱 스탬프 캐시가 걸려 있습니다.
    //   - IsDesignated는 딕셔너리 조회라 그대로 둡니다.
    public static class MUGB_LivestockGizmos
    {
        public static IEnumerable<Gizmo> AppendTo(IEnumerable<Gizmo> gizmos, Pawn pawn)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                yield return gizmo;
            }

            if (!MUGB_LivestockUtility.CanEverDesignate(pawn))
            {
                yield break;
            }

            Command_Toggle toggle = new Command_Toggle
            {
                defaultLabel = "MUGB_DesignateForSlaughter".Translate(),
                defaultDesc = "MUGB_DesignateForSlaughterDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Icons/MGB_slavebutcher", false),
                isActive = () => MUGB_LivestockUtility.IsDesignated(pawn),
                toggleAction = delegate
                {
                    bool designated = MUGB_LivestockUtility.IsDesignated(pawn);
                    MUGB_LivestockUtility.SetDesignated(pawn, !designated);

                    if (!designated)
                    {
                        WarnIfNoUsableStation(pawn);
                    }
                }
            };

            // 규율이 없으면 숨기지 않고 잠급니다. 숨겨 버리면 플레이어가 "왜 이 기능이
            // 없지"를 알 방법이 없습니다. 잠긴 버튼은 이유를 말해 줍니다.
            if (!MUGB_LivestockUtility.PreceptAllowsButchering())
            {
                toggle.Disable("MUGB_DesignateNoPrecept".Translate());
            }

            yield return toggle;
        }

        // 지정해도 받아줄 계획서가 없으면 아무도 오지 않습니다. 조용히 방치되면 플레이어가
        // 헤매므로 이 시점에 한 번 알립니다. 클릭할 때만 도는 검사입니다.
        public static void WarnIfNoUsableStation(Pawn pawn)
        {
            if (MUGB_LivestockUtility.AnyStationAcceptsCorpseOf(pawn))
            {
                return;
            }

            Messages.Message(
                "MUGB_NoButcherStationWarning".Translate(pawn.Named("PAWN")),
                pawn,
                MessageTypeDefOf.CautionInput,
                historical: false);
        }
    }
}
