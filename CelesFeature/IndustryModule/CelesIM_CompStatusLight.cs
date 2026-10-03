using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 5 U2 追加：自动生产建筑状态指示灯（2026-10-02，用户裁决=贴图对位方案）
    //  · 模式母本：CompDrawAdditionalGraphics（comp.PostDraw 静态附加图形——RimWorld 同名类），
    //    本 comp 增量=按宿主工作状态三态选色
    //  · 贴图对位：drawSize 与建筑底图一致（(5,5)·640² 同画布 1:1）——灯点在画布上的位置
    //    即其世界位置（位置由贴图决定，offset 仅微调）；未来扩展=每朝向一张对位图
    //    （_north/_east/_west，与建筑贴图体系同构）
    //  · 染色：GraphicDatabase 按 (path,shader,size,color) 缓存=同贴图三实例；
    //    实心图形+默认 Cutout shader（无 MoteGlow R 通道偏好坑）
    //  · 渲染层：相对建筑微浮 +0.05（压建筑本体之上、流光 BuildingOnTop 之下——灯=建筑部件非浮层）
    //  · 三态判定（宿主限 CelesIM_Building_AutoProducer，非该类型不画）：
    //      Formed=绿（待取）＞Forming∧CanWork=蓝（锻造中）＞其余=红
    //      （无单/装料 Gathering/等待操作 Preparing/断电断连与绑定死亡冻结 CanWork=false）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_CompStatusLight : ThingComp
    {
        [Unsaved] private Graphic idleGraphic;      // 三色 Graphic 惰性构造（GraphicDatabase 全局缓存，跨建筑共享）
        [Unsaved] private Graphic workingGraphic;
        [Unsaved] private Graphic completeGraphic;

        private CelesIM_CompProperties_StatusLight Props => (CelesIM_CompProperties_StatusLight)props;

        public override void PostDraw()
        {
            if (!(parent is CelesIM_Building_AutoProducer producer))
            {
                return;
            }
            // activeBill 为 protected（子类内可达）——comp 侧走 public 属性 ActiveBill（:12-27）
            Bill_Autonomous bill = producer.ActiveBill;
            Graphic graphic;
            // Formed 恒绿（待取态与供电无关——提取为人工工作段）；Forming 须 CanWork（冻结=红）；
            // 其余（无单/装料/等待操作/冻结）红
            if (bill != null && bill.State == FormingState.Formed)
            {
                graphic = completeGraphic ?? (completeGraphic = MakeGraphic(Props.colorComplete));
            }
            else if (bill != null && bill.State == FormingState.Forming && producer.CanWork())
            {
                graphic = workingGraphic ?? (workingGraphic = MakeGraphic(Props.colorWorking));
            }
            else
            {
                graphic = idleGraphic ?? (idleGraphic = MakeGraphic(Props.colorIdle));
            }
            Vector3 loc = parent.DrawPos + Props.offset;
            loc.y += 0.05f;
            // ═══ 绘制制（10-02 三轮实测定案）：恒 North 基准+extraRotation 显式补偿 ═══
            // North 基准的 AngleFromRot 恒 0（North.AsAngle=0）且永不触发 East/West 的 flip
            // 分支（GridPlaneFlip 镜像+flip+180°）——绕开实装 Multi 缺向回退的全部魔法
            // （offset 抵消未生效/East 镜像回退——反编译与实装 1.6.4871 行为有差，实测为准），
            // 旋转完全由 extra 单变量控制：South 朝向=0°原样，其余朝向画布刚体旋转跟随。
            // 灯点为点对称图形——刚体旋转无视觉损失。⚠ 未来若配齐四向真变体，此制需切回
            // 材质选择制（Draw(parent.Rotation) 无 extra）——见 MakeGraphic 注释。
            // extra=朝向相对南向基准的转角（符号=10-02 四向实测标定：South 0/North 180/
            // East -90/West +90——此前 +90/-90 反号致 East/West 互换）
            graphic.Draw(loc, Rot4.North, parent, parent.Rotation.AsAngle - Rot4.South.AsAngle);
        }

        // Graphic_Multi+基础名 texPath：引擎按朝向解析变体族（_north/_east/_south/_west 后缀，
        // Glow 族同约定）——当前仅 _south 一张时 mats[0] 回退为 south 图，恒 North 绘制下
        // 等价 Single 直绘。⚠ 四向真变体齐备后：Multi 的 mats[0]=真 north 图，恒 North 制会
        // 双重旋转——届时绘制制切回 Draw(parent.Rotation) 材质选择制（并删 extra 补偿）。
        // ⚠ 勿用 Graphic_Single 直构：Single 不解析变体后缀（texPath 基础名找不到图）。
        private Graphic MakeGraphic(Color color)
        {
            return GraphicDatabase.Get<Graphic_Multi>(Props.texPath, ShaderDatabase.Cutout, Props.drawSize, color);
        }
    }

    public class CelesIM_CompProperties_StatusLight : CompProperties
    {
        public string texPath;                          // 灯点贴图（640² 同画布对位图）

        public Vector2 drawSize = new Vector2(5f, 5f);  // 与建筑底图一致=画布 1:1 对位

        public Vector3 offset = Vector3.zero;           // 微调（默认零——位置由贴图画布位决定）

        public Color colorIdle = new Color(0.9f, 0.2f, 0.2f);      // 红：未工作/未填充/等待操作/冻结

        public Color colorWorking = new Color(0.4f, 0.7f, 1.0f);   // 蓝：锻造中（线程主题色同源）

        public Color colorComplete = new Color(0.3f, 0.9f, 0.4f);  // 绿：完成待取

        public CelesIM_CompProperties_StatusLight()
        {
            compClass = typeof(CelesIM_CompStatusLight);
        }
    }
}
