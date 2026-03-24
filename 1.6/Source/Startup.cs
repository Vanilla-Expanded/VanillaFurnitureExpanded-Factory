using System.Collections.Generic;
using Verse;

namespace VanillaFurnitureExpandedFactory
{
	[StaticConstructorOnStartup]
	public static class Startup
	{
		static Startup()
		{
			foreach (TerrainDef terrainDef in DefDatabase<TerrainDef>.AllDefs)
			{
				if (terrainDef.IsSubstructure || ModsConfig.OdysseyActive && terrainDef == InternalDefOf.HeavyBridge)
				{
					terrainDef.affordances ??= new List<TerrainAffordanceDef>();
					if (!terrainDef.affordances.Contains(InternalDefOf.FactoryFloor))
					{
						terrainDef.affordances.Add(InternalDefOf.FactoryFloor);
					}
				}
			}
		}
	}
}
