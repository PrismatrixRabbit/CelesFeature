using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // 坠落物/战利品表内容条目类型（2026-08-15 星铃轨道垃圾箱坠落）
    public enum CelesFD_DropContentType
    {
        Thing,              // 物品（thingDefName）
        PawnKind,           // 常规 pawnKind（pawnKindDefName——Fingerspike/Mech_Militor；派系 = pawnKind.defaultFactionDef 默认）
        RefugeeInjured,     // 逃生舱式（SpaceRefugee + 随机派系 60%/无派系 40% + 重伤倒地——原版 ThingSetMaker_RefugeePod.cs:13-24 实证）
        ManhunterAnimal,    // 狂暴动物（pawnKindDefName——Scaria hediff + ManhunterPermanent 精神状态 + exitMapAfterTick——原版 AggressiveAnimals.cs:47-51 模式）
        Building            // 建筑（落点 r=10 就近生成；容器则按战利品表填内容）
    }

    public class CelesFD_DropContentEntry
    {
        public CelesFD_DropContentType type = CelesFD_DropContentType.Thing;
        public string thingDefName;
        public string pawnKindDefName;
        public IntRange amountRange = new IntRange(1, 1);
        public float weight = 1f;
        // W-2a：品质/材质约束（仅 Thing 条目消费——物品生成时注入）
        public QualityRange? qualityRange;   // XML "min~max"（QualityRange.FromString；单值写 X~X；null = 不设品质）
        public string stuffDefName;          // 材质 defName（可制作材质物；空 = 默认无材质）
    }

    // 坠落物（轨道垃圾箱/晶体坠落——二级 Def）：落地物 + 内容清单
    public class CelesFD_DropCrateDef : Def
    {
        public List<CelesFD_DropContentEntry> contents;   // 内容清单（随机选一项生成）
        public ThingDef fallerDef;                        // 建筑内容时：下降空投舱 def（贴图 = 目标建筑贴图）；省略 → 星铃空投舱（faction: beacon）
        public CelesFD_LootTableDef lootTableDef;         // 建筑内容（容器）时：战利品表（落地生成后填容器——可搜刮出 pawn）
        // FD-G37（2026-09-02 用户裁决）：掉落建筑阵营归属数据驱动——false = 无归属（不调用 SetFaction，
        // Faction 保持 null，与原版野外遗迹同构；不计殖民地财富/殖民建筑列表）；true = 玩家阵营（武备系统掉落建筑用）
        public bool playerOwned = false;
    }

    // 容器战利品表（轨道垃圾箱建筑——Building_Casket 可打开搜刮）
    public class CelesFD_LootTableDef : Def
    {
        public List<CelesFD_DropContentEntry> contents;
        public ThingDef buildingDef;   // 该战利品表所属容器建筑（Skyfaller 落地生成用）
        public bool showContents = true;   // 2026-08-15 用户裁决：容器内容是否显示（默认显示；危险类 false——contentsKnown 控制，Building_Casket.cs:176 实证）
        // W-2a：true = 固定清单全条目生成（早期武备空投等支援投送）；false = 按权重抽一（现行事件语义）
        public bool spawnAll = false;
    }

    // W-2b 终版：落地爆炸扩展（爆炸显式伤害 def 化 + AP 直传 + 气体解耦独立成圈）
    public class CelesFD_SkyfallerImpactExtension : DefModExtension
    {
        public float explosionRadius;                  // 0 = 无爆炸
        public DamageDef explosionDamage;              // 自配 def 族（OrdnanceBomb/Flame——降伤降穿唯一调参点）
        public float explosionDamageFactor = 1f;       // 伤害 = def 默认 × 此系数
        public float armorPenetration = -1f;           // -1 = def 默认（defaultArmorPenetration）
        public ThingDef postExplosionSpawnThingDef;    // 爆后生成物
        public float postExplosionSpawnChance;
        public ThingDef preExplosionSpawnThingDef;     // 爆前生成物（a-3 落地先洒燃料后引燃）
        public float preExplosionSpawnChance;
        public float chanceToStartFire = 0f;           // 地面起火概率（原版燃烧弹 0.22——起火唯一通道实证）
        public GasType? postExplosionGasType;          // 独立烟幕圈
        public float gasRadius = 7.9f;                 // 烟幕圈半径
        public int gasAmount = 100;                    // 每格气体量（逐格圆形投放；255/格会过饱和外溢——圈增长根因）
    }

    // W-2a：容器开态贴图扩展（Building_Grave.fullGraveGraphicData 模式反转——满态换图 → 开态换图；A2-11）
    public class CelesFD_OpenGraphicExtension : DefModExtension
    {
        public GraphicData openGraphicData;   // 被打开后的贴图（openedEver=true 时生效）
    }

    // 事件扩展（IncidentDef modExtensions：坠落物清单——crateDef + 数量范围）
    public class CelesFD_OrbitDropExtension : DefModExtension
    {
        public List<CelesFD_OrbitDropCrateEntry> crates;

        public class CelesFD_OrbitDropCrateEntry
        {
            public CelesFD_DropCrateDef crateDef;
            public IntRange countRange = new IntRange(1, 1);
        }
    }

    // fallerDef 扩展（引用 crateDef——下降 def ↔ 坠落物配置静态绑定；thingClass = CelesFD_Skyfaller_OrbitCrate）
    public class CelesFD_OrbitDropFallerExtension : DefModExtension
    {
        public CelesFD_DropCrateDef crateDef;
    }
}

