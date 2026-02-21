using System;
using UnityEngine;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
    [StaticConstructorOnStartup]
    public static class GraphicsCache
    {
        public static readonly Graphic_Multi hopperNormal = (Graphic_Multi)GraphicDatabase.Get<Graphic_Multi>("Things/Building/FactoryHopper/FactoryHopper_Unset", ShaderDatabase.CutoutComplex, new Vector2(1.05f, 1.05f), Color.white);
        public static readonly Graphic_Multi hopperInput = (Graphic_Multi)GraphicDatabase.Get<Graphic_Multi>("Things/Building/FactoryHopper/FactoryHopper_Input", ShaderDatabase.CutoutComplex, new Vector2(1.05f,1.05f), Color.white);
        public static readonly Graphic_Multi hopperOutput = (Graphic_Multi)GraphicDatabase.Get<Graphic_Multi>("Things/Building/FactoryHopper/FactoryHopper_Output", ShaderDatabase.CutoutComplex, new Vector2(1.05f, 1.05f), Color.white);
        public static readonly Texture2D GizmoMakeLink = ContentFinder<Texture2D>.Get("UI/Gizmo/UndergroundConveyor_MakeLink");
        public static readonly Texture2D GizmoBreakLink = ContentFinder<Texture2D>.Get("UI/Gizmo/UndergroundConveyor_BreakLink");
        public static readonly Texture2D GizmoSelectLinked = ContentFinder<Texture2D>.Get("UI/Gizmo/UndergroundConveyor_SelectLinked");
        public static readonly Material OverlayNoLink = MaterialPool.MatFrom("UI/Gizmo/UndergroundConveyor_Overlay_NoLink", ShaderDatabase.MetaOverlay);

    }
}
