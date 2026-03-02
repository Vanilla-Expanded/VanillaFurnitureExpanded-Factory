using System.Collections.Generic;
using System.Linq;
using System.Text;
using LudeonTK;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace VanillaFurnitureExpandedFactory
{
    public enum DownstreamBlockReason
    {
        None,
        TargetOutOfBounds,
        TargetMoving,
        TargetFull,
        CompetitorAhead,
        CompetitorTying,
        CellNotWalkable,
        CellFull,
    }

    public enum DumpCapability
    {
        CannotDump,
        CanDumpPartial,
        CanDump,
    }

    [HotSwappable]
    public class Building_Conveyor : Building, IStoreSettingsParent, IThingHolder
    {
        private static readonly Rot4[] CanonicalOrder = { Rot4.East, Rot4.West, Rot4.North, Rot4.South };

        private static readonly Rot4[][] InputDirectionsByRot;
        private static readonly Rot4[][] OutputDirectionsByRot;

        static Building_Conveyor()
        {
            InputDirectionsByRot = new Rot4[4][];
            OutputDirectionsByRot = new Rot4[4][];
            for (int i = 0; i < 4; i++)
            {
                Rot4 rot = new Rot4(i);
                InputDirectionsByRot[i] = new Rot4[] {
                    rot.Opposite,
                    rot.Rotated(RotationDirection.Clockwise),
                    rot.Rotated(RotationDirection.Counterclockwise)
                };
                OutputDirectionsByRot[i] = new Rot4[] {
                    rot,
                    rot.Rotated(RotationDirection.Clockwise),
                    rot.Rotated(RotationDirection.Counterclockwise)
                };
            }
        }

        public ThingOwner<Thing> innerContainer;
        private bool dirtied;

        private readonly List<Thing> transferred = new List<Thing>(4);
        private readonly Rot4[] validOutputs = new Rot4[3];
        private readonly List<GameCondition> conditions = new List<GameCondition>();

        private int cachedItemsPerCell;
        private Vector3 cachedItemDrawPos;
        private float lastCachedProgress = float.MinValue;

        public Building_Conveyor()
        {
            innerContainer = new ThingOwner<Thing>(this);
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }
        public float itemProgress;
        private float lastItemProgress;
        public enum ConveyorState { Empty, Moving, Waiting }
        public ConveyorState state = ConveyorState.Empty;
        private bool? cachedIsTurn;
        private int? cachedInputCount;
        private int? cachedOutputCount;
        private Rot4 cachedSelectedOutput = Rot4.Invalid;
        private Rot4 cachedSingleOutput = Rot4.Invalid;
        private int splitterOutputIndex;
        private StorageSettings storageSettings;
        private Graphic cachedGraphic;
        private bool cachedIsSingleDirectional;
        private ConveyorExtension cachedProps;
        private bool isRecaching = false;
        private bool? cachedHasRefuelableTarget;
        private bool cachedShowItems = true;
        private CompRefuelable cachedRefuelComp;
        private bool isDrawnDynamically;
        private Building cachedForwardBuilding;
        private Map cachedMap;
        private IntVec3 cachedPos;
        private Building[] cachedNeighborBuildings;
        private IntVec3[] cachedNeighborPositions;
        private IntVec3 cachedForwardBuildingPos = IntVec3.Invalid;
        private IntVec3 cachedForwardCell = IntVec3.Invalid;
        private Vector3 cachedDrawPos;
        private Vector3 cachedForwardVector;
        private bool cachedIsSplitter;
        private bool hasAdjacentHopper;
        private float cachedYOffset;
        private Rot4 cachedInputDir;
        private bool cachedNextIsTurn;

        protected ConveyorExtension Props
        {
            get
            {
                if (cachedProps == null)
                {
                    cachedProps = def.GetModExtension<ConveyorExtension>();
                }
                return cachedProps;
            }
        }

        public StorageSettings GetStoreSettings()
        {
            if (storageSettings == null)
            {
                storageSettings = new StorageSettings(this);
                storageSettings.CopyFrom(StorageSettings.EverStorableFixedSettings());
            }
            return storageSettings;
        }

        public StorageSettings GetParentStoreSettings()
        {
            return StorageSettings.EverStorableFixedSettings();
        }

        public bool StorageTabVisible => IsSplitter;

        public void Notify_SettingsChanged()
        {
            cachedGraphic = null;
            if (cachedMap?.mapDrawer != null)
            {
                cachedMap.mapDrawer.MapMeshDirty(cachedPos, MapMeshFlagDefOf.Things);
            }
        }

        private void SetState(ConveyorState newState)
        {
            state = newState;
        }

        public IntVec3 ForwardCell
        {
            get
            {
                if (cachedForwardCell.IsValid) return cachedForwardCell;

                cachedForwardCell = ComputeForwardCell();
                cachedForwardVector = cachedForwardCell.ToVector3Shifted();

                return cachedForwardCell;
            }
        }

        private IntVec3 ComputeForwardCell()
        {
            if (isRecaching) return cachedPos + Rotation.FacingCell;

            if (cachedIsSplitter && cachedSelectedOutput.IsValid)
            {
                return cachedPos + cachedSelectedOutput.FacingCell;
            }

            if (cachedOutputCount == null)
            {
                cachedOutputCount = CountOutputs();
            }

            if (OutputCount == 1 && cachedSingleOutput.IsValid)
            {
                return cachedPos + cachedSingleOutput.FacingCell;
            }

            return cachedPos + Rotation.FacingCell;
        }

        public bool IsTurn
        {
            get
            {
                if (cachedIsTurn == null)
                {
                    if (isRecaching) return false;
                    cachedIsTurn = DetectTurn();
                }
                return cachedIsTurn.Value;
            }
        }

        public int InputCount
        {
            get
            {
                if (cachedInputCount == null)
                {
                    if (isRecaching) return 0;
                    cachedInputCount = CountInputs();
                }
                return cachedInputCount.Value;
            }
        }

        public int OutputCount
        {
            get
            {
                if (cachedOutputCount == null)
                {
                    if (isRecaching) return 0;
                    cachedOutputCount = CountOutputs();
                }
                return cachedOutputCount.Value;
            }
        }

        public bool IsSplitter
        {
            get
            {
                if (cachedOutputCount == null)
                {
                    if (isRecaching) return false;
                    cachedOutputCount = CountOutputs();
                }
                return cachedIsSplitter;
            }
        }
        public bool IsMerger => InputCount > 1 && !IsTurn;

        private Building GetCachedForwardBuilding()
        {
            IntVec3 targetPos = ForwardCell;
            if (cachedForwardBuildingPos == targetPos)
            {
                return cachedForwardBuilding;
            }

            if (!targetPos.InBounds(cachedMap))
            {
                cachedForwardBuilding = null;
                cachedForwardBuildingPos = targetPos;
                return null;
            }

            cachedForwardBuilding = targetPos.GetFirstBuilding(cachedMap);
            cachedForwardBuildingPos = targetPos;
            return cachedForwardBuilding;
        }

        private Building GetCachedNeighborBuilding(Rot4 direction)
        {
            if (cachedNeighborBuildings == null)
            {
                cachedNeighborBuildings = new Building[4];
                cachedNeighborPositions = new IntVec3[4] { IntVec3.Invalid, IntVec3.Invalid, IntVec3.Invalid, IntVec3.Invalid };
            }

            int idx = direction.AsInt;
            IntVec3 neighborPos = cachedPos + direction.FacingCell;

            if (cachedNeighborPositions[idx] == neighborPos)
            {
                Building cached = cachedNeighborBuildings[idx];
                if (cached == null || (!cached.DestroyedOrNull() && cached.Position == neighborPos))
                {
                    return cached;
                }
            }

            if (!neighborPos.InBounds(cachedMap))
            {
                cachedNeighborBuildings[idx] = null;
                cachedNeighborPositions[idx] = neighborPos;
                return null;
            }

            Building neighbor = neighborPos.GetFirstBuilding(cachedMap);
            cachedNeighborBuildings[idx] = neighbor;
            cachedNeighborPositions[idx] = neighborPos;
            return neighbor;
        }

        private bool HasRefuelableTarget(out CompRefuelable refuelComp)
        {
            if (cachedHasRefuelableTarget == null)
            {
                RebuildRefuelableCache();
            }
            refuelComp = cachedRefuelComp;
            return cachedHasRefuelableTarget.Value;
        }

        private void RebuildRefuelableCache()
        {
            var target = GetCachedForwardBuilding();
            cachedRefuelComp = target?.GetComp<CompRefuelable>();
            cachedHasRefuelableTarget = cachedRefuelComp != null;
            cachedShowItems = !cachedHasRefuelableTarget.Value;
        }

        private void InvalidateCache()
        {
            cachedIsTurn = null;
            cachedInputCount = null;
            cachedOutputCount = null;
            cachedSingleOutput = Rot4.Invalid;
            cachedGraphic = null;
            cachedIsSingleDirectional = false;
            cachedHasRefuelableTarget = null;
            cachedRefuelComp = null;
            isDrawnDynamically = false;
            cachedIsSplitter = false;
            cachedForwardCell = IntVec3.Invalid;
            lastCachedProgress = float.MinValue;
            cachedNextIsTurn = false;

            cachedForwardBuilding = null;
            cachedForwardBuildingPos = IntVec3.Invalid;

            if (cachedNeighborPositions != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    cachedNeighborPositions[i] = IntVec3.Invalid;
                    cachedNeighborBuildings[i] = null;
                }
            }

            RefreshDrawCache();
        }

        private void PeriodicCacheCheck()
        {
            bool changed = false;

            IntVec3 targetPos = ForwardCell;
            if (targetPos.InBounds(cachedMap))
            {
                Building currentForward = targetPos.GetFirstBuilding(cachedMap);
                if (cachedForwardBuildingPos == targetPos)
                {
                    if (currentForward != cachedForwardBuilding) changed = true;
                }
                else
                {
                    cachedForwardBuildingPos = targetPos;
                    cachedForwardBuilding = currentForward;
                }
            }

            bool foundHopper = false;
            for (int i = 0; i < 4; i++)
            {
                IntVec3 neighborPos = cachedPos + GenAdj.CardinalDirections[i];
                if (!neighborPos.InBounds(cachedMap)) continue;

                var currentNeighbor = neighborPos.GetFirstBuilding(cachedMap);
                if (currentNeighbor is Building_FactoryHopper) foundHopper = true;

                if (!changed)
                {
                    if (cachedNeighborPositions == null)
                    {
                        cachedNeighborBuildings = new Building[4];
                        cachedNeighborPositions = new IntVec3[] { IntVec3.Invalid, IntVec3.Invalid, IntVec3.Invalid, IntVec3.Invalid };
                    }

                    if (cachedNeighborPositions[i] == neighborPos)
                    {
                        if (currentNeighbor != cachedNeighborBuildings[i])
                        {
                            changed = true;
                        }
                    }
                    else
                    {
                        cachedNeighborPositions[i] = neighborPos;
                        cachedNeighborBuildings[i] = currentNeighbor;
                    }
                }
            }

            hasAdjacentHopper = foundHopper;

            if (changed)
            {
                InvalidateCache();
            }
        }

        private void InvalidateNeighborCaches()
        {
            for (int i = 0; i < 4; i++)
            {
                IntVec3 adj = cachedPos + GenAdj.CardinalDirections[i];
                if (adj.InBounds(cachedMap))
                {
                    Building adjacentBuilding = adj.GetFirstBuilding(cachedMap);
                    if (adjacentBuilding is Building_Conveyor neighbor)
                    {
                        neighbor.InvalidateCache();
                        cachedMap.mapDrawer.MapMeshDirty(neighbor.Position, MapMeshFlagDefOf.Things);
                    }
                }
            }
        }

        private bool CanStackWithAny(List<Thing> carried, List<Thing> target)
        {
            int carriedCount = carried.Count;
            int targetCount = target.Count;
            if (targetCount == 0) return false;

            for (int i = 0; i < carriedCount; i++)
            {
                Thing cThing = carried[i];
                for (int j = 0; j < targetCount; j++)
                {
                    Thing tThing = target[j];
                    if (tThing.stackCount < tThing.def.stackLimit && cThing.CanStackWith(tThing))
                        return true;
                }
            }
            return false;
        }

        private bool TryFindInputConveyor(out Rot4 inputDir)
        {
            var dirs = PossibleInputDirections();
            for (int i = 0; i < dirs.Length; i++)
            {
                Building neighbor = GetCachedNeighborBuilding(dirs[i]);
                if (neighbor is Building_Conveyor neighborComp && neighborComp.ForwardCell == cachedPos)
                {
                    inputDir = dirs[i];
                    return true;
                }
            }
            inputDir = Rot4.Invalid;
            return false;
        }

        private Vector2 GetLabelScreenPos(Vector3 worldPos)
        {
            Vector3 labelWorldPos = worldPos;
            labelWorldPos.z += -0.4f;
            Vector2 labelScreenPos = Find.Camera.WorldToScreenPoint(labelWorldPos) / Prefs.UIScale;
            labelScreenPos.y = (float)UI.screenHeight - labelScreenPos.y;
            return labelScreenPos;
        }

        private void SetDirection(Rot4 newRotation)
        {
            Rotation = newRotation;
            InvalidateCache();
            InvalidateNeighborCaches();
        }

        private void ResetConveyorState(bool fullReset = true)
        {
            state = ConveyorState.Empty;
            itemProgress = 0f;
            cachedSelectedOutput = Rot4.Invalid;
            cachedForwardCell = IntVec3.Invalid;
            if (fullReset)
            {
                lastItemProgress = 0f;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            cachedMap = map;
            cachedPos = Position;
            cachedDrawPos = this.DrawPos;
            cachedItemsPerCell = Props.itemsPerCell;
            InvalidateCache();
            InvalidateNeighborCaches();
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Spawned)
            {
                InvalidateNeighborCaches();
                var items = innerContainer.InnerListForReading;
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    GenSpawn.Spawn(items[i], cachedPos, cachedMap);
                }
                innerContainer.Clear();
                base.DeSpawn(mode);
                cachedMap.mapDrawer.MapMeshDirty(cachedPos, MapMeshFlagDefOf.Things);
                cachedMap = null;
            }
            else
            {
                base.DeSpawn(mode);
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (!dirtied)
            {
                InvalidateCache();
                dirtied = true;
            }

            if (innerContainer.Count == 0 && state == ConveyorState.Waiting)
                return;

            int ticksGame = Find.TickManager.TicksGame;

            if ((ticksGame + thingIDNumber) % 60 == 0)
            {
                PeriodicCacheCheck();
            }

            if ((ticksGame + thingIDNumber) % 10 == 0)
            {
                CheckForItemsOnCell();
                CheckRefuelableTarget();
            }

            if (innerContainer.Count > 0 && state == ConveyorState.Moving)
            {
                if (itemProgress > 0.8f || (ticksGame + thingIDNumber) % 5 == 0)
                {
                    var reason = CanMoveDownstream();
                    if (reason != DownstreamBlockReason.None)
                    {
                        SetState(ConveyorState.Waiting);
                        return;
                    }
                }

                itemProgress += 1f / Props.ticksPerCell;

                if (itemProgress >= 0.999f)
                    CompleteTransfer();
            }
            else
            {
                ProcessMovement();
            }
        }

        private DownstreamBlockReason CanMoveDownstream()
        {
            IntVec3 targetPos = ForwardCell;
            if (!targetPos.InBounds(cachedMap))
                return DownstreamBlockReason.TargetOutOfBounds;

            var targetBuilding = GetCachedForwardBuilding();

            if (targetBuilding is Building_Conveyor targetConveyor)
            {
                if (!CanTransferToTarget(targetConveyor))
                {
                    if (targetConveyor.state == ConveyorState.Moving)
                    {
                        return DownstreamBlockReason.TargetMoving;
                    }
                    return DownstreamBlockReason.TargetFull;
                }

                float aheadProgress = float.MinValue;
                bool tyingCompetitor = false;

                bool canStack = targetConveyor.innerContainer.Count > 0 && CanStackWithAny(innerContainer.InnerListForReading, targetConveyor.innerContainer.InnerListForReading);

                if (targetConveyor.InputCount > 1)
                {
                    for (int i = 0; i < CanonicalOrder.Length; i++)
                    {
                        Building adjBuilding = targetConveyor.GetCachedNeighborBuilding(CanonicalOrder[i]);

                        if (adjBuilding is Building_Conveyor adjConveyor
                            && adjConveyor != this
                            && adjConveyor.ForwardCell == targetPos
                            && adjConveyor.innerContainer.Count > 0)
                        {
                            if (adjConveyor.itemProgress > aheadProgress)
                            {
                                aheadProgress = adjConveyor.itemProgress;
                            }

                            if (!canStack)
                            {
                                if (adjConveyor.itemProgress > itemProgress ||
                                    (adjConveyor.itemProgress == itemProgress &&
                                     adjConveyor.Position.GetHashCode() > cachedPos.GetHashCode()))
                                {
                                    tyingCompetitor = true;
                                }
                            }
                        }
                    }
                }

                if (aheadProgress != float.MinValue && aheadProgress > itemProgress + 0.1f)
                {
                    if (!canStack)
                        return DownstreamBlockReason.CompetitorAhead;
                }

                if (tyingCompetitor)
                {
                    return DownstreamBlockReason.CompetitorTying;
                }

                return DownstreamBlockReason.None;
            }
            else if (targetBuilding is Building_FactoryHopper hopper)
            {
                if (!CanTransferToHopper(hopper))
                    return DownstreamBlockReason.TargetFull;

                return DownstreamBlockReason.None;
            }
            else if (HasRefuelableTarget(out var refuelComp))
            {
                if (refuelComp.IsFull)
                    return DownstreamBlockReason.TargetFull;
                return DownstreamBlockReason.None;
            }
            else
            {
                if (GetDumpCapability(targetPos) == DumpCapability.CannotDump)
                    return DownstreamBlockReason.CellFull;

                return DownstreamBlockReason.None;
            }
        }

        private bool CanTransferToHopper(Building_FactoryHopper hopper)
        {
            var items = innerContainer.InnerListForReading;
            for (int i = 0; i < items.Count; i++)
            {
                if (!hopper.slotGroup.Settings.AllowedToAccept(items[i]))
                    return false;
            }

            bool canStack = false;
            bool hasRoom = false;

            List<Thing> thingsInCell = hopper.Map.thingGrid.ThingsListAt(hopper.Position);

            for (int t = 0; t < thingsInCell.Count; t++)
            {
                Thing existing = thingsInCell[t];
                if (existing.def.category != ThingCategory.Item) continue;

                for (int i = 0; i < items.Count; i++)
                {
                    Thing carried = items[i];
                    if (carried.def == existing.def && carried.CanStackWith(existing))
                    {
                        canStack = true;
                        if (existing.def.stackLimit > existing.stackCount)
                        {
                            hasRoom = true;
                            break;
                        }
                    }
                }
                if (hasRoom) break;
            }

            return canStack && hasRoom || !canStack;
        }

        private DumpCapability GetDumpCapability(IntVec3 cell)
        {
            if (!cell.InBounds(cachedMap))
            {
                return DumpCapability.CannotDump;
            }

            Building edifice = cell.GetEdifice(cachedMap);

            if (edifice is Building_FactoryHopper hopper)
            {
                bool canDumpAny = CanTransferToHopper(hopper);
                if (!canDumpAny) return DumpCapability.CannotDump;

                int heldCount = 0;
                List<Thing> thingsInCell = hopper.Map.thingGrid.ThingsListAt(hopper.Position);
                for (int t = 0; t < thingsInCell.Count; t++)
                {
                    if (thingsInCell[t].def.category == ThingCategory.Item) heldCount++;
                }

                int allowedDefCount = hopper.slotGroup.Settings.filter.AllowedDefCount;

                var items = innerContainer.InnerListForReading;
                for (int i = 0; i < items.Count; i++)
                {
                    bool canFitAll = false;
                    var thing = items[i];
                    for (int t = 0; t < thingsInCell.Count; t++)
                    {
                        Thing existing = thingsInCell[t];
                        if (existing.CanStackWith(thing) && (existing.def.stackLimit - existing.stackCount) >= thing.stackCount)
                        {
                            canFitAll = true;
                            break;
                        }
                    }
                    if (!canFitAll && heldCount >= allowedDefCount)
                        return DumpCapability.CanDumpPartial;
                }
                return DumpCapability.CanDump;
            }

            if (edifice != null)
            {
                var refuelComp = edifice.GetComp<CompRefuelable>();
                if (refuelComp != null)
                {
                    if (refuelComp.IsFull) return DumpCapability.CannotDump;

                    int total = 0;
                    var items = innerContainer.InnerListForReading;
                    for (int i = 0; i < items.Count; i++) total += items[i].stackCount;
                    return refuelComp.GetFuelCountToFullyRefuel() >= total ? DumpCapability.CanDump : DumpCapability.CanDumpPartial;
                }
            }

            if (!cell.Walkable(cachedMap))
            {
                return DumpCapability.CannotDump;
            }

            List<Thing> thingList = cachedMap.thingGrid.ThingsListAt(cell);
            int maxItems = cell.GetMaxItemsAllowedInCell(cachedMap);
            int currentItemCount = 0;
            bool canStack = false;
            bool hasRoom = false;

            var currentItems = innerContainer.InnerListForReading;

            for (int i = 0; i < thingList.Count; i++)
            {
                Thing existing = thingList[i];
                if (existing.def.category == ThingCategory.Item)
                {
                    currentItemCount++;
                    for (int j = 0; j < currentItems.Count; j++)
                    {
                        var carried = currentItems[j];
                        if (carried.CanStackWith(existing))
                        {
                            canStack = true;
                            if (existing.stackCount < existing.def.stackLimit) hasRoom = true;
                        }
                    }
                }
            }

            bool hasEmptySpace = currentItemCount < maxItems;
            if (!canStack && currentItemCount >= maxItems) return DumpCapability.CannotDump;
            if (canStack && !hasRoom && currentItemCount >= maxItems) return DumpCapability.CannotDump;

            if (edifice is IHaulDestination dest)
            {
                for (int i = 0; i < currentItems.Count; i++)
                {
                    if (!dest.Accepts(currentItems[i]))
                    {
                        return DumpCapability.CannotDump;
                    }
                }
            }

            for (int i = 0; i < currentItems.Count; i++)
            {
                bool canFitAll = false;
                var thing = currentItems[i];
                for (int j = 0; j < thingList.Count; j++)
                {
                    var existing = thingList[j];
                    if (existing.def.category == ThingCategory.Item && existing.CanStackWith(thing))
                    {
                        if ((existing.def.stackLimit - existing.stackCount) >= thing.stackCount)
                        {
                            canFitAll = true;
                            break;
                        }
                    }
                }
                if (!canFitAll && !hasEmptySpace) return DumpCapability.CanDumpPartial;
            }

            return DumpCapability.CanDump;
        }

        private void ProcessMovement()
        {
            if (hasAdjacentHopper && HasSpace() && state != ConveyorState.Waiting)
                TryPullItem();

            if (innerContainer.Count > 0 && state != ConveyorState.Moving)
            {
                IntVec3 targetPos = ForwardCell;
                if (!targetPos.InBounds(cachedMap))
                {
                    SetState(ConveyorState.Waiting);
                    return;
                }

                var targetBuilding = GetCachedForwardBuilding();
                if (targetBuilding is Building_Conveyor targetConveyor)
                {
                    var downstreamReason = CanMoveDownstream();

                    if (IsSplitter && downstreamReason != DownstreamBlockReason.None && itemProgress <= 0.2f)
                    {
                        var dirs = PossibleOutputDirections();
                        for (int i = 0; i < dirs.Length; i++)
                        {
                            if (cachedPos + dirs[i].FacingCell == targetPos) continue;
                            if (!IsValidOutput(dirs[i])) continue;

                            Rot4 oldOutput = cachedSelectedOutput;
                            cachedSelectedOutput = dirs[i];
                            cachedForwardCell = IntVec3.Invalid;

                            if (CanMoveDownstream() == DownstreamBlockReason.None)
                            {
                                SetState(ConveyorState.Moving);
                                itemProgress += (1f / Props.ticksPerCell);
                                lastItemProgress = itemProgress;
                                return;
                            }

                            cachedSelectedOutput = oldOutput;
                            cachedForwardCell = IntVec3.Invalid;
                        }
                    }

                    if (downstreamReason == DownstreamBlockReason.None)
                    {
                        InitiateTransfer();
                        itemProgress += 1f / Props.ticksPerCell;
                        lastItemProgress = itemProgress;
                    }
                    else
                    {
                        if (downstreamReason == DownstreamBlockReason.TargetFull &&
                            targetConveyor.itemProgress <= 0.05f &&
                            CanStackWithAny(innerContainer.InnerListForReading, targetConveyor.innerContainer.InnerListForReading))
                        {
                            CompleteTransfer();
                        }
                        SetState(ConveyorState.Waiting);
                    }
                }
                else
                {
                    TryDumpItem(targetPos);
                }
            }
        }

        private void TryPullItem()
        {
            if (TryPullFromDir(Rotation.Opposite)) return;
            if (TryPullFromDir(Rotation.Rotated(RotationDirection.Clockwise))) return;
            TryPullFromDir(Rotation.Rotated(RotationDirection.Counterclockwise));
        }

        private bool TryPullFromDir(Rot4 dir)
        {
            if (GetCachedNeighborBuilding(dir) is Building_FactoryHopper hopper)
            {
                Thing item = null;
                foreach (Thing t in hopper.slotGroup.HeldThings)
                {
                    item = t;
                    break;
                }

                if (item != null)
                {
                    Thing taken = item.SplitOff(item.stackCount);
                    if (taken.Spawned) taken.DeSpawn(DestroyMode.Vanish);
                    innerContainer.TryAdd(taken);
                    state = ConveyorState.Moving;
                    itemProgress = -1f;
                    lastItemProgress = -1f;
                    return true;
                }
            }
            return false;
        }

        private void CheckForItemsOnCell()
        {
            if (!HasSpace())
            {
                return;
            }

            List<Thing> thingsOnCell = cachedMap.thingGrid.ThingsListAt(cachedPos);
            for (int i = thingsOnCell.Count - 1; i >= 0; i--)
            {
                Thing thing = thingsOnCell[i];
                if (thing.def.category != ThingCategory.Item)
                {
                    continue;
                }

                if (innerContainer.Contains(thing))
                {
                    continue;
                }

                if (CanAcceptItem(thing))
                {
                    if (thing.Spawned)
                    {
                        thing.DeSpawn(DestroyMode.Vanish);
                    }

                    innerContainer.TryAdd(thing);
                    ResetConveyorState();
                }
            }
        }

        private void CheckRefuelableTarget()
        {
            if (cachedGraphic != null && HasRefuelableTarget(out var refuelComp))
            {
                if (refuelComp.parent.DestroyedOrNull())
                {
                    InvalidateCache();
                }
            }
        }

        private bool CanTransferToTarget(Building_Conveyor target)
        {
            if (target.innerContainer.Count == 0 && target.HasSpace())
                return true;

            if (itemProgress < target.itemProgress)
                return true;

            if (target.itemProgress <= 0.05f && (target.state == ConveyorState.Waiting || target.state == ConveyorState.Empty))
            {
                bool fullyFits = true;
                var myItems = innerContainer.InnerListForReading;
                var targetItems = target.innerContainer.InnerListForReading;

                for (int i = 0; i < myItems.Count; i++)
                {
                    Thing thing = myItems[i];
                    bool thingFits = false;
                    for (int j = 0; j < targetItems.Count; j++)
                    {
                        Thing targetThing = targetItems[j];
                        if (thing.CanStackWith(targetThing) && (targetThing.def.stackLimit - targetThing.stackCount) >= thing.stackCount)
                        {
                            thingFits = true;
                            break;
                        }
                    }
                    if (!thingFits && !target.HasSpace())
                    {
                        fullyFits = false;
                        break;
                    }
                }

                if (fullyFits) return true;
            }

            return false;
        }

        private void InitiateTransfer()
        {
            if (IsSplitter)
            {
                SelectNextOutput();
            }
            SetState(ConveyorState.Moving);
        }

        private void CompleteTransfer()
        {
            if (innerContainer.Count == 0) return;

            IntVec3 targetPos = ForwardCell;
            var targetBuilding = GetCachedForwardBuilding();

            if (HasRefuelableTarget(out var refuelComp))
            {
                if (refuelComp.IsFull) return;
                int fuelNeeded = refuelComp.GetFuelCountToFullyRefuel();

                transferred.Clear();
                var items = innerContainer.InnerListForReading;

                for (int i = items.Count - 1; i >= 0; i--)
                {
                    Thing thing = items[i];
                    if (refuelComp.Props.fuelFilter.Allows(thing))
                    {
                        int amountAvailable = thing.stackCount;
                        int amountToRefuel = Mathf.Min(fuelNeeded, amountAvailable);

                        thing.stackCount -= amountToRefuel;
                        refuelComp.Refuel(amountToRefuel);
                        fuelNeeded -= amountToRefuel;

                        if (thing.stackCount <= 0)
                        {
                            transferred.Add(thing);
                        }

                        if (fuelNeeded <= 0)
                        {
                            break;
                        }
                    }
                }

                for (int i = transferred.Count - 1; i >= 0; i--)
                {
                    innerContainer.Remove(transferred[i]);
                }

                if (innerContainer.Count == 0)
                {
                    ResetConveyorState();
                }
                else
                {
                    SetState(ConveyorState.Waiting);
                    cachedSelectedOutput = Rot4.Invalid;
                    cachedForwardCell = IntVec3.Invalid;
                }
                return;
            }

            if (targetBuilding is Building_Conveyor targetConveyor)
            {
                var items = innerContainer.InnerListForReading;

                for (int i = items.Count - 1; i >= 0; i--)
                {
                    Thing thing = items[i];
                    Selector_Deselect_Patch.transferringItems.Add(thing);

                    TransferItemTo(targetConveyor, thing);

                    Selector_Deselect_Patch.transferringItems.Remove(thing);
                }

                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if (items[i].stackCount <= 0)
                        items.RemoveAt(i);
                }

                if (innerContainer.Count == 0)
                {
                    SetState(ConveyorState.Empty);
                    itemProgress = 0f;
                    lastItemProgress = 0f;
                    cachedSelectedOutput = Rot4.Invalid;
                    cachedForwardCell = IntVec3.Invalid;
                }
                else
                {
                    SetState(ConveyorState.Waiting);
                }
                if (targetConveyor.state == ConveyorState.Empty && targetConveyor.innerContainer.Count > 0)
                {
                    targetConveyor.SetState(ConveyorState.Waiting);
                }
            }
            else if (targetBuilding is Building_FactoryHopper hopper)
            {
                transferred.Clear();
                var items = innerContainer.InnerListForReading;

                for (int i = items.Count - 1; i >= 0; i--)
                {
                    Thing thing = items[i];
                    Selector_Deselect_Patch.transferringItems.Add(thing);

                    bool fullyTransferred = false;
                    foreach (Thing targetThing in hopper.slotGroup.HeldThings)
                    {
                        if (thing.CanStackWith(targetThing))
                        {
                            int spaceLeft = targetThing.def.stackLimit - targetThing.stackCount;
                            if (spaceLeft > 0)
                            {
                                int toTransfer = Mathf.Min(thing.stackCount, spaceLeft);
                                targetThing.stackCount += toTransfer;
                                thing.stackCount -= toTransfer;

                                if (thing.stackCount <= 0)
                                {
                                    transferred.Add(thing);
                                    fullyTransferred = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!fullyTransferred)
                    {
                        if (hopper.slotGroup.Settings.AllowedToAccept(thing))
                        {
                            if (GenPlace.TryPlaceThing(thing, hopper.Position, cachedMap, ThingPlaceMode.Direct))
                            {
                                transferred.Add(thing);
                            }
                        }
                    }
                }

                for (int i = transferred.Count - 1; i >= 0; i--)
                {
                    innerContainer.Remove(transferred[i]);
                    Selector_Deselect_Patch.transferringItems.Remove(transferred[i]);
                }

                if (innerContainer.Count == 0)
                {
                    ResetConveyorState();
                }
                else
                {
                    SetState(ConveyorState.Waiting);
                    cachedSelectedOutput = Rot4.Invalid;
                    cachedForwardCell = IntVec3.Invalid;
                }
            }
            else
            {
                transferred.Clear();
                var items = innerContainer.InnerListForReading;

                for (int i = items.Count - 1; i >= 0; i--)
                {
                    Thing item = items[i];
                    Selector_Deselect_Patch.transferringItems.Add(item);

                    bool fullyMerged = false;
                    List<Thing> thingList = cachedMap.thingGrid.ThingsListAt(targetPos);

                    for (int j = 0; j < thingList.Count; j++)
                    {
                        Thing existing = thingList[j];
                        if (existing.def.category == ThingCategory.Item && item.CanStackWith(existing))
                        {
                            int spaceLeft = existing.def.stackLimit - existing.stackCount;
                            if (spaceLeft > 0)
                            {
                                int toTransfer = Mathf.Min(item.stackCount, spaceLeft);
                                existing.stackCount += toTransfer;
                                item.stackCount -= toTransfer;

                                if (item.stackCount <= 0)
                                {
                                    transferred.Add(item);
                                    fullyMerged = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!fullyMerged)
                    {
                        int currentItems = 0;
                        for (int k = 0; k < thingList.Count; k++)
                            if (thingList[k].def.category == ThingCategory.Item) currentItems++;
                        if (currentItems < targetPos.GetMaxItemsAllowedInCell(cachedMap))
                        {
                            if (GenPlace.TryPlaceThing(item, targetPos, cachedMap, ThingPlaceMode.Direct))
                            {
                                transferred.Add(item);
                            }
                        }
                    }
                }

                for (int i = transferred.Count - 1; i >= 0; i--)
                {
                    innerContainer.Remove(transferred[i]);
                    Selector_Deselect_Patch.transferringItems.Remove(transferred[i]);
                }

                if (innerContainer.Count == 0)
                {
                    ResetConveyorState();
                }
                else
                {
                    SetState(ConveyorState.Waiting);
                    cachedSelectedOutput = Rot4.Invalid;
                    cachedForwardCell = IntVec3.Invalid;
                }
            }
        }

        private void TryDumpItem(IntVec3 targetPos)
        {
            var capability = GetDumpCapability(targetPos);
            if (capability == DumpCapability.CannotDump)
            {
                SetState(ConveyorState.Waiting);
                return;
            }

            if (itemProgress >= 0.999f && innerContainer.Count > 0)
            {
                CompleteTransfer();
            }
            else if (capability == DumpCapability.CanDumpPartial)
            {
                CompleteTransfer();
            }
            else
            {
                InitiateTransfer();
            }
        }

        public bool HasSpace()
        {
            return innerContainer.Count < cachedItemsPerCell;
        }

        public bool CanAcceptItem(Thing item)
        {
            if (state == ConveyorState.Moving)
                return false;

            if (HasSpace())
                return true;

            var items = innerContainer.InnerListForReading;
            for (int i = 0; i < items.Count; i++)
            {
                var existing = items[i];
                if (item.CanStackWith(existing))
                {
                    int spaceLeft = existing.def.stackLimit - existing.stackCount;
                    if (spaceLeft > 0)
                        return true;
                }
            }

            return false;
        }

        public bool TryAdd(Thing thing)
        {
            if (thing == null)
            {
                return false;
            }

            var items = innerContainer.InnerListForReading;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                Thing existing = items[i];
                if (thing.CanStackWith(existing))
                {
                    int spaceLeft = existing.def.stackLimit - existing.stackCount;
                    if (spaceLeft > 0)
                    {
                        int toMerge = Mathf.Min(thing.stackCount, spaceLeft);
                        existing.stackCount += toMerge;
                        thing.stackCount -= toMerge;

                        if (thing.stackCount <= 0 && thing.Spawned)
                        {
                            thing.DeSpawn(DestroyMode.Vanish);
                        }
                        ResetConveyorState();
                        return true;
                    }
                }
            }

            if (HasSpace())
            {
                if (thing.Spawned)
                {
                    thing.DeSpawn(DestroyMode.Vanish);
                }

                innerContainer.TryAdd(thing);
                ResetConveyorState();
                return true;
            }

            return false;
        }

        private bool TransferItemTo(Building_Conveyor targetConveyor, Thing thing)
        {
            var targetItems = targetConveyor.innerContainer.InnerListForReading;
            for (int i = 0; i < targetItems.Count; i++)
            {
                Thing targetThing = targetItems[i];
                if (targetThing.CanStackWith(thing))
                {
                    int spaceLeft = targetThing.def.stackLimit - targetThing.stackCount;
                    if (spaceLeft > 0)
                    {
                        int amount = Mathf.Min(thing.stackCount, spaceLeft);
                        targetThing.stackCount += amount;
                        thing.stackCount -= amount;
                        if (thing.stackCount <= 0)
                            return true;
                    }
                }
            }

            if (thing.stackCount > 0 && targetConveyor.HasSpace())
            {
                thing.holdingOwner = null;
                innerContainer.InnerListForReading.Remove(thing);
                thing.holdingOwner = targetConveyor.innerContainer;
                targetConveyor.innerContainer.InnerListForReading.Add(thing);
                return true;
            }

            return false;
        }

        private bool DetectTurn()
        {
            if (InputCount > 1) return false;

            if (OutputCount > 1) return false;

            if (OutputCount == 1 && cachedSingleOutput.IsValid && cachedSingleOutput != Rotation)
            {
                return true;
            }

            if (TryFindInputConveyor(out var inputDir))
            {
                Rot4 outputDir = Rot4.FromIntVec3(ForwardCell - cachedPos);
                if (inputDir != outputDir && inputDir != outputDir.Opposite)
                {
                    return true;
                }
            }

            return false;
        }

        private int CountInputs()
        {
            isRecaching = true;
            int count = 0;
            var dirs = PossibleInputDirections();
            for (int i = 0; i < dirs.Length; i++)
            {
                Building neighbor = GetCachedNeighborBuilding(dirs[i]);
                if (neighbor is Building_Conveyor neighborComp && neighborComp.ForwardCell == cachedPos)
                    count++;
            }
            isRecaching = false;
            return count;
        }

        public virtual Rot4[] PossibleInputDirections() => InputDirectionsByRot[Rotation.AsInt];

        public Rot4[] PossibleOutputDirections() => OutputDirectionsByRot[Rotation.AsInt];

        public bool CanAcceptFrom(IntVec3 fromPos)
        {
            var dirs = PossibleInputDirections();
            for (int i = 0; i < dirs.Length; i++)
            {
                if (cachedPos + dirs[i].FacingCell == fromPos)
                    return true;
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Values.Look(ref itemProgress, "itemProgress", 0f);
            Scribe_Values.Look(ref lastItemProgress, "lastItemProgress", 0f);
            Scribe_Values.Look(ref state, "state", ConveyorState.Empty);
            Scribe_Values.Look(ref splitterOutputIndex, "splitterOutputIndex", 0);
            Scribe_Deep.Look(ref storageSettings, "storageSettings", this);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (innerContainer == null)
                {
                    innerContainer = new ThingOwner<Thing>(this);
                }
                if (storageSettings == null)
                {
                    storageSettings = new StorageSettings(this);
                    storageSettings.CopyFrom(StorageSettings.EverStorableFixedSettings());
                }
            }
        }

        public void ApplyEnvironmentalEffects()
        {
            if (innerContainer.Count == 0 || !Spawned) return;

            bool roofed = cachedPos.Roofed(cachedMap);
            bool roomUsesOutdoorTemp = cachedPos.GetRoom(cachedMap)?.UsesOutdoorTemperature ?? false;
            TerrainDef terrain = cachedPos.GetTerrain(cachedMap);

            var items = innerContainer.InnerListForReading;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                Thing t = items[i];
                float rate = SteadyEnvironmentEffects.FinalDeteriorationRate(t, roofed, roomUsesOutdoorTemp, terrain);

                if (rate > 0.001f && Rand.Chance(rate / 36f))
                {
                    t.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, 1f));

                    if (t.Destroyed)
                    {
                        if (innerContainer.Count == 0)
                        {
                            ResetConveyorState();
                        }
                    }
                }
            }

            if (!roofed)
            {
                conditions.Clear();
                cachedMap.gameConditionManager.GetAllGameConditionsAffectingMap(cachedMap, conditions);

                for (int i = 0; i < conditions.Count; i++)
                {
                    if (conditions[i] is GameCondition_ToxicFallout)
                    {
                        ApplyToxicFallout();
                    }
                }
            }
        }

        private void ApplyToxicFallout()
        {
            var items = innerContainer.InnerListForReading;
            for (int i = 0; i < items.Count; i++)
            {
                Thing t = items[i];
                if (t.def.category == ThingCategory.Item)
                {
                    CompRottable comp = t.TryGetComp<CompRottable>();
                    if (comp != null && (int)comp.Stage < 2)
                    {
                        comp.RotProgress += 3000f;
                    }
                }
            }
        }

        public override Graphic Graphic
        {
            get
            {
                if (cachedGraphic == null)
                {
                    cachedGraphic = DetermineGraphic();
                }
                return cachedGraphic;
            }
        }

        private Graphic DetermineGraphic()
        {
            if (string.IsNullOrEmpty(Props.baseTexPath))
                return base.Graphic;

            string texturePath;
            bool useSingleGraphic = false;

            if (IsSplitter)
            {
                if (HasFilterRestrictions())
                {
                    texturePath = $"{Props.baseTexPath}/ConveyorFilter";
                    useSingleGraphic = false;
                }
                else
                {
                    var outputs = new List<Rot4>(4);

                    for (int i = 0; i < CanonicalOrder.Length; i++)
                    {
                        if (IsValidOutput(CanonicalOrder[i]))
                            outputs.Add(CanonicalOrder[i]);
                    }

                    if (outputs.Count == 2)
                    {
                        string outputsPrefix = outputs[0].ToStringWord() + outputs[1].ToStringWord();
                        string rotationSuffix = Rotation.ToStringWord().ToLower();
                        texturePath = $"{Props.baseTexPath}/ConveyorSplitter_{outputsPrefix}_{rotationSuffix}";
                        useSingleGraphic = true;
                    }
                    else
                    {
                        texturePath = $"{Props.baseTexPath}/ConveyorSplitter";
                        useSingleGraphic = false;
                    }
                }
            }
            else if (IsMerger)
            {
                var inputs = new List<Rot4>(4);

                for (int i = 0; i < CanonicalOrder.Length; i++)
                {
                    var n = GetCachedNeighborBuilding(CanonicalOrder[i]) as Building_Conveyor;
                    if (n != null && n.ForwardCell == cachedPos)
                        inputs.Add(CanonicalOrder[i]);
                }

                if (inputs.Count == 2)
                {
                    string inputsPrefix = inputs[0].ToStringWord() + inputs[1].ToStringWord();
                    string rotationSuffix = Rotation.ToStringWord().ToLower();
                    texturePath = $"{Props.baseTexPath}/ConveyorMerger_{inputsPrefix}_{rotationSuffix}";
                    useSingleGraphic = true;
                }
                else
                {
                    texturePath = $"{Props.baseTexPath}/ConveyorMerger";
                    useSingleGraphic = false;
                }
            }
            else if (IsTurn)
            {
                texturePath = $"{Props.baseTexPath}/{DetermineTurnGraphic()}";
                useSingleGraphic = true;
            }
            else
            {
                string baseName = HasRefuelableTarget(out var _) ? "ConveyorRefuelingPort" : "Conveyor";
                texturePath = $"{Props.baseTexPath}/{baseName}";
                useSingleGraphic = false;
            }

            cachedIsSingleDirectional = useSingleGraphic;

            if (useSingleGraphic)
            {
                return GraphicDatabase.Get<Graphic_Single>(
                    texturePath,
                    ShaderDatabase.Cutout,
                    def.graphicData.drawSize,
                    Color.white
                );
            }
            else
            {
                return GraphicDatabase.Get<Graphic_Multi>(
                    texturePath,
                    ShaderDatabase.Cutout,
                    def.graphicData.drawSize,
                    Color.white
                );
            }
        }

        private bool HasFilterRestrictions()
        {
            int baselineCount = StorageSettings.EverStorableFixedSettings().filter.AllowedDefCount;
            return GetStoreSettings().filter.AllowedDefCount < baselineCount;
        }

        private string DetermineTurnGraphic()
        {
            string baseName = "Conveyor";
            Rot4 outDir = Rotation;

            if (OutputCount == 1 && cachedSingleOutput.IsValid)
            {
                outDir = cachedSingleOutput;
            }

            Rot4 inDir = Rotation.Opposite;
            if (TryFindInputConveyor(out var inputDir))
            {
                inDir = inputDir;
            }

            Rot4 moveIn = inDir.Opposite;

            if (moveIn != outDir && moveIn != outDir.Opposite)
            {
                int fromRot = moveIn.AsInt;
                int toRot = outDir.AsInt;
                int diff = (toRot - fromRot + 4) % 4;

                if (diff == 1) baseName = "ConveyorTurnRight";
                else if (diff == 3) baseName = "ConveyorTurnLeft";
            }

            return $"{baseName}_{outDir.ToStringWord().ToLower()}";
        }

        private bool IsValidOutput(Rot4 dir)
        {
            Building neighbor = GetCachedNeighborBuilding(dir);
            if (neighbor == null) return false;

            if (neighbor is Building_Conveyor neighborConveyor)
            {
                if (!neighborConveyor.CanAcceptFrom(cachedPos))
                    return false;

                if (neighborConveyor.ForwardCell == cachedPos)
                    return false;

                if (neighborConveyor.Rotation == Rotation || neighborConveyor.Rotation == Rotation.Opposite)
                {
                    if (dir != Rotation)
                    {
                        return false;
                    }
                }

                return true;
            }
            return false;
        }

        private int CountOutputs()
        {
            isRecaching = true;
            int count = 0;
            Rot4 singleOut = Rot4.Invalid;

            var dirs = PossibleOutputDirections();
            for (int i = 0; i < dirs.Length; i++)
            {
                if (IsValidOutput(dirs[i]))
                {
                    count++;
                    singleOut = dirs[i];
                }
            }

            if (count == 1)
                cachedSingleOutput = singleOut;
            else
                cachedSingleOutput = Rot4.Invalid;

            cachedIsSplitter = count > 1;

            isRecaching = false;
            return count;
        }

        private void SelectNextOutput()
        {
            int validCount = 0;
            var dirs = PossibleOutputDirections();
            for (int i = 0; i < dirs.Length; i++)
            {
                if (IsValidOutput(dirs[i]))
                {
                    validOutputs[validCount] = dirs[i];
                    validCount++;
                }
            }

            if (validCount > 0)
            {
                Rot4 forwardDir = Rotation;
                bool allAllowed = true;

                var items = innerContainer.InnerListForReading;
                var storeSettings = GetStoreSettings();

                for (int i = 0; i < items.Count; i++)
                {
                    if (!storeSettings.AllowedToAccept(items[i]))
                    {
                        allAllowed = false;
                        break;
                    }
                }

                bool forwardIsValid = false;
                for (int i = 0; i < validCount; i++)
                {
                    if (validOutputs[i] == forwardDir)
                    {
                        forwardIsValid = true;
                        break;
                    }
                }

                if (allAllowed && forwardIsValid)
                {
                    cachedSelectedOutput = forwardDir;
                    cachedForwardCell = IntVec3.Invalid;
                    return;
                }
                else if (!allAllowed && forwardIsValid && validCount > 1)
                {
                    int nonForwardCount = 0;
                    for (int i = 0; i < validCount; i++)
                    {
                        if (validOutputs[i] != forwardDir)
                        {
                            validOutputs[nonForwardCount] = validOutputs[i];
                            nonForwardCount++;
                        }
                    }
                    if (nonForwardCount > 0)
                    {
                        cachedSelectedOutput = validOutputs[splitterOutputIndex % nonForwardCount];
                        splitterOutputIndex++;
                        cachedForwardCell = IntVec3.Invalid;
                        return;
                    }
                }

                cachedSelectedOutput = validOutputs[splitterOutputIndex % validCount];
                splitterOutputIndex++;
                cachedForwardCell = IntVec3.Invalid;
            }
        }

        public virtual bool ShowItems => cachedShowItems;

        private void RefreshDrawCache()
        {
            if (IsMerger || IsSplitter || this is Building_UndergroundConveyorEntrance or Building_UndergroundConveyorExit || HasRefuelableTarget(out _))
                cachedYOffset = 2f;
            else
                cachedYOffset = 0f;

            if (!TryFindInputConveyor(out cachedInputDir))
                cachedInputDir = Rotation.Opposite;

            var fwd = GetCachedForwardBuilding();
            cachedNextIsTurn = fwd is Building_Conveyor nc && nc.IsTurn;

            isDrawnDynamically = IsSplitter || IsMerger || HasRefuelableTarget(out _);

            if (cachedMap?.mapDrawer != null) cachedMap.mapDrawer.MapMeshDirty(cachedPos, MapMeshFlagDefOf.Things);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float baseItemY = drawLoc.y + 1f;
            drawLoc.y += cachedYOffset;

            if (isDrawnDynamically)
            {
                if (cachedIsSingleDirectional)
                    Graphic.Draw(drawLoc, Rot4.North, this, 0f);
                else
                    this.Graphic.Draw(drawLoc, flip ? this.Rotation.Opposite : this.Rotation, this);
            }

            if (ShowItems && innerContainer.Count > 0)
            {
                if (itemProgress != lastCachedProgress)
                {
                    cachedItemDrawPos = CalculateItemPosition();
                    lastCachedProgress = itemProgress;
                }
                cachedItemDrawPos.y = baseItemY;

                var items = innerContainer.InnerListForReading;
                for (int i = 0; i < items.Count; i++)
                {
                    items[i].Graphic.Draw(cachedItemDrawPos, Rot4.North, items[i], 0f);
                }
            }
        }

        public override void Print(SectionLayer layer)
        {
            if (isDrawnDynamically)
                return;

            var graphic = Graphic;

            if (cachedIsSingleDirectional)
            {
                Printer_Plane.PrintPlane(layer, DrawPos + new Vector3(0f, cachedYOffset, 0f), def.graphicData.drawSize, graphic.MatSingle);
            }
            else
            {
                graphic.Print(layer, this, 0f);
            }
        }

        public Vector3 CalculateItemPosition()
        {
            if (TryGetHopperPullPosition(cachedDrawPos, out var hopperPos))
                return hopperPos;

            Vector3 endPos = cachedNextIsTurn
                ? cachedDrawPos + ((ForwardCell - cachedPos).ToVector3() * 0.5f)
                : cachedForwardVector;

            if (IsTurn)
            {
                Vector3 startPos = cachedDrawPos + (cachedInputDir.FacingCell.ToVector3() * 0.5f);
                float t = itemProgress;
                float u = 1 - t;
                return (u * u * startPos) + (2 * u * t * cachedDrawPos) + (t * t * endPos);
            }

            return Vector3.Lerp(cachedDrawPos, endPos, itemProgress);
        }

        private bool TryGetHopperPullPosition(Vector3 localDrawPos, out Vector3 result)
        {
            if (itemProgress >= 0f)
            {
                result = default;
                return false;
            }

            var dirs = PossibleInputDirections();
            for (int i = 0; i < dirs.Length; i++)
            {
                if (GetCachedNeighborBuilding(dirs[i]) is Building_FactoryHopper)
                {
                    Vector3 hopperPos = cachedPos.ToVector3Shifted() + dirs[i].FacingCell.ToVector3();
                    result = Vector3.Lerp(hopperPos, localDrawPos, itemProgress + 1f);
                    return true;
                }
            }

            result = localDrawPos;
            return false;
        }

        public override void DrawGUIOverlay()
        {
            if (Find.CameraDriver.CurrentZoom != CameraZoomRange.Closest) return;
            if (!ShowItems || innerContainer.Count == 0) return;

            var itemPos = cachedItemDrawPos;
            itemPos.y = AltitudeLayer.ItemImportant.AltitudeFor();

            var items = innerContainer.InnerListForReading;
            for (int i = 0; i < items.Count; i++)
            {
                Thing thing = items[i];
                if (thing.def.stackLimit > 1 && thing.stackCount > 1)
                {
                    GenMapUI.DrawThingLabel(GetLabelScreenPos(itemPos), thing.stackCount.ToStringCached(), GenMapUI.DefaultThingLabelColor);
                }
                else
                {
                    if (thing.def.drawGUIOverlayQuality && thing.TryGetQuality(out QualityCategory qc))
                    {
                        GenMapUI.DrawThingLabel(GetLabelScreenPos(itemPos), qc.GetLabelShort(), GenMapUI.DefaultThingLabelColor);
                    }
                }
                itemPos.y += 0.01f;
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
            {
                yield return g;
            }

            var items = innerContainer.InnerListForReading;
            for (int i = 0; i < items.Count; i++)
            {
                Thing carriedThing = items[i];
                Gizmo gizmo = ContainingSelectionUtility.SelectCarriedThingGizmo(this, carriedThing);
                if (gizmo != null)
                {
                    yield return gizmo;
                }
            }

            if (Faction == Faction.OfPlayer)
            {
                yield return new Command_Action
                {
                    defaultLabel = "VFEFactory_ChangeDirection".Translate(),
                    defaultDesc = "VFEFactory_ChangeDirectionDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Things/Building/ConveyorBelts/CustomizeConveyorBelt"),
                    action = () =>
                    {
                        var options = new List<FloatMenuOption>
                        {
                            new FloatMenuOption("VFEFactory_DirectionNorth".Translate(), () => SetDirection(Rot4.North)),
                            new FloatMenuOption("VFEFactory_DirectionEast".Translate(), () => SetDirection(Rot4.East)),
                            new FloatMenuOption("VFEFactory_DirectionSouth".Translate(), () => SetDirection(Rot4.South)),
                            new FloatMenuOption("VFEFactory_DirectionWest".Translate(), () => SetDirection(Rot4.West))
                        };
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                };
            }

            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Copy Stats",
                    action = () =>
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("=== CURRENT CONVEYOR ===");
                        sb.AppendLine(GetInspectString());
                        sb.AppendLine();
                        IntVec3 fwd = ForwardCell;
                        var fwdBuilding = GetCachedForwardBuilding();
                        if (fwdBuilding is Building_Conveyor fwdConv)
                        {
                            sb.AppendLine();
                            sb.AppendLine("=== FORWARD CONVEYOR ===");
                            sb.AppendLine(fwdConv.GetInspectString());
                        }

                        GUIUtility.systemCopyBuffer = sb.ToString();
                        Messages.Message("Copied stats to clipboard", MessageTypeDefOf.TaskCompletion);
                    }
                };
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (var opt in base.GetFloatMenuOptions(selPawn))
                yield return opt;

            if (state == ConveyorState.Waiting && innerContainer.Count > 0)
            {
                if (!selPawn.CanReach(this, PathEndMode.Touch, Danger.Deadly))
                {
                    yield return new FloatMenuOption("VFEFactory_CannotHaulFromConveyor".Translate() + ": " + "NoPath".Translate(), null);
                    yield break;
                }
                Thing firstItem = innerContainer.InnerListForReading[0];
                yield return new FloatMenuOption(
                    "VFEFactory_HaulFromConveyor".Translate(),
                    () =>
                    {
                        var job = JobMaker.MakeJob(InternalDefOf.VFEFactory_HaulFromConveyor, this);
                        selPawn.jobs.TryTakeOrderedJob(job);
                    },
                    iconThing: firstItem,
                    iconColor: Color.white
                );
            }
        }

        public override string GetInspectString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"State: {state}, Progress: {itemProgress:F2} (last: {lastItemProgress:F2})");
            foreach (var thing in innerContainer)
                sb.AppendLine($"  - x{thing.stackCount} {thing.def.defName}");

            sb.AppendLine($"Rotation: {Rotation.ToStringHuman()}, ForwardCell: {ForwardCell}");
            sb.AppendLine($"IsTurn: {IsTurn}, IsMerger: {IsMerger}, IsSplitter: {IsSplitter}, IsRefuelingPort: {HasRefuelableTarget(out _)}");
            sb.AppendLine($"InputCount: {InputCount}, OutputCount: {OutputCount}");
            sb.AppendLine($"cachedYOffset: {cachedYOffset}");

            sb.AppendLine("--- Neighbors ---");
            foreach (var dir in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                Building n = GetCachedNeighborBuilding(dir);
                if (n is Building_Conveyor nc)
                {
                    bool feedsIntoUs = nc.ForwardCell == cachedPos;
                    bool weCanFeedIt = nc.CanAcceptFrom(cachedPos);
                    bool weFeedIt = ForwardCell == nc.Position;
                    sb.AppendLine($"  {dir}: rot={nc.Rotation.ToStringHuman()} feedsUs={feedsIntoUs} acceptsUs={weCanFeedIt} weFeedIt={weFeedIt}");
                }
                else if (n != null)
                {
                    sb.AppendLine($"  {dir}: {n.def.defName} (non-conveyor)");
                }
            }

            var downstreamReason = CanMoveDownstream();
            sb.AppendLine($"CanMoveDownstream: {downstreamReason == DownstreamBlockReason.None} ({downstreamReason})");
            return sb.ToString().TrimEndNewlines();
        }
    }
}
