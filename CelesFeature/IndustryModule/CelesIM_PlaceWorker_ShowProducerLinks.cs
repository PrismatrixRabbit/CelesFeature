using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 7 P1：中枢调整设备族放置预览（真实化 · 2026-10-04 用户裁决）
    //  · 原版 PlaceWorker_ShowFacilitiesConnections 两缺陷（实证）：
    //    ① if/else 单侧绘制（:27-34）——双 comp 建筑（协调器）只画受影响侧，
    //      设施侧（连中枢）预览缺失；
    //    ② 设施侧通道（CompFacility.DrawLines…/DrawPlaceMouseAttachments…）不查
    //      CanLink()——单目标设备把中枢与协调器全画/全列，与实际只连一家不符。
    //  · 本 worker：设施侧按真链接规则预演（linkableBuildings 顺序首家可连者，取前
    //    maxLinkedTargets 个，与 CompFacility.LinkToNearbyBuildings :234-255 同序同判）；
    //    受影响侧照走原版准确通道；另画「在范围内」宿主环（真白色 = 默认环色，中枢/协调器同色）。
    //  · 环规则（定稿）：仅当放置点（TrueCenter 口径，与原版距离判定 :181-184 同源）
    //    中心距 ≤ 该宿主接纳半径（=本设备 facility maxDistance）时，画该宿主的环。
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_PlaceWorker_ShowProducerLinks : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot,
            Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            ThingDef def = checkingDef as ThingDef;
            if (def == null)
            {
                return true;
            }
            CelesIM_CompProperties_AmplifierFacility facilityProps =
                def.GetCompProperties<CelesIM_CompProperties_AmplifierFacility>();
            if (facilityProps != null)
            {
                Vector3 myCenter = GenThing.TrueCenter(loc, rot, def.size, def.Altitude);
                List<Thing> linked = PotentialRealLinks(def, facilityProps, loc, rot, map);
                for (int i = 0; i < linked.Count; i++)
                {
                    GenDraw.DrawLineBetween(myCenter, linked[i].TrueCenter());
                    linked[i].TryGetComp<CompAffectedByFacilities>()
                        ?.DrawRedLineToPotentiallySupplantedFacility(def, loc, rot);
                }
                DrawInRangeRings(def, facilityProps, loc, rot, map);
            }
            if (def.HasComp(typeof(CompAffectedByFacilities)))
            {
                // 受影响侧照走原版通道（该侧本就检查 CanLink/上限，判定准确）
                CompAffectedByFacilities.DrawLinesToPotentialThingsToLinkTo(def, loc, rot, map);
            }
            return true;
        }

        // 真链接预演：与 CompFacility.LinkToNearbyBuildings（:234-255）同序同判——
        // linkableBuildings def 序 × 地图实例序，CanPotentiallyLinkTo 实参实判（距离/上限/顶替/LOS）；
        // 取前 maxLinkedTargets 个（-1=不限）——预览=实际将链接的集合
        private static List<Thing> PotentialRealLinks(ThingDef def, CelesIM_CompProperties_AmplifierFacility facilityProps,
            IntVec3 loc, Rot4 rot, Map map)
        {
            List<Thing> result = new List<Thing>();
            List<ThingDef> targets = facilityProps.linkableBuildings;
            if (targets == null)
            {
                return result;
            }
            int max = facilityProps.maxLinkedTargets;
            for (int i = 0; i < targets.Count; i++)
            {
                foreach (Thing candidate in map.listerThings.ThingsOfDef(targets[i]))
                {
                    CompAffectedByFacilities affected = candidate.TryGetComp<CompAffectedByFacilities>();
                    if (affected == null || !affected.CanPotentiallyLinkTo(def, loc, rot))
                    {
                        continue;
                    }
                    result.Add(candidate);
                    if (max >= 0 && result.Count >= max)
                    {
                        return result;
                    }
                }
            }
            return result;
        }

        // 范围环（P1 定稿）：仅当放置点在宿主接纳半径内时画该宿主环；中枢/协调器同色（默认环色）
        private static void DrawInRangeRings(ThingDef def, CelesIM_CompProperties_AmplifierFacility facilityProps,
            IntVec3 loc, Rot4 rot, Map map)
        {
            List<ThingDef> targets = facilityProps.linkableBuildings;
            if (targets == null)
            {
                return;
            }
            Vector3 myCenter = GenThing.TrueCenter(loc, rot, def.size, def.Altitude);
            float radius = facilityProps.maxDistance;
            for (int i = 0; i < targets.Count; i++)
            {
                foreach (Thing host in map.listerThings.ThingsOfDef(targets[i]))
                {
                    if (Vector3.Distance(myCenter, host.TrueCenter()) <= radius)
                    {
                        GenDraw.DrawRadiusRing(host.Position, radius);
                    }
                }
            }
        }

        // 「链接至」文本（覆写）：只列真将链接者；无候选=原版「无潜在链接」键；协调器另附受影响侧文本
        public override void DrawPlaceMouseAttachments(float curX, ref float curY, BuildableDef bdef, IntVec3 center, Rot4 rot)
        {
            ThingDef def = bdef as ThingDef;
            if (def == null)
            {
                return;
            }
            CelesIM_CompProperties_AmplifierFacility facilityProps =
                def.GetCompProperties<CelesIM_CompProperties_AmplifierFacility>();
            if (facilityProps != null)
            {
                List<Thing> linked = PotentialRealLinks(def, facilityProps, center, rot, Find.CurrentMap);
                if (linked.Count == 0)
                {
                    DrawTextLine(ref curY, "FacilityNoPotentialLinks".Translate(), curX);
                }
                else
                {
                    DrawTextLine(ref curY, "FacilityPotentiallyLinkedTo".Translate() + ":", curX);
                    for (int i = 0; i < linked.Count; i++)
                    {
                        DrawTextLine(ref curY, "  - " + linked[i].LabelCap, curX);
                    }
                }
            }
            if (def.HasComp(typeof(CompAffectedByFacilities)))
            {
                CompAffectedByFacilities.DrawPlaceMouseAttachmentsToPotentialThingsToLinkTo(curX, ref curY, def, center, rot, Find.CurrentMap);
            }
        }

        private static void DrawTextLine(ref float y, string text, float x)
        {
            float lineHeight = Text.LineHeight;
            Widgets.Label(new Rect(x, y, 999f, lineHeight), text);
            y += lineHeight;
        }
    }
}
