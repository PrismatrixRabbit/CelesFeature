using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Celes_CompProperties_MultiVerb : CompProperties
    {
        public List<Celes_VerbInfo> verbInfos;
        public bool showGizmos = true;

        public Celes_CompProperties_MultiVerb()
        {
            compClass = typeof(Celes_CompMultiVerb);
        }
        // [吸收 AWL 2026-10-11] 配置校验：verbInfos 与 verbs 数量必须一致（静默配置错误 → 启动期红字）
        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            if (verbInfos.NullOrEmpty())
            {
                yield return parentDef + "：Celes_CompProperties_MultiVerb 缺 verbInfos。";
                yield break;
            }
            if (verbInfos.Count != parentDef.Verbs.Count)
            {
                yield return parentDef + "：verbInfos 数量（" + verbInfos.Count + "）必须与 verbs 数量（" + parentDef.Verbs.Count + "）一致。";
            }
            for (int i = 0; i < verbInfos.Count; i++)
            {
                if (verbInfos[i] == null || verbInfos[i].iconPath.NullOrEmpty())
                {
                    yield return parentDef + "：verbInfos[" + i + "] 缺 iconPath（gizmo 图标将缺失）。";
                }
            }
        }
    }
    
    public class Celes_CompMultiVerb : ThingComp
    {
        public int verbIndex = 0;
        public Celes_CompProperties_MultiVerb Props => (Celes_CompProperties_MultiVerb)props;
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref verbIndex, "verbIndex", 0);
        }
        // [事实] 参照 CompMultiVerbByHediff.Notify_Equipped（CompProperties_MultiVerb.md:363-371）
        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            // [吸收 AWL 2026-10-11] NPC 随机模式：非玩家阵营装备时掷一次。
            // 时点必须在 Notify_Equipped（Initialize/PostMake 无 pawn、无法判阵营）；
            // Rand.Range(min,max) = max 排他（Rand.cs:162）；verbIndex 走 Scribe → 读档稳定、换武器重掷。
            // TODO: 将来若建 ModSettings，挪为开关（AWL_Settings.randomWeaponModeForNonPlayerPawn 同款）。
            if (pawn.Faction == null || !pawn.Faction.IsPlayer)
            {
                int count = parent.def.Verbs.Count;
                if (count > 1)
                {
                    verbIndex = Rand.Range(0, count);
                }
            }
            Hediff hediff = HediffMaker.MakeHediff(Celes_HediffDefOf.Celes_MultiVerbSelect, pawn);
            pawn.health.AddHediff(hediff);
        }
        // [事实] 参照 CompMultiVerbByHediff.Notify_Unequipped（CompProperties_MultiVerb.md:373-384）
        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(Celes_HediffDefOf.Celes_MultiVerbSelect);
            if (existing != null)
                pawn.health.RemoveHediff(existing);
        }
        public void SetNextVerbIndex()
        {
            verbIndex++;
            if (verbIndex >= parent.def.Verbs.Count)
                verbIndex = 0;
        }
        // [吸收 AWL 2026-10-11] 按索引取 verb（供切换迁移与守卫）
        public Verb VerbByIndex(int index)
        {
            return parent.GetComp<CompEquippable>().AllVerbs[index];
        }
    }
}