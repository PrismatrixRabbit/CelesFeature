using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 7（D46/D51）：「当前功率修正」stat parts——值链 =
    //  基础值（worker 读电力 props=1600）→ 本加减 part → 本乘区 part
    //  → StatDef.minValue=1 钳制（StatWorker.FinalizeValue:823-826，零代码下限）
    //  · 实际耗电写入（CompProducerPower）与全部显示同读此 stat（单源）
    //  · 拆分行：ExplanationPart 挂点 StatWorker.cs:843-852（位置=最终值行前）；
    //    加值行按 def 聚合（「Nx 名: ±W」），乘区行（「Nx 协调器: ×F」）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_StatPart_ProducerPowerOffset : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!Valid(req))
            {
                return;
            }
            val += CelesIM_ProducerDeviceUtility.TotalPowerOffset(req.Thing);
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!Valid(req))
            {
                return null;
            }
            Dictionary<ThingDef, float> sums = new Dictionary<ThingDef, float>();
            Dictionary<ThingDef, int> counts = new Dictionary<ThingDef, int>();
            List<ThingDef> order = new List<ThingDef>();
            foreach (Thing device in CelesIM_ProducerDeviceUtility.ActiveDevicesOf(req.Thing))
            {
                CelesIM_AmplifierExt ext = CelesIM_ProducerDeviceUtility.ExtOf(device);
                if (ext == null || ext.producerPowerOffset == 0f)
                {
                    continue;
                }
                if (!sums.ContainsKey(device.def))
                {
                    sums[device.def] = 0f;
                    counts[device.def] = 0;
                    order.Add(device.def);
                }
                sums[device.def] += ext.producerPowerOffset;
                counts[device.def]++;
            }
            if (order.Count == 0)
            {
                return null;   // 无加减来源不占分解行（原版 StatPart 生效时才显示，同构）
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < order.Count; i++)
            {
                ThingDef def = order[i];
                float sum = sums[def];
                string labelPart = (counts[def] > 1 ? counts[def] + "x " : "") + def.LabelCap;
                string valPart = (sum > 0f ? "+" : "") + sum.ToString("#####0") + " W";
                if (i > 0)
                {
                    sb.Append('\n');
                }
                sb.Append("CelesIM_Keyed_ProducerPowerSourceLine".Translate(labelPart, valPart));
            }
            return sb.ToString();
        }

        private static bool Valid(StatRequest req)
        {
            return req.HasThing && req.Thing.TryGetComp<CelesIM_CompProducerPower>() != null;
        }
    }

    public class CelesIM_StatPart_ProducerPowerFactor : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!Valid(req))
            {
                return;
            }
            float factor = CelesIM_ProducerDeviceUtility.TotalPowerFactor(req.Thing);
            if (factor > 1f)
            {
                val *= factor;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!Valid(req))
            {
                return null;
            }
            int n = CelesIM_ProducerDeviceUtility.ActiveCoordinatorCount(req.Thing);
            if (n <= 0)
            {
                return null;
            }
            float total = CelesIM_ProducerDeviceUtility.TotalPowerFactor(req.Thing);
            string labelPart = (n > 1 ? n + "x " : "") + "CelesIM_Keyed_CoordinatorName".Translate();
            return "CelesIM_Keyed_ProducerPowerSourceLine".Translate(labelPart, "×" + total.ToString("0.#"));
        }

        private static bool Valid(StatRequest req)
        {
            return req.HasThing && req.Thing.TryGetComp<CelesIM_CompProducerPower>() != null;
        }
    }
}
