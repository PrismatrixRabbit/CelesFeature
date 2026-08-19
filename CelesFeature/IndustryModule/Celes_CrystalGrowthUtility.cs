using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    // 判定链共享工具：培育器与晶种共用同一生长算法
    public static class Celes_CrystalGrowthUtility
    {
        // 返回本次检测创建晶簇数（DEV 用）
        public static int DoGrowCheck(Map map, IntVec3 center, Celes_CompProperties_CrystalGrowth props,
            Func<IntVec3, bool> cellValidator, TerrainAffordanceDef affordanceNeeded)
        {
            // 单次 GenRadial 遍历同时收集：范围内晶簇（削减/生长驱动用）+ 可选生成格
            List<Celes_CrystalCluster> clusters = new List<Celes_CrystalCluster>();
            List<IntVec3> candidates = new List<IntVec3>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, props.growRadius, true))
            {
                if (!cell.InBounds(map))
                    continue;
                bool hasCluster = false;
                List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Celes_CrystalCluster cluster)
                    {
                        clusters.Add(cluster);
                        hasCluster = true;
                        break;
                    }
                }
                if (!hasCluster && cell.Standable(map) && cellValidator(cell))
                    candidates.Add(cell);
            }

            // 初始概率：进链前一次性计算，当次新生晶簇不参与削减
            int maxLevel = props.pointToGrow.Count;
            int nonMaxCount = 0;
            for (int i = 0; i < clusters.Count; i++)
            {
                if (clusters[i].Level < maxLevel)
                    nonMaxCount++;
            }
            float currentChance = props.totalGrowChance - props.perDecreaseChance * nonMaxCount;
            if (currentChance <= 0f)
                currentChance = props.minGrowChance;

            // 判定链：无论成败步进减量，数学自限终止
            int spawned = 0;
            while (currentChance > 0f)
            {
                float p = Mathf.Min(currentChance, props.maxGrowChance);
                if (Rand.Value < p && candidates.TryRandomElement(out IntVec3 cell))
                {
                    SpawnCluster(cell, map, props, affordanceNeeded);
                    spawned++;
                    candidates.Remove(cell);
                }
                currentChance -= props.growStepDecrement;
            }

            // 源驱动晶簇生长（本次新建的晶簇不参与——clusters 在链前收集）
            for (int i = 0; i < clusters.Count; i++)
            {
                if (Rand.Value < props.chanceToGrow)
                    clusters[i].AddGrowthPoint(1);
            }
            return spawned;
        }

        public static Celes_CrystalCluster SpawnCluster(IntVec3 cell, Map map, Celes_CompProperties_CrystalGrowth props,
            TerrainAffordanceDef affordanceNeeded)
        {
            Celes_CrystalCluster cluster = (Celes_CrystalCluster)ThingMaker.MakeThing(props.baseGrowDef);
            cluster.InitGrowth(new List<int>(props.pointToGrow),
                props.terrainToGrow != null ? new List<TerrainDef>(props.terrainToGrow) : null,
                affordanceNeeded);
            GenSpawn.Spawn(cluster, cell, map);
            return cluster;
        }
    }
}
