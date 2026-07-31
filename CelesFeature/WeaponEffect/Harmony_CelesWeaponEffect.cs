using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    [StaticConstructorOnStartup]
    public static class Harmony_CelesWeaponEffect
    {
        static Harmony_CelesWeaponEffect()
        {
            Harmony harmony = new Harmony("CelesFeature.WeaponEffect");
            harmony.Patch(
                AccessTools.Method(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming)),
                postfix: new HarmonyMethod(typeof(Harmony_CelesWeaponEffect), nameof(Postfix_DrawEquipmentAiming))
            );
            harmony.Patch(
                AccessTools.Method(typeof(Verb_LaunchProjectile), "TryCastShot"),
                postfix: new HarmonyMethod(typeof(Harmony_CelesWeaponEffect), nameof(Postfix_TryCastShot))
            );
        }
        public static void Postfix_DrawEquipmentAiming(Thing eq, Vector3 drawLoc, float aimAngle)
        {
            if (eq == null)
                return;
            var glow = eq.TryGetComp<Celes_CompWeaponGlow>();
            glow?.DrawEffect(drawLoc, aimAngle, eq.Graphic.drawSize);
            var muzzle = eq.TryGetComp<Celes_CompMuzzleEffect>();
            muzzle?.DrawEffect(drawLoc, aimAngle, eq.Graphic.drawSize);
        }
        public static void Postfix_TryCastShot(Verb __instance, bool __result)
        {
            if (!__result)
                return;
            var eq = __instance.EquipmentSource;
            if (eq == null)
                return;
            var muzzle = eq.TryGetComp<Celes_CompMuzzleEffect>();
            muzzle?.Trigger();
        }
    }
}