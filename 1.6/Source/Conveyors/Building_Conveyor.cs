using System.Collections.Generic;
using System.Linq;
using System.Text;
using LudeonTK;
using UnityEngine;
using Verse;
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
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
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
        private bool cachedHasRefuelableTarget;
        private bool cachedShowItems = true;
        private CompRefuelable cachedRefuelComp;

        private Building cachedForwardBuilding;
        private Building[] cachedNeighborBuildings;
        private IntVec3[] cachedNeighborPositions;
        private IntVec3 cachedForwardBuildingPos = IntVec3.Invalid;
        private IntVec3 cachedForwardCell = IntVec3.Invalid;
        private bool cachedIsSplitter;
        private bool hasAdjacentHopper;

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
            if (Map?.mapDrawer != null)
            {
                Map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things);
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
                return cachedForwardCell;
            }
        }

        private IntVec3 ComputeForwardCell()
        {
            if (isRecaching) return Position + Rotation.FacingCell;

            if (cachedIsSplitter && cachedSelectedOutput.IsValid)
            {
                return Position + cachedSelectedOutput.FacingCell;
            }

            if (cachedOutputCount == null)
            {
                cachedOutputCount = CountOutputs();
            }

            if (OutputCount == 1 && cachedSingleOutput.IsValid)
            {
                return Position + cachedSingleOutput.FacingCell;
            }

            return Position + Rotation.FacingCell;
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

            if (!targetPos.InBounds(Map))
            {
                cachedForwardBuilding = null;
                cachedForwardBuildingPos = targetPos;
                return null;
            }

            cachedForwardBuilding = targetPos.GetFirstBuilding(Map);
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
            IntVec3 neighborPos = Position + direction.FacingCell;

            if (cachedNeighborPositions[idx] == neighborPos)
            {
                Building cached = cachedNeighborBuildings[idx];
                if (cached == null || (!cached.DestroyedOrNull() && cached.Position == neighborPos))
                {
                    return cached;
                }
            }

            if (!neighborPos.InBounds(Map))
            {
                cachedNeighborBuildings[idx] = null;
                cachedNeighborPositions[idx] = neighborPos;
                return null;
            }

            Building neighbor = neighborPos.GetFirstBuilding(Map);
            cachedNeighborBuildings[idx] = neighbor;
            cachedNeighborPositions[idx] = neighborPos;
            return neighbor;
        }

        private bool HasRefuelableTarget(out CompRefuelable refuelComp)
        {
            if (!cachedHasRefuelableTarget)
            {
                RebuildRefuelableCache();
            }
            refuelComp = cachedRefuelComp;
            return cachedHasRefuelableTarget;
        }

        private void RebuildRefuelableCache()
        {
            var target = GetCachedForwardBuilding();
            cachedRefuelComp = target?.GetComp<CompRefuelable>();
            cachedHasRefuelableTarget = cachedRefuelComp != null;
            cachedShowItems = !cachedHasRefuelableTarget;
        }

        private void InvalidateCache()
        {
            cachedIsTurn = null;
            cachedInputCount = null;
            cachedOutputCount = null;
            cachedSingleOutput = Rot4.Invalid;
            cachedGraphic = null;
            cachedIsSingleDirectional = false;
            cachedHasRefuelableTarget = false;
            cachedRefuelComp = null;
            cachedIsSplitter = false;
            cachedForwardCell = IntVec3.Invalid;
            lastCachedProgress = float.MinValue;

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
        }

        private void PeriodicCacheCheck()
        {
            bool changed = false;
            Map map = Map;

            IntVec3 targetPos = ForwardCell;
            if (targetPos.InBounds(map))
            {
                Building currentForward = targetPos.GetFirstBuilding(map);
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
                Rot4 dir = new Rot4(i);
                IntVec3 neighborPos = Position + dir.FacingCell;
                if (!neighborPos.InBounds(map)) continue;

                var currentNeighbor = neighborPos.GetFirstBuilding(map);
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
                if (map?.mapDrawer != null) map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things);
            }
        }

        private void InvalidateNeighborCaches()
        {
            Map map = Map;
            IntVec3 position = Position;
            for (int i = 0; i < 4; i++)
            {
                IntVec3 adj = position + GenAdj.CardinalDirections[i];
                if (adj.InBounds(map))
                {
                    Building adjacentBuilding = adj.GetFirstBuilding(map);
                    if (adjacentBuilding is Building_Conveyor neighbor)
                    {
                        neighbor.InvalidateCache();
                        map.mapDrawer.MapMeshDirty(neighbor.Position, MapMeshFlagDefOf.Things);
                    }
                }
            }
        }

        private bool CanStackWithAny(IList<Thing> carried, IList<Thing> target)
        {
            foreach (var thing in carried)
            {
                foreach (Thing targetThing in target)
                {
                    if (thing.CanStackWith(targetThing))
                    {
                        int spaceLeft = targetThing.def.stackLimit - targetThing.stackCount;
                        if (spaceLeft > 0)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool TryFindInputConveyor(out Rot4 inputDir)
        {
            foreach (var dir in PossibleInputDirections())
            {
                Building neighbor = GetCachedNeighborBuilding(dir);
                if (neighbor is Building_Conveyor neighborComp && neighborComp.ForwardCell == Position)
                {
                    inputDir = dir;
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
            Map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things);
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
            cachedItemsPerCell = Props.itemsPerCell;
            InvalidateCache();
            InvalidateNeighborCaches();
            map.mapDrawer.MapMeshDirty(Position, MapMeshFlagDefOf.Things);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Spawned)
            {
                Map map = Map;
                IntVec3 position = Position;
                InvalidateNeighborCaches();
                for (int i = innerContainer.Count - 1; i >= 0; i--)
                {
                    GenSpawn.Spawn(innerContainer[i], position, map);
                }
                innerContainer.Clear();
                base.DeSpawn(mode);
                map.mapDrawer.MapMeshDirty(position, MapMeshFlagDefOf.Things);
            }
            else
            {
                base.DeSpawn(mode);
            }
        }

        protected override void Tick()
        {
            base.Tick();
            Map localMap = this.Map;
            IntVec3 localPos = this.Position;
            if (this.IsHashIntervalTick(60))
            {
                PeriodicCacheCheck();
            }
            if (this.IsHashIntervalTick(10))
            {
                CheckForItemsOnCell(localMap, localPos);
                CheckRefuelableTarget(localMap, localPos);
            }

            if (innerContainer.Count > 0 && state == ConveyorState.Moving)
            {
                var reason = CanMoveDownstream(localMap, localPos);
                if (reason != DownstreamBlockReason.None)
                {
                    SetState(ConveyorState.Waiting);
                }
                else
                {
                    itemProgress += (1f / Props.ticksPerCell);

                    if (itemProgress >= 0.999f)
                        CompleteTransfer(localMap);
                }
                lastItemProgress = itemProgress;
            }
            else
            {
                ProcessMovement(localMap);
            }
        }

        private DownstreamBlockReason CanMoveDownstream(Map map, IntVec3 position)
        {
            IntVec3 targetPos = ForwardCell;
            if (!targetPos.InBounds(map))
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

                bool canStack = targetConveyor.innerContainer.Count > 0 && CanStackWithAny(innerContainer, targetConveyor.innerContainer);

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
                                 adjConveyor.Position.GetHashCode() > position.GetHashCode()))
                            {
                                tyingCompetitor = true;
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
            else if (targetBuilding != null && targetBuilding.GetComp<CompRefuelable>() != null)
            {
                var refuelComp = targetBuilding.GetComp<CompRefuelable>();
                if (refuelComp.IsFull)
                    return DownstreamBlockReason.TargetFull;

                return DownstreamBlockReason.None;
            }
            else
            {
                if (!targetPos.Walkable(map))
                    return DownstreamBlockReason.CellNotWalkable;

                if (!CanDumpToCell(targetPos, map))
                    return DownstreamBlockReason.CellFull;

                return DownstreamBlockReason.None;
            }
        }

        private bool CanTransferToHopper(Building_FactoryHopper hopper)
        {
            foreach (var thing in innerContainer)
            {
                if (!hopper.slotGroup.Settings.AllowedToAccept(thing))
                {
                    return false;
                }
            }

            bool canStack = false;
            bool hasRoom = false;
            foreach (var existing in hopper.slotGroup.HeldThings)
            {
                foreach (Thing carried in innerContainer)
                {
                    if (carried.CanStackWith(existing))
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

            if (canStack && hasRoom)
            {
                return true;
            }

            if (!canStack)
            {
                return true;
            }

            return false;
        }

        private bool CanDumpToCell(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
            {
                return false;
            }

            List<Thing> thingList = map.thingGrid.ThingsListAt(cell);
            Building edifice = cell.GetEdifice(map);

            if (edifice is Building_FactoryHopper hopper)
            {
                return CanTransferToHopper(hopper);
            }

            if (edifice != null)
            {
                var refuelComp = edifice.GetComp<CompRefuelable>();
                if (refuelComp != null)
                {
                    return !refuelComp.IsFull;
                }
            }

            if (!cell.Walkable(map))
            {
                return false;
            }

            int currentItemCount = 0;
            bool canStack = false;
            bool hasRoom = false;

            for (int i = 0; i < thingList.Count; i++)
            {
                Thing existing = thingList[i];
                if (existing.def.category == ThingCategory.Item)
                {
                    currentItemCount++;
                    foreach (Thing carried in innerContainer)
                    {
                        if (carried.CanStackWith(existing))
                        {
                            canStack = true;
                            if (existing.stackCount < existing.def.stackLimit) hasRoom = true;
                        }
                    }
                }
            }

            int maxItems = cell.GetMaxItemsAllowedInCell(map);
            if (!canStack && currentItemCount >= maxItems) return false;
            if (canStack && !hasRoom && currentItemCount >= maxItems) return false;

            if (edifice is IHaulDestination dest)
            {
                foreach (var thing in innerContainer)
                {
                    if (!dest.Accepts(thing))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private void ProcessMovement(Map map)
        {
            if (HasSpace() && state != ConveyorState.Waiting)
            {
                TryPullItem(map);
            }

            if (innerContainer.Count > 0 && state != ConveyorState.Moving)
            {
                IntVec3 targetPos = ForwardCell;
                if (!targetPos.InBounds(map))
                {
                    SetState(ConveyorState.Waiting);
                    return;
                }

                if (IsSplitter)
                {
                    var downstreamReason = CanMoveDownstream(map, Position);

                    if (downstreamReason != DownstreamBlockReason.None && itemProgress <= 0.2f)
                    {
                        foreach (var dir in PossibleOutputDirections())
                        {
                            if (Position + dir.FacingCell == targetPos) continue;
                            if (!IsValidOutput(dir)) continue;

                            Rot4 oldOutput = cachedSelectedOutput;
                            cachedSelectedOutput = dir;
                            cachedForwardCell = IntVec3.Invalid;

                            if (CanMoveDownstream(map, Position) == DownstreamBlockReason.None)
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
                }

                var targetBuilding = GetCachedForwardBuilding();
                if (targetBuilding is Building_Conveyor targetConveyor)
                {
                    var downstreamReason = CanMoveDownstream(map, Position);
                    if (downstreamReason == DownstreamBlockReason.None)
                    {
                        InitiateTransfer();
                        itemProgress += (1f / Props.ticksPerCell);
                        lastItemProgress = itemProgress;
                    }
                    else
                    {
                        if (downstreamReason == DownstreamBlockReason.TargetFull &&
                            targetConveyor.itemProgress <= 0.05f &&
                            CanStackWithAny(innerContainer, targetConveyor.innerContainer))
                        {
                            CompleteTransfer(map);
                        }
                        SetState(ConveyorState.Waiting);
                    }
                }
                else
                {
                    TryDumpItem(targetPos, map);
                }
            }
        }

        private void TryPullItem(Map map)
        {
            if (state == ConveyorState.Waiting) return;
            if (!hasAdjacentHopper) return;

            if (TryPullFromDir(Rotation.Opposite)) return;
            if (TryPullFromDir(Rotation.Rotated(RotationDirection.Clockwise))) return;
            TryPullFromDir(Rotation.Rotated(RotationDirection.Counterclockwise));
        }

        private bool TryPullFromDir(Rot4 dir)
        {
            if (GetCachedNeighborBuilding(dir) is Building_FactoryHopper hopper)
            {
                Thing item = hopper.slotGroup.HeldThings.FirstOrDefault();
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

        private void CheckForItemsOnCell(Map map, IntVec3 position)
        {
            if (!HasSpace())
            {
                return;
            }

            List<Thing> thingsOnCell = map.thingGrid.ThingsListAt(position);
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

        private void CheckRefuelableTarget(Map map, IntVec3 position)
        {
            if (cachedGraphic != null && HasRefuelableTarget(out var refuelComp))
            {
                if (refuelComp.parent.DestroyedOrNull())
                {
                    InvalidateCache();
                    if (map?.mapDrawer != null)
                    {
                        map.mapDrawer.MapMeshDirty(position, MapMeshFlagDefOf.Things);
                    }
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
                foreach (var thing in innerContainer)
                {
                    bool thingFits = false;
                    foreach (var targetThing in target.innerContainer)
                    {
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

        private void CompleteTransfer(Map map)
        {
            if (innerContainer.Count == 0) return;

            IntVec3 targetPos = ForwardCell;
            var targetBuilding = GetCachedForwardBuilding();

            if (HasRefuelableTarget(out var refuelComp))
            {
                if (refuelComp.IsFull) return;
                int fuelNeeded = refuelComp.GetFuelCountToFullyRefuel();

                transferred.Clear();

                for (int i = innerContainer.Count - 1; i >= 0; i--)
                {
                    Thing thing = innerContainer[i];
                    if (refuelComp.Props.fuelFilter.Allows(thing))
                    {
                        int amountAvailable = thing.stackCount;
                        int amountToRefuel = Mathf.Min(fuelNeeded, amountAvailable);

                        var fuelToUse = thing.SplitOff(amountToRefuel);
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
                for (int i = innerContainer.Count - 1; i >= 0; i--)
                {
                    Thing thing = innerContainer[i];
                    Selector_Deselect_Patch.transferringItems.Add(thing);

                    TransferItemTo(targetConveyor, thing);

                    Selector_Deselect_Patch.transferringItems.Remove(thing);
                }

                for (int i = innerContainer.Count - 1; i >= 0; i--)
                {
                    if (innerContainer[i].stackCount <= 0)
                        innerContainer.InnerListForReading.RemoveAt(i);
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

                for (int i = innerContainer.Count - 1; i >= 0; i--)
                {
                    Thing thing = innerContainer[i];
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
                            if (GenPlace.TryPlaceThing(thing, hopper.Position, map, ThingPlaceMode.Direct))
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
                for (int i = innerContainer.Count - 1; i >= 0; i--)
                {
                    Thing item = innerContainer[i];
                    Selector_Deselect_Patch.transferringItems.Add(item);

                    bool fullyMerged = false;
                    List<Thing> thingList = map.thingGrid.ThingsListAt(targetPos);

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
                        if (currentItems < targetPos.GetMaxItemsAllowedInCell(map))
                        {
                            if (GenPlace.TryPlaceThing(item, targetPos, map, ThingPlaceMode.Direct))
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

        private void TryDumpItem(IntVec3 targetPos, Map map)
        {
            if (CanDumpToCell(targetPos, map))
            {
                if (itemProgress >= 0.999f && innerContainer.Count > 0)
                {
                    CompleteTransfer(map);
                }
                else if (!CanFullyDump(targetPos, map))
                {
                    CompleteTransfer(map);
                }
                else
                {
                    InitiateTransfer();
                }
            }
            else
            {
                SetState(ConveyorState.Waiting);
            }
        }

        private bool CanFullyDump(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map)) return false;

            Building edifice = cell.GetEdifice(map);
            if (edifice is Building_FactoryHopper hopper)
            {
                foreach(var thing in innerContainer)
                {
                    bool canFitAll = false;
                    foreach(var existing in hopper.slotGroup.HeldThings)
                    {
                        if (existing.CanStackWith(thing) && (existing.def.stackLimit - existing.stackCount) >= thing.stackCount)
                        {
                            canFitAll = true;
                            break;
                        }
                    }
                    if (!canFitAll && hopper.slotGroup.HeldThings.Count() >= hopper.slotGroup.Settings.filter.AllowedDefCount)
                        return false;
                }
                return true;
            }
            else if (edifice != null && edifice.GetComp<CompRefuelable>() != null)
            {
                var comp = edifice.GetComp<CompRefuelable>();
                int total = 0;
                for (int i = 0; i < innerContainer.Count; i++) total += innerContainer[i].stackCount;
                return comp.GetFuelCountToFullyRefuel() >= total;
            }
            else
            {
                List<Thing> thingList = map.thingGrid.ThingsListAt(cell);
                int currentItems = thingList.Count(t => t.def.category == ThingCategory.Item);
                bool hasEmptySpace = currentItems < cell.GetMaxItemsAllowedInCell(map);

                foreach (var thing in innerContainer)
                {
                    bool canFitAll = false;
                    foreach (var existing in thingList)
                    {
                        if (existing.def.category == ThingCategory.Item && existing.CanStackWith(thing))
                        {
                            if ((existing.def.stackLimit - existing.stackCount) >= thing.stackCount)
                            {
                                canFitAll = true;
                                break;
                            }
                        }
                    }
                    if (!canFitAll && !hasEmptySpace) return false;
                }
                return true;
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

            foreach (var existing in innerContainer)
            {
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

            for (int i = innerContainer.Count - 1; i >= 0; i--)
            {
                Thing existing = innerContainer[i];
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
            for (int i = 0; i < targetConveyor.innerContainer.Count; i++)
            {
                Thing targetThing = targetConveyor.innerContainer[i];
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
                innerContainer.InnerListForReading.Remove((Thing)thing);
                thing.holdingOwner = targetConveyor.innerContainer;
                targetConveyor.innerContainer.InnerListForReading.Add(thing);
                return true;
            }

            return false;
        }

        private bool DetectTurn()
        {
            if (OutputCount > 1) return false;

            if (OutputCount == 1 && cachedSingleOutput.IsValid && cachedSingleOutput != Rotation)
            {
                return true;
            }

            if (TryFindInputConveyor(out var inputDir))
            {
                Rot4 outputDir = Rot4.FromIntVec3(ForwardCell - Position);
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
            foreach (var dir in PossibleInputDirections())
            {
                Building neighbor = GetCachedNeighborBuilding(dir);
                if (neighbor is Building_Conveyor neighborComp && neighborComp.ForwardCell == Position)
                    count++;
            }
            isRecaching = false;
            return count;
        }

        public virtual Rot4[] PossibleInputDirections() => InputDirectionsByRot[Rotation.AsInt];

        public Rot4[] PossibleOutputDirections() => OutputDirectionsByRot[Rotation.AsInt];

        public bool CanAcceptFrom(IntVec3 fromPos)
        {
            foreach (var inputDir in PossibleInputDirections())
            {
                if (Position + inputDir.FacingCell == fromPos)
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

            Map map = Map;
            IntVec3 position = Position;
            bool roofed = position.Roofed(map);
            bool roomUsesOutdoorTemp = position.GetRoom(map)?.UsesOutdoorTemperature ?? false;
            TerrainDef terrain = position.GetTerrain(map);

            for (int i = innerContainer.Count - 1; i >= 0; i--)
            {
                Thing t = innerContainer[i];
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
                map.gameConditionManager.GetAllGameConditionsAffectingMap(map, conditions);

                for (int i = 0; i < conditions.Count; i++)
                {
                    if (conditions[i] is GameCondition_ToxicFallout)
                    {
                        ApplyToxicFallout(innerContainer);
                    }
                }
            }
        }

        private void ApplyToxicFallout(ThingOwner container)
        {
            for (int i = 0; i < container.Count; i++)
            {
                Thing t = container[i];
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
                string baseName = HasFilterRestrictions() ? "ConveyorFilter" : "ConveyorSplitter";
                var outputs = CanonicalOrder.Where(IsValidOutput).ToList();

                if (outputs.Count == 2)
                {
                    string outputsPrefix = outputs[0].ToStringWord() + outputs[1].ToStringWord();
                    string rotationSuffix = Rotation.ToStringWord().ToLower();
                    texturePath = $"{Props.baseTexPath}/{baseName}_{outputsPrefix}_{rotationSuffix}";
                    useSingleGraphic = true;
                }
                else
                {
                    texturePath = $"{Props.baseTexPath}/{baseName}";
                    useSingleGraphic = false;
                }
            }
            else if (IsMerger)
            {
                var inputs = CanonicalOrder
                    .Where(dir =>
                    {
                        var n = GetCachedNeighborBuilding(dir) as Building_Conveyor;
                        return n?.ForwardCell == Position;
                    }).ToList();

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
            IntVec3 position = Position;
            Building neighbor = GetCachedNeighborBuilding(dir);
            if (neighbor == null) return false;

            if (neighbor is Building_Conveyor neighborConveyor)
            {
                if (!neighborConveyor.CanAcceptFrom(position))
                    return false;

                if (neighborConveyor.ForwardCell == position)
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

            foreach (var dir in PossibleOutputDirections())
            {
                if (IsValidOutput(dir))
                {
                    count++;
                    singleOut = dir;
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
            foreach (var dir in PossibleOutputDirections())
            {
                if (IsValidOutput(dir))
                {
                    validOutputs[validCount] = dir;
                    validCount++;
                }
            }

            if (validCount > 0)
            {
                Rot4 forwardDir = Rotation;
                bool allAllowed = true;
                for (int i = 0; i < innerContainer.Count; i++)
                {
                    if (!GetStoreSettings().AllowedToAccept(innerContainer[i]))
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
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            var baseItemY = drawLoc.y + 1;
            bool hasRefuel = cachedHasRefuelableTarget;
            if (IsMerger || IsSplitter || this is Building_UndergroundConveyorEntrance or Building_UndergroundConveyorExit || hasRefuel)
            {
                drawLoc.y += 2f;
            }
            if (cachedIsSingleDirectional)
                Graphic.Draw(drawLoc, Rot4.North, this, 0f);
            else
                base.DrawAt(drawLoc, flip);
            if (ShowItems && innerContainer.Count > 0)
            {
                if (itemProgress != lastCachedProgress)
                {
                    cachedItemDrawPos = CalculateItemPosition(itemProgress);
                    lastCachedProgress = itemProgress;
                }
                var itemPos = cachedItemDrawPos;
                itemPos.y = baseItemY;
                foreach (var thing in innerContainer)
                {
                    thing.Graphic.Draw(itemPos, Rot4.North, thing, 0f);
                }
            }
        }

        public Vector3 CalculateItemPosition(float progress)
        {
            Map localMap = Map;
            IntVec3 localPos = Position;

            if (progress < 0f)
            {
                Rot4 inputDir = Rot4.Invalid;
                foreach (var dir in PossibleInputDirections())
                {
                    var neighbor = GetCachedNeighborBuilding(dir);
                    if (neighbor is Building_FactoryHopper)
                    {
                        inputDir = dir;
                        break;
                    }
                }

                if (!inputDir.IsValid) return DrawPos;

                Vector3 hopperPos = localPos.ToVector3Shifted() + inputDir.FacingCell.ToVector3();
                Vector3 beltStart = DrawPos;
                return Vector3.Lerp(hopperPos, beltStart, progress + 1f);
            }

            Vector3 startPos = DrawPos;
            Vector3 endPos = ForwardCell.ToVector3Shifted();

            IntVec3 targetPos = ForwardCell;
            if (targetPos.InBounds(localMap))
            {
                var targetBuilding = GetCachedForwardBuilding();
                if (targetBuilding is Building_Conveyor nextConveyor && nextConveyor.IsTurn)
                {
                    endPos = DrawPos + ((ForwardCell - localPos).ToVector3() * 0.5f);
                }
            }

            if (IsTurn)
            {
                if (!TryFindInputConveyor(out var inputDir))
                {
                    inputDir = Rotation.Opposite;
                }

                startPos = DrawPos + (inputDir.FacingCell.ToVector3() * 0.5f);

                Vector3 controlPoint = DrawPos;

                float t = progress;
                float u = 1 - t;

                return (u * u * startPos) + (2 * u * t * controlPoint) + (t * t * endPos);
            }

            return Vector3.Lerp(startPos, endPos, progress);
        }

        public override void DrawGUIOverlay()
        {
            if (Find.CameraDriver.CurrentZoom != CameraZoomRange.Closest) return;
            if (!ShowItems || innerContainer.Count == 0) return;

            if (itemProgress != lastCachedProgress)
            {
                cachedItemDrawPos = CalculateItemPosition(itemProgress);
                lastCachedProgress = itemProgress;
            }
            var itemPos = cachedItemDrawPos;
            itemPos.y = AltitudeLayer.ItemImportant.AltitudeFor();

            foreach (var thing in innerContainer)
            {
                if (thing.def.stackLimit > 1 && thing.stackCount > 1)
                {
                    GenMapUI.DrawThingLabel(GetLabelScreenPos(itemPos), thing.stackCount.ToStringCached(), GenMapUI.DefaultThingLabelColor);
                }
                else
                {
                    QualityCategory qc;
                    if (thing.def.drawGUIOverlayQuality && thing.TryGetQuality(out qc))
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

            foreach (Thing carriedThing in innerContainer)
            {
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

        public override string GetInspectString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"State: {state}, Progress: {itemProgress:F2} (last: {lastItemProgress:F2})");
            foreach (var thing in innerContainer)
                sb.AppendLine($"  - x{thing.stackCount} {thing.def.defName}");

            sb.AppendLine($"Rotation: {Rotation.ToStringHuman()}, ForwardCell: {ForwardCell}");
            sb.AppendLine($"IsTurn: {IsTurn}, IsMerger: {IsMerger}, IsSplitter: {IsSplitter}");
            sb.AppendLine($"InputCount: {InputCount}, OutputCount: {OutputCount}");
            sb.AppendLine($"CachedSingleOutput: {(cachedSingleOutput.IsValid ? cachedSingleOutput.ToStringHuman() : "None")}");

            sb.AppendLine("--- Neighbors ---");
            Map map = Map;
            IntVec3 position = Position;
            foreach (var dir in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                Building n = GetCachedNeighborBuilding(dir);
                if (n is Building_Conveyor nc)
                {
                    bool feedsIntoUs = nc.ForwardCell == position;
                    bool weCanFeedIt = nc.CanAcceptFrom(position);
                    bool weFeedIt = ForwardCell == nc.Position;
                    sb.AppendLine($"  {dir}: rot={nc.Rotation.ToStringHuman()} feedsUs={feedsIntoUs} acceptsUs={weCanFeedIt} weFeedIt={weFeedIt}");
                }
                else if (n != null)
                {
                    sb.AppendLine($"  {dir}: {n.def.defName} (non-conveyor)");
                }
            }

            sb.AppendLine("--- AutoDetect Trace ---");
            int inputCount = 0;
            Rot4 foundInput = Rot4.Invalid;
            foreach (var dir in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                if (GetCachedNeighborBuilding(dir) is Building_Conveyor nc && nc.ForwardCell == position)
                {
                    inputCount++;
                    foundInput = dir;
                }
            }
            sb.AppendLine($"  RawInputCount: {inputCount}, RawInputDir: {foundInput.ToStringHuman()}");

            var fwdBuilding = GetCachedForwardBuilding();
            bool fwdEstablished = fwdBuilding != null &&
                fwdBuilding is Building_Conveyor fwdC && fwdC.CanAcceptFrom(position);
            sb.AppendLine($"  ForwardEstablished: {fwdEstablished}");
            sb.AppendLine($"  StraightFromInput: {Rotation == foundInput.Opposite}");

            var downstreamReason = CanMoveDownstream(Map, Position);
            sb.AppendLine($"CanMoveDownstream: {downstreamReason == DownstreamBlockReason.None} ({downstreamReason})");
            sb.AppendLine($"HasSpace: {HasSpace()}");

            return sb.ToString().TrimEndNewlines();
        }
    }
}
