using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 5 二类生产：自动生产建筑（U1 单元 · 2026-09-29，同日修订=音频五件套+CanWork 收编）
    //  · 基类 Building_WorkTableAutonomous 全链直接复用（装料/提取/周期/容器/DEV gizmos）
    //  · CanWork（F5-2 扩展点）：断电 ∨ 依赖型断连（CannotWorkUnconnected 单点消费——
    //    值义 1=依赖已封装于 Consumer 属性，勿在本类再散写比较式）→ false
    //    → 基类 Tick 不调 BillTick = 冻结不重置；绝不加第二次 BillTick（V-1 双计疑云规避）
    //  · 音频=培育器模式五件套（母本 Building_MechGestator:105-188）——Biotech 音效
    //    def 全 null 守卫（About 未声明 Biotech 依赖，F5-22：无 DLC=静默降级）
    //  · U1 不落（按单元生长）：GetGizmos/GetFloatMenuOptions（U4）；ExposeData（零新增）；
    //    Notify_FormingCompleted 的 byproduct 钩子（U5 在现有覆写的 base 调用后追加）
    // ════════════════════════════════════════════════════════════════
    [StaticConstructorOnStartup]   // U2 增补：进度条材质静态构造时机（母本 Building_MechGestator:9 同款）
    public class CelesIM_Building_AutoProducer : Building_WorkTableAutonomous
    {
        // U2 增补（D31）：锻造进度条材质（母本 :24-26 形态；填充色=线程主题蓝——用户裁决 10-02，
        // 底图全透明同母本=仅填充段可见）
        private static Material ForgeBarFilledMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.4f, 0.7f, 1.0f));

        private static Material ForgeBarUnfilledMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0f, 0f, 0f, 0f));
        [Unsaved] private CompPowerTrader powerComp;      // 基类 powerComp 为 private（Building_WorkTable.cs:10）——自缓存
        [Unsaved] private CelesIM_CompThreadConsumer threadComp;
        [Unsaved] private Sustainer workingSound;         // Forming 周期环境音（母本 :14 同位）
        [Unsaved] private Mote workingMote;               // 锻造流光（裂解扫描仪附着式 mote，Mote_Visual 基类同款）

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
            threadComp = GetComp<CelesIM_CompThreadConsumer>();
        }

        public override bool CanWork()
        {
            if (powerComp != null && !powerComp.PowerOn)
            {
                return false;   // 断电（本机）——与 Building_WorkTable.CurrentlyUsableForBills :92 同语义
            }
            if (threadComp != null && threadComp.CannotWorkUnconnected)
            {
                return false;   // 依赖型断连——与 bill 双闸 patch 同一单点（Consumer 属性，值义 1=依赖）
            }
            // U2 增补（D31）：绑定者死亡=冻结（原版 BoundPawnStateAllowsForming :56-66 意图同款；
            // 落 CanWork 单闸=与断电/断连同构的统一冻结语言[计时停+流光停+电力回落+音停]——
            // 偏离原版细节[电仍满载/音仍响]已报备。解冻出口=删单[Reset 清绑定+原料弹出]）
            if (activeBill is CelesIM_Bill_AutoProducer bill && bill.BindingBroken)
            {
                return false;
            }
            return true;
        }

        // 容器内 Pawn 先销毁再弹出（母本 Building_MechGestator.cs:124-135——「消失非击杀」；
        // b 模型半成品/产物 Pawn 的拆毁与 bill 删除语义。U1 无 Pawn 产物，前置就位）
        public override void EjectContents()
        {
            for (int i = innerContainer.Count - 1; i >= 0; i--)
            {
                if (innerContainer[i] is Pawn pawn)
                {
                    innerContainer.RemoveAt(i);
                    pawn.Destroy();
                }
            }
            base.EjectContents();
        }

        // 容器内活体 Pawn（母本 GestatingMech :68-79 同款，去复活 Corpse 支路）——
        // b 模型 Formed 后产物所在；提取路由 patch（Patch_GenRecipe_PawnRoute）与 DrawAt 序0 双消费
        public Pawn GestatingPawn => innerContainer.FirstOrDefault((Thing t) => t is Pawn) as Pawn;

        // ═══ U2 修订（D32）：DEV 工具补齐——母本增量件「Complete all cycles」═══
        // 基类 GetGizmos（Building_WorkTableAutonomous:140-163）自带 Forming 态两件（+25%/Complete
        // cycle 单周期）——多周期配方 Preparing 态无 DEV、无一步完成；母本 :272-278 增量件=全部
        // 周期一步完成（ForceCompleteAllCycles：置满 cyclesDone+formingTicks=0→下一 tick 即 Formed）
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (DebugSettings.ShowDevGizmos && activeBill is CelesIM_Bill_AutoProducer bill
                && bill.State != FormingState.Gathering && bill.State != FormingState.Formed)
            {
                yield return new Command_Action
                {
                    action = bill.ForceCompleteAllCycles,
                    defaultLabel = "DEV: Complete all cycles"
                };
            }
        }

        // ═══ 培育器音频模式五件套（母本 Building_MechGestator:105-188；Biotech def null 守卫=无 DLC 静默降级）═══

        // Forming 开始一次性音（母本 :105-107）
        public override void Notify_StartForming(Pawn billDoer)
        {
            if (SoundDefOf.MechGestatorCycle_Started != null)
                SoundDefOf.MechGestatorCycle_Started.PlayOneShot(this);
        }

        // Forming 完成一次性音（母本 :109-117）——base 必须保留（清容器+产物入容器）；U5 在 base 后追加 byproduct
        public override void Notify_FormingCompleted()
        {
            base.Notify_FormingCompleted();
            if (SoundDefOf.MechGestatorBill_Completed != null)
                SoundDefOf.MechGestatorBill_Completed.PlayOneShot(this);
        }

        // 原料入容器一次性音（母本 :119-122）
        public override void Notify_HauledTo(Pawn hauler, Thing thing, int count)
        {
            if (SoundDefOf.MechGestator_MaterialInserted != null)
                SoundDefOf.MechGestator_MaterialInserted.PlayOneShot(this);
        }

        protected override void Tick()
        {
            base.Tick();   // 基类内已门控 BillTick——绝不二次调用（V-1）
            // ── Q2 待机电力分档（母本 :168-176，值参考母本：满载=basePowerConsumption/idle=50）──
            // 条件含 CanWork()=冻结回落待机（母本无断连概念，此处为 D7 对齐的有意偏离，已报备）
            if (this.IsHashIntervalTick(250) && powerComp != null)
            {
                powerComp.PowerOutput = (activeBill != null && activeBill.State == FormingState.Forming && CanWork())
                    ? -powerComp.Props.PowerConsumption    // 锻造中=满载
                    : -powerComp.Props.idlePowerDraw;      // 待机（装料/间期/待取/冻结）
            }
            // ── 锻造周期环境音：sustainer 逐 tick 维持（母本 :177-188）。
            //    门控偏离报备：母本 PoweredOn → 我方 CanWork()（统一覆盖断电+断连=冻结静音，D7 语义对齐）
            if (activeBill != null && activeBill.State != FormingState.Gathering && CanWork())
            {
                if (workingSound == null || workingSound.Ended)
                {
                    if (SoundDefOf.MechGestator_Ambience != null)
                        workingSound = SoundDefOf.MechGestator_Ambience.TrySpawnSustainer(this);
                }
                workingSound?.Maintain();
                // 锻造流光（Building_SubcoreScanner.cs:389-393 同款：附着式 mote+逐 tick Maintain；
                // needsMaintenance 生命周期——门控转 false 即断维持，fadeOutUnmaintained 自动淡出=冻结淡出）
                if (workingMote == null || workingMote.Destroyed)
                {
                    if (CelesIM_DefOf.Celes_Mote_ForgeGlow_South != null)
                        workingMote = MoteMaker.MakeAttachedOverlay(this, CelesIM_DefOf.Celes_Mote_ForgeGlow_South, Vector3.zero);
                }
                workingMote?.Maintain();
            }
            else if (workingSound != null)
            {
                workingSound.End();
                workingSound = null;
                // 流光不主动 End——断维持自动淡出（mote def fadeOutUnmaintained）
            }
        }

        // ═══ 锻造内容物绘制（视觉修订批 2026-09-30 + U2 双态 2026-10-02）：配方侧 ext 定义 ═══
        // 图层（高→低）：内容物(MoteOverhead) > 流光(BuildingOnTop) > 底层建筑(Building)——遮挡靠 y 排序；
        // 浮起感走 z（轴语义实证：y 仅排序不产生屏幕位移，z 才是视觉高度）
        // 状态门=State≠Gathering（不判 CanWork——冻结时半成品仍留舱，培育器 DrawAt 同款）
        // 优先级四态：序0 容器含 Pawn（b 模型 Formed）→ 本体图（母本 TryGetMechFormingGraphic :238-255
        //   同构：CurKindLifeStage.bodyGraphicData.Graphic+尺寸守卫超限回落；恒南向 :210）；
        //   其余按 ext 三态（useProductGraphic → 产物 def 图 / graphic → 专用图 / 均无 → 不显示）——
        //   a 模型容器永无 Pawn，序0 不命中=行为零变化
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)   // 基类为 protected virtual（Thing.cs:1324）
        {
            base.DrawAt(drawLoc, flip);
            if (activeBill == null || activeBill.State == FormingState.Gathering)
            {
                return;
            }
            CelesIM_FormingGraphicExt ext = ResolveContentExt();
            Graphic formedGraphic = ResolveFormedGraphic(ext);          // 序0：容器内 Pawn 本体图（Multi 真变体族）
            Graphic content = formedGraphic ?? ResolveContentGraphic(ext);   // 其余：ext 三态图
            if (content == null)
            {
                return;
            }
            Vector3 loc = drawLoc + (ext?.drawOffset ?? Vector3.zero);
            loc.y = AltitudeLayer.MoteOverhead.AltitudeFor();   // 压流光（BuildingOnTop）之上
            if (ext != null && ext.bobDistance > 0f)
            {
                loc.z += Mathf.PingPong(Find.TickManager.TicksGame * ext.bobSpeed, ext.bobDistance);   // 母本 PingPong 同款（z 浮动）
            }
            // ═══ 绘制制（10-02 用户裁定 Content 同病后统一）：ext 图=恒 North 基准+extraRotation 显式 ═══
            // （原恒 South+Single=恒转 180°且不随建筑——与状态灯同病同源，修法同参 CompStatusLight 注释：
            // North 基准绕开 flip/offset/ShouldDrawRotated 全部实装魔法，extra 单变量=画布刚体旋转跟随建筑）；
            // 本体图（序0，Graphic_Multi 真变体族）走材质选择制=Draw(实际朝向)。
            float extraRot = base.Rotation.AsAngle - Rot4.South.AsAngle;   // 符号=10-02 四向实测标定（同 CompStatusLight）
            if (formedGraphic != null)
            {
                formedGraphic.Draw(loc, base.Rotation, this);
            }
            else
            {
                content.Draw(loc, Rot4.North, this, extraRot);
            }
            // ── U2 增补（D31）：锻造进度条（母本 :217-223 同款五件套）──
            // 进度语义=当前周期（原版 CurrentBillFormingPercent 同源：State!=Forming→0——多周期每周期
            // 0→100 回卷、Preparing 归零条消失）；FillableBarRequest 为 struct 赋值拷贝=免共享污染；
            // 底图全透明=仅填充段可见（fillPercent≤0.001 时填充段也不画，GenDraw :629）
            GenDraw.FillableBarRequest barDrawData = base.BarDrawData;   // 基类现成属性（:41→def.building.BarDrawDataFor）
            barDrawData.center = drawLoc;
            barDrawData.fillPercent = CurrentBillFormingPercent;
            barDrawData.filledMat = ForgeBarFilledMat;
            barDrawData.unfilledMat = ForgeBarUnfilledMat;
            barDrawData.rotation = base.Rotation;
            GenDraw.DrawFillableBar(barDrawData);
        }

        // 序0：容器含 Pawn → 自动本体图（方案 A 裁决 10-02）。守卫限值取 ext.maxFormedDrawSize
        // （默认 1.5 对齐原版 BuildingProperties.cs:366）——超大 race 回落 ext 专用图防溢出。
        // 非人形机械体 CurKindLifeStage 直取 lifeStages[当前段]（Pawn_AgeTracker.cs:193；
        // humanlike 报错分支不触及——.? 守卫兜异常形态）
        private Graphic ResolveFormedGraphic(CelesIM_FormingGraphicExt ext)
        {
            Pawn gestatingPawn = GestatingPawn;
            if (gestatingPawn == null)
            {
                return null;
            }
            Graphic bodyGraphic = gestatingPawn.ageTracker?.CurKindLifeStage?.bodyGraphicData?.Graphic;
            if (bodyGraphic == null)
            {
                return null;
            }
            Vector2 max = ext?.maxFormedDrawSize ?? new Vector2(1.5f, 1.5f);
            if (bodyGraphic.drawSize.x <= max.x && bodyGraphic.drawSize.y <= max.y)
            {
                return bodyGraphic;
            }
            return null;
        }

        // last-wins：li 跨继承追加（父类默认在前，子配方覆盖在后）——取末位=最specific定义；
        // Def 无 GetModExtensions<T> 复数 API（仅单数 :192），走 modExtensions 字段（Def.cs:36）
        private CelesIM_FormingGraphicExt ResolveContentExt()
        {
            return activeBill.recipe.modExtensions?.OfType<CelesIM_FormingGraphicExt>().LastOrDefault();
        }

        private Graphic ResolveContentGraphic(CelesIM_FormingGraphicExt ext)
        {
            if (ext == null)
            {
                return null;
            }
            if (ext.useProductGraphic)
            {
                return activeBill.recipe.ProducedThingDef?.graphicData?.Graphic;   // 产物本体图（GraphicData 内部惰性缓存，免手工缓存）
            }
            return ext.graphic?.Graphic;
        }
    }
}
