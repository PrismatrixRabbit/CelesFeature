using System.Collections.Generic;
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
    }

    // 坠落物（轨道垃圾箱/晶体坠落——二级 Def）：落地物 + 内容清单
    public class CelesFD_DropCrateDef : Def
    {
        public List<CelesFD_DropContentEntry> contents;   // 内容清单（随机选一项生成）
        public ThingDef fallerDef;                        // 建筑内容时：下降空投舱 def（贴图 = 目标建筑贴图）；省略 → 星铃空投舱（faction: beacon）
        public CelesFD_LootTableDef lootTableDef;         // 建筑内容（容器）时：战利品表（落地生成后填容器——可搜刮出 pawn）
    }

    // 容器战利品表（轨道垃圾箱建筑——Building_Casket 可打开搜刮）
    public class CelesFD_LootTableDef : Def
    {
        public List<CelesFD_DropContentEntry> contents;
        public ThingDef buildingDef;   // 该战利品表所属容器建筑（Skyfaller 落地生成用）
        public bool showContents = true;   // 2026-08-15 用户裁决：容器内容是否显示（默认显示；危险类 false——contentsKnown 控制，Building_Casket.cs:176 实证）
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
