using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 7：中枢设备聚合遍历（单源 helper · D45/D46）
    //  · 枚举 = 直挂活跃设备 + 经活跃协调器托管的一层嵌套设备
    //    （协调器不可连协调器=白名单天然保证嵌套仅一层，无需防环）
    //  · 消费方：Producer.ProducerBuffOffset（线程加成）/ StatPart_ProducerPowerOffset
    //    （电力加减）/ StatPart_ProducerPowerFactor（乘区）——显示与实算同源（显示同步红线）
    //  · 活跃 = CompFacility.CanBeActive 单门（开关∧断电∧消费者态——ProducerDevice 族）
    //  · 协调器判据：ext.producerPowerFactor > 1（数值即标记）
    // ════════════════════════════════════════════════════════════════
    public static class CelesIM_ProducerDeviceUtility
    {
        public static IEnumerable<Thing> ActiveDevicesOf(Thing producer)
        {
            if (producer == null)
            {
                yield break;
            }
            CompAffectedByFacilities affected = producer.TryGetComp<CompAffectedByFacilities>();
            if (affected == null)
            {
                yield break;
            }
            List<Thing> direct = affected.LinkedFacilitiesListForReading;
            for (int i = 0; i < direct.Count; i++)
            {
                Thing fac = direct[i];
                CompFacility facComp = fac.TryGetComp<CompFacility>();
                if (facComp == null || !facComp.CanBeActive)
                {
                    continue;
                }
                yield return fac;
                CelesIM_AmplifierExt ext = ExtOf(fac);
                if (ext == null || ext.producerPowerFactor <= 1f)
                {
                    continue;   // 非协调器（普通设备——无托管层）
                }
                // 协调器：托管设备加成回流（一层）
                CompAffectedByFacilities nested = fac.TryGetComp<CompAffectedByFacilities>();
                if (nested == null)
                {
                    continue;
                }
                List<Thing> hosted = nested.LinkedFacilitiesListForReading;
                for (int j = 0; j < hosted.Count; j++)
                {
                    CompFacility hostedComp = hosted[j].TryGetComp<CompFacility>();
                    if (hostedComp != null && hostedComp.CanBeActive)
                    {
                        yield return hosted[j];
                    }
                }
            }
        }

        public static CelesIM_AmplifierExt ExtOf(Thing thing)
        {
            return thing?.def?.GetModExtension<CelesIM_AmplifierExt>();
        }

        // 批 7 P6：中枢调整设备接纳半径（显示用单源）——枢纽设备 def（带 AmpExt 的
        // producerPowerOffset/Factor/boost）的 facility maxDistance；静态缓存一次，
        // XML 配置漂移自动跟随；无命中返回 0（调用方不画环）
        private static float cachedDeviceAcceptRadius = -1f;

        public static float DeviceAcceptRadius
        {
            get
            {
                if (cachedDeviceAcceptRadius < 0f)
                {
                    float radius = 0f;
                    foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                    {
                        CelesIM_AmplifierExt ext = def.GetModExtension<CelesIM_AmplifierExt>();
                        if (ext == null
                            || (ext.producerPowerOffset == 0f && ext.producerPowerFactor <= 1f && ext.boostPerUnit == 0))
                        {
                            continue;
                        }
                        CelesIM_CompProperties_AmplifierFacility props =
                            def.GetCompProperties<CelesIM_CompProperties_AmplifierFacility>();
                        if (props != null && props.maxDistance > radius)
                        {
                            radius = props.maxDistance;
                        }
                    }
                    cachedDeviceAcceptRadius = radius;
                }
                return cachedDeviceAcceptRadius;
            }
        }

        // 电力加减合计（D46 先加：全部活跃设备，含托管）
        public static float TotalPowerOffset(Thing producer)
        {
            float sum = 0f;
            foreach (Thing device in ActiveDevicesOf(producer))
            {
                CelesIM_AmplifierExt ext = ExtOf(device);
                if (ext != null)
                {
                    sum += ext.producerPowerOffset;
                }
            }
            return sum;
        }

        // 电力乘区连乘（协调器 producerPowerFactor；无协调器=1）
        public static float TotalPowerFactor(Thing producer)
        {
            float product = 1f;
            foreach (Thing device in ActiveDevicesOf(producer))
            {
                CelesIM_AmplifierExt ext = ExtOf(device);
                if (ext != null && ext.producerPowerFactor > 1f)
                {
                    product *= ext.producerPowerFactor;
                }
            }
            return product;
        }

        // 活跃协调器数（乘区拆分行显示用）
        public static int ActiveCoordinatorCount(Thing producer)
        {
            int count = 0;
            foreach (Thing device in ActiveDevicesOf(producer))
            {
                CelesIM_AmplifierExt ext = ExtOf(device);
                if (ext != null && ext.producerPowerFactor > 1f)
                {
                    count++;
                }
            }
            return count;
        }

        // 线程加成合计（含托管；Producer.TotalCapacity 消费）
        public static int TotalBoost(Thing producer)
        {
            int sum = 0;
            foreach (Thing device in ActiveDevicesOf(producer))
            {
                CelesIM_AmplifierExt ext = ExtOf(device);
                if (ext != null)
                {
                    sum += ext.boostPerUnit;
                }
            }
            return sum;
        }
    }
}
