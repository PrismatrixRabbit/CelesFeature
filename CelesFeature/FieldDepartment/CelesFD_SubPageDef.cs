using System.Collections.Generic;
using Verse;

namespace CelesFeature
{
    public enum CelesFD_SubPageType
    {
        WelcomeNews,       // 欢迎页：本季度要闻
        TradeDescription,  // 交易页：订单 description（M0 测试文本；M5b 后订单数据）
        LogisticsStatus    // 物流页：物流系统状态
    }

    // 发信器窗口正上子页内容 Def（M4 §10.3 M0 单元）
    public class CelesFD_SubPageDef : Def
    {
        public CelesFD_SubPageType subPageType;
        public List<string> newsPool;        // 欢迎页要闻池（WelcomeNews）
        public string logisticsTemplate;     // 物流状态模板（{station}/{position}/{status}；M0 用 string.Replace，M5d 统一换插值引擎）
        public string tradeDescription;      // TradeDescription 承载（M0 测试文本；M5b 后订单 description 走订单数据，本字段保留兜底；改名避免遮蔽 Def.description 基类字段）
    }
}
