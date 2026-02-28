using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace VanillaFurnitureExpandedFactory
{
	[StaticConstructorOnStartup]
	[HotSwappable]
	public class Building_Autofarmer : Building, IPlantToGrowSettable
	{
		public enum AutofarmerState { Idle, MovingForward, MovingBackward }
		private static readonly Texture2D IncreaseZoneIcon = ContentFinder<Texture2D>.Get("UI/Gizmo/ZoneLarger_Gizmo");
		private static readonly Texture2D DecreaseZoneIcon = ContentFinder<Texture2D>.Get("UI/Gizmo/ZoneSmaller_Gizmo");
		private static readonly Texture2D StartIcon = ContentFinder<Texture2D>.Get("UI/Gizmo/Start_Gizmo");
		private static readonly Texture2D CancelIcon = ContentFinder<Texture2D>.Get("UI/Gizmo/Cancel_Gizmo");
		private static readonly Texture2D AutoHarvestIcon = ContentFinder<Texture2D>.Get("UI/Gizmo/Autoharvesting_Gizmo");
		private static readonly Texture2D AutoSowIcon = ContentFinder<Texture2D>.Get("UI/Gizmo/Autosowing_Gizmo");
		public static readonly Graphic AutofarmerBaseGraphic = GraphicDatabase.Get<Graphic_Multi>(
			"Things/Building/Factories/Autofarmer/AutofarmerBase",
			ShaderDatabase.Cutout, new Vector2(7, 3), Color.white);
		public static readonly Graphic AutofarmerMachineGraphic = GraphicDatabase.Get<Graphic_Multi>(
			"Things/Building/Factories/Autofarmer/Autofarmer",
			ShaderDatabase.Cutout, new Vector2(7, 3), Color.white);

		private int zoneLength = 3;
		private bool autoHarvest = true;
		private bool autoSow = true;
		private ThingDef plantDefToGrow = ThingDefOf.Plant_Potato;

		private AutofarmerState state = AutofarmerState.Idle;
		private float currentOffset = 0f;
		private int lastProcessedRow = 0;

		private CompPowerTrader powerComp;
		private Sustainer sustainer;

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			powerComp = GetComp<CompPowerTrader>();
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref zoneLength, "zoneLength", 3);
			Scribe_Values.Look(ref autoHarvest, "autoHarvest", true);
			Scribe_Values.Look(ref autoSow, "autoSow", true);
			Scribe_Defs.Look(ref plantDefToGrow, "plantDefToGrow");
			Scribe_Values.Look(ref state, "state", AutofarmerState.Idle);
			Scribe_Values.Look(ref currentOffset, "currentOffset", 0f);
			Scribe_Values.Look(ref lastProcessedRow, "lastProcessedRow", 0);
		}

		protected override void Tick()
		{
			base.Tick();
			UpdatePowerConsumption();
			if (!powerComp.PowerOn && state != AutofarmerState.Idle && state != AutofarmerState.MovingBackward)
			{
				state = AutofarmerState.MovingBackward;
			}

			if (state == AutofarmerState.Idle)
			{
				if (sustainer != null && !sustainer.Ended)
				{
					sustainer.End();
				}
				return;
			}

			MaintainSustainer();

			float speedTicks = 60f;
			if (state == AutofarmerState.MovingForward)
			{
				if (!autoHarvest && !autoSow) speedTicks = 60f;
				else if (autoHarvest && !autoSow) speedTicks = 180f;
				else if (!autoHarvest && autoSow) speedTicks = 240f;
				else speedTicks = 420f;
			}

			float moveAmount = 1f / speedTicks;

			if (state == AutofarmerState.MovingForward)
			{
				currentOffset += moveAmount;
				int currentRow = Mathf.FloorToInt(currentOffset);

				if (currentRow > lastProcessedRow && currentRow <= zoneLength)
				{
					if (IsZoneObstructed())
					{
						Messages.Message("VFEFactory_AutofarmerObstructed".Translate(), MessageTypeDefOf.RejectInput, false);
						state = AutofarmerState.MovingBackward;
						return;
					}
					ProcessRow(currentRow);
					lastProcessedRow = currentRow;
				}

				if (currentOffset >= zoneLength)
				{
					state = AutofarmerState.MovingBackward;
				}
			}
			else if (state == AutofarmerState.MovingBackward)
			{
				currentOffset -= moveAmount;
				if (currentOffset <= 0f)
				{
					currentOffset = 0f;
					state = AutofarmerState.Idle;
					lastProcessedRow = 0;
				}
			}
		}

		private void ProcessRow(int rowOffset)
		{
			foreach (IntVec3 c in GetRowCells(rowOffset))
			{
				if (!c.InBounds(Map)) continue;

				Plant plant = c.GetPlant(Map);
				if (autoHarvest && plant != null)
				{
					EffecterDef effecterDef = plant.def.plant.IsTree ? EffecterDefOf.Harvest_Tree : EffecterDefOf.Harvest_Plant;
					Effecter effecter = effecterDef.Spawn(c, Map);
					effecter.Trigger(new TargetInfo(c, Map), new TargetInfo(c, Map));
					effecter.Cleanup();

					if (plant.HarvestableNow)
					{
						int yield = plant.YieldNow();
						if (yield > 0)
						{
							Thing t = ThingMaker.MakeThing(plant.def.plant.harvestedThingDef);
							t.stackCount = yield;
							GenPlace.TryPlaceThing(t, c, Map, ThingPlaceMode.Near);
						}
					}

					if (plant.def.plant.HarvestDestroys)
					{
						plant.Destroy();
					}
					else
					{
						plant.Growth = plant.def.plant.harvestAfterGrowth;
						Map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
					}
				}

				if (autoSow && plantDefToGrow != null && c.GetPlant(Map) == null)
				{
					if (plantDefToGrow.CanNowPlantAt(c, Map) && PlantUtility.AdjacentSowBlocker(plantDefToGrow, c, Map) == null)
					{
						Effecter effecter = EffecterDefOf.Sow.Spawn(c, Map);
						effecter.Trigger(new TargetInfo(c, Map), new TargetInfo(c, Map));
						effecter.Cleanup();
						Plant newPlant = (Plant)GenSpawn.Spawn(plantDefToGrow, c, Map);
						newPlant.Growth = 0.0001f;
						newPlant.sown = true;
					}
				}
			}
		}

		private IEnumerable<IntVec3> GetRowCells(int rowOffset)
		{
			CellRect rect = this.OccupiedRect();
			IntVec3 right = Rotation.RighthandCell;

			IntVec3 frontEdge;
			int halfWidth;

			if (Rotation == Rot4.North)
			{
				frontEdge = new IntVec3(rect.CenterCell.x, 0, rect.maxZ + rowOffset);
				halfWidth = (rect.Width - 1) / 2;
			}
			else if (Rotation == Rot4.South)
			{
				frontEdge = new IntVec3(rect.CenterCell.x, 0, rect.minZ - rowOffset);
				halfWidth = (rect.Width - 1) / 2;
			}
			else if (Rotation == Rot4.East)
			{
				frontEdge = new IntVec3(rect.maxX + rowOffset, 0, rect.CenterCell.z);
				halfWidth = (rect.Height - 1) / 2;
			}
			else
			{
				frontEdge = new IntVec3(rect.minX - rowOffset, 0, rect.CenterCell.z);
				halfWidth = (rect.Height - 1) / 2;
			}

			for (int i = -halfWidth; i <= halfWidth; i++)
				yield return frontEdge + right * i;
		}

		private void UpdatePowerConsumption()
		{
			if (state == AutofarmerState.Idle)
			{
				powerComp.PowerOutput = -50f;
			}
			else
			{
				powerComp.PowerOutput = -1200f;
			}
		}

		private void MaintainSustainer()
		{
			Vector3 sustainerPos = DrawPos + Rotation.FacingCell.ToVector3() * currentOffset;
			if (sustainer == null || sustainer.Ended)
			{
				SoundInfo info = SoundInfo.InMap(new TargetInfo(sustainerPos.ToIntVec3(), Map));
				sustainer = InternalDefOf.VFEFactory_DefaultFactorySustainer.TrySpawnSustainer(info);
			}
			sustainer.Maintain();
		}

		public override IEnumerable<Gizmo> GetGizmos()
		{
			foreach (var g in base.GetGizmos()) yield return g;

			yield return new Command_Action
			{
				defaultLabel = "VFEFactory_IncreaseZone".Translate(),
				defaultDesc = "VFEFactory_IncreaseZoneDesc".Translate(),
				icon = IncreaseZoneIcon,
				action = () => { if (zoneLength < 30) zoneLength++; }
			};

			yield return new Command_Action
			{
				defaultLabel = "VFEFactory_DecreaseZone".Translate(),
				defaultDesc = "VFEFactory_DecreaseZoneDesc".Translate(),
				icon = DecreaseZoneIcon,
				action = () => { if (zoneLength > 3) zoneLength--; }
			};

			if (state == AutofarmerState.Idle)
			{
				yield return new Command_Action
				{
					defaultLabel = "VFEFactory_StartAutofarmer".Translate(),
					defaultDesc = "VFEFactory_StartAutofarmerDesc".Translate(),
					icon = StartIcon,
					action = () =>
					{
						if (IsZoneObstructed())
						{
							Messages.Message("VFEFactory_AutofarmerObstructed".Translate(), MessageTypeDefOf.RejectInput, false);
							return;
						}
						state = AutofarmerState.MovingForward;
						lastProcessedRow = 0;
					}
				};
			}
			else
			{
				yield return new Command_Action
				{
					defaultLabel = "VFEFactory_CancelAutofarmer".Translate(),
					defaultDesc = "VFEFactory_CancelAutofarmerDesc".Translate(),
					icon = CancelIcon,
					action = () => { state = AutofarmerState.MovingBackward; }
				};
			}

			yield return SetPlantToGrowCommand(this);

			yield return new Command_Toggle
			{
				defaultLabel = "VFEFactory_Autoharvesting".Translate(),
				defaultDesc = "VFEFactory_AutoharvestingDesc".Translate(),
				icon = AutoHarvestIcon,
				isActive = () => autoHarvest,
				toggleAction = () => autoHarvest = !autoHarvest
			};

			yield return new Command_Toggle
			{
				defaultLabel = "VFEFactory_Autosowing".Translate(),
				defaultDesc = "VFEFactory_AutosowingDesc".Translate(),
				icon = AutoSowIcon,
				isActive = () => autoSow,
				toggleAction = () => autoSow = !autoSow
			};
		}

		public static Command_SetPlantToGrowAutofarmer SetPlantToGrowCommand(IPlantToGrowSettable settable)
		{
			return new Command_SetPlantToGrowAutofarmer
			{
				defaultDesc = "CommandSelectPlantToGrowDesc".Translate(),
				hotKey = KeyBindingDefOf.Misc12,
				settable = settable
			};
		}

		public IEnumerable<IntVec3> Cells
		{
			get
			{
				List<IntVec3> cells = new List<IntVec3>();
				for (int i = 1; i <= zoneLength; i++)
				{
					cells.AddRange(GetRowCells(i));
				}
				return cells;
			}
		}

		public bool CanAcceptSowNow()
		{
			return true;
		}

		public ThingDef GetPlantDefToGrow()
		{
			return plantDefToGrow;
		}

		public void SetPlantDefToGrow(ThingDef plantDef)
		{
			plantDefToGrow = plantDef;
		}

		private bool IsZoneObstructed()
		{
			for (int i = 1; i <= zoneLength; i++)
			{
				foreach (IntVec3 c in GetRowCells(i))
				{
					if (!c.InBounds(Map)) continue;
					Building b = c.GetEdifice(Map);
					if (b != null && b.def.passability == Traversability.Impassable) return true;
				}
			}
			return false;
		}

		protected override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			Vector3 basePos = drawLoc;
			AutofarmerBaseGraphic?.Draw(basePos, Rotation, this, 0f);

			Vector3 machinePos = drawLoc + Rotation.FacingCell.ToVector3() * currentOffset;
			machinePos.y = drawLoc.y + 0.0405f;
			AutofarmerMachineGraphic?.Draw(machinePos, flip ? Rotation.Opposite : Rotation, this, 0f);
		}

		public override void DrawExtraSelectionOverlays()
		{
			base.DrawExtraSelectionOverlays();
			List<IntVec3> zoneCells = new List<IntVec3>();
			for (int i = 1; i <= zoneLength; i++)
			{
				zoneCells.AddRange(GetRowCells(i));
			}
			GenDraw.DrawFieldEdges(zoneCells, Color.white);
		}
	}
}
