using UnityEngine;
using Verse;

namespace CelesFeature
{
    // W-1.5：支援信标光束（完全参考 Bombardment 红色束——CompOrbitalBeam 完整实现，RimWorld/CompOrbitalBeam.cs:1-119）
    // 实现模式（用户定稿 2026-08-31）：
    //   - 贴地横梁（XZ 平面——俯视参考系可见）：长条光束从落点向地图北边缘（angle 固定 0 = 向北）
    //   - 动画全复刻：淡入 10 tick / 呼吸 sin ±2.5% / 淡出线性（StartAnimation(duration, fadeOut)）
    //   - 颜色：SupportDef.beamColor（RGB 动态——攻击红 / 防御浅蓝；非 def 静态色）
    //   - 材质/端盖：Other/OrbitalBeam + MoteGlow + OrbitalBeam 队列 / BeamEndMat 落点端盖
    // 历史修正回退（2026-08-31）：
    //   - 竖立 mesh（Euler 90 + scale 高度）——垂直俯视参考系投影为线（蓝点）——参考系层面失败，回退贴地
    //   - 十字双平面 / 高度公式（map.z - 落点 z）× 1.2 / 双字段错位修复的 dev 日志——全部清理
    [StaticConstructorOnStartup]
    public class CelesFD_CompSupportBeam : ThingComp
    {
        private int startTick;
        private int totalDuration;
        private int fadeOutDuration;

        private static readonly Material BeamMat =
            MaterialPool.MatFrom("Other/OrbitalBeam", ShaderDatabase.MoteGlow, MapMaterialRenderQueues.OrbitalBeam);
        private static readonly Material BeamEndMat =
            MaterialPool.MatFrom("Other/OrbitalBeamEnd", ShaderDatabase.MoteGlow, MapMaterialRenderQueues.OrbitalBeam);
        private static readonly MaterialPropertyBlock MatPropertyBlock = new MaterialPropertyBlock();

        private int TicksPassed => Find.TickManager.TicksGame - startTick;
        private int TicksLeft => totalDuration - TicksPassed;

        public void StartAnimation(int totalDuration, int fadeOutDuration)
        {
            startTick = Find.TickManager.TicksGame;
            this.totalDuration = totalDuration;
            this.fadeOutDuration = fadeOutDuration;
        }

        public override void PostDraw()
        {
            base.PostDraw();
            // 颜色来源：宿主信标的 SupportDef（单一数据源——PostDraw 从 parent 读取，此前双字段错位已统一）
            // W-2a：totalDuration<=0 = 无限时长（信标存续期常亮，信标销毁即移除）；仅有限时长才按时终止
            CelesFD_SupportDef supportDef = (parent as CelesFD_SupportBeacon)?.SupportDef;
            if ((totalDuration > 0 && TicksLeft <= 0) || supportDef == null || parent.Map == null)
                return;
            Vector3 drawPos = parent.DrawPos;
            float width = supportDef.beamWidth;

            // 贴地横梁（复刻 CompOrbitalBeam.cs:83-105——angle 固定 0 = 向北 -Z）
            // 横梁长：地图 z 剩余距离 × √2（原版公式——从落点延伸至地图北边缘）
            float num = ((float)parent.Map.Size.z - drawPos.z) * 1.4142135f;
            Vector3 vector = Vector3Utility.FromAngleFlat(-90f);   // angle(0) - 90 = -90 = 北（-Z）
            Vector3 vector2 = drawPos + vector * num * 0.5f;
            vector2.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            float fadeIn = Mathf.Min((float)TicksPassed / 10f, 1f);   // 淡入 10 tick
            Vector3 vector3 = vector * ((1f - fadeIn) * num);          // 淡入时从北边缘"打来"
            float alpha = 0.975f + Mathf.Sin((float)TicksPassed * 0.3f) * 0.025f;   // 呼吸
            if (totalDuration > 0 && TicksLeft < fadeOutDuration)
                alpha *= (float)TicksLeft / fadeOutDuration;                          // 淡出线性（仅有限时长）
            Color color = supportDef.beamColor;
            color.a *= alpha;
            MatPropertyBlock.SetColor(ShaderPropertyIDs.Color, color);
            Matrix4x4 matrix = default(Matrix4x4);
            matrix.SetTRS(vector2 + vector * (width * 0.5f * 0.5f) + vector3, Quaternion.Euler(0f, 0f, 0f),
                new Vector3(width, 1f, num));
            Graphics.DrawMesh(MeshPool.plane10, matrix, BeamMat, 0, null, 0, MatPropertyBlock);

            // 落点端盖（复刻 CompOrbitalBeam.cs:101-105）
            Vector3 endPos = drawPos + vector3;
            endPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            Matrix4x4 matrix2 = default(Matrix4x4);
            matrix2.SetTRS(endPos, Quaternion.identity, new Vector3(width, 1f, width * 0.5f));
            Graphics.DrawMesh(MeshPool.plane10, matrix2, BeamEndMat, 0, null, 0, MatPropertyBlock);
        }
    }
}
