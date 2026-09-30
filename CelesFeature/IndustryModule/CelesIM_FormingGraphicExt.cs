using UnityEngine;
using Verse;

namespace CelesFeature
{
    // ════════════════════════════════════════════════════════════════
    //  批 5 视觉修订批：锻造内容物配方侧定义（2026-09-30）
    //  · 挂 RecipeDef modExtensions（抽象父 Celes_AutoRecipe_Base 携全族默认图——
    //    li 跨继承追加，消费侧 last-wins=子配方 li 覆盖父 li，规避双实例首中陷阱）
    //  · 三态解析（建筑侧 ResolveContentGraphic）：useProductGraphic → 产物 def 本体图；
    //    graphic ≠ null → 该图；均无 → 不显示
    //  · 轴语义（drawSize 速查+linkHeight 实证）：x=东西/y=仅排序/z=视觉高度——
    //    drawOffset.z 才产生屏幕"高低"，bob 走 z（母本 PingPong 同款）
    // ════════════════════════════════════════════════════════════════
    public class CelesIM_FormingGraphicExt : DefModExtension
    {
        public GraphicData graphic;               // 内容物图（null=按 useProductGraphic/不显示）

        public bool useProductGraphic = false;    // true=显示配方产物 def 本体图（军刀临时验证路径）

        public Vector3 drawOffset = Vector3.zero;   // x=东西 y=排序微调 z=视觉高度

        public float bobDistance = 0f;            // z 向浮动幅度（0=静止；>0 时 PingPong 呼吸，母本同款）

        public float bobSpeed = 0.04f;            // 浮动速率（占位，实测调；母本 formingMechBobSpeed 同位）
    }
}
