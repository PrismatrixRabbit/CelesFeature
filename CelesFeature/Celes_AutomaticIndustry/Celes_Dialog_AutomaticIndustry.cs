using RimWorld;
using System.Text;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class Celes_Dialog_AutomaticIndustry : Dialog_BillConfig
    {
        private static float formingInfoHeight;

        public Celes_Dialog_AutomaticIndustry(Celes_Bill_AutomaticIndustry bill, IntVec3 billGiverPos)
            : base(bill, billGiverPos)
        {
        }

        protected override void DoIngredientConfigPane(float x, ref float y, float width, float height)
        {
            float y2 = y;
            base.DoIngredientConfigPane(x, ref y2, width, height - formingInfoHeight);
            if (bill.billStack.billGiver is Celes_Building_AutomaticIndustry building && building.ActiveBill == bill)
            {
                Rect rect = new Rect(x, y2, width, 9999f);
                Listing_Standard listing_Standard = new Listing_Standard();
                listing_Standard.Begin(rect);
                StringBuilder stringBuilder = new StringBuilder();
                listing_Standard.Label("Celes_Keyed_ProductionCycle_FormerIngredients".Translate() + ":");
                building.ActiveBill.AppendCurrentIngredientCount(stringBuilder);
                listing_Standard.Label(stringBuilder.ToString());
                Celes_Bill_AutomaticIndustry bill_industry = (Celes_Bill_AutomaticIndustry)bill;
                listing_Standard.Label((string)"Celes_Keyed_ProductionCycle_Completed".Translate());
                listing_Standard.Gap();
                listing_Standard.End();
                formingInfoHeight = listing_Standard.CurHeight;
            }
        }
    }
}
