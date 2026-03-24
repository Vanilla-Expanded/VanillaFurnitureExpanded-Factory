using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
namespace VanillaFurnitureExpandedFactory
{
	[DefOf]
	public static class InternalDefOf
	{
		static InternalDefOf()
		{
			DefOfHelper.EnsureInitializedInCtor(typeof(InternalDefOf));
		}

		public static RoomRoleDef VFEFactory_FactoryRoom;

		public static ThingDef HandTailoringBench;
		public static ThingDef TableMachining;
		public static ThingDef VFEFactory_Autoloom;
		public static ThingDef VFEFactory_FactoryHopper;
		public static ThingDef VFEFactory_AutomatedMachiningBay;
		[MayRequireOdyssey]
		public static ThingDef VFEFactory_AutomatedFishfarm;

		public static SoundDef VFEFactory_DefaultFactorySustainer;

		public static JobDef VFEFactory_HaulFromConveyor;

		public static TerrainAffordanceDef FactoryFloor;

		[MayRequire("CETeam.CombatExtended")]
		public static StuffCategoryDef Steeled;

		[MayRequireOdyssey]
		public static TerrainDef HeavyBridge;
	}
}
