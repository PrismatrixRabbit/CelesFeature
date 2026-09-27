using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 3 B3：常显连线特效渲染器（中枢↔中继 + 中继链 Relay→父级；终端不画）
    //  · 实体层自画：每 Relay 在 PostDraw 画「自己→父级」恰一条（原版连线先例均实体层：
    //    Building_MechCharger.cs:353 / CompOrbitalBeam.cs:78 / CompTargetingBeam.cs:15）
    //  · Q3 B 终案（裁决 2026-09-26，轴语义修正）：伪 3D 视觉高度 = 贴图 V 向 = 世界 z——
    //    双端连点各向 z+ 偏移（Producer/Relay.linkHeight，中枢~5/中继~1.2），落差由 z 差表达；
    //    y 两端统一 MetaOverlays（水平面片+最高排序层）。世界 y 非视觉高度（IntVec3/Altitudes/Graphic 渲染链实证）
    //  · Q1 修复：线专用 mesh = NewPlaneMesh(1f) + 1000 大 bounds（MeshMakerShadows.cs:77 手法）
    //    ——防长线拉伸后被视锥剔除误杀（DrawLineBetween 固定 plane10 不可改，故自写等价八行）
    //  · 绘制核心 = GenDraw.cs:179-195 结构照抄（:181 近垂直保护 / :183 中点 / :189 宽长 TRS）
    //  · 材质：MoteGlow additive 发光（CompOrbitalBeam.cs:26 先例）；def 级缓存惰性获取（渲染线程，坑#15）
    //  · 静态线无脉动；选中交互线（PostDrawExtraSelectionOverlays）不在此层
    // ════════════════════════════════════════════════════════════════
    public static class CelesIM_LinkEffectRenderer
    {
        private const string LinkTexPath = "Celes/UI/Overlays/ThreadLinkLine";

        // Q1：线专用 mesh（与 MeshPool.plane10 同构 + 大 bounds 防拉伸剔除；惰性构建于首次 Draw）
        private static Mesh lineMesh;
        private static Mesh LineMesh
        {
            get
            {
                if (lineMesh == null)
                {
                    Mesh m = MeshMakerPlanes.NewPlaneMesh(1f);
                    m.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
                    lineMesh = m;
                }
                return lineMesh;
            }
        }

        // def 级材质（颜色 XML 可调 → 缓存挂 CompProperties；MaterialPool 以 path+shader+color 为键，
        // 多色并存先例 = GenDraw.cs:46-58 LineMat 族）
        public static Material LinkMatFor(CelesIM_CompProperties_ThreadRelay props, bool active)
        {
            if (active)
            {
                if (props.LinkMatActive == null)
                    props.LinkMatActive = MaterialPool.MatFrom(LinkTexPath, ShaderDatabase.MoteGlow, props.lineColorActive);
                return props.LinkMatActive;
            }
            if (props.LinkMatBroken == null)
                props.LinkMatBroken = MaterialPool.MatFrom(LinkTexPath, ShaderDatabase.MoteGlow, props.lineColorBroken);
            return props.LinkMatBroken;
        }

        // Q3 B：节点连点 z+ 偏移（伪 3D 视觉高度——中枢~5 / 中继~1.2，按贴图顶端实测定稿）
        public static float LinkHeightOf(Thing node)
        {
            CelesIM_CompThreadProducer hub = node.TryGetComp<CelesIM_CompThreadProducer>();
            if (hub != null)
                return hub.Props.linkHeight;
            CelesIM_CompThreadRelay relay = node.TryGetComp<CelesIM_CompThreadRelay>();
            if (relay != null)
                return relay.Props.linkHeight;
            return 0f;
        }

        // Relay.PostDraw 每帧调用（断电守卫外——断电中继画红线）：
        // 无父级（未绑定）不画；色判定 = IsActiveRelay（含本机电力）
        public static void DrawLink(CelesIM_CompThreadRelay relay, CelesIM_ThreadNetworkManager mgr)
        {
            if (mgr == null || relay.parent == null || !relay.parent.Spawned)
                return;
            Thing parentNode = mgr.GetRelayParent(relay.parent);
            if (parentNode == null)
                return;
            // 伪 3D 轴语义：建筑视觉高度 = 贴图 V 向 = 世界 z；世界 y 仅为渲染排序层
            Vector3 a = relay.parent.TrueCenter();
            a.z += relay.Props.linkHeight;                // 本端连点偏移（中继贴图顶端）
            Vector3 b = parentNode.TrueCenter();
            b.z += LinkHeightOf(parentNode);              // 父端连点偏移（中枢贴图塔顶）
            a.y = (b.y = AltitudeLayer.MetaOverlays.AltitudeFor());   // 两端同 y：水平面片 + 最高排序层防遮挡
            Vector3 delta = a - b;
            if (Mathf.Abs(delta.x) < 0.01f && Mathf.Abs(delta.z) < 0.01f)
                return;                                    // GenDraw :181 近垂直保护（同款）
            Vector3 pos = (a + b) / 2f;                    // GenDraw :183 线中点
            Matrix4x4 matrix = default(Matrix4x4);
            matrix.SetTRS(pos, Quaternion.LookRotation(delta),
                new Vector3(relay.Props.lineWidth, 1f, delta.magnitude));
            Graphics.DrawMesh(LineMesh, matrix, LinkMatFor(relay.Props, mgr.IsActiveRelay(relay)), 0);
        }
    }
}
