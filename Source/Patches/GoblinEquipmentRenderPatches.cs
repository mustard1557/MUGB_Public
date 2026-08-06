using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MUGB.Patches
{
    [HarmonyPatch]
    public static class PawnRenderUtility_DrawEquipmentAiming_MugbRangedPatch
    {
        private const float AimingForwardOffset = 0.1f;
        private const float GunAimingForwardOffset = 0.2f;
        private const float GunAimingLeftOffset = 0.035f;
        private const float GunAimingAngleOffset = -5f;
        private const float SlingIdleForwardOffset = 0f;
        private const float SlingEastWestIdleInwardOffset = 0.14f;
        private const float SlingEastWestIdleNorthOffset = 0.06f;
        private const float SlingGripBackOffset = 0.07f;
        private const float SlingEquippedAngleOffset = 48f;
        private const float SlingEastWestIdleMirrorAngleOffset = 90f;
        private const float DraftedIdleAngleOffset = -90f;
        private const float WarbowNorthSouthIdleAngleOffset = 62f;
        private const float GunEastWestIdleAngleOffset = 105f;
        private const float StickNorthSouthIdleAngleOffset = -71f;
        private const float FlailNorthSouthIdleAngleCorrection = -25f;
        private const float StickNorthSouthIdleNorthOffset = 0.4f;
        private const float StickNorthSouthIdleSideOffset = 0.25f;
        private const float StickNorthDrawAltitudeOffset = -0.08f;
        private const float MeleeEastDrawAltitudeOffset = 0.08f;
        private const float MeleeWestDrawAltitudeOffset = -0.25f;
        private const float NonStickMeleeNorthSouthIdleAngleOffset = -120f;
        private const float NonStickMeleeNorthSouthIdleSideOffset = 0.31f;
        private const float NonStickMeleeNorthSouthIdleNorthOffset = 0.24f;
        private const float NonStickMeleeMacheteNorthSouthIdleNorthOffset = 0.30f;
        private const float NonStickMeleeAxeNorthSouthIdleSideOffset = 0.35f;
        private const float NonStickMeleeMacheteNorthSouthIdleSideOffset = 0.39f;
        private const float SpearMeleeThrustForwardOffset = 0.35f;
        private const string YayoAnimationContinuedPackageId = "com.yayo.yayoAni.continued";
        private const string YayoAnimationOriginalPackageId = "com.yayo.yayoAni";

        private static readonly bool YayoAnimationActive = IsModActive(YayoAnimationContinuedPackageId)
            || IsModActive(YayoAnimationOriginalPackageId);

        private static readonly string[] MugbRangedWeaponDefNames =
        {
            "MUGB_GoblinStaffSling",
            "MUGB_GoblinWarbow",
            "MUGB_GoblinCrossbow",
            "MUGB_GoblinRepeatingCrossbow",
            "MUGB_GoblinArquebus",
            "MUGB_GoblinMusket",
            "MUGB_GoblinHandgonne",
            "MUGB_GoblinHandcannon",
            "MUGB_GoblinBlowdart",
            "MUGB_GoblinChainSnare"
        };

        private static readonly string[] MugbGunWeaponDefNames =
        {
            "MUGB_GoblinArquebus",
            "MUGB_GoblinMusket",
            "MUGB_GoblinHandgonne",
            "MUGB_GoblinHandcannon"
        };

        private static readonly string[] MugbStickWeaponDefNames =
        {
            "MUGB_GoblinSpear",
            "MUGB_GoblinBannerSpear",
            "MUGB_GoblinBoneSpear",
            "MUGB_GoblinFlail",
            "MUGB_GoblinShamanStaff"
        };

        private static readonly string[] MugbSpearWeaponDefNames =
        {
            "MUGB_GoblinBoneSpear",
            "MUGB_GoblinSpear",
            "MUGB_GoblinBannerSpear"
        };

        private static readonly string[] MugbMeleeWeaponDefNames =
        {
            "MUGB_GoblinBoneHammer",
            "MUGB_GoblinBoneClub",
            "MUGB_GoblinBoneAxe",
            "MUGB_GoblinBoneSpear",
            "MUGB_GoblinDagger",
            "MUGB_GoblinCleaver",
            "MUGB_GoblinCurvedBlade",
            "MUGB_GoblinAxe",
            "MUGB_GoblinSpear",
            "MUGB_GoblinBannerSpear",
            "MUGB_GoblinMace",
            "MUGB_GoblinFlail",
            "MUGB_GoblinMachete",
            "MUGB_GoblinShamanStaff",
            "MUGB_GoblinBoomstick"
        };

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(PawnRenderUtility),
                nameof(PawnRenderUtility.DrawEquipmentAiming),
                new[] { typeof(Thing), typeof(Vector3), typeof(float) });
        }

        public static bool Prefix(Thing eq, ref Vector3 drawLoc, ref float aimAngle)
        {
            if (eq?.def == null || (!IsMugbRangedWeapon(eq.def) && !IsMugbStickWeapon(eq.def) && !IsMugbMeleeWeapon(eq.def)))
            {
                return true;
            }

            Pawn pawn = HoldingPawn(eq);
            bool hasSpecialAim = TryGetSpecialWeaponAimAngle(pawn, eq.def, out float specialAimAngle);
            if (hasSpecialAim)
            {
                return !TryDrawSpecialStickAim(eq, drawLoc, specialAimAngle);
            }
            if (!YayoAnimationActive && IsRegularSpearMeleeAttack(eq, pawn))
            {
                if (pawn.Rotation == Rot4.North)
                {
                    drawLoc.y += StickNorthDrawAltitudeOffset;
                }
                else if (pawn.Rotation == Rot4.East)
                {
                    drawLoc.y += MeleeEastDrawAltitudeOffset;
                }
                else if (pawn.Rotation == Rot4.West)
                {
                    drawLoc.y += MeleeWestDrawAltitudeOffset;
                }

                return !TryDrawSpecialStickAim(eq, drawLoc, aimAngle, SpearMeleeThrustForwardOffset);
            }
            bool isAiming = hasSpecialAim || IsInAimingStance(pawn);
            if (pawn?.Drafted == true
                && !isAiming
                && MUGBMod.Settings?.enableDraftedWeaponPoseOffsets == false)
            {
                // Only the custom drafted idle pose is optional. Attacks, spear thrusts,
                // throws and charges have already taken their normal render paths above.
                return true;
            }
            if (pawn?.Drafted == true
                && !isAiming
                && eq is ThingWithComps equipment
                && equipment.TryGetComp<MUGB.CompGoblinShamanStaff>() != null)
            {
                // Staves keep their ordinary carried pose while drafted instead of using
                // the weapon-specific north/south idle offsets intended for spears.
                return true;
            }
            if (TryDrawMirroredNonStickMelee(eq, pawn, isAiming, drawLoc, aimAngle))
            {
                return false;
            }

            if (IsMugbMeleeWeapon(eq.def))
            {
                if (pawn?.Rotation == Rot4.East)
                {
                    drawLoc.y += MeleeEastDrawAltitudeOffset;
                }
                else if (pawn?.Rotation == Rot4.West)
                {
                    drawLoc.y += MeleeWestDrawAltitudeOffset;
                }
            }

            if (IsMugbStickWeapon(eq.def))
            {
                if (pawn?.Rotation == Rot4.North)
                {
                    drawLoc.y += StickNorthDrawAltitudeOffset;
                }

                if (pawn?.Drafted == true && !isAiming && ShouldUseDraftedIdleAngle(pawn.Rotation))
                {
                    aimAngle += StickNorthSouthIdleAngleOffset + StickIdleAngleCorrectionFor(eq.def);
                    drawLoc.z += StickNorthSouthIdleNorthOffset;
                    drawLoc.x += pawn.Rotation == Rot4.South ? -StickNorthSouthIdleSideOffset : StickNorthSouthIdleSideOffset;
                }

                if (!IsMugbRangedWeapon(eq.def))
                {
                    return true;
                }
            }
            else if (IsMugbMeleeWeapon(eq.def))
            {
                if (pawn?.Drafted == true && !isAiming && ShouldUseDraftedIdleAngle(pawn.Rotation))
                {
                    aimAngle += NonStickNorthSouthAngleOffsetFor(eq.def);
                    float sideOffset = NonStickNorthSouthSideOffsetFor(eq.def);
                    drawLoc.x += pawn.Rotation == Rot4.South ? -sideOffset : sideOffset;
                    drawLoc.z += NonStickNorthSouthNorthOffsetFor(eq.def);
                }

                if (!IsMugbRangedWeapon(eq.def))
                {
                    return true;
                }
            }

            if (isAiming && IsMugbGunWeapon(eq.def))
            {
                aimAngle += GunAimingAngleOffset;
            }

            if (pawn?.Drafted == true && !isAiming && ShouldUseDraftedIdleAngle(pawn.Rotation))
            {
                aimAngle += DraftedIdleAngleOffset;
                if (IsWarbow(eq.def))
                {
                    aimAngle += WarbowNorthSouthIdleAngleOffset;
                }
            }
            else if (pawn?.Drafted == true && !isAiming && IsMugbGunWeapon(eq.def))
            {
                if (pawn.Rotation == Rot4.West)
                {
                    aimAngle += GunEastWestIdleAngleOffset;
                }
                else if (pawn.Rotation == Rot4.East)
                {
                    aimAngle -= GunEastWestIdleAngleOffset;
                }
            }
            else if (pawn?.Drafted == true && !isAiming && IsSling(eq.def))
            {
                if (pawn.Rotation == Rot4.East)
                {
                    aimAngle -= SlingEastWestIdleMirrorAngleOffset;
                }
                else if (pawn.Rotation == Rot4.West)
                {
                    aimAngle += SlingEastWestIdleMirrorAngleOffset;
                }
            }

            // Yayo already animates the weapon's attack position in the caller. Applying
            // MUGB's normal aiming displacement as well makes ordinary attacks reach too far.
            if (!YayoAnimationActive || !isAiming)
            {
                drawLoc += new Vector3(0f, 0f, ForwardOffsetFor(eq.def, isAiming)).RotatedBy(aimAngle);

                float leftOffset = LeftOffsetFor(eq.def);
                if (leftOffset != 0f)
                {
                    drawLoc += new Vector3(-leftOffset, 0f, 0f).RotatedBy(aimAngle);
                }

                float slingBackOffset = SlingBackOffsetFor(eq.def, isAiming);
                if (slingBackOffset != 0f)
                {
                    drawLoc += new Vector3(0f, 0f, -slingBackOffset).RotatedBy(aimAngle + SlingEquippedAngleOffset);
                }
            }

            ApplySlingIdlePositionCorrection(eq.def, pawn, isAiming, ref drawLoc);
            return true;
        }

        private static bool TryDrawMirroredNonStickMelee(Thing eq, Pawn pawn, bool isAiming, Vector3 drawLoc, float aimAngle)
        {
            if (eq?.def == null
                || pawn?.Drafted != true
                || isAiming
                || !ShouldUseDraftedIdleAngle(pawn.Rotation)
                || IsMugbStickWeapon(eq.def)
                || IsMugbRangedWeapon(eq.def)
                || !IsMugbMeleeWeapon(eq.def))
            {
                return false;
            }

            aimAngle += NonStickNorthSouthAngleOffsetFor(eq.def);
            float sideOffset = NonStickNorthSouthSideOffsetFor(eq.def);
            drawLoc.x += pawn.Rotation == Rot4.South ? -sideOffset : sideOffset;
            drawLoc.z += NonStickNorthSouthNorthOffsetFor(eq.def);

            Graphic graphic = eq.Graphic;
            if (graphic == null)
            {
                return false;
            }

            Material material = graphic.MatSingleFor(eq);
            if (material == null)
            {
                return false;
            }

            Vector2 drawSize = graphic.drawSize;
            Matrix4x4 matrix = default(Matrix4x4);
            matrix.SetTRS(drawLoc, Quaternion.AngleAxis(aimAngle, Vector3.up), new Vector3(drawSize.x, 1f, drawSize.y));
            Graphics.DrawMesh(MeshPool.plane10Flip, matrix, material, 0);
            return true;
        }

        private static bool IsMugbRangedWeapon(ThingDef def)
        {
            for (int i = 0; i < MugbRangedWeaponDefNames.Length; i++)
            {
                if (def.defName == MugbRangedWeaponDefNames[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsModActive(string packageId)
        {
            foreach (ModMetaData mod in ModsConfig.ActiveModsInLoadOrder)
            {
                if (string.Equals(mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMugbStickWeapon(ThingDef def)
        {
            for (int i = 0; i < MugbStickWeaponDefNames.Length; i++)
            {
                if (def.defName == MugbStickWeaponDefNames[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMugbSpearWeapon(ThingDef def)
        {
            for (int i = 0; i < MugbSpearWeaponDefNames.Length; i++)
            {
                if (def.defName == MugbSpearWeaponDefNames[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static float StickIdleAngleCorrectionFor(ThingDef def)
        {
            // Spears already carry a -25 equipped angle in XML. The flail does not,
            // so compensate only in the drafted idle pose to keep its attacks intact.
            return def?.defName == "MUGB_GoblinFlail" ? FlailNorthSouthIdleAngleCorrection : 0f;
        }

        private static bool IsMugbMeleeWeapon(ThingDef def)
        {
            for (int i = 0; i < MugbMeleeWeaponDefNames.Length; i++)
            {
                if (def.defName == MugbMeleeWeaponDefNames[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static float NonStickNorthSouthSideOffsetFor(ThingDef def)
        {
            if (def == null)
            {
                return NonStickMeleeNorthSouthIdleSideOffset;
            }

            if (def.defName == "MUGB_GoblinAxe" || def.defName == "MUGB_GoblinBoneAxe")
            {
                return NonStickMeleeAxeNorthSouthIdleSideOffset;
            }

            if (def.defName == "MUGB_GoblinMachete")
            {
                return NonStickMeleeMacheteNorthSouthIdleSideOffset;
            }

            return NonStickMeleeNorthSouthIdleSideOffset;
        }

        private static float NonStickNorthSouthAngleOffsetFor(ThingDef def)
        {
            if (def?.defName == "MUGB_GoblinCurvedBlade")
            {
                return NonStickMeleeNorthSouthIdleAngleOffset + 5f;
            }

            return NonStickMeleeNorthSouthIdleAngleOffset;
        }

        private static float NonStickNorthSouthNorthOffsetFor(ThingDef def)
        {
            if (def?.defName == "MUGB_GoblinMachete")
            {
                return NonStickMeleeMacheteNorthSouthIdleNorthOffset;
            }

            return NonStickMeleeNorthSouthIdleNorthOffset;
        }

        private static float ForwardOffsetFor(ThingDef def, bool isAiming)
        {
            if (IsSling(def) && !isAiming)
            {
                return SlingIdleForwardOffset;
            }

            return IsMugbGunWeapon(def) ? GunAimingForwardOffset : AimingForwardOffset;
        }

        private static float LeftOffsetFor(ThingDef def)
        {
            return IsMugbGunWeapon(def) ? GunAimingLeftOffset : 0f;
        }

        private static float SlingBackOffsetFor(ThingDef def, bool isAiming)
        {
            return IsSling(def) && isAiming ? SlingGripBackOffset : 0f;
        }

        private static void ApplySlingIdlePositionCorrection(ThingDef def, Pawn pawn, bool isAiming, ref Vector3 drawLoc)
        {
            if (!IsSling(def) || pawn?.Drafted != true || isAiming)
            {
                return;
            }

            if (pawn.Rotation == Rot4.East)
            {
                drawLoc.x -= SlingEastWestIdleInwardOffset;
                drawLoc.z += SlingEastWestIdleNorthOffset;
            }
            else if (pawn.Rotation == Rot4.West)
            {
                drawLoc.x += SlingEastWestIdleInwardOffset;
                drawLoc.z += SlingEastWestIdleNorthOffset;
            }
        }

        private static bool IsSling(ThingDef def)
        {
            return def.defName == "MUGB_GoblinStaffSling";
        }

        private static bool IsWarbow(ThingDef def)
        {
            return def.defName == "MUGB_GoblinWarbow";
        }

        private static bool IsMugbGunWeapon(ThingDef def)
        {
            for (int i = 0; i < MugbGunWeaponDefNames.Length; i++)
            {
                if (def.defName == MugbGunWeaponDefNames[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static Pawn HoldingPawn(Thing thing)
        {
            IThingHolder holder = thing.ParentHolder;
            while (holder != null)
            {
                if (holder is Pawn_EquipmentTracker equipmentTracker)
                {
                    return equipmentTracker.pawn;
                }

                if (holder is Pawn pawn)
                {
                    return pawn;
                }

                holder = holder.ParentHolder;
            }

            return null;
        }

        private static bool IsInAimingStance(Pawn pawn)
        {
            return pawn.stances?.curStance is Stance_Busy;
        }

        private static bool TryDrawSpecialStickAim(Thing eq, Vector3 drawLoc, float aimAngle, float forwardOffset = 0f)
        {
            if (eq?.Graphic == null)
            {
                return false;
            }

            Material material = eq.Graphic.MatSingleFor(eq);
            if (material == null)
            {
                return false;
            }

            // Korean source intent: 창/뼈창/깃창 텍스처는 512 캔버스의 오른쪽 위 꼭지점(45도)이 창끝이다.
            // For throw/charge renders, bypass vanilla equippedAngleOffset and rotate that 45-degree tip directly toward the target.
            if (forwardOffset != 0f)
            {
                drawLoc += new Vector3(0f, 0f, forwardOffset).RotatedBy(aimAngle);
            }

            float renderAngle = aimAngle - 45f;
            Vector2 drawSize = eq.Graphic.drawSize;
            Matrix4x4 matrix = default(Matrix4x4);
            matrix.SetTRS(drawLoc, Quaternion.AngleAxis(renderAngle, Vector3.up), new Vector3(drawSize.x, 1f, drawSize.y));
            Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
            return true;
        }

        private static bool IsRegularSpearMeleeAttack(Thing eq, Pawn pawn)
        {
            if (eq?.def == null
                || !(pawn?.stances?.curStance is Stance_Busy stance)
                || stance.neverAimWeapon
                || !stance.focusTarg.IsValid
                || !IsMugbSpearWeapon(eq.def))
            {
                return false;
            }

            Verb verb = pawn.CurrentEffectiveVerb;
            return verb?.verbProps?.IsMeleeAttack == true
                && (verb.EquipmentSource == eq || pawn.equipment?.Primary == eq);
        }

        private static bool TryGetSpecialWeaponAimAngle(Pawn pawn, ThingDef def, out float aimAngle)
        {
            aimAngle = 0f;
            if (pawn?.CurJob == null || pawn.Map == null || def == null || !IsMugbSpearWeapon(def))
            {
                return false;
            }

            JobDef jobDef = pawn.CurJob.def;
            if (jobDef != MUGBDefOf.MUGB_ThrowSpear && jobDef != MUGBDefOf.MUGB_SpearCharge)
            {
                return false;
            }

            LocalTargetInfo target = pawn.CurJob.GetTarget(TargetIndex.A);
            if (!target.IsValid)
            {
                return false;
            }

            Vector3 targetPos = target.HasThing ? target.Thing.DrawPos : target.Cell.ToVector3Shifted();
            Vector3 direction = targetPos - pawn.DrawPos;
            if (direction.MagnitudeHorizontalSquared() <= 0.001f)
            {
                return false;
            }

            aimAngle = direction.AngleFlat();
            return true;
        }

        private static bool ShouldUseDraftedIdleAngle(Rot4 rotation)
        {
            return rotation == Rot4.South || rotation == Rot4.North;
        }
    }
}
