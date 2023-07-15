using System;
using System.Collections.Generic;
using System.Linq;
using HydroponicsExpanded.Enums;
using HydroponicsExpanded.ModExtension;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace HydroponicsExpanded {
    public class BuildingDenseHydroponicsBasin : Building_PlantGrower, IThingHolder, IPlantToGrowSettable {
        private ThingOwner _innerContainer;
        private int _capacity = 4;
        private float _fertility = 1.0f;
        private float _highestGrowth = 0f;
        private HydroponicsStage _stage = HydroponicsStage.Sowing;

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

                // Otherwise, we move the plant underground.
                plant.DeSpawn();
                TryAcceptThing(plant);

                // Recalculate capacity.
                capacityReached = _innerContainer.Count >= _capacity;
            }

            // If the maximum capacity is reached, then we should move to the growing stage.
            if (capacityReached) {
                SoundStarter.PlayOneShot(SoundDefOf.CryptosleepCasket_Accept,
                    new TargetInfo(Position, Map));
                _stage = HydroponicsStage.Grow;
            }
        }

        private void GrowTick() {
            // If there are no plants stored, reset the growth % and move back to the sowing stage.
            if (_innerContainer.Count == 0) {
                _stage = HydroponicsStage.Sowing;
                _highestGrowth = 0f;
                return;
            }

            // Despite the name, this is just a power check. We don't want to grow the plants if there is no power.
            if (!base.CanAcceptSowNow()) return;

            // Temperature & time of day check.
            float temperature = Position.GetTemperature(Map);
            if (!(temperature > 10f) || !(temperature < 42f) ||
                !(GenLocalDate.DayPercent(this) > 0.25f) || !(GenLocalDate.DayPercent(this) < 0.8f)) return;

            // The first plant in the container is the 'tracked' plant.
            var growthTrackingPlant = _innerContainer[0] as Plant;

            // ReSharper disable once PossibleNullReferenceException
            float growthAmount = 1f / (60_000f * growthTrackingPlant.def.plant.growDays) * 250f;
            growthTrackingPlant.Growth += _fertility * growthAmount;
            _highestGrowth = growthTrackingPlant.Growth;

            // When growth is complete, move to the harvesting stage.
            if (growthTrackingPlant.LifeStage == PlantLifeStage.Mature)
                _stage = HydroponicsStage.Harvest;
        }

        private void HarvestTick() {
            // Try to place every plant in the container in any cell.
            foreach (Thing nextInnerThing in _innerContainer) {
                var nextPlant = (Plant)nextInnerThing;

                int occupiedCells = 0;

                foreach (IntVec3 current in this.OccupiedRect()) {
                    List<Thing> list = Map.thingGrid.ThingsListAt(current);

                    // Skip this cell if it's occupied by another plant.
                    if (list.OfType<Plant>().Any()) {
                        occupiedCells++;
                        continue;
                    }

                    nextPlant.Growth = 1f;
                    _innerContainer.TryDrop(nextPlant, current, Map, ThingPlaceMode.Direct, out _);
                    break;
                }

                if (occupiedCells >= 4)
                    break;
            }

            // All plants have been harvested. Switch back to sowing stage.
            if (_innerContainer.Count == 0)
                _stage = HydroponicsStage.Sowing;
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
            HydroponicsStage initialStage = _stage;
            TickStage(_stage);

            // If the stage changed, re-run the next tick. This can allow for instant Grow -> Harvest transition.
            if (_stage != initialStage)
                TickStage(_stage);

            // Apply rotting damage to all plants while power is cut.
            if (!base.CanAcceptSowNow())
                foreach (Thing thing in _innerContainer)
                    ((Plant)thing).TakeDamage(new DamageInfo(DamageDefOf.Rotting, 1f));
        }

        public override void Draw() {
            base.Draw();

            // TODO: Shouldn't this be checking the bay grow stage?
            if (_innerContainer.Count < _capacity || !base.CanAcceptSowNow()) return;

            var fillableBar = default(GenDraw.FillableBarRequest);

            fillableBar.center = DrawPos + Vector3.up * 0.1f;
            fillableBar.size = new Vector2(3.6f, 0.6f);
            fillableBar.fillPercent = _highestGrowth;
            fillableBar.filledMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.2f, 0.85f, 0.2f));
            fillableBar.unfilledMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.3f, 0.3f, 0.3f));
            fillableBar.margin = 0.15f;

            Rot4 rotation = Rotation;
            rotation.Rotate(RotationDirection.Clockwise);
            fillableBar.rotation = rotation;

            GenDraw.DrawFillableBar(fillableBar);
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
            inspectString += "HydroponicsExpanded.OccupiedBays".Translate() + $": {_innerContainer.Count()}";

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
    }
}