using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
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
    public class Building_Conveyor : Building, IStoreSettingsParent
    {
        [TweakValue("0VFEFactory", 0.5f, 2.0f)]
        public static float curveSpeedMultiplier = 0.8f;

        public List<Thing> carriedThings = new List<Thing>();
        public float itemProgress;
        private float lastItemProgress;
        public enum ConveyorState { Empty, Moving, Waiting }
        public ConveyorState state = ConveyorState.Empty;
        private bool? cachedIsTurn;
        private int? cachedInputCount;
        private int? cachedOutputCount;
        private Rot4? cachedSelectedOutput;
        private int splitterOutputIndex;
        private StorageSettings storageSettings;
        private Graphic cachedGraphic;
        private ConveyorExtension cachedProps;
        private bool isRecaching = false;
        private ConveyorExtension Props
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
            //if (carriedThings.Any(x => x.def == ThingDefOf.ComponentIndustrial))
            {
                //Log.Message($"{message} | State: {state} | Progress: {itemProgress} | Carried: {carriedThings.ToStringSafeEnumerable()}");
            }
            //Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        }

        public IntVec3 ForwardCell
        {
            get
            {
                if (IsSplitter && cachedSelectedOutput.HasValue && cachedSelectedOutput.Value.IsValid)
                {
                    return Position + cachedSelectedOutput.Value.FacingCell;
                }
                return Position + Rotation.FacingCell;
            }
        }

        public bool IsTurn
        {
            get
            {
                if (cachedIsTurn == null)
                    cachedIsTurn = DetectTurn();
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

        public bool IsSplitter => InputCount >= 1 && OutputCount > 1 && IsMerger is false;
        public bool IsMerger => InputCount > 1 && IsSplitter is false;

        private void InvalidateCache()
        {
            cachedIsTurn = null;
            cachedInputCount = null;
            cachedOutputCount = null;
            cachedGraphic = null;
        }

        private void InvalidateNeighborCaches()
        {
            foreach (IntVec3 adj in GenAdj.CellsAdjacentCardinal(this))
            {
                if (adj.InBounds(Map))
                {
                    Building adjacentBuilding = adj.GetFirstBuilding(Map);
                    if (adjacentBuilding is Building_Conveyor neighbor)
                    {
                        neighbor.InvalidateCache();
                        Map.mapDrawer.MapMeshDirty(neighbor.Position, MapMeshFlagDefOf.Things);
                    }
                }
            }
        }

        private bool CanStackWithAny(List<Thing> carried, List<Thing> target)
        {
            foreach (Thing thing in carried)
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
            foreach (Rot4 dir in PossibleInputDirections())
            {
                IntVec3 neighborPos = Position + dir.FacingCell;
                if (neighborPos.InBounds(Map))
                {
                    Building neighbor = neighborPos.GetFirstBuilding(Map);
                    if (neighbor is Building_Conveyor neighborComp && neighborComp.ForwardCell == Position)
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
            if (state == ConveyorState.Moving)
            {
                float tickFraction = Find.TickManager.TickRateMultiplier * Time.deltaTime * 60f;
                return Mathf.Lerp(lastItemProgress, itemProgress, tickFraction);
            }
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
                foreach (Thing carriedThing in carriedThings.ToList())
                {
                    GenSpawn.Spawn(carriedThing, position, map);
                }
                carriedThings.Clear();
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
            if (this.IsHashIntervalTick(10))
            {
                CheckForItemsOnCell();
            }

            if (carriedThings.Any() && state == ConveyorState.Moving)
            {
                if (CanMoveDownstream() != DownstreamBlockReason.None)
                {
                    state = ConveyorState.Waiting;
                }
                else
                {
                    float currentPathLen = CalculatePathLength();
                    if (currentPathLen < 0.1f) currentPathLen = 0.1f;
                    float speedMod = 1f / currentPathLen;
                    if (IsTurn) speedMod *= curveSpeedMultiplier;

                    itemProgress += (1f / Props.ticksPerCell) * speedMod;

                    if (itemProgress >= 1.0f)
                        CompleteTransfer();
                }
                lastItemProgress = itemProgress;
            }
            else if (carriedThings.Any() && state == ConveyorState.Waiting && IsTurn && itemProgress < 0.5f)
            {
                float currentPathLen = CalculatePathLength();
                if (currentPathLen < 0.1f) currentPathLen = 0.1f;
                float speedMod = 1f / currentPathLen * curveSpeedMultiplier;
                itemProgress = Mathf.Min(itemProgress + (1f / Props.ticksPerCell) * speedMod, 0.5f);
                lastItemProgress = itemProgress;
            }
            else
            {
                ProcessMovement();
            }
        }

        private DownstreamBlockReason CanMoveDownstream()
        {
            IntVec3 targetPos = ForwardCell;
            if (!targetPos.InBounds(Map))
                return DownstreamBlockReason.TargetOutOfBounds;

            Building targetBuilding = targetPos.GetFirstBuilding(Map);

            if (targetBuilding is Building_Conveyor targetConveyor)
            {
                if (!CanTransferToTarget(targetConveyor))
                {
                    if (targetConveyor.state == ConveyorState.Moving)
                        return DownstreamBlockReason.TargetMoving;
                    return DownstreamBlockReason.TargetFull;
                }

                float aheadProgress = float.MinValue;

                foreach (IntVec3 adj in GenAdj.CellsAdjacent8Way(targetConveyor))
                {
                    if (!adj.InBounds(Map)) continue;
                    Building adjBuilding = adj.GetFirstBuilding(Map);
                    if (adjBuilding is Building_Conveyor adjConveyor
                        && adjConveyor != this
                        && adjConveyor.ForwardCell == targetPos
                        && adjConveyor.carriedThings.Any()
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

                foreach (IntVec3 adj in GenAdj.CellsAdjacentCardinal(targetConveyor))
                {
                    if (!adj.InBounds(Map)) continue;
                    Building adjBuilding = adj.GetFirstBuilding(Map);
                    if (adjBuilding is Building_Conveyor adjConveyor
                        && adjConveyor != this
                        && adjConveyor.ForwardCell == targetPos
                        && adjConveyor.carriedThings.Any())
                    {
                        if (!CanStackWithAny(carriedThings, targetConveyor.carriedThings))
                        {
                            if (adjConveyor.itemProgress > itemProgress ||
                                (adjConveyor.itemProgress == itemProgress &&
                                 adjConveyor.Position.GetHashCode() > Position.GetHashCode()))
                            {
                                return DownstreamBlockReason.CompetitorTying;
                            }
                        }
                    }
                }
                return DownstreamBlockReason.None;
            }
            else
            {
                if (!targetPos.Walkable(Map))
                    return DownstreamBlockReason.CellNotWalkable;

                if (!CanDumpToCell(targetPos))
                    return DownstreamBlockReason.CellFull;

                return DownstreamBlockReason.None;
            }
        }

        private bool CanDumpToCell(IntVec3 cell)
        {
            if (!cell.InBounds(Map))
            {
                return false;
            }

            if (!cell.Walkable(Map))
            {
                return false;
            }

            bool canStack = false;
            bool hasRoom = false;
            List<Thing> cellThings = cell.GetThingList(Map);
            foreach (Thing existing in cellThings)
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

            int currentItemCount = cell.GetItemCount(Map);
            int maxItems = cell.GetMaxItemsAllowedInCell(Map);

            if (!canStack && currentItemCount >= maxItems)
            {
                return false;
            }

            if (canStack && !hasRoom && currentItemCount >= maxItems)
            {
                return false;
            }

            Building edifice = cell.GetEdifice(Map);
            if (edifice is IHaulDestination dest)
            {
                foreach (Thing thing in carriedThings)
                {
                    if (!dest.Accepts(thing))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private void ProcessMovement()
        {
            if (HasSpace() && state != ConveyorState.Waiting)
            {
                TryPullItem();
            }

            if (carriedThings.Any() && state != ConveyorState.Moving)
            {
                IntVec3 targetPos = ForwardCell;
                if (!targetPos.InBounds(Map))
                {
                    state = ConveyorState.Waiting;
                    return;
                }

                Building targetBuilding = targetPos.GetFirstBuilding(Map);
                if (targetBuilding is Building_Conveyor)
                {
                    if (CanMoveDownstream() == DownstreamBlockReason.None)
                    {
                        InitiateTransfer();
                    }
                    else
                    {
                        state = ConveyorState.Waiting;
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
            if (state == ConveyorState.Waiting)
            {
                return;
            }

            foreach (Rot4 inputDir in PossibleInputDirections())
            {
                IntVec3 inputPos = Position + inputDir.FacingCell;
                if (!inputPos.InBounds(Map)) continue;

                if (inputPos.GetFirstBuilding(Map) is Building_FactoryHopper hopper)
                {
                    if (hopper.slotGroup.HeldThings.FirstOrDefault() is Thing item)
                    {
                        Thing taken = item.SplitOff(item.stackCount);
                        taken.DeSpawn(DestroyMode.Vanish);
                        carriedThings.Add(taken);
                        ResetConveyorState();
                        return;
                    }
                }
            }
        }

        private void CheckForItemsOnCell()
        {
            if (!HasSpace())
            {
                return;
            }

            List<Thing> thingsOnCell = Map.thingGrid.ThingsListAt(Position);
            foreach (Thing thing in thingsOnCell.ToList())
            {
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

                    carriedThings.Add(thing);
                    ResetConveyorState();
                }
            }
        }

        private bool CanTransferToTarget(Building_Conveyor target)
        {
            if (target.state == ConveyorState.Moving)
                return false;

            if (target.HasSpace())
                return true;

            return CanStackWithAny(carriedThings, target.carriedThings);
        }

        private void InitiateTransfer()
        {
            if (IsSplitter)
            {
                SelectNextOutput();
            }
            state = ConveyorState.Moving;
            LogConveyor("InitiateTransfer");
        }

        private void CompleteTransfer()
        {
            if (carriedThings.Count == 0) return;

            IntVec3 targetPos = ForwardCell;
            Building targetBuilding = targetPos.GetFirstBuilding(Map);

            if (targetBuilding is Building_Conveyor targetConveyor)
            {
                List<Thing> transferred = new List<Thing>();

                foreach (Thing thing in carriedThings.ToList())
                {
                    bool fullyTransferred = false;
                    foreach (Thing targetThing in targetConveyor.carriedThings.ToList())
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

                    if (!fullyTransferred && targetConveyor.HasSpace())
                    {
                        targetConveyor.carriedThings.Add(thing);
                        transferred.Add(thing);
                    }
                }

                foreach (Thing t in transferred)
                {
                    carriedThings.Remove(t);
                }

                if (carriedThings.Count == 0)
                {
                    state = ConveyorState.Empty;
                    itemProgress = 0f;
                    lastItemProgress = 0f;
                    cachedSelectedOutput = Rot4.Invalid;
                }
                else
                {
                    state = ConveyorState.Waiting;
                }
                if (targetConveyor.state == ConveyorState.Empty && targetConveyor.carriedThings.Any())
                {
                    targetConveyor.state = ConveyorState.Waiting;
                }
            }
            else
            {
                List<Thing> dumped = new List<Thing>();
                LogConveyor($"Attempting to dump {carriedThings.Count} stacks to ground");

                foreach (Thing item in carriedThings.ToList())
                {
                    if (GenPlace.TryPlaceThing(item, targetPos, Map, ThingPlaceMode.Direct))
                    {
                        dumped.Add(item);
                    }
                    else
                    {
                        LogConveyor($"Failed to place {item.Label}. Cell full or blocked");
                    }
                }

                foreach (Thing t in dumped)
                {
                    carriedThings.Remove(t);
                }

                if (carriedThings.Count == 0)
                {
                    ResetConveyorState();
                }
                else
                {
                    state = ConveyorState.Waiting;
                    itemProgress = 0f;
                    cachedSelectedOutput = Rot4.Invalid;
                }
            }
        }

        private void TryDumpItem(IntVec3 targetPos)
        {
            if (CanDumpToCell(targetPos))
            {
                if (itemProgress >= 1.0f && carriedThings.Count > 0)
                {
                    CompleteTransfer();
                }
                else
                {
                    InitiateTransfer();
                }
            }
            else
            {
                state = ConveyorState.Waiting;
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

            foreach (Thing existing in carriedThings)
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

            foreach (Thing existing in carriedThings.ToList())
            {
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

                carriedThings.Add(thing);
                ResetConveyorState();
                return true;
            }

            return false;
        }

        private bool DetectTurn()
        {
            if (InputCount > 1) return false;
            if (TryFindInputConveyor(out var inputDir))
                return inputDir != Rotation.Opposite;
            return false;
        }

        private int CountInputs()
        {
            isRecaching = true;
            int count = 0;
            foreach (Rot4 dir in PossibleInputDirections())
            {
                IntVec3 neighborPos = Position + dir.FacingCell;
                if (!neighborPos.InBounds(Map)) continue;

                Building neighbor = neighborPos.GetFirstBuilding(Map);
                if (neighbor is Building_Conveyor neighborComp && neighborComp.ForwardCell == Position)
                    count++;
            }
            isRecaching = false;
            return count;
        }

        public IEnumerable<Rot4> PossibleInputDirections()
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
            foreach (Rot4 inputDir in PossibleInputDirections())
            {
                if (Position + inputDir.FacingCell == fromPos)
                    return true;
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref carriedThings, "carriedThings", LookMode.Deep);
            Scribe_Values.Look(ref itemProgress, "itemProgress", 0f);
            Scribe_Values.Look(ref lastItemProgress, "lastItemProgress", 0f);
            Scribe_Values.Look(ref state, "state", ConveyorState.Empty);
            Scribe_Values.Look(ref splitterOutputIndex, "splitterOutputIndex", 0);
            Scribe_Deep.Look(ref storageSettings, "storageSettings", this);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (carriedThings == null)
                    carriedThings = new List<Thing>();
                if (storageSettings == null)
                {
                    storageSettings = new StorageSettings(this);
                    storageSettings.CopyFrom(StorageSettings.EverStorableFixedSettings());
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
            string path = Props.baseTexPath + "/";

            if (IsSplitter)
            {
                if (HasFilterRestrictions())
                {
                    path += "ConveyorFilter";
                }
                else
                {
                    path += "ConveyorSplitter";
                }
            }
            else if (IsMerger)
            {
                path += "ConveyorMerger";
            }
            else if (IsTurn)
            {
                path += DetermineTurnGraphic();
            }
            else
            {
                path += "Conveyor";
            }

            return GraphicDatabase.Get<Graphic_Multi>(
                path,
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
            if (TryFindInputConveyor(out var inputDir))
            {
                Rot4 itemEnterDirection = inputDir.Opposite;
                if (itemEnterDirection != Rotation && itemEnterDirection != Rotation.Opposite)
                {
                    int fromRot = itemEnterDirection.AsInt;
                    int toRot = Rotation.AsInt;
                    int diff = (toRot - fromRot + 4) % 4;

                    if (diff == 1)
                    {
                        return "ConveyorTurnRight";
                    }
                    else if (diff == 3)
                    {
                        return "ConveyorTurnLeft";
                    }
                }
            }

            return "Conveyor";
        }

        private bool IsValidOutput(Rot4 dir)
        {
            IntVec3 neighborPos = Position + dir.FacingCell;
            if (!neighborPos.InBounds(Map)) return false;

            Building neighbor = neighborPos.GetFirstBuilding(Map);
            if (neighbor is Building_Conveyor neighborConveyor && neighborConveyor.CanAcceptFrom(Position) && neighborConveyor.IsTurn is false)
            {
                if (neighbor.Position == Position + Rotation.FacingCell && neighbor.Rotation == Rotation || (neighborConveyor.Rotation != Rotation && neighborConveyor.Rotation != Rotation.Opposite))
                {
                    return true;
                }
            }
            return false;
        }

        private int CountOutputs()
        {
            isRecaching = true;
            int count = 0;
            foreach (Rot4 dir in PossibleOutputDirections())
            {
                if (IsValidOutput(dir))
                {
                    count++;
                }
            }
            isRecaching = false;
            return count;
        }

        private void SelectNextOutput()
        {
            var validOutputs = new List<Rot4>();
            foreach (Rot4 dir in PossibleOutputDirections())
            {
                if (IsValidOutput(dir))
                {
                    validOutputs.Add(dir);
                }
            }

            if (validOutputs.Any())
            {
                Rot4 forwardDir = Rotation;
                bool allAllowed = carriedThings.All(t => GetStoreSettings().AllowedToAccept(t));
                bool forwardIsValid = validOutputs.Contains(forwardDir);

                if (allAllowed && forwardIsValid)
                {
                    cachedSelectedOutput = forwardDir;
                    return;
                }
                else if (!allAllowed && forwardIsValid && validOutputs.Count > 1)
                {
                    var nonForwardOutputs = validOutputs.Where(d => d != forwardDir).ToList();
                    if (nonForwardOutputs.Any())
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

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            var baseItemY = drawLoc.y + 1;
            if (IsMerger || IsSplitter)
            {
                drawLoc.y += 2f;
            }
            base.DrawAt(drawLoc, flip);
            if (carriedThings.Any())
            {
                Vector3 itemPos = CalculateItemPosition(CalculateVisualProgress());
                itemPos.y = baseItemY;
                foreach (Thing thing in carriedThings)
                {
                    thing.Graphic.Draw(itemPos, Rot4.North, thing, 0f);
                }
            }
        }

        private Vector3 CalculateItemPosition(float progress)
        {
            Vector3 p0 = DrawPos;
            Vector3 p1 = ForwardCell.ToVector3Shifted();

            IntVec3 targetPos = ForwardCell;
            if (targetPos.InBounds(Map))
            {
                Building target = targetPos.GetFirstBuilding(Map);
                if (target is Building_Conveyor nextConveyor && nextConveyor.IsTurn)
                {
                    p1 = DrawPos + (Rotation.FacingCell.ToVector3() * 0.5f);
                }
            }

            if (IsTurn && TryFindInputConveyor(out Rot4 inputDir))
            {
                p0 = DrawPos + (inputDir.FacingCell.ToVector3() * 0.5f);

                Vector3 controlPoint = DrawPos;

                float t = progress;
                float u = 1 - t;

                return (u * u * p0) + (2 * u * t * controlPoint) + (t * t * p1);
            }
            return Vector3.Lerp(p0, p1, progress);
        }

        private float CalculatePathLength()
        {
            Vector3 p0 = new Vector3(DrawPos.x, 0, DrawPos.z);
            Vector3 p1 = ForwardCell.ToVector3Shifted();
            p1.y = 0;

            IntVec3 targetPos = ForwardCell;
            if (targetPos.InBounds(Map))
            {
                Building target = targetPos.GetFirstBuilding(Map);
                if (target is Building_Conveyor nextConveyor && nextConveyor.IsTurn)
                {
                    p1 = new Vector3(DrawPos.x, 0, DrawPos.z) + (Rotation.FacingCell.ToVector3() * 0.5f);
                }
            }

            if (IsTurn && TryFindInputConveyor(out Rot4 inputDir))
            {
                p0 = new Vector3(DrawPos.x, 0, DrawPos.z) + (inputDir.FacingCell.ToVector3() * 0.5f);
                Vector3 controlPoint = new Vector3(DrawPos.x, 0, DrawPos.z);

                float chord = Vector3.Distance(p0, p1);
                float cont_net = Vector3.Distance(p0, controlPoint) + Vector3.Distance(controlPoint, p1);

                float estimatedLength = (2f * chord + cont_net) / 3f;

                return estimatedLength;
            }

            return Vector3.Distance(p0, p1);
        }

        public override void DrawGUIOverlay()
        {
            base.DrawGUIOverlay();

            if (carriedThings.Any() && Find.CameraDriver.CurrentZoom == CameraZoomRange.Closest)
            {
                Vector3 itemPos = CalculateItemPosition(CalculateVisualProgress());
                itemPos.y = AltitudeLayer.ItemImportant.AltitudeFor();

                foreach (Thing thing in carriedThings)
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
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
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
                        List<FloatMenuOption> options = new List<FloatMenuOption>
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
            sb.AppendLine($"State: {state}, ItemProgress: {itemProgress} (last: {lastItemProgress})");
            foreach (var thing in carriedThings)
            {
                sb.AppendLine($"  - x{thing.stackCount} {thing.def.defName}");
            }
            sb.AppendLine($"IsTurn: {IsTurn}, InputCount: {InputCount}, OutputCount: {OutputCount}");
            var downstreamReason = CanMoveDownstream();
            sb.AppendLine($"CanMoveDownstream: {downstreamReason == DownstreamBlockReason.None} ({downstreamReason}), CanDumpToForwardCell: {CanDumpToCell(ForwardCell)}");
            IntVec3 fwd = ForwardCell;
            if (fwd.InBounds(Map) && fwd.GetFirstBuilding(Map) is Building_Conveyor fwdConv)
            {
                sb.AppendLine($"CanStackWithNext: {CanStackWithAny(carriedThings, fwdConv.carriedThings)}");
                sb.AppendLine($"CanTransferToNext: {CanTransferToTarget(fwdConv)}");
                foreach (Thing mine in carriedThings)
                    foreach (Thing theirs in fwdConv.carriedThings)
                        sb.AppendLine($"  CanStackWith({mine.def.defName} hp={mine.HitPoints} vs hp={theirs.HitPoints}): {mine.CanStackWith(theirs)}");
            }
            sb.AppendLine($"HasSpace: {HasSpace()}");
            return sb.ToString().TrimEndNewlines();
        }
    }
}
