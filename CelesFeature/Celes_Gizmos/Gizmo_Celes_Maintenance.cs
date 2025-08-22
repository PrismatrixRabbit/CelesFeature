using RimWorld;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace CelesFeature
{
    public class Gizmo_Celes_Maintenance : Gizmo_Slider
    {
        private Comp_Celes_Maintenance compMaintenance;
        protected override Color BarColor => Color.cyan;
        private static bool draggingBar;

        protected override float Target
        {
            get
            {
                return compMaintenance.maintenanceThreshold / 1.2f;
            }
            set
            {
                compMaintenance.maintenanceThreshold = value * 1.2f;
            }
        }

        protected override float ValuePercent => compMaintenance.maintenancePercentOfMax;

        protected override string Title => "Celes_Keyed_Maintenance".Translate();

        protected override bool DraggingBar
        {
            get
            {
                return draggingBar;
            }
            set
            {
                draggingBar = value;
            }
        }

        public Gizmo_Celes_Maintenance(Comp_Celes_Maintenance comp)
        {
            this.compMaintenance = comp;
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            Rect rect = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            TooltipHandler.TipRegionByKey(rect, "Celes_Keyed_Maintenance_Tooltip".Translate(compMaintenance.maintenance, compMaintenance.Props.decayPerDay, compMaintenance.Efficiency.ToStringPercent()));

            if (SteamDeck.IsSteamDeckInNonKeyboardMode)
            {
                return base.GizmoOnGUI(topLeft, maxWidth, parms);
            }

            KeyCode keyCode = ((KeyBindingDefOf.Command_ItemForbid != null) ? KeyBindingDefOf.Command_ItemForbid.MainKey : KeyCode.None);
            if (keyCode != 0 && !GizmoGridDrawer.drawnHotKeys.Contains(keyCode) && KeyBindingDefOf.Command_ItemForbid.KeyDownEvent)
            {
                Event.current.Use();
            }

            return base.GizmoOnGUI(topLeft, maxWidth, parms);
        }

        protected override string GetTooltip()
        {
            return "";
        }
    }
}
