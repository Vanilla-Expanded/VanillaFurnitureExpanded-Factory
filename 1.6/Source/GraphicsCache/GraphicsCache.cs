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

        public static readonly Material AutoFarmerGreenMaterial = MaterialPool.MatFrom("UI/Overlays/InteractionCell", ShaderDatabase.Transparent, new Color(0.365f, 0.576f, 0.373f));
        public static readonly Material AutoFarmerYellowMaterial = MaterialPool.MatFrom("UI/Overlays/InteractionCell", ShaderDatabase.Transparent, new Color(0.525f, 0.529f, 0.365f));
        public static readonly Material AutoFarmerBrownMaterial = MaterialPool.MatFrom("UI/Overlays/InteractionCell", ShaderDatabase.Transparent, new Color(0.529f, 0.431f, 0.376f));
        public static readonly Material AutoFarmerPurpleMaterial = MaterialPool.MatFrom("UI/Overlays/InteractionCell", ShaderDatabase.Transparent, new Color(0.514f, 0.365f, 0.529f));
        public static readonly Material AutoFarmerBlueMaterial = MaterialPool.MatFrom("UI/Overlays/InteractionCell", ShaderDatabase.Transparent, new Color(0.365f, 0.51f, 0.529f));


    }
}
