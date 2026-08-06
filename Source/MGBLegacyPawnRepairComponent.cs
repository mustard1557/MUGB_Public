using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MUGB
{
    // 한국어 참고: 이미 진행 중인 세이브에 남아 있는 고블린을 한 번만 손봐 주는 컴포넌트입니다.
    //
    // 예전 폰 생성 코드가 제노타입 주사위를 두 번 굴리는 바람에, 한 폰에 체형 유전자가 둘 다
    // (MUGB_Gene_GoblinCore + MUGB_Gene_HobgoblinFrame) 붙는 경우가 있었습니다. 둘은 같은
    // exclusionTags를 공유해 충돌하고 GoblinCore가 이기기 때문에, 제노타입은 홉고블린인데
    // 몸은 띤(0.8배)으로 그려지는 "작은 홉고블린"이 생겼습니다.
    // 생성 쪽은 이미 고쳤지만 그건 새로 만들어지는 폰에만 적용되므로, 기존 세이브의 폰은
    // 로드할 때 한 번 정리해 줍니다.
    //
    // 안전을 위해 지키는 것:
    // - 세이브당 1회만 실행하고 플래그를 저장합니다(매 틱 도는 코드가 아닙니다).
    // - 체형 유전자가 둘 다 있는 폰만 건드립니다. 정상 폰은 아예 손대지 않습니다.
    // - 유전자 하나만 제거하며 스킬/관계/나이/건강은 읽지도 않습니다.
    // - 설정에서 끌 수 있고, 예외가 나도 세이브 로딩이 멈추지 않게 감쌉니다.
    public class MGBLegacyPawnRepairComponent : GameComponent
    {
        private bool bodyPlanGenesRepaired;
        private bool textureSkinColorsNormalized;
        private bool goblinBirthStrainsMigrated;
        private bool goblinBabyBornThoughtsMigrated;

        public MGBLegacyPawnRepairComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref bodyPlanGenesRepaired, "MUGB_bodyPlanGenesRepaired", false);
            Scribe_Values.Look(ref textureSkinColorsNormalized, "MUGB_textureSkinColorsNormalized", false);
            Scribe_Values.Look(ref goblinBirthStrainsMigrated, "MUGB_goblinBirthStrainsMigrated", false);
            Scribe_Values.Look(ref goblinBabyBornThoughtsMigrated, "MUGB_goblinBabyBornThoughtsMigrated", false);
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            TryRepairDuplicateBodyPlanGenes();
            TryNormalizeTextureSkinColors();
            TryMigrateGoblinBirthStrains();
            TryMigrateGoblinBabyBornThoughts();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            // 새 게임에는 손볼 낡은 폰이 없으므로 바로 완료 처리합니다.
            bodyPlanGenesRepaired = true;
            textureSkinColorsNormalized = true;
            goblinBirthStrainsMigrated = true;
            goblinBabyBornThoughtsMigrated = true;
        }

        private void TryRepairDuplicateBodyPlanGenes()
        {
            if (bodyPlanGenesRepaired || MUGBMod.Settings?.repairLegacyGoblinPawns == false)
            {
                return;
            }

            int repaired = 0;
            try
            {
                foreach (Pawn pawn in AllCandidatePawns())
                {
                    if (TryRemoveConflictingBodyPlanGene(pawn))
                    {
                        repaired++;
                    }
                }

                bodyPlanGenesRepaired = true;
                if (repaired > 0)
                {
                    Log.Message($"[MUGB] Repaired {repaired} goblin(s) that carried both body-plan genes.");
                }
            }
            catch (Exception e)
            {
                // 실패해도 세이브 로딩은 계속되어야 합니다. 플래그를 세우지 않아 다음 로드에서 다시 시도합니다.
                Log.Warning("[MUGB] Could not repair legacy goblin body-plan genes: " + e);
            }
        }

        private static IEnumerable<Pawn> AllCandidatePawns()
        {
            // 맵 위의 폰과 월드 폰(세력 지도자, 캐러밴 등)을 모두 훑습니다. 로드 시 1회뿐입니다.
            IEnumerable<Pawn> pawns = PawnsFinder.AllMapsWorldAndTemporary_Alive ?? Enumerable.Empty<Pawn>();
            foreach (Pawn pawn in pawns.ToList())
            {
                if (pawn?.genes != null)
                {
                    yield return pawn;
                }
            }
        }

        private static bool TryRemoveConflictingBodyPlanGene(Pawn pawn)
        {
            GeneDef core = MUGBDefOf.MUGB_Gene_GoblinCore;
            GeneDef frame = MUGBDefOf.MUGB_Gene_HobgoblinFrame;
            if (core == null || frame == null)
            {
                return false;
            }

            Gene coreGene = pawn.genes.GetGene(core);
            Gene frameGene = pawn.genes.GetGene(frame);
            if (coreGene == null || frameGene == null)
            {
                // 하나만 갖고 있으면 정상입니다.
                return false;
            }

            // 제노타입이 어느 쪽인지에 따라 남길 유전자를 정합니다.
            Gene loser = pawn.genes.Xenotype == MUGBDefOf.MUGB_Hobgoblin ? coreGene : frameGene;
            pawn.genes.RemoveGene(loser);
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            return true;
        }

        private void TryMigrateGoblinBirthStrains()
        {
            if (goblinBirthStrainsMigrated)
            {
                return;
            }

            try
            {
                int migrated = 0;
                IEnumerable<Pawn> pawns = PawnsFinder.AllMapsWorldAndTemporary_Alive ?? Enumerable.Empty<Pawn>();
                foreach (Pawn pawn in pawns.ToList())
                {
                    if (Patches.GoblinBirthStrainUtility.MigrateLegacyStrain(pawn))
                    {
                        migrated++;
                    }
                }

                goblinBirthStrainsMigrated = true;
                if (migrated > 0)
                {
                    Log.Message($"[MUGB] Migrated {migrated} legacy goblin birth strain record(s).");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MUGB] Could not migrate legacy goblin birth strain records: " + e);
            }
        }

        private void TryNormalizeTextureSkinColors()
        {
            if (textureSkinColorsNormalized)
            {
                return;
            }

            int normalized = 0;
            try
            {
                foreach (Pawn pawn in AllCandidatePawns())
                {
                    if (!GoblinUtility.HasGoblinCoreMarker(pawn)
                        || pawn.story == null
                        || pawn.story.skinColorOverride == GoblinUtility.GoblinTextureSkinColor)
                    {
                        continue;
                    }

                    pawn.story.skinColorOverride = GoblinUtility.GoblinTextureSkinColor;
                    pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                    normalized++;
                }

                textureSkinColorsNormalized = true;
                if (normalized > 0)
                {
                    Log.Message($"[MUGB] Normalized the texture skin color of {normalized} existing goblin(s).");
                }
            }
            catch (Exception e)
            {
                // 실패한 세이브는 완료 처리하지 않아 다음 로드에서 다시 시도합니다.
                Log.Warning("[MUGB] Could not normalize legacy goblin texture skin colors: " + e);
            }
        }

        private void TryMigrateGoblinBabyBornThoughts()
        {
            if (goblinBabyBornThoughtsMigrated)
            {
                return;
            }

            try
            {
                ThoughtDef vanilla = DefDatabase<ThoughtDef>.GetNamedSilentFail("BabyBorn");
                ThoughtDef replacement = MUGBDefOf.MUGB_GoblinBabyBorn;
                if (vanilla == null || replacement == null)
                {
                    goblinBabyBornThoughtsMigrated = true;
                    return;
                }

                int migrated = 0;
                foreach (Pawn pawn in AllCandidatePawns())
                {
                    if (!GoblinUtility.IsGoblin(pawn))
                    {
                        continue;
                    }

                    MemoryThoughtHandler memories = pawn.needs?.mood?.thoughts?.memories;
                    List<Thought_Memory> existing = memories?.Memories?
                        .Where(memory => memory?.def == vanilla)
                        .ToList();
                    if (existing.NullOrEmpty())
                    {
                        continue;
                    }

                    Thought_Memory newest = existing.OrderBy(memory => memory.age).First();
                    Pawn baby = newest.otherPawn;
                    int age = newest.age;
                    for (int i = 0; i < existing.Count; i++)
                    {
                        memories.RemoveMemory(existing[i]);
                    }

                    if (memories.GetFirstMemoryOfDef(replacement) == null)
                    {
                        Thought_Memory converted = ThoughtMaker.MakeThought(replacement) as Thought_Memory;
                        if (converted != null)
                        {
                            converted.age = age;
                            memories.TryGainMemory(converted, baby);
                        }
                    }
                    migrated++;
                }

                goblinBabyBornThoughtsMigrated = true;
                if (migrated > 0)
                {
                    Log.Message($"[MUGB] Migrated {migrated} goblin baby-birth thought record(s).");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MUGB] Could not migrate legacy goblin baby-birth thoughts: " + e);
            }
        }
    }
}
