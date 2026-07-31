using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Celes_CompProperties_WeaponGlow : Celes_CompProperties_WeaponEffect
    {
        public bool useMultiMode = false;
        public Color colorA = new Color(0.843f, 0.973f, 1.000f);
        public Color colorB = Color.clear;
        public float pulseCycleSeconds = 2f;
        public float alphaMin = 0.2f;
        public float alphaMax = 0.8f;
        public Celes_CompProperties_WeaponGlow()
        {
            compClass = typeof(Celes_CompWeaponGlow);
        }
    }
    
    public class Celes_CompWeaponGlow : Celes_CompWeaponEffect
    {
        public new Celes_CompProperties_WeaponGlow Props => (Celes_CompProperties_WeaponGlow)props;

        private Celes_CompMultiVerb multiVerb;

        public override void Initialize(CompProperties p)
        {
            base.Initialize(p);

            if (Props.useMultiMode)
            {
                multiVerb = parent.TryGetComp<Celes_CompMultiVerb>();
                if (multiVerb == null)
                {
                    Log.Error($"[Celes] WeaponGlow on {parent.def.defName}: useMultiMode=true but no Celes_CompMultiVerb found. Falling back to colorA only.");
                }
            }
        }

        public override void DrawEffect(Vector3 drawLoc, float aimAngle, Vector3 weaponDrawSize)
        {
            EnsureMat();
            if (mat == null)
                return;
            Color color = GetCurrentColor();
            if (color == Color.clear)
                return;
            float t = Mathf.PingPong(Time.realtimeSinceStartup * (2f / Props.pulseCycleSeconds), 1f);
            float alpha = Mathf.Lerp(Props.alphaMin, Props.alphaMax, t);
            Mesh mesh;
            float angle = aimAngle - 90f;
            // [事实] 原版 DrawEquipmentAiming 第83-98行：用 aimAngle 判定方位
            if (aimAngle > 200f && aimAngle < 340f)
            {
                mesh = MeshPool.plane10Flip;
                angle -= 180f;
                angle -= parent.def.equippedAngleOffset;
            }
            else
            {
                mesh = MeshPool.plane10;
                angle += parent.def.equippedAngleOffset;
            }
            angle %= 360f;
            Vector3 pos = drawLoc + Props.drawOffset;
            pos.y += Props.layerDepth;
            Matrix4x4 matrix = Matrix4x4.TRS(
                pos,
                Quaternion.AngleAxis(angle, Vector3.up),
                new Vector3(Props.drawSize.x, 1f, Props.drawSize.y));
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetColor("_Color", new Color(color.r, color.g, color.b, alpha));
            Graphics.DrawMesh(mesh, matrix, mat, 0, null, 0, block);
        }

        private Color GetCurrentColor()
        {
            if (!Props.useMultiMode || multiVerb == null)
                return Props.colorA;

            int idx = multiVerb.verbIndex;
            if (idx == 0) return Props.colorA;
            if (idx == 1) return Props.colorB;
            return Props.colorA;
        }
    }
}