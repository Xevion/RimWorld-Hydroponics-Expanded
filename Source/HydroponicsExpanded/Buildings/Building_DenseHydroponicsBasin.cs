using System;
using System.Collections.Generic;
using System.Linq;
using HydroponicsExpanded.Enums;
using HydroponicsExpanded.ModExtension;
using HydroponicsExpanded.Utility;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace HydroponicsExpanded {
    [StaticConstructorOnStartup]
    public class BuildingDenseHydroponicsBasin : Building_PlantGrower, IThingHolder, IPlantToGrowSettable {
        private ThingOwner _innerContainer;
        private int _capacity = 4; // Modified by CapacityExtension DefModExtension
        private float _highestGrowth = 0f;
        private CompPowerTrader _compPowerTrader;

        private HydroponicsStage _stage = HydroponicsStage.Sowing;

        public HydroponicsStage Stage {
            get => _stage;
            private set {
                switch (value) {
                    case HydroponicsStage.Sowing:
                    case HydroponicsStage.Harvest:
                        _compPowerTrader.PowerOutput = -_compPowerTrader.Props.idlePowerDraw;
                        break;
                    case HydroponicsStage.Grow:
                        _compPowerTrader.PowerOutput = -_compPowerTrader.Props.PowerConsumption;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                _stage = value;
            }
        }

        public BuildingDenseHydroponicsBasin() {
            _innerContainer = new ThingOwner<Thing>(this, false);
        }

        IEnumerable<IntVec3> IPlantToGrowSettable.Cells {
            get { return this.OccupiedRect().Cells; }
        }

        private void SowTick() {
            bool capacityReached = _innerContainer.Count >= _capacity;

            // TODO: Why is this here?
            foreach (Plant plant in PlantsOnMe) {
                // Blighted plants will be destroyed and not added to the internal container.
                // Once capacity is reached, all plants will be ignored.
                if (capacityReached || plant.Blighted) {
                    plant.Destroy();
                    continue;
                }

                // When plants are being sown, they are invisible, but we want to wait until they are sown before adding them to the internal container.
                // When plants are harvested, they are placed on top, but we don't want to take those. Therefore, we only want 'Growing' stage plants.
                if (plant.LifeStage != PlantLifeStage.Growing)
                    continue;

                // Otherwise, we move the plant underground.
                plant.DeSpawn();
                TryAcceptThing(plant);

                // Recalculate if capacity was reached
                capacityReached = _innerContainer.Count >= _capacity;
            }

            // If the maximum capacity is reached, then we should move to the growing stage.
            if (capacityReached) {
                Stage = HydroponicsStage.Grow;

                // Some plants might have been skipped, so go back and kill anything still on top.
                foreach (Plant plant in PlantsOnMe)
                    plant.Destroy();
                
                // Play the sound effect to signal the transition to the grow stage.
                SoundDefOf.CryptosleepCasket_Accept.PlayOneShot(new TargetInfo(Position, Map));

                // Set the highest growth to the tracked plant's growth, to ensure the bar is accurate.
                _highestGrowth = ((Plant)_innerContainer[0]).Growth;

                // Some active sowing jobs may be in progress for pathing, and thus will sow plants AFTER the stage
                // transition is made. This results in some weird looking random plants very rarely.
                // In order to fix this, we'll just remove all pending sowing jobs that relate to THIS hydroponics.
                CancelActiveJobs();
            }
        }

        private void GrowTick() {
            // If there are no plants stored, reset the growth % and move back to the sowing stage.
            if (_innerContainer.Count == 0) {
                Stage = HydroponicsStage.Sowing;
                _highestGrowth = 0f;
                return;
            }

            // Despite the name, this is just a power check. We don't want to grow the plants if there is no power.
            if (!base.CanAcceptSowNow()) return;


            // The first plant in the container is the 'tracked' plant.
            // This set/type check is for preventing NPE warnings. The previous .Count check implicitly makes this impossible, but the compiler doesn't know that.
            if (!(_innerContainer[0] is Plant growthTrackingPlant)) {
                Log.Message(
                    $"Unexpected thing in BuildingDenseHydroponicsBasin.innerContainer ({_innerContainer.GetType()})");
                return;
            }

            // Temperature & time of day check.
            float temperature = Position.GetTemperature(Map);
            if (temperature.Between(10f, 42f) && GenLocalDate.DayPercent(this).Between(0.25f, 0.8f)) {
                float growthAmount = 1f / (60_000f * growthTrackingPlant.def.plant.growDays) * 250f;

                // Debug gizmo can set growth to 100%, thus Math.min check here.
                growthTrackingPlant.Growth = Math.Min(1f, growthTrackingPlant.Growth + def.fertility * growthAmount);
                _highestGrowth = growthTrackingPlant.Growth;
            }

            // When growth is complete, move to the harvesting stage.
            // This is ran even if growing is not available as debug gizmo can push growth to 'mature' stage outside normal growing times.
            if (growthTrackingPlant.LifeStage == PlantLifeStage.Mature)
                Stage = HydroponicsStage.Harvest;
        }

        private void HarvestTick() {
            // var plantsLeft = _innerContainer.Count;
            // var potentialCellCount = this.OccupiedRect().Area;

            // Try to place every plant in the container in any cell.
            foreach (Thing nextInnerThing in _innerContainer) {
                var nextPlant = (Plant)nextInnerThing;

                int occupiedCells = 0;

                foreach (IntVec3 currentCell in this.OccupiedRect()) {
                    List<Thing> cellThings = Map.thingGrid.ThingsListAt(currentCell);

                    // Skip this cell if it's occupied by another plant.
                    if (cellThings.OfType<Plant>().Any()) {
                        occupiedCells++;
                        continue;
                    }

                    nextPlant.Growth = 1f;
                    _innerContainer.TryDrop(nextPlant, currentCell, Map, ThingPlaceMode.Direct, out _);
                    break;
                }

                if (occupiedCells >= this.OccupiedRect().Area)
                    break;
            }

            // Re-harvestable plants will be destroyed if we think they've been harvested recently.
            foreach (Plant plant in PlantsOnMe) {
                if (plant.def.plant.HarvestDestroys) continue;

                // Only consider re-harvestable plants eligible if they're still within 20% of their harvest growth level,
                // up to 90%. This may need tuning if there are harvestable plants that go to 90% growth.
                var minGrowth = plant.def.plant.harvestAfterGrowth;
                if (plant.Growth.Between(minGrowth, Math.Min(0.9f, minGrowth + 0.2f), inclusive: true))
                    plant.Destroy();
            }

            // All plants have been harvested. Switch back to sowing stage.
            if (_innerContainer.Count == 0)
                Stage = HydroponicsStage.Sowing;
        }

        private void TickStage(HydroponicsStage stage) {
            switch (stage) {
                case HydroponicsStage.Sowing:
                    SowTick();
                    break;
                case HydroponicsStage.Grow:
                    GrowTick();
                    break;
                case HydroponicsStage.Harvest:
                    HarvestTick();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(stage), "Unable to select stage tick for BuildingDenseHydroponicsBasin.TickRare.");
            }
        }

        public override void TickRare() {
            // Tick the current stage.
            HydroponicsStage initialStage = Stage;
            TickStage(Stage);

            // If the stage changed, re-run the next tick. This can allow for instant Grow -> Harvest transition.
            if (_stage != initialStage)
                TickStage(Stage);

            // Apply rotting damage to all plants while power is cut.
            if (!base.CanAcceptSowNow()) {
                foreach (Thing thing in _innerContainer)
                    ((Plant)thing).TakeDamage(new DamageInfo(DamageDefOf.Rotting, 1f));
                foreach (Plant plant in PlantsOnMe)
                    plant.TakeDamage(new DamageInfo(DamageDefOf.Rotting, 1f));
            }
        }

        /**
         * This method cancels all active sowing jobs that interact with this given hydroponics' growing zone.
         */
        private void CancelActiveJobs() {
            CellRect region = this.OccupiedRect();
            foreach (Pawn pawn in Map.mapPawns.AllPawnsSpawned) {
                // Prisoners can't do sow jobs (slaves could though)
                if (pawn.IsPrisoner) continue;

                foreach (Job job in pawn.jobs.AllJobs()) {
                    // Only worry about sowing jobs
                    if (job.def != JobDefOf.Sow) continue;
                    // Only care if it's in our hydroponics region
                    if (!region.Contains(job.targetA.Cell)) continue;

                    Log.Message($"Canceled a Sow Job at {job.targetA.Cell} for {pawn.NameFullColored}");

                    // Cancel the job, make sure it doesn't get regenerated
                    pawn.jobs.EndCurrentOrQueuedJob(job, JobCondition.Incompletable, false);
                }
            }
        }

        private static readonly Material HydroponicPoweredFillMaterial =
            SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.2f, 0.85f, 0.2f));

        private static readonly Material HydroponicUnpoweredFillMaterial =
            SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.82f, 0f, 0f));

        private static readonly Material HydroponicUnfilledMaterial =
            SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.3f, 0.3f, 0.3f));

        protected override void DrawAt(Vector3 drawLoc, bool flip=false) {
            base.DrawAt(drawLoc, flip);

            // Only draw growth percentage bar during Sowing stage
            if (_stage == HydroponicsStage.Grow) {
                var bar = new GenDraw.FillableBarRequest {
                    center = drawLoc + Vector3.up * 0.1f,
                    size = new Vector2(DrawSize.y - 0.4f, DrawSize.x - 0.4f),
                    margin = 0.15f,
                    fillPercent = _highestGrowth,
                    // Switch to red when no power is provided.
                    filledMat = base.CanAcceptSowNow()
                        ? HydroponicPoweredFillMaterial
                        : HydroponicUnpoweredFillMaterial,
                    unfilledMat = HydroponicUnfilledMaterial
                };

                Rot4 rotation = Rotation;
                rotation.Rotate(RotationDirection.Clockwise);
                bar.rotation = rotation;

                GenDraw.DrawFillableBar(bar);
            }
        }

        public override string GetInspectString() {
            string inspectString = base.GetInspectString();

            // Include information about the current growth stage of the basin
            inspectString += "\n";
            switch (_stage) {
                case HydroponicsStage.Sowing:
                    inspectString += "HydroponicsExpanded.SowingStage".Translate();
                    break;
                case HydroponicsStage.Grow:
                    inspectString += "HydroponicsExpanded.GrowStage".Translate();
                    break;
                case HydroponicsStage.Harvest:
                    inspectString += "HydroponicsExpanded.HarvestStage".Translate();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            inspectString += "\n";
            inspectString += "HydroponicsExpanded.OccupiedBays".Translate() +
                             $": {_innerContainer.Count()} / {_capacity}";

            if (_innerContainer.Count > 0) {
                inspectString += "\n";
                inspectString += "HydroponicsExpanded.Growth".Translate() + $": {_highestGrowth * 100f:#0}%";
            }

            return inspectString;
        }

        protected virtual bool TryAcceptThing(Thing thing) {
            // Make sure that the plant container can accept the plant Thing.
            if (!_innerContainer.CanAcceptAnyOf(thing))
                return false;

            // If the thing does not have an owner, just add it. Perhaps it was created in memory?
            if (thing.holdingOwner == null) return _innerContainer.TryAdd(thing);

            // Thing has an owner, so use TryTransferToContainer.
            thing.holdingOwner.TryTransferToContainer(thing, _innerContainer, thing.stackCount);
            return true;
        }


        /**
         * Only allows sowing in the 'sowing' stage. Otherwise, the hydroponics bay is 'sealed' while
         * all the plants are growing.
         */
        public new bool CanAcceptSowNow() {
            return base.CanAcceptSowNow() && _stage == HydroponicsStage.Sowing;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad) {
            base.SpawnSetup(map, respawningAfterLoad);

            _compPowerTrader = GetComp<CompPowerTrader>();

            var modExtension = def.GetModExtension<CapacityExtension>();
            if (modExtension != null) {
                _capacity = modExtension.capacity;
            }

            // Delete all plants underneath the hydroponics
            if (!respawningAfterLoad)
                foreach (Plant plant in PlantsOnMe) {
                    plant.Destroy();
                }
        }

        public void GetChildHolders(List<IThingHolder> outChildren) {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings() {
            // TODO: Why was the original mod returning 'null'? Is this the intended usage? Research.
            return _innerContainer;
        }

        public override void ExposeData() {
            base.ExposeData();
            Scribe_Deep.Look(ref _innerContainer, "innerContainer", this);
            Scribe_Values.Look(ref _stage, "growingStage", HydroponicsStage.Grow);
            Scribe_Values.Look(ref _highestGrowth, "highestGrowth");
        }


        public override void Destroy(DestroyMode mode = DestroyMode.Vanish) {
            _innerContainer.ClearAndDestroyContents();
            base.Destroy(mode);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish) {
            _innerContainer.ClearAndDestroyContents();
            foreach (Plant item in PlantsOnMe)
                item.Destroy();
            base.DeSpawn(mode);
        }

        public new void SetPlantDefToGrow(ThingDef plantDef) {
            base.SetPlantDefToGrow(plantDef);
            if (_stage != HydroponicsStage.Sowing) return;

            _innerContainer.ClearAndDestroyContents();
            foreach (Plant item in PlantsOnMe) item.Destroy();
        }

        public override IEnumerable<Gizmo> GetGizmos() {
            // Literally useless, but in case hydroponics get a developer gizmo, here we go.
            foreach (Gizmo gizmo in base.GetGizmos()) {
                yield return gizmo;
            }

            // Don't show gizmos unless in developer mode.
            if (!DebugSettings.ShowDevGizmos) {
                yield break;
            }

            // Developer trick to grow the 'first' plant internally, allowing growth tracking.
            if (_innerContainer.Count > 0) {
                var growTrackedAction = new Command_Action {
                    defaultLabel = "DEV: Grow tracked plant",
                    action = delegate {
                        var trackedPlant = (Plant)_innerContainer[0];
                        trackedPlant.Growth = 1f;
                    }
                };
                yield return growTrackedAction;
            }

            var clearContainedPlants = new Command_Action {
                defaultLabel = "DEV: Clear contained plants",
                action = delegate {
                    var i = 0;

                    while (_innerContainer.Count > 0) {
                        var plant = (Plant)_innerContainer[0];
                        plant.Destroy();
                        i++;
                    }

                    Messages.Message(new Message($"{i} plants destroyed.", MessageTypeDefOf.SilentInput, this));
                }
            };
            yield return clearContainedPlants;
        }
    }
}