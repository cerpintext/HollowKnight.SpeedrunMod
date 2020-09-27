﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GlobalEnums;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using JetBrains.Annotations;
using Modding;
using SpeedrunMod.Components;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vasi;
using UObject = UnityEngine.Object;
using ReflectionHelper = Modding.ReflectionHelper;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace SpeedrunMod.Modules {
    [UsedImplicitly]
    public class SpeedBroke : FauxMod {

        [SerializeToSetting] public static bool MenuDrop = true;

        [SerializeToSetting] public static bool Storage = true;

        [SerializeToSetting] public static bool Superslides = true;

        [SerializeToSetting] public static bool Televator = true;

        [SerializeToSetting] public static bool ExplosionPogo = true;

        [SerializeToSetting] public static bool GrubsThroughWalls = true;

        [SerializeToSetting] public static bool LeverSkips = true;

        [SerializeToSetting] public static bool NoHardFalls;

        [SerializeToSetting] public static bool ShadeSoulLeverSkip;

        [SerializeToSetting] public static bool CrystalisedMoundSpikes = true;

        public override void Initialize() {
            On.HeroController.CanOpenInventory += CanOpenInventory;
            On.HeroController.CanQuickMap += CanQuickMap;
            On.TutorialEntryPauser.Start += AllowPause;
            On.HeroController.ShouldHardLand += CanHardLand;
            On.PlayMakerFSM.OnEnable += ModifyFsm;
            On.InputHandler.Update += EnableSuperslides;
            ModHooks.Instance.ObjectPoolSpawnHook += OnObjectPoolSpawn;
            USceneManager.activeSceneChanged += SceneChanged;
        }

        public override void Unload() {
            On.HeroController.CanOpenInventory -= CanOpenInventory;
            On.HeroController.CanQuickMap -= CanQuickMap;
            On.TutorialEntryPauser.Start -= AllowPause;
            On.HeroController.ShouldHardLand -= CanHardLand;
            On.PlayMakerFSM.OnEnable -= ModifyFsm;
            On.InputHandler.Update -= EnableSuperslides;
            ModHooks.Instance.ObjectPoolSpawnHook -= OnObjectPoolSpawn;
            USceneManager.activeSceneChanged -= SceneChanged;
        }

        private static void AllowPause(On.TutorialEntryPauser.orig_Start orig, TutorialEntryPauser self) {
            HeroController.instance.isEnteringFirstLevel = false;
        }

        private static bool CanQuickMap(On.HeroController.orig_CanQuickMap orig, HeroController self) {
            HeroControllerStates cs = self.cState;

            return Storage
                ? !GameManager.instance.isPaused
                  && !cs.onConveyor
                  && !cs.dashing
                  && !cs.backDashing
                  && (!cs.attacking || ReflectionHelper.GetAttr<HeroController, float>(self, "attack_time") >= self.ATTACK_RECOVERY_TIME)
                  && !cs.recoiling
                  && !cs.hazardDeath
                  && !cs.hazardRespawning
                : orig(self);
        }

        private static bool CanOpenInventory(On.HeroController.orig_CanOpenInventory orig, HeroController self) {
            HeroControllerStates cs = self.cState;

            return MenuDrop
                ? !GameManager.instance.isPaused
                  && !self.controlReqlinquished
                  && !cs.recoiling
                  && !cs.transitioning
                  && !cs.hazardDeath
                  && !cs.hazardRespawning
                  && !self.playerData.disablePause
                  && self.CanInput()
                  || self.playerData.atBench
                : orig(self);
        }

        private static void EnableSuperslides(On.InputHandler.orig_Update orig, InputHandler self) {
            if (Superslides && GameManager.instance.TimeSlowed) {
                // Ensure the slide has the correct speed
                ReflectionHelper.SetAttr(HeroController.instance, "recoilSteps", 0);

                // Kill the thing that kills superslides
                ref int timeSlowedCount = ref Mirror.GetFieldRef<GameManager, int>(GameManager.instance, "timeSlowedCount");

                int origCount = timeSlowedCount;

                timeSlowedCount = 0;

                orig(self);

                // Restore to old value
                timeSlowedCount = origCount;
            } else {
                orig(self);
            }
        }

        private static bool CanHardLand(On.HeroController.orig_ShouldHardLand orig, HeroController self, Collision2D collision) {
            return !NoHardFalls && orig(self, collision);
        }

        private static void ModifyFsm(On.PlayMakerFSM.orig_OnEnable orig, PlayMakerFSM self) {
            switch (self.FsmName) {
                case "Control" when self.name == "Initial Fall Impact" && NoHardFalls: {
                    self.ChangeTransition("Idle", "LAND", "Return Control");
                    break;
                }

                case "Call Lever" when self.name.StartsWith("Lift Call Lever") && Televator: {
                    // Don't change big elevators.
                    if (self.GetState("Check Already Called") == null) break;

                    self.ChangeTransition("Left", "FINISHED", "Send Msg");
                    self.ChangeTransition("Right", "FINISHED", "Send Msg");
                    break;
                }

                case "Bottle Control" when self.GetState("Shatter") is FsmState shatter && GrubsThroughWalls: {
                    shatter.RemoveAllOfType<BoolTest>();
                    break;
                }

                case "Switch Control" when self.name.Contains("Ruins Lever") && LeverSkips: {
                    self.GetState("Range").RemoveAllOfType<BoolTest>();
                    self.GetState("Check If Nail").RemoveAllOfType<BoolTest>();
                    break;
                }

                case "Dream Nail" when self.name == "Knight" && Storage: {
                    self.GetState("Cancelable").GetAction<ListenForDreamNail>().activeBool = true;
                    self.GetState("Cancelable Dash").GetAction<ListenForDreamNail>().activeBool = true;
                    self.GetState("Queuing").GetAction<ListenForDreamNail>().activeBool = true;
                    self.GetState("Queuing").RemoveAllOfType<BoolTest>();
                    break;
                }

                case "Stun Control" when self.name == "Hornet Boss 1" || self.name == "Hornet Boss 2": {
                    // set both "Combo Counter" and "Hits Total" to 1 hit instead of 0
                    foreach (SetIntValue setIntValue in self.GetState("Stun").Actions.OfType<SetIntValue>()) {
                        setIntValue.intValue.Value = 1;
                    }

                    break;
                }

                case "break_floor" when self.name == "One Way Wall": {
                    self.GetState("Idle").GetAction<Trigger2dEvent>().trigger = PlayMakerUnity2d.Trigger2DType.OnTriggerStay2D;
                    break;
                }

                // 1221 hp
                case "Heavy Flyer" when self.name.StartsWith("Mantis Heavy Flyer"): {
                    self.gameObject.GetComponent<HealthManager>().hp = 40;
                    break;
                }

                case "IK Control" when self.name == "Infected Knight": {
                    // 1221 early spell hits
                    FsmState start = self.GetState("Roar Start");
                    FsmState end = self.GetState("Roar End");

                    start.AddAction(end.GetAction<SetInvincible>());
                    start.AddAction(end.GetAction<SetCollider>());
                    start.AddAction(end.GetAction<SetIsKinematic2d>());

                    end.RemoveAction<SetInvincible>();
                    end.RemoveAction<SetCollider>();
                    end.RemoveAction<SetIsKinematic2d>();

                    // use the godhome wait for normal world fight as well to allow for left side fight start
                    self.GetState("Waiting").ChangeTransition("BATTLE START", "GG Wait");

                    break;
                }

                // 1221 broken vessel staggers
                case "Stun Control" when self.name == "Infected Knight": {
                    // 1221 combo value
                    self.GetOrCreateInt("Stun Combo").Value = 7;

                    // set both "Combo Counter" and "Hits Total" to 1 hit instead of 0
                    foreach (SetIntValue setIntValue in self.GetState("Stun").Actions.OfType<SetIntValue>()) {
                        setIntValue.intValue.Value = 1;
                    }

                    break;
                }

                // chain dives
                case "Spell Control" when self.name == "Knight": {
                    self.GetState("Quake Antic").RemoveAction(1);
                    break;
                }
            }

            orig(self);
        }

        private static GameObject OnObjectPoolSpawn(GameObject go) {
            if (!ExplosionPogo)
                return go;

            if (!go.name.StartsWith("Gas Explosion Recycle M"))
                return go;

            go.layer = (int) PhysLayers.ENEMIES;

            var bouncer = go.GetComponent<NonBouncer>();

            if (bouncer)
                bouncer.active = false;

            return go;
        }

        private static void SceneChanged(Scene from, Scene to) {
            switch (to.name) {
                case "Ruins1_31" when ShadeSoulLeverSkip: {
                    HeroController.instance.StartCoroutine(EnableShadeSoulLeverSkip());

                    break;
                }
                case "Mines_35" when CrystalisedMoundSpikes: {
                    HeroController.instance.StartCoroutine(AddCrystalisedMoundSpikes());

                    break;
                }
                case "Fungus3_05": {
                    HeroController.instance.StartCoroutine(QgThornRespawn());
                    break;
                }
                case "Ruins1_24": {
                    HeroController.instance.StartCoroutine(AllSkillsSoulVials());
                    break;
                }
            }
        }

        private static IEnumerator EnableShadeSoulLeverSkip() {
            yield return null;

            GameObject chunk = GameObject.Find("Chunk 1 1");

            var leverGo = new GameObject
            (
                "Ruins Lever",
                typeof(Lever),
                typeof(BoxCollider2D)
            );

            leverGo.transform.position = new Vector3(38f, 56.7f);
            leverGo.layer = (int) PhysLayers.TERRAIN;

            var lever = leverGo.GetComponent<Lever>();
            lever.OnHit = () => GameObject.Find("Ruins Gate").LocateMyFSM("Toll Gate").SendEvent("OPEN");

            var bcol = leverGo.GetComponent<BoxCollider2D>();
            bcol.size = new Vector2(.4f, .6f);
            bcol.isTrigger = true;

            // Extends a wall in Ruins1_31 to enable climbing it with claw only (like on 1221)
            Vector2[] points = {
                new Vector2(21.4f, 19),
                new Vector2(21.5f, 16),
                new Vector2(21.5f, 19)
            };

            EdgeCollider2D col = chunk
                .GetComponents<EdgeCollider2D>()
                .First(x => x.points[0] == new Vector2(0, 12));

            List<Vector2> pts = col.points.ToList();

            pts.RemoveRange(6, 2);
            pts.InsertRange(6, points);

            col.points = pts.ToArray();
        }

        private static IEnumerator AddCrystalisedMoundSpikes() {
            yield return null;

            foreach (NonBouncer nonBounce in UObject.FindObjectsOfType<NonBouncer>()) {
                if (!nonBounce.gameObject.name.StartsWith("Spike Collider"))
                    continue;

                nonBounce.active = false;

                AudioSource tinkAudio = new GameObject("tinkAudio").AddComponent<AudioSource>();
                tinkAudio.clip = GameObject.Find("Mines Platform").GetComponent<FlipPlatform>().hitSound;
                tinkAudio.volume = FixVolume.Volume;

                nonBounce.gameObject.AddComponent<TinkEffect>().blockEffect = tinkAudio.gameObject;
            }
        }

        // extends hazard respawn trigger box upwards through the thorns
        private static IEnumerator QgThornRespawn() {
            yield return null;

            BoxCollider2D box = GameObject.Find("Hazard Respawn Trigger v2 (4)").GetComponent<BoxCollider2D>();

            box.size = new Vector2(1, 13.7f); // default: (1.0, 9.7)
            box.offset = new Vector2(0, 2.4f); // default: (0.0, 0.4)
        }

        // breaks all 3 soul vials if the first one gets hit with shade soul
        private static IEnumerator AllSkillsSoulVials() {
            yield return null;

            GameObject.Find("Ruins Vial Empty").AddComponent<ShadeSoulTrigger>().OnShadeSoulHit = () => {
                GameObject.Find("Ruins Vial Empty (1)").GetComponent<Breakable>().BreakSelf();
                GameObject.Find("Ruins Soul Vial").GetComponent<Breakable>().BreakSelf();
            };
        }

    }
}