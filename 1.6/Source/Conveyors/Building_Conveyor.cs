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
        [TweakValue("0VFEFactory", 0.5f, 2.0f)]
        public static float curveSpeedMultiplier = 0.8f;

        private static readonly Rot4[] CanonicalOrder = { Rot4.East, Rot4.West, Rot4.North, Rot4.South };

        protected ThingOwner<Thing> innerContainer;

        public IList<Thing> carriedThings => innerContainer;

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
        private Rot4? cachedSelectedOutput;
        private Rot4? cachedSingleOutput;
        private int splitterOutputIndex;
        private StorageSettings storageSettings;
        private Graphic cachedGraphic;
        private bool cachedIsSingleDirectional;
        private ConveyorExtension cachedProps;
        private bool isRecaching = false;
        private DownstreamBlockReason lastDownstreamReason = DownstreamBlockReason.None;
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

        private void LogConveyor(string message)
        {
            if (carriedThings.Count > 0 && carriedThings.Any(x => x.def.defName.Contains("Lance")))
            {
                Log.Message($"{message}");
            }
        }

        private void SetState(ConveyorState newState, string reason)
        {
            if (state != newState)
            {
                LogConveyor($"[STATE] tick={Find.TickManager.TicksGame} pos={Position} {state}→{newState} reason={reason}");
            }
            state = newState;
        }

        public IntVec3 ForwardCell
        {
            get
            {

                if (isRecaching) return Position + Rotation.FacingCell;

                if (IsSplitter && cachedSelectedOutput.HasValue && cachedSelectedOutput.Value.IsValid)
                {
                    return Position + cachedSelectedOutput.Value.FacingCell;
                }

                if (cachedOutputCount == null)
                {
                    cachedOutputCount = CountOutputs();
                }

                if (OutputCount == 1 && cachedSingleOutput.HasValue && cachedSingleOutput.Value.IsValid)
                {
                    return Position + cachedSingleOutput.Value.FacingCell;
                }

                return Position + Rotation.FacingCell;
            }
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

        public bool IsSplitter => OutputCount > 1;
        public bool IsMerger => InputCount > 1 && !IsTurn;

        private bool HasRefuelableTarget(Map map, IntVec3 position, out CompRefuelable refuelComp)
        {
            IntVec3 targetPos = ForwardCell;
            if (!targetPos.InBounds(map))
            {
                refuelComp = null;
                return false;
            }
            var target = targetPos.GetFirstBuilding(map);
            refuelComp = target?.GetComp<CompRefuelable>();
            return refuelComp != null;
        }

        private void InvalidateCache()
        {
            cachedIsTurn = null;
            cachedInputCount = null;
            cachedOutputCount = null;
            cachedSingleOutput = null;
            cachedGraphic = null;
            cachedIsSingleDirectional = false;
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
            Map map = Map;
            IntVec3 position = Position;
            foreach (var dir in PossibleInputDirections())
            {
                IntVec3 neighborPos = position + dir.FacingCell;
                if (neighborPos.InBounds(map))
                {
                    Building neighbor = neighborPos.GetFirstBuilding(map);
                    if (neighbor is Building_Conveyor neighborComp && neighborComp.ForwardCell == position)
                    {
                        inputDir = dir;
                        return true;
                    }
                }
            }
            inputDir = Rot4.Invalid;
            return false;
        }

        private float CalculateVisualProgress()
        {
            return itemProgress;
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
            LogConveyor("ResetConveyorState: fullReset: " + fullReset);
            if (fullReset)
            {
                lastItemProgress = 0f;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
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
            if (this.IsHashIntervalTick(10))
            {
                CheckForItemsOnCell(localMap, localPos);
                CheckRefuelableTarget(localMap, localPos);
            }

            if (carriedThings.Count > 0 && state == ConveyorState.Moving)
            {
                var reason = CanMoveDownstream(localMap, localPos);
                if (reason != lastDownstreamReason)
                {
                    lastDownstreamReason = reason;
                }

                if (reason != DownstreamBlockReason.None)
                {
                    SetState(ConveyorState.Waiting, $"CanMoveDownstream:{reason}");
                }
                else
                {
                    itemProgress += (1f / Props.ticksPerCell);

                    if (itemProgress >= 0.999f)
                        CompleteTransfer(localMap, localPos);
                }
                lastItemProgress = itemProgress;
            }
            else
            {
                ProcessMovement(localMap, localPos);
            }
        }

        private DownstreamBlockReason CanMoveDownstream(Map map, IntVec3 position)
        {
            IntVec3 targetPos = ForwardCell;
            if (!targetPos.InBounds(map))
                return DownstreamBlockReason.TargetOutOfBounds;

            var targetBuilding = targetPos.GetFirstBuilding(map);

            if (targetBuilding is Building_Conveyor targetConveyor)
            {
                if (!CanTransferToTarget(targetConveyor))
                {
                    if (targetConveyor.state == ConveyorState.Moving)
                    {
                        LogConveyor($"[TARGET_MOVING] tick={Find.TickManager.TicksGame} pos={position} " +
                            $"target={targetPos} target.progress={targetConveyor.itemProgress:F2} " +
                            $"my.progress={itemProgress:F2} " +
                            $"target.items={string.Join(",", targetConveyor.carriedThings.Select(t => t.def.defName))}");
                        return DownstreamBlockReason.TargetMoving;
                    }
                    return DownstreamBlockReason.TargetFull;
                }

                float aheadProgress = float.MinValue;

                foreach (var adj in GenAdj.CellsAdjacent8Way(targetConveyor))
                {
                    if (!adj.InBounds(map)) continue;
                    Building adjBuilding = adj.GetFirstBuilding(map);
                    if (adjBuilding is Building_Conveyor adjConveyor
                        && adjConveyor != this
                        && adjConveyor.ForwardCell == targetPos
                        && adjConveyor.carriedThings.Count > 0
                        && adjConveyor.itemProgress > aheadProgress)
                    {
                        aheadProgress = adjConveyor.itemProgress;
                    }
                }

                if (aheadProgress != float.MinValue && aheadProgress > itemProgress + 0.1f)
                {
                    if (!CanStackWithAny(carriedThings, targetConveyor.carriedThings))
                        return DownstreamBlockReason.CompetitorAhead;
                }

                foreach (var adj in GenAdj.CellsAdjacentCardinal(targetConveyor))
                {
                    if (!adj.InBounds(map)) continue;
                    Building adjBuilding = adj.GetFirstBuilding(map);
                    if (adjBuilding is Building_Conveyor adjConveyor
                        && adjConveyor != this
                        && adjConveyor.ForwardCell == targetPos
                        && adjConveyor.carriedThings.Count > 0)
                    {
                        if (!CanStackWithAny(carriedThings, targetConveyor.carriedThings))
                        {
                            if (adjConveyor.itemProgress > itemProgress ||
                                (adjConveyor.itemProgress == itemProgress &&
                                 adjConveyor.Position.GetHashCode() > position.GetHashCode()))
                            {
                                return DownstreamBlockReason.CompetitorTying;
                            }
                        }
                    }
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
            foreach (var thing in carriedThings)
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
                foreach (Thing carried in carriedThings)
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
                    foreach (Thing carried in carriedThings)
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
                foreach (var thing in carriedThings)
                {
                    if (!dest.Accepts(thing))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private void ProcessMovement(Map map, IntVec3 position)
        {
            if (HasSpace() && state != ConveyorState.Waiting)
            {
                TryPullItem(map, position);
            }

            if (carriedThings.Count > 0 && state != ConveyorState.Moving)
            {
                IntVec3 targetPos = ForwardCell;
                if (!targetPos.InBounds(map))
                {
                    SetState(ConveyorState.Waiting, "TargetOutOfBounds");
                    return;
                }

                var targetBuilding = targetPos.GetFirstBuilding(map);
                if (targetBuilding is Building_Conveyor targetConveyor)
                {
                    var downstreamReason = CanMoveDownstream(map, position);
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
                            CanStackWithAny(carriedThings, targetConveyor.carriedThings))
                        {
                            CompleteTransfer(map, position);
                        }
                        SetState(ConveyorState.Waiting, $"CanMoveDownstream:{downstreamReason}");
                    }
                }
                else
                {
                    TryDumpItem(targetPos, map);
                }
            }
        }

        private void TryPullItem(Map map, IntVec3 position)
        {
            if (state == ConveyorState.Waiting) return;

            Rot4 rotation = Rotation;

            if (TryPullFromCell(map, position + rotation.Opposite.FacingCell)) return;
            if (TryPullFromCell(map, position + rotation.Rotated(RotationDirection.Clockwise).FacingCell)) return;
            TryPullFromCell(map, position + rotation.Rotated(RotationDirection.Counterclockwise).FacingCell);
        }

        private bool TryPullFromCell(Map map, IntVec3 inputPos)
        {
            if (!inputPos.InBounds(map)) return false;

            if (inputPos.GetFirstBuilding(map) is Building_FactoryHopper hopper)
            {
                var heldThings = hopper.slotGroup.HeldThings.ToList();
                if (heldThings.Count > 0)
                {
                    Thing item = heldThings[0];
                    Thing taken = item.SplitOff(item.stackCount);
                    if (taken.Spawned)
                    {
                        taken.DeSpawn(DestroyMode.Vanish);
                    }
                    innerContainer.TryAdd(taken);
                    state = ConveyorState.Moving;
                    itemProgress = -1f;
                    lastItemProgress = -1f;
                    return true;
                }
                return false;
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

                if (carriedThings.Contains(thing))
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
            if (cachedGraphic != null && HasRefuelableTarget(map, position, out var refuelComp))
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
            if (target.carriedThings.Count == 0 && target.HasSpace())
                return true;

            if (itemProgress < target.itemProgress)
                return true;

            if (target.itemProgress <= 0.05f && (target.state == ConveyorState.Waiting || target.state == ConveyorState.Empty))
            {
                bool fullyFits = true;
                foreach (var thing in carriedThings)
                {
                    bool thingFits = false;
                    foreach (var targetThing in target.carriedThings)
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
            SetState(ConveyorState.Moving, "InitiateTransfer");
            var visualPos = CalculateItemPosition(0f);
            LogConveyor($"InitiateTransfer at {Position} tick={Find.TickManager.TicksGame} - Visual Position: {visualPos}");
        }

        private void CompleteTransfer(Map map, IntVec3 position)
        {
            if (carriedThings.Count == 0) return;

            IntVec3 targetPos = ForwardCell;
            var targetBuilding = targetPos.GetFirstBuilding(map);
            var visualPos = CalculateItemPosition(1f);
            LogConveyor($"CompleteTransfer from {position} to {targetPos} tick={Find.TickManager.TicksGame} - Visual Position: {visualPos}");

            if (HasRefuelableTarget(map, position, out var refuelComp))
            {
                if (refuelComp.IsFull) return;
                int fuelNeeded = refuelComp.GetFuelCountToFullyRefuel();

                var transferred = new List<Thing>();
                bool refueledAny = false;

                for (int i = carriedThings.Count - 1; i >= 0; i--)
                {
                    Thing thing = carriedThings[i];
                    if (refuelComp.Props.fuelFilter.Allows(thing))
                    {
                        int amountAvailable = thing.stackCount;
                        int amountToRefuel = Mathf.Min(fuelNeeded, amountAvailable);

                        var fuelToUse = thing.SplitOff(amountToRefuel);
                        refuelComp.Refuel(amountToRefuel);
                        refueledAny = true;
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

                if (carriedThings.Count == 0)
                {
                    ResetConveyorState();
                }
                else if (refueledAny)
                {
                    SetState(ConveyorState.Waiting, "RefuelComplete_HasItems");
                    cachedSelectedOutput = Rot4.Invalid;
                }
                else
                {
                    SetState(ConveyorState.Waiting, "RefuelableFull");
                    cachedSelectedOutput = Rot4.Invalid;
                }
                return;
            }

            if (targetBuilding is Building_Conveyor targetConveyor)
            {
                var transferred = new List<Thing>();

                for (int i = carriedThings.Count - 1; i >= 0; i--)
                {
                    Thing thing = carriedThings[i];
                    Selector_Deselect_Patch.transferringItems.Add(thing);

                    bool fullyTransferred = false;
                    for (int j = targetConveyor.carriedThings.Count - 1; j >= 0; j--)
                    {
                        Thing targetThing = targetConveyor.carriedThings[j];
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

                    if (!fullyTransferred && targetConveyor.HasSpace())
                    {
                        targetConveyor.innerContainer.TryAddOrTransfer(thing);
                        transferred.Add(thing);
                    }
                }

                for (int i = transferred.Count - 1; i >= 0; i--)
                {
                    innerContainer.Remove(transferred[i]);
                    Selector_Deselect_Patch.transferringItems.Remove(transferred[i]);
                }

                if (carriedThings.Count == 0)
                {
                    SetState(ConveyorState.Empty, "TransferComplete_Empty");
                    itemProgress = 0f;
                    lastItemProgress = 0f;
                    cachedSelectedOutput = Rot4.Invalid;
                }
                else
                {
                    SetState(ConveyorState.Waiting, "TransferComplete_HasItems");
                }
                if (targetConveyor.state == ConveyorState.Empty && targetConveyor.carriedThings.Count > 0)
                {
                    targetConveyor.SetState(ConveyorState.Waiting, "TargetReceivedItems");
                }
            }
            else if (targetBuilding is Building_FactoryHopper hopper)
            {
                var transferred = new List<Thing>();

                for (int i = carriedThings.Count - 1; i >= 0; i--)
                {
                    Thing thing = carriedThings[i];
                    Selector_Deselect_Patch.transferringItems.Add(thing);

                    bool fullyTransferred = false;
                    var heldThings = hopper.slotGroup.HeldThings.ToList();
                    for (int j = heldThings.Count - 1; j >= 0; j--)
                    {
                        Thing targetThing = heldThings[j];
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

                if (carriedThings.Count == 0)
                {
                    ResetConveyorState();
                }
                else
                {
                    SetState(ConveyorState.Waiting, "HopperTransfer_HasItems");
                    cachedSelectedOutput = Rot4.Invalid;
                }
            }
            else
            {
                var dumped = new List<Thing>();
                LogConveyor($"Attempting to dump {carriedThings.Count} stacks to ground");

                for (int i = carriedThings.Count - 1; i >= 0; i--)
                {
                    Thing item = carriedThings[i];
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
                                    dumped.Add(item);
                                    fullyMerged = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!fullyMerged)
                    {
                        int currentItems = thingList.Count(t => t.def.category == ThingCategory.Item);
                        if (currentItems < targetPos.GetMaxItemsAllowedInCell(map))
                        {
                            if (GenPlace.TryPlaceThing(item, targetPos, map, ThingPlaceMode.Direct))
                            {
                                dumped.Add(item);
                            }
                        }
                    }
                }

                for (int i = dumped.Count - 1; i >= 0; i--)
                {
                    innerContainer.Remove(dumped[i]);
                    Selector_Deselect_Patch.transferringItems.Remove(dumped[i]);
                }

                if (carriedThings.Count == 0)
                {
                    ResetConveyorState();
                }
                else
                {
                    SetState(ConveyorState.Waiting, "DumpComplete_HasItems");
                    cachedSelectedOutput = Rot4.Invalid;
                }
            }
        }

        private void TryDumpItem(IntVec3 targetPos, Map map)
        {
            if (CanDumpToCell(targetPos, map))
            {
                if (itemProgress >= 0.999f && carriedThings.Count > 0)
                {
                    CompleteTransfer(map, Position);
                }
                else if (!CanFullyDump(targetPos, map))
                {
                    CompleteTransfer(map, Position);
                }
                else
                {
                    InitiateTransfer();
                }
            }
            else
            {
                SetState(ConveyorState.Waiting, "CannotDumpToCell");
            }
        }

        private bool CanFullyDump(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map)) return false;

            Building edifice = cell.GetEdifice(map);
            if (edifice is Building_FactoryHopper hopper)
            {
                foreach(var thing in carriedThings)
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
                return comp.GetFuelCountToFullyRefuel() >= carriedThings.Sum(t => t.stackCount);
            }
            else
            {
                List<Thing> thingList = map.thingGrid.ThingsListAt(cell);
                int currentItems = thingList.Count(t => t.def.category == ThingCategory.Item);
                bool hasEmptySpace = currentItems < cell.GetMaxItemsAllowedInCell(map);

                foreach (var thing in carriedThings)
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
            return carriedThings.Count < Props.itemsPerCell;
        }

        public bool CanAcceptItem(Thing item)
        {
            if (state == ConveyorState.Moving)
                return false;

            if (HasSpace())
                return true;

            foreach (var existing in carriedThings)
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

            for (int i = carriedThings.Count - 1; i >= 0; i--)
            {
                Thing existing = carriedThings[i];
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

        private bool DetectTurn()
        {

            if (OutputCount > 1) return false;

            if (OutputCount == 1 && cachedSingleOutput.HasValue && cachedSingleOutput.Value.IsValid && cachedSingleOutput.Value != Rotation)
            {
                return true;
            }

            if (TryFindInputConveyor(out var inputDir))
            {
                if (inputDir != Rotation.Opposite)
                    return true;
            }

            return false;
        }

        private int CountInputs()
        {
            isRecaching = true;
            Map map = Map;
            IntVec3 position = Position;
            int count = 0;
            foreach (var dir in PossibleInputDirections())
            {
                IntVec3 neighborPos = position + dir.FacingCell;
                if (!neighborPos.InBounds(map)) continue;

                Building neighbor = neighborPos.GetFirstBuilding(map);
                if (neighbor is Building_Conveyor neighborComp && neighborComp.ForwardCell == position)
                    count++;
            }
            isRecaching = false;
            return count;
        }

        public virtual IEnumerable<Rot4> PossibleInputDirections()
        {
            yield return Rotation.Opposite;
            yield return Rotation.Rotated(RotationDirection.Clockwise);
            yield return Rotation.Rotated(RotationDirection.Counterclockwise);
        }

        public IEnumerable<Rot4> PossibleOutputDirections()
        {
            yield return Rotation;
            yield return Rotation.Rotated(RotationDirection.Clockwise);
            yield return Rotation.Rotated(RotationDirection.Counterclockwise);
        }

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
                List<GameCondition> conditions = new List<GameCondition>();
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
                    if (comp != null && (int)comp.Stage < 2 && Rand.Value < 0.0065f)
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

        private string DetermineDirectionalGraphic(string baseName, List<Rot4> dirs)
        {
            if (dirs.Count != 2)
                return baseName;

            string prefix = dirs[0].ToStringWord() + dirs[1].ToStringWord();
            var suffix = Rotation.ToStringWord().ToLower();
            return $"{baseName}_{prefix}_{suffix}";
        }

        private Graphic DetermineGraphic()
        {
            if (string.IsNullOrEmpty(Props.baseTexPath))
                return base.Graphic;

            string name;
            bool isSingle = false;

            if (IsSplitter)
            {
                string baseName = HasFilterRestrictions() ? "ConveyorFilter" : "ConveyorSplitter";
                var outputs = CanonicalOrder.Where(IsValidOutput).ToList();
                name = DetermineDirectionalGraphic(baseName, outputs);
                isSingle = outputs.Count == 2;
            }
            else if (IsMerger)
            {
                Map map = Map;
                IntVec3 position = Position;
                var inputs = CanonicalOrder
                    .Where(dir =>
                    {
                        IntVec3 pos = position + dir.FacingCell;
                        if (!pos.InBounds(map)) return false;
                        var n = pos.GetFirstBuilding(map) as Building_Conveyor;
                        return n?.ForwardCell == position;
                    }).ToList();
                name = DetermineDirectionalGraphic("ConveyorMerger", inputs);
                isSingle = inputs.Count == 2;
            }
            else if (IsTurn)
            {
                name = DetermineTurnGraphic();
                isSingle = true;
            }
            else
            {
                name = HasRefuelableTarget(Map, Position, out var _) ? "ConveyorRefuelingPort" : "Conveyor";
            }

            string fullPath = Props.baseTexPath + "/" + name;

            if (isSingle)
            {
                cachedIsSingleDirectional = true;
                return GraphicDatabase.Get<Graphic_Single>(
                    fullPath,
                    ShaderDatabase.Cutout,
                    def.graphicData.drawSize,
                    Color.white
                );
            }
            cachedIsSingleDirectional = false;
            return GraphicDatabase.Get<Graphic_Multi>(
                fullPath,
                ShaderDatabase.Cutout,
                def.graphicData.drawSize,
                Color.white
            );
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

            if (OutputCount == 1 && cachedSingleOutput.HasValue && cachedSingleOutput.Value.IsValid)
            {
                outDir = cachedSingleOutput.Value;
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
            Map map = Map;
            IntVec3 position = Position;
            IntVec3 neighborPos = position + dir.FacingCell;
            if (!neighborPos.InBounds(map)) return false;

            var neighbor = neighborPos.GetFirstBuilding(map);
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

            isRecaching = false;
            return count;
        }

        private void SelectNextOutput()
        {
            var validOutputs = new List<Rot4>();
            foreach (var dir in PossibleOutputDirections())
            {
                if (IsValidOutput(dir))
                {
                    validOutputs.Add(dir);
                }
            }

            if (validOutputs.Count > 0)
            {
                Rot4 forwardDir = Rotation;
                bool allAllowed = true;
                for (int i = 0; i < carriedThings.Count; i++)
                {
                    if (!GetStoreSettings().AllowedToAccept(carriedThings[i]))
                    {
                        allAllowed = false;
                        break;
                    }
                }
                var forwardIsValid = validOutputs.Contains(forwardDir);

                if (allAllowed && forwardIsValid)
                {
                    cachedSelectedOutput = forwardDir;
                    return;
                }
                else if (!allAllowed && forwardIsValid && validOutputs.Count > 1)
                {
                    var nonForwardOutputs = new List<Rot4>();
                    for (int i = 0; i < validOutputs.Count; i++)
                    {
                        if (validOutputs[i] != forwardDir)
                        {
                            nonForwardOutputs.Add(validOutputs[i]);
                        }
                    }
                    if (nonForwardOutputs.Count > 0)
                    {
                        cachedSelectedOutput = nonForwardOutputs[splitterOutputIndex % nonForwardOutputs.Count];
                        splitterOutputIndex++;
                        return;
                    }
                }

                cachedSelectedOutput = validOutputs[splitterOutputIndex % validOutputs.Count];
                splitterOutputIndex++;
            }
        }

        public virtual bool ShowItems => HasRefuelableTarget(Map, Position, out _) is false;
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            var baseItemY = drawLoc.y + 1;
            if (IsMerger || IsSplitter || this is Building_UndergroundConveyorEntrance or Building_UndergroundConveyorExit || HasRefuelableTarget(Map, Position, out _))
            {
                drawLoc.y += 2f;
            }
            if (cachedIsSingleDirectional)
                Graphic.Draw(drawLoc, Rot4.North, this, 0f);
            else
                base.DrawAt(drawLoc, flip);
            if (ShowItems && carriedThings.Count > 0)
            {
                var itemPos = CalculateItemPosition(CalculateVisualProgress());
                itemPos.y = baseItemY;
                foreach (var thing in carriedThings)
                {
                    thing.Graphic.Draw(itemPos, Rot4.North, thing, 0f);
                }
            }
        }

        public Vector3 CalculateItemPosition(float progress)
        {
            Map map = Map;
            IntVec3 position = Position;

            if (progress < 0f)
            {
                Rot4 rotation = Rotation;
                Rot4 inputDir = Rot4.Invalid;

                foreach (var dir in PossibleInputDirections())
                {
                    IntVec3 neighborPos = position + dir.FacingCell;
                    if (neighborPos.InBounds(map))
                    {
                        if (neighborPos.GetFirstBuilding(map) is Building_FactoryHopper)
                        {
                            inputDir = dir;
                            break;
                        }
                    }
                }

                if (inputDir.IsValid)
                {
                    Vector3 hopperPos = position.ToVector3Shifted() + inputDir.FacingCell.ToVector3();
                    Vector3 beltStart = DrawPos;
                    return Vector3.Lerp(hopperPos, beltStart, progress + 1f);
                }
                return DrawPos;
            }

            Vector3 p0 = DrawPos;
            var p1 = ForwardCell.ToVector3Shifted();

            IntVec3 targetPos = ForwardCell;
            if (targetPos.InBounds(map))
            {
                var target = targetPos.GetFirstBuilding(map);
                if (target is Building_Conveyor nextConveyor && nextConveyor.IsTurn)
                {
                    p1 = DrawPos + ((ForwardCell - position).ToVector3() * 0.5f);
                }
            }

            if (IsTurn)
            {
                Rot4 inputDir;
                if (!TryFindInputConveyor(out inputDir))
                {
                    inputDir = Rotation.Opposite;
                }

                p0 = DrawPos + (inputDir.FacingCell.ToVector3() * 0.5f);

                Vector3 controlPoint = DrawPos;

                float t = progress;
                float u = 1 - t;

                return (u * u * p0) + (2 * u * t * controlPoint) + (t * t * p1);
            }
            return Vector3.Lerp(p0, p1, progress);
        }

        public override void DrawGUIOverlay()
        {
            base.DrawGUIOverlay();

            if (ShowItems && carriedThings.Count > 0 && Find.CameraDriver.CurrentZoom == CameraZoomRange.Closest)
            {
                var itemPos = CalculateItemPosition(CalculateVisualProgress());
                itemPos.y = AltitudeLayer.ItemImportant.AltitudeFor();

                foreach (var thing in carriedThings)
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
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos())
            {
                yield return g;
            }

            foreach (Thing carriedThing in carriedThings)
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
                        if (fwd.InBounds(Map) && fwd.GetFirstBuilding(Map) is Building_Conveyor fwdConv)
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
            foreach (var thing in carriedThings)
                sb.AppendLine($"  - x{thing.stackCount} {thing.def.defName}");

            sb.AppendLine($"Rotation: {Rotation.ToStringHuman()}, ForwardCell: {ForwardCell}");
            sb.AppendLine($"IsTurn: {IsTurn}, IsMerger: {IsMerger}, IsSplitter: {IsSplitter}");
            sb.AppendLine($"InputCount: {InputCount}, OutputCount: {OutputCount}");
            sb.AppendLine($"CachedSingleOutput: {(cachedSingleOutput.HasValue ? cachedSingleOutput.Value.ToStringHuman() : "None")}");

            sb.AppendLine("--- Neighbors ---");
            Map map = Map;
            IntVec3 position = Position;
            foreach (var dir in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                IntVec3 neighborPos = position + dir.FacingCell;
                if (!neighborPos.InBounds(map)) continue;
                Building n = neighborPos.GetFirstBuilding(map);
                if (n is Building_Conveyor nc)
                {
                    bool feedsIntoUs = nc.ForwardCell == position;
                    bool weCanFeedIt = nc.CanAcceptFrom(position);
                    bool weFeedIt = ForwardCell == neighborPos;
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
                IntVec3 neighborPos = position + dir.FacingCell;
                if (!neighborPos.InBounds(map)) continue;
                if (neighborPos.GetFirstBuilding(map) is Building_Conveyor nc && nc.ForwardCell == position)
                {
                    inputCount++;
                    foundInput = dir;
                }
            }
            sb.AppendLine($"  RawInputCount: {inputCount}, RawInputDir: {foundInput.ToStringHuman()}");

            IntVec3 fwdCell = position + Rotation.FacingCell;
            bool fwdEstablished = fwdCell.InBounds(map) &&
                fwdCell.GetFirstBuilding(map) is Building_Conveyor fwdC && fwdC.CanAcceptFrom(position);
            sb.AppendLine($"  ForwardEstablished: {fwdEstablished}");
            sb.AppendLine($"  StraightFromInput: {Rotation == foundInput.Opposite}");

            var downstreamReason = CanMoveDownstream(Map, Position);
            sb.AppendLine($"CanMoveDownstream: {downstreamReason == DownstreamBlockReason.None} ({downstreamReason})");
            sb.AppendLine($"HasSpace: {HasSpace()}");

            return sb.ToString().TrimEndNewlines();
        }
    }
}
