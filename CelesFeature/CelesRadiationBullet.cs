using System;
using RimWorld;
using Verse;

namespace CelesFeature
{
    public class ThingDef_RadiationBullet : ThingDef
    {
        public int addHediffChance; //默认值会被xml覆盖
        public HediffDef hediffToAdd;
    }
        public class Projectile_RadiationBullet : Bullet
    {
        #region data
        public ThingDef_RadiationBullet ThingDef_RadiationBullet
        {
            get
            {
                //底层通过名字读取了我们定义的ThingDef_TestBullet这个xml格式的新数据，并存放到了this.def中，我们将this.def拆箱拿到我们定义好的ThingDef_TestBullet格式数据
                return this.def as ThingDef_RadiationBullet;
            }
        }
        #endregion
        protected override void Impact(Thing hitThing , bool blockedByShield = false)
        {
            //子弹的影响，底层实现了伤害 击杀之类的方法，感兴趣的话可以用dnspy反编译Assembly-Csharp.dll研究里面到底写了什么
            base.Impact(hitThing);
            //绝大多数mod报错都是因为没判断好非空，写注释和判断非空是好习惯
            //大佬在这里用了一个语法糖hitThing is Pawn hitPawn
            //如果hitThing可以被拆箱为Pawn的话 这个值返回true并且会声明一个变量hitPawn=hitThing as Pawn
            //否则返回false hitPawn是null
            if (ThingDef_RadiationBullet != null && hitThing != null && hitThing is Pawn hitPawn)
            {
                Random random = new Random();
                //触发瘟疫
                if (random.Next(1, 100) <= ThingDef_RadiationBullet.addHediffChance)
                {
                    Hediff hediffOnPawn = hitPawn.health?.hediffSet?.GetFirstHediffOfDef(ThingDef_RadiationBullet.hediffToAdd);
                    //我们为本次触发的瘟疫随机生成一个严重程度
                    //已经触发瘟疫
                    if (hediffOnPawn != null)
                    {
                        //严重程度叠加，超过100%会即死
                        hediffOnPawn.Severity += 0.05f;
                    }
                    else
                    {
                        //我们调用HediffMaker.MakeHediff生成一个新的hediff状态，类型就是我们之前设置过的HediffDefOf.Plague瘟疫类型
                        Hediff hediff = HediffMaker.MakeHediff(ThingDef_RadiationBullet.hediffToAdd, hitPawn);
                        //设置这个状态的严重程度
                        hediff.Severity = 0.05f;
                        //把状态添加到被击中的目标身上
                        hitPawn.health.AddHediff(hediff);
                    }
                }
            }
        }
    }
}