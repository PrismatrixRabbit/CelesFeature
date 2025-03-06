using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace CelesFeature
{
    public class Celes_BolterGun_Extension : DefModExtension
    {
        public float radius = 1f;
        public DamageDef damageDef=Celes_DamageDefOf.Bomb;
        public int damageAmount = 50;
        public float armorPenetration = -1;
    }
    [HarmonyPatch(typeof(DamageWorker_AddInjury), "Apply")]
    public class Celes_BolterGun
    {

        [HarmonyPostfix]
        public static void BoltGunExplosion(Thing thing, DamageInfo dinfo)
        {
            if (dinfo.Def.GetModExtension<Celes_BolterGun_Extension>() is Celes_BolterGun_Extension ext)
            {
                GenExplosion.DoExplosion(thing.Position,thing.Map,ext.radius,ext.damageDef,dinfo.Instigator,ext.damageAmount,ext.armorPenetration,weapon:dinfo.Weapon);
            }
        }
    }
}