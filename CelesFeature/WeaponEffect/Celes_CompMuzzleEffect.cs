using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Celes_CompProperties_MuzzleEffect : Celes_CompProperties_WeaponEffect
    {
        public float muzzleDistance = 0.5f;
        public float fadeInTime = 0.05f;
        public float holdTime = 0.1f;
        public float fadeOutTime = 0.15f;
        public float TotalCycleTime => fadeInTime + holdTime + fadeOutTime;
        public Celes_CompProperties_MuzzleEffect()
        {
            compClass = typeof(Celes_CompMuzzleEffect);
        }
    }
    
     public class Celes_CompMuzzleEffect : Celes_CompWeaponEffect
    {
        public new Celes_CompProperties_MuzzleEffect Props => (Celes_CompProperties_MuzzleEffect)props;
        private float muzzleTimer = 0f;
        public void Trigger()
        {
            muzzleTimer = Props.TotalCycleTime;
        }
        public override void DrawEffect(Vector3 drawLoc, float aimAngle, Vector3 weaponDrawSize)
        {
            EnsureMat();
            if (mat == null)
                return;
            if (muzzleTimer <= 0f)
                return;
            float alpha = CalcAlpha();
            if (!Find.TickManager.Paused)
                muzzleTimer -= Time.deltaTime;
            if (muzzleTimer < 0f) muzzleTimer = 0f;
            float angle = aimAngle - 90f;
            Mesh mesh;
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
            Vector3 muzzleDir = Quaternion.AngleAxis(aimAngle, Vector3.up) * Vector3.forward;
            Vector3 muzzlePos = drawLoc + muzzleDir * Props.muzzleDistance + Props.drawOffset;
            muzzlePos.y += Props.layerDepth;
            Matrix4x4 matrix = Matrix4x4.TRS(
                muzzlePos,
                Quaternion.AngleAxis(angle, Vector3.up),
                new Vector3(Props.drawSize.x, 1f, Props.drawSize.y));
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
            Graphics.DrawMesh(mesh, matrix, mat, 0, null, 0, block);
        }
        private float CalcAlpha()
        {
            float remaining = muzzleTimer;
            if (remaining > Props.holdTime + Props.fadeOutTime)
            {
                float elapsed = Props.TotalCycleTime - remaining;
                return Mathf.Clamp01(elapsed / Props.fadeInTime);
            }
            else if (remaining > Props.fadeOutTime)
            {
                return 1f;
            }
            else
            {
                return Mathf.Clamp01(remaining / Props.fadeOutTime);
            }
        }
    }
}