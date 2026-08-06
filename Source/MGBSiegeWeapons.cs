using HarmonyLib;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MUGB
{
    public class GoblinTurretVisualExtension : DefModExtension
    {
        public Vector2 rotatingTopOffset = Vector2.zero;
    }

    // 한국어 의도: 조작자가 잡고 있을 때만 적을 찾고, 표적이 없으면 포탑 머리를 설치 방향에 고정한다.
    public class Building_GoblinMannedTurret : Building_TurretGun
    {
        private bool HasUsableOperator
        {
            get
            {
                Pawn operatorPawn = mannableComp?.ManningPawn;
                return operatorPawn != null && !operatorPawn.Dead && !operatorPawn.Downed;
            }
        }

        public override LocalTargetInfo TryFindNewTarget()
        {
            if (!HasUsableOperator)
            {
                return LocalTargetInfo.Invalid;
            }

            if (def == MUGBDefOf.MUGB_GoblinMortar
                && gun.TryGetComp<CompChangeableProjectile>()?.LoadedShell == MUGBDefOf.MUGB_GoblinStinkMortarShell)
            {
                float minRange = AttackVerb.verbProps.minRange;
                float maxRange = AttackVerb.EffectiveRange;
                if (Map.mapPawns.AllPawnsSpawned
                    .Where(pawn => pawn != null
                        && !pawn.Dead
                        && !GoblinUtility.IsGoblin(pawn)
                        && pawn.Faction != null
                        && pawn.Faction.HostileTo(Faction)
                        && pawn.Position.DistanceToSquared(Position) >= minRange * minRange
                        && pawn.Position.DistanceToSquared(Position) <= maxRange * maxRange
                        && Map.roofGrid.RoofAt(pawn.Position)?.isThickRoof != true)
                    .TryRandomElement(out Pawn gasTarget))
                {
                    return gasTarget;
                }
            }

            return base.TryFindNewTarget();
        }

        protected override void Tick()
        {
            base.Tick();

            if (!HasUsableOperator)
            {
                currentTargetInt = LocalTargetInfo.Invalid;
                Top.SetRotationFromOrientation();
                return;
            }

            if (!CurrentTarget.IsValid && !ForcedTarget.IsValid && burstWarmupTicksLeft <= 0)
            {
                Top.SetRotationFromOrientation();
            }
        }
    }

    [HarmonyPatch(typeof(SoundStarter), nameof(SoundStarter.PlayOneShot))]
    public static class SoundStarter_GoblinMannedTurretAcquireSoundPatch
    {
        public static bool Prefix(SoundDef soundDef, SoundInfo info)
        {
            return soundDef != SoundDefOf.TurretAcquireTarget
                || !info.Maker.HasThing
                || !(info.Maker.Thing is Building_GoblinMannedTurret);
        }
    }

    [HarmonyPatch(typeof(TurretTop), nameof(TurretTop.DrawTurret))]
    public static class TurretTop_GoblinRotatingOffsetPatch
    {
        private static readonly AccessTools.FieldRef<TurretTop, Building_Turret> ParentTurret =
            AccessTools.FieldRefAccess<TurretTop, Building_Turret>("parentTurret");

        public static bool Prefix(
            TurretTop __instance,
            Vector3 drawLoc,
            Vector3 recoilDrawOffset,
            float recoilAngleOffset)
        {
            Building_Turret parent = ParentTurret(__instance);
            GoblinTurretVisualExtension extension = parent?.def.GetModExtension<GoblinTurretVisualExtension>();
            if (extension == null || extension.rotatingTopOffset == Vector2.zero)
            {
                return true;
            }

            Vector3 localOffset = new Vector3(extension.rotatingTopOffset.x, 0f, extension.rotatingTopOffset.y);
            float aimAngle = parent.CurrentEffectiveVerb?.AimAngleOverride ?? __instance.CurRotation;
            float visualAngle = TurretTop.ArtworkRotation + aimAngle;
            Vector3 rotatingOffset = localOffset.RotatedBy(visualAngle);
            Vector2 fixedOffset = parent.def.building.turretTopOffset;
            Vector3 baseOffset = new Vector3(fixedOffset.x, 0f, fixedOffset.y);
            baseOffset = baseOffset.RotatedBy(recoilAngleOffset) + recoilDrawOffset;
            Vector3 drawPos = drawLoc + Altitudes.AltIncVect + baseOffset + rotatingOffset;
            float drawSize = parent.def.building.turretTopDrawSize;

            Matrix4x4 matrix = Matrix4x4.TRS(
                drawPos,
                visualAngle.ToQuat(),
                new Vector3(drawSize, 1f, drawSize));
            Graphics.DrawMesh(MeshPool.plane10, matrix, parent.TurretTopMaterial, 0);
            return false;
        }
    }

    public class Projectile_GoblinStinkMortarShell : Projectile_Explosive
    {
        // 한국어 의도: 잘자가스탄은 작은 폭발 뒤 페르몬등짐과 같은 농도/범위의 잘자가스를 퍼뜨린다.
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = Map;
            IntVec3 cell = Position;
            Pawn source = launcher as Pawn;

            base.Impact(hitThing, blockedByShield);

            if (blockedByShield || map == null || MUGBDefOf.MUGB_StinkGasCloud == null)
            {
                return;
            }

            GoblinStinkGasUtility.SpawnVanillaStyleSleepGas(cell, map, radius: 4f);
            Thing cloudThing = ThingMaker.MakeThing(MUGBDefOf.MUGB_StinkGasCloud);
            if (cloudThing is GoblinStinkGasCloud cloud)
            {
                GenSpawn.Spawn(cloud, cell, map, WipeMode.Vanish);
                cloud.Initialize(source, gasPower: 1.2f, radius: 4f, fullDurationTicks: 1500, fadeDurationTicks: 240);
            }
        }
    }

    public class Projectile_GoblinHeavyBolt : Bullet
    {
        // 한국어 의도: 대형볼트는 직격(찌르기 30, 관통 20%) 후 파편 피해(반경 1.2, 15, 관통 10%)를 한 번만 준다.
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            Map map = Map;
            IntVec3 cell = Position;
            Thing instigator = launcher;

            base.Impact(hitThing, blockedByShield);

            if (!blockedByShield && map != null)
            {
                SoundDef impactSound = DefDatabase<SoundDef>.GetNamedSilentFail("BulletImpact_Wood");
                GenExplosion.DoExplosion(cell, map, 1.2f, DamageDefOf.Stab, instigator, 15, 0.10f,
                    explosionSound: impactSound, weapon: equipmentDef, projectile: def, intendedTarget: hitThing);
            }
        }
    }
}
