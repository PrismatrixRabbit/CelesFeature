using RimWorld;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 7（需求 3）：显示为主 stat 族 worker——ShouldShowFor 门 + 单源读值
    //  · 门=挂对应 comp/ext 才显示（先例=StatWorker_ThreadLink 族 batch 4）
    //  · 值全部现算读单源（comp 属性/ext 字段/props），零 statBases 双写
    //  · 单位/符号在 C# 预格式化（键内零格式符）；W/× 单位符号非可译文本
    // ════════════════════════════════════════════════════════════════

    // 「当前功率修正」（中枢）：base=电力 props 基础耗电；值=parts 加减乘全链（StatDef.parts）
    public class CelesIM_StatWorker_ProducerPower : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            if (stat.alwaysHide || !stat.CanShowWithLoadedMods())
            {
                return false;
            }
            return req.HasThing && req.Thing.TryGetComp<CelesIM_CompProducerPower>() != null;
        }

        public override float GetBaseValueFor(StatRequest request)
        {
            if (request.HasThing)
            {
                CompPowerTrader pc = request.Thing.TryGetComp<CompPowerTrader>();
                if (pc != null)
                {
                    return pc.Props.PowerConsumption;   // 与中枢 def 电力字段单源（不双写）
                }
            }
            return base.GetBaseValueFor(request);
        }

        public override string GetStatDrawEntryLabel(StatDef stat, float value, ToStringNumberSense numberSense, StatRequest optionalReq, bool finalized = true)
        {
            return value.ToString("#####0") + " W";
        }
    }

    // 「编译线程产出」（中枢）
    public class CelesIM_StatWorker_ProducerThreadOutput : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            if (stat.alwaysHide || !stat.CanShowWithLoadedMods())
            {
                return false;
            }
            return req.HasThing && req.Thing.TryGetComp<CelesIM_CompThreadProducer>() != null;
        }

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            CelesIM_CompThreadProducer producer = req.HasThing ? req.Thing.TryGetComp<CelesIM_CompThreadProducer>() : null;
            return producer != null ? producer.TotalCapacity : base.GetValueUnfinalized(req, applyPostProcess);
        }
    }

    // 「增幅」（设备）：ext.boostPerUnit
    public class CelesIM_StatWorker_DeviceBoost : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            if (stat.alwaysHide || !stat.CanShowWithLoadedMods() || !req.HasThing)
            {
                return false;
            }
            CelesIM_AmplifierExt ext = req.Thing.def.GetModExtension<CelesIM_AmplifierExt>();
            return ext != null && ext.boostPerUnit != 0;
        }

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            CelesIM_AmplifierExt ext = req.HasThing ? req.Thing.def.GetModExtension<CelesIM_AmplifierExt>() : null;
            return ext != null ? ext.boostPerUnit : 0f;
        }

        public override string GetStatDrawEntryLabel(StatDef stat, float value, ToStringNumberSense numberSense, StatRequest optionalReq, bool finalized = true)
        {
            return (value > 0f ? "+" : "") + value.ToString("#####0");
        }
    }

    // 「占用」（设备）：消费者 baseThreadsCost
    public class CelesIM_StatWorker_DeviceThreadCost : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            if (stat.alwaysHide || !stat.CanShowWithLoadedMods())
            {
                return false;
            }
            return req.HasThing && req.Thing.TryGetComp<CelesIM_CompThreadConsumer>() != null;
        }

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            CelesIM_CompThreadConsumer consumer = req.HasThing ? req.Thing.TryGetComp<CelesIM_CompThreadConsumer>() : null;
            return consumer != null ? consumer.Props.baseThreadsCost : 0f;
        }
    }

    // 「中枢负载增幅」（设备）：ext.producerPowerOffset（W）或 ext.producerPowerFactor（×）
    public class CelesIM_StatWorker_DeviceProducerLoad : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            if (stat.alwaysHide || !stat.CanShowWithLoadedMods() || !req.HasThing)
            {
                return false;
            }
            CelesIM_AmplifierExt ext = req.Thing.def.GetModExtension<CelesIM_AmplifierExt>();
            return ext != null && (ext.producerPowerOffset != 0f || ext.producerPowerFactor > 1f);
        }

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            CelesIM_AmplifierExt ext = req.HasThing ? req.Thing.def.GetModExtension<CelesIM_AmplifierExt>() : null;
            if (ext == null)
            {
                return 0f;
            }
            return ext.producerPowerOffset != 0f ? ext.producerPowerOffset : ext.producerPowerFactor;
        }

        public override string GetStatDrawEntryLabel(StatDef stat, float value, ToStringNumberSense numberSense, StatRequest optionalReq, bool finalized = true)
        {
            CelesIM_AmplifierExt ext = optionalReq.HasThing
                ? optionalReq.Thing.def.GetModExtension<CelesIM_AmplifierExt>()
                : null;
            if (ext != null && ext.producerPowerOffset != 0f)
            {
                return (ext.producerPowerOffset > 0f ? "+" : "") + ext.producerPowerOffset.ToString("#####0") + " W";
            }
            if (ext != null && ext.producerPowerFactor > 1f)
            {
                return "×" + ext.producerPowerFactor.ToString("0.#");
            }
            return value.ToString("#####0");
        }
    }

    // 「连接数」（设备）：自身 facility props.maxSimultaneous（单一真相源）
    public class CelesIM_StatWorker_DeviceConnCount : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            if (stat.alwaysHide || !stat.CanShowWithLoadedMods() || !req.HasThing)
            {
                return false;
            }
            return req.Thing.def.GetCompProperties<CelesIM_CompProperties_AmplifierFacility>() != null;
        }

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            CelesIM_CompProperties_AmplifierFacility props = req.HasThing
                ? req.Thing.def.GetCompProperties<CelesIM_CompProperties_AmplifierFacility>()
                : null;
            return props != null ? props.maxSimultaneous : 0f;
        }
    }

    // 「连接范围」（设备）：自身 facility props.maxDistance（单一真相源）
    public class CelesIM_StatWorker_DeviceConnRange : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            if (stat.alwaysHide || !stat.CanShowWithLoadedMods() || !req.HasThing)
            {
                return false;
            }
            return req.Thing.def.GetCompProperties<CelesIM_CompProperties_AmplifierFacility>() != null;
        }

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            CelesIM_CompProperties_AmplifierFacility props = req.HasThing
                ? req.Thing.def.GetCompProperties<CelesIM_CompProperties_AmplifierFacility>()
                : null;
            return props != null ? props.maxDistance : 0f;
        }

        public override string GetStatDrawEntryLabel(StatDef stat, float value, ToStringNumberSense numberSense, StatRequest optionalReq, bool finalized = true)
        {
            return value.ToString("0.#");
        }
    }
}
