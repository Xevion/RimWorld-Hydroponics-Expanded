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
        private int _capacity = 52;
        private float _fertility = 2.8f;
        private float _highestGrowth = 0f;
        private HydroponicsStage _stage = HydroponicsStage.Sowing;

        public BuildingDenseHydroponicsBasin() {
            _innerContainer = new ThingOwner<Thing>(this, false);
        }

        IEnumerable<IntVec3> IPlantToGrowSettable.Cells {
            get { return this.OccupiedRect().Cells; }
        }

        public override void TickRare() {
            int growingPlants = 0;
            foreach (Plant plant in PlantsOnMe) {
                if (plant.LifeStage != PlantLifeStage.Growing)
                    continue;

                growingPlants++;
                if (_innerContainer.Count >= _capacity)
                    plant.Destroy();
            }

            if (growingPlants >= 2) {
                foreach (Plant plant in PlantsOnMe) {
                    if (plant.LifeStage != PlantLifeStage.Growing || plant.Blighted) continue;
                    plant.DeSpawn();
                    TryAcceptThing(plant);

                    if (_innerContainer.Count < _capacity) continue;

                    // All plants were planted in hydroponics - switch to grow stage.
                    SoundStarter.PlayOneShot(SoundDefOf.CryptosleepCasket_Accept,
                        new TargetInfo(Position, Map));
                    _stage = HydroponicsStage.Grow;
                    break;
                }
            }

            if (_innerContainer.Count == 0) {
                _stage = HydroponicsStage.Sowing;
                _highestGrowth = 0f;
            }

            if (CanAcceptSowNow()) {
                float temperature = Position.GetTemperature(Map);
                if (_stage == HydroponicsStage.Grow && temperature > 10f && temperature < 42f &&
                    GenLocalDate.DayPercent(this) > 0.25f && GenLocalDate.DayPercent(this) < 0.8f) {
                    var plant = _innerContainer[0] as Plant;
                    float num2 = 1f / (60000f * plant.def.plant.growDays) * 250f;
                    plant.Growth += _fertility * num2;
                    _highestGrowth = plant.Growth;
                    if (plant.LifeStage == PlantLifeStage.Mature) {
                        _stage = HydroponicsStage.Harvest;
                    }
                }

                if (_stage != HydroponicsStage.Harvest)
                    return;

                {
                    var thing = default(Thing);
                    foreach (Thing thing1 in _innerContainer) {
                        var item3 = (Plant)thing1;

                        // TOOD: What is this counter variable for?
                        int c = 0;
                        foreach (IntVec3 current in this.OccupiedRect()) {
                            List<Thing> list = Map.thingGrid.ThingsListAt(current);
                            bool flag = list.OfType<Plant>().Any();

                            if (!flag) {
                                item3.Growth = 1f;
                                _innerContainer.TryDrop(item3, current, Map, ThingPlaceMode.Direct, out thing);
                                break;
                            }

                            c++;
                        }

                        if (c >= 4) {
                            break;
                        }
                    }

                    return;
                }
            }

            // TODO: Why is 1 rotting damage applied to all plants in the container?
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

            inspectString += "\n";
            inspectString += "HydroponicsExpanded.OccupiedBays".Translate() + $": {_innerContainer.Count()}";

            if (_innerContainer.Count > 0) {
                inspectString += "\n";
                inspectString += "HydroponicsExpanded.Growth".Translate() + $": {_highestGrowth * 100f:#0}%";
            }

            return inspectString;
        }

        protected virtual bool TryAcceptThing(Thing thing) {
            if (!Accepts(thing))
                return false;

            if (thing.holdingOwner == null) return _innerContainer.TryAdd(thing);

            thing.holdingOwner.TryTransferToContainer(thing, _innerContainer, thing.stackCount);
            return true;
        }

        protected virtual bool Accepts(Thing thing) {
            return _innerContainer.CanAcceptAnyOf(thing);
        }

        public new bool CanAcceptSowNow() {
            return base.CanAcceptSowNow() && _stage == HydroponicsStage.Sowing;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad) {
            base.SpawnSetup(map, respawningAfterLoad);
            LoadConfig();
        }

        private void LoadConfig() {
            var modExtension = def.GetModExtension<CapacityExtension>();
            if (modExtension == null) return;

            _capacity = modExtension.capacity;
        }

        public void GetChildHolders(List<IThingHolder> outChildren) {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings() {
            return null;
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