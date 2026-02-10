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

    }
}
