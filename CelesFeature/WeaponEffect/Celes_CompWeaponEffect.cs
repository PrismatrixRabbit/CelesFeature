using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Celes_CompProperties_WeaponEffect : CompProperties
    {
        public string texPath;
        public Vector3 drawOffset = Vector3.zero;
        public float layerDepth = 0f;
        public Vector2 drawSize = Vector2.one;
        public Celes_CompProperties_WeaponEffect()
        {
            compClass = typeof(Celes_CompWeaponEffect);
        }
    }
    
    public abstract class Celes_CompWeaponEffect : ThingComp
    {
        public Celes_CompProperties_WeaponEffect Props => (Celes_CompProperties_WeaponEffect)props;
        protected Material mat;
        public override void Initialize(CompProperties p)
        {
            base.Initialize(p);
        }
        protected void EnsureMat()
        {
            if (mat == null && !Props.texPath.NullOrEmpty())
            {
                mat = MaterialPool.MatFrom(Props.texPath, ShaderDatabase.TransparentPostLight);
                Log.Message($"[Celes] WeaponEffect loaded: {Props.texPath} on {parent.def.defName}");
            }
        }

        // [事实] drawLoc=武器世界坐标, aimAngle=武器旋转角度, weaponDrawSize=武器贴图尺寸
        public virtual void DrawEffect(Vector3 drawLoc, float aimAngle, Vector3 weaponDrawSize)
        {
            EnsureMat();
            if (mat == null)
                return;
        }
    }
}