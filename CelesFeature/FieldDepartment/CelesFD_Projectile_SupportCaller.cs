using RimWorld;
using Verse;

namespace CelesFeature
{
    // W-1.5：投掷式呼叫信标弹（Projectile_SpawnsThing 模式——原版先例 Grenade_TurretPack/DisruptorFlare，Anomaly Apparel_Packs.xml:320-334）
    // 消耗语义（用户裁决）：**落地生效前检测 + 扣除**——允许双投（每个投掷物落地独立检测+扣除）；
    //   检测失败 → 仅销毁（无信标/无光柱/无触发）；被拦截（飞行中）→ 未生效不扣
    public class CelesFD_Projectile_SupportCaller : Projectile_SpawnsThing
    {
        public CelesFD_SupportDef SupportDefSnapshot;   // 投掷时刻快照（防双投串号——2026-08-31：CurrentSupportDef 是 comp 当前选中，
                                                        //   飞行期间被后续投掷覆盖会导致投掷物与支援错配；快照优先，launcher 兜底）
        public IntVec3 OriginCellSnapshot;              // 投掷者投出时刻位置快照（W-2a：方向源——W-2b 渐进弹幕/一字烟幕连线基准）

        protected override void Impact(Thing hitThing, bool blockedByShield = false)   // 基类 Impact 为 protected（Projectile.cs:559）——访问级别须一致
        {
            CelesFD_SupportDef supportDef = SupportDefSnapshot ?? GetSupportDefFromLauncher();
            CelesFD_GameComponent gc = CelesFD_GameComponent.Instance;
            // 被盾牌拦截：未生效不扣（与"拦截不扣"语义一致）
            if (blockedByShield)
            {
                Destroy();
                return;
            }
            // W-2b（用户裁决保留）：信标厚顶失效——落点厚岩顶 → 不扣次、不生成信标 + 弹窗
            //（防"呼叫支援塌山"取巧；空投本体按原版砸穿规则不受此限）
            RoofDef roof = Position.GetRoof(Map);
            if (roof != null && roof.isThickRoof)
            {
                Messages.Message("CelesFD_Keyed_SupportFailedBeaconRoof".Translate(),
                    MessageTypeDefOf.NeutralEvent, historical: false);
                Destroy();
                return;
            }
            if (supportDef == null || gc == null || !gc.TryConsumeSupport(supportDef))
            {
                // 失败：仅销毁（不生成信标/光柱/不触发）——不调 base.Impact（其生成 spawnsThingDef）
                Destroy();
                return;
            }
            // 成功：生成信标（复刻 Projectile_SpawnsThing 逻辑——需拿生成物引用设 SupportDef 供光束颜色读取）
            // 投掷物自身 def（this.def = ThingDef）的 projectile.spawnsThingDef（变量名 supportDef 避免遮蔽 this.def）
            if (def.projectile.spawnsThingDef != null)
            {
                CelesFD_SupportBeacon beacon = (CelesFD_SupportBeacon)ThingMaker.MakeThing(def.projectile.spawnsThingDef);
                GenSpawn.Spawn(beacon, Position, Map);
                beacon.SupportDef = supportDef;
                // W-2a：方向源与敌对基准写入信标（投掷时刻快照优先；无效则落点兜底）
                beacon.originCell = OriginCellSnapshot.IsValid ? OriginCellSnapshot : Position;
                beacon.caster = Launcher as Pawn;
            }
            Destroy();
            // 触发占位（W-1.5）：letter——支援位置 = 投掷实际落点（含技能散布）；W-2 在此分发真实坠落
            Find.LetterStack.ReceiveLetter(
                "CelesFD_Keyed_SupportCalledTitle".Translate(),
                "CelesFD_Keyed_SupportCalledDesc".Translate(supportDef.LabelCap, Position.ToString()),
                LetterDefOf.NeutralEvent);
            Log.Message("[CelesFD] Support called: " + supportDef.defName + " landed at " + Position
                + " (remaining: " + gc.AvailableCount(supportDef) + ")");
        }

        // 从投掷者（launcher pawn）的呼叫信标 comp 读当前选中支援（W-1 GetCallerComp 模式——apparel 非 EquipmentSource 链）
        private CelesFD_SupportDef GetSupportDefFromLauncher()
        {
            Pawn p = Launcher as Pawn;
            if (p?.apparel == null) return null;
            foreach (Apparel a in p.apparel.WornApparel)
            {
                CelesFD_CompSupportCaller c = a.TryGetComp<CelesFD_CompSupportCaller>();
                if (c != null) return c.CurrentSupportDef;
            }
            return null;
        }
    }
}
