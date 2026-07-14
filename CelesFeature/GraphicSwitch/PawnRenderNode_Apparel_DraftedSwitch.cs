using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace CelesFeature
{
    public class PawnRenderNode_Apparel_DraftedSwitch : PawnRenderNode_Apparel
    {
        public PawnRenderNode_Apparel_DraftedSwitch(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree) : base(pawn, props, tree)
        {
            this.useHeadMesh = (props.parentTagDef == PawnRenderNodeTagDefOf.ApparelHead);
            this.meshSet = this.MeshSetFor(pawn);
        }

        public PawnRenderNode_Apparel_DraftedSwitch(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree, Apparel apparel) : base(pawn, props, tree)
        {
            this.apparel = apparel;
            this.useHeadMesh = (props.parentTagDef == PawnRenderNodeTagDefOf.ApparelHead);
            this.meshSet = this.MeshSetFor(pawn);
        }

        public PawnRenderNode_Apparel_DraftedSwitch(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree, Apparel apparel, bool useHeadMesh) : base(pawn, props, tree)
        {
            this.apparel = apparel;
            this.useHeadMesh = useHeadMesh;
            this.meshSet = this.MeshSetFor(pawn);
        }

        protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
        {
            if (this.HasGraphic(this.tree.pawn))
            {
                yield return this.GraphicFor(pawn);
            }
            yield break;
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            string text = this.TexPathFor(pawn);
            if (text.NullOrEmpty())
            {
                return null;
            }
            Shader shader = this.ShaderFor(pawn);
            if (shader == null)
            {
                return null;
            }
            return GraphicDatabase.Get<Graphic_Multi>(text, shader, Vector2.one, this.ColorFor(pawn));
        }

        protected override string TexPathFor(Pawn pawn)
        {
            string path = null;

            if (this.Props.bodyTypeGraphicPaths != null)
            {
                foreach (var bodyTypeGraphicData in this.Props.bodyTypeGraphicPaths)
                {
                    if (pawn.story.bodyType == bodyTypeGraphicData.bodyType)
                    {
                        path = bodyTypeGraphicData.texturePath;
                        break;
                    }
                }
            }

            if (path == null && pawn.gender == Gender.Female)
            {
                if (!this.props.texPathsFemale.NullOrEmpty())
                {
                    using (new RandBlock(this.TexSeedFor(pawn)))
                    {
                        path = this.props.texPathsFemale.RandomElement();
                    }
                }
                else if (!this.props.texPathFemale.NullOrEmpty())
                {
                    path = this.props.texPathFemale;
                }
            }

            if (path == null && !this.props.texPaths.NullOrEmpty())
            {
                using (new RandBlock(this.TexSeedFor(pawn)))
                {
                    path = this.props.texPaths.RandomElement();
                }
            }

            if (path == null)
            {
                path = this.props.texPath;
            }

            if (pawn.Drafted)
            {
                path += "_Drafted";
            }
            //Log.Message(path);
            return path;
        }

    }
}

