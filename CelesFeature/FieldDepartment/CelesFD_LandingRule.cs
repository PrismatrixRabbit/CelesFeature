using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // W-5：落点规则基类——开放性接口（用户需求：允许扩展 Mod 写入落点规则）
    // XML 载体：<landingRule Class="CelesFeature.XxxRule"/>（单字段 Class 节点——A2-9）
    // null = 默认就近（信标格——TryTriggerEffect 兜底，W-2 旧版同语义）
    // W-5 签名升级：单格 → 格集（5 类空间逻辑统一为 Grid/Scatter 两种算法）
    public abstract class CelesFD_LandingRule
    {
        public abstract bool TryResolveCells(IntVec3 beaconCell, IntVec3 originCell, Map map, out List<IntVec3> cells);
    }

    // 布局元数据接口（W-5 时序接口）：Grid 以行主序扁平输出格集（r 外循环 c 内循环——顺序是排程契约），
    //   Effect_Barrage 据此按索引还原 (r, c) 计算排间/同排偏移——规则只保证顺序与维度，时序知识仍在效果侧
    public interface CelesFD_IGridLayout
    {
        int ForwardCount { get; }
        int LateralCount { get; }
    }

    // 规则网格：以投掷线为固有坐标系（forward = 投掷者→信标方向，lateral = 垂线）——无方向字段
    //   单点 = (1,1)；渐进弹幕 = (5,3)；一字烟幕 = (1,5)
    public class CelesFD_LandingRule_Grid : CelesFD_LandingRule, CelesFD_IGridLayout
    {
        public int forwardCount = 1;         // 前向位数（沿投掷线步进；1=不推进）
        public int lateralCount = 1;         // 侧向位数（沿垂线展开；1=单列）
        public int spacing = 5;              // 相邻间距（格；非半径类——不适用 x.9 约定）
        public bool requireWalkable = false; // false=仅 InBounds（弹幕旧语义）；容器落点由 faller 落地 TryFindPlaceNear 兜底

        public int ForwardCount => forwardCount;
        public int LateralCount => lateralCount;

        public override bool TryResolveCells(IntVec3 beaconCell, IntVec3 originCell, Map map, out List<IntVec3> cells)
        {
            // 方向向量：投掷者→信标（零向量兜底向北——W-2b 渐进弹幕同款）
            Vector3 dir = (beaconCell - originCell).ToVector3();
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = new Vector3(0f, 0f, -1f);
            dir.Normalize();
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
            cells = new List<IntVec3>(forwardCount * lateralCount);
            for (int r = 0; r < forwardCount; r++)
            {
                for (int c = 0; c < lateralCount; c++)
                {
                    // 居中系数浮点（*0.5f——整数除法在偶数 lateralCount 时偏心）
                    IntVec3 cell = (beaconCell.ToVector3()
                        + dir * (r * spacing)
                        + perp * ((c - (lateralCount - 1) * 0.5f) * spacing)).ToIntVec3();
                    if (!cell.InBounds(map)) continue;
                    if (requireWalkable && !cell.Walkable(map)) continue;
                    cells.Add(cell);
                }
            }
            return cells.Count > 0;   // 全格无效 = 解析失败（信标 Warning 路径）
        }
    }

    // 范围随机：半径内均匀无放回抽取 count 格（Fisher-Yates 洗牌取前 N——原版无 RandomSample 工具，实证）
    //   radius 走 x.9 约定（整数半径多含恰距=半径的轴向 4 格，边缘非圆——
    //   GenRadial.NumCellsInRadius 按 d²≤(r+ε)² 取格的机制，GenRadial.cs:166-174）
    public class CelesFD_LandingRule_Scatter : CelesFD_LandingRule
    {
        public float radius = 2.9f;
        public int count = 1;
        public bool requireWalkable = true;  // 散布选格默认服务投放实体（容器/建筑链需要）

        public override bool TryResolveCells(IntVec3 beaconCell, IntVec3 originCell, Map map, out List<IntVec3> cells)
        {
            List<IntVec3> candidates = new List<IntVec3>();
            foreach (IntVec3 c in GenRadial.RadialCellsAround(beaconCell, radius, useCenter: true))
                if (c.InBounds(map) && (!requireWalkable || c.Walkable(map))) candidates.Add(c);
            if (candidates.Count == 0)
            {
                cells = new List<IntVec3> { beaconCell };   // 无候选回落信标格（旧 UniformScatter 同款）
                return true;
            }
            for (int i = candidates.Count - 1; i > 0; i--)   // Fisher-Yates 洗牌（无放回）
            {
                int j = Rand.RangeInclusive(0, i);
                IntVec3 tmp = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = tmp;
            }
            cells = candidates.GetRange(0, Math.Min(count, candidates.Count));
            return true;
        }
    }
}
