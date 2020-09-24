﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Modding;
using SpeedrunMod.Modules;

namespace SpeedrunMod {
    public class SpeedrunMod : Mod, ITogglableMod {

        private Settings _globalSettings = new Settings();

        private readonly List<FauxMod> _fauxMods = new List<FauxMod>();

        // So that UnencryptedSaves' BeforeSavegameSave runs last, showing all Mod settings.
        public override int LoadPriority() => int.MaxValue;

        public override void Initialize() {
            foreach (Type t in Assembly.GetAssembly(typeof(SpeedrunMod)).GetTypes().Where(x => x.IsSubclassOf(typeof(FauxMod)))) {
                var fm = (FauxMod) Activator.CreateInstance(t);

                // If Disable isn't overridden then it can't be toggled.
                bool cantDisable = t.GetMethod(nameof(FauxMod.Unload))?.DeclaringType == typeof(FauxMod);

                if (!GlobalSettings.BoolValues.TryGetValue(t.Name, out bool enabled)) {
                    enabled = fm.DefaultState;

                    GlobalSettings.BoolValues.Add(t.Name, enabled);
                }

                if (cantDisable)
                    enabled = true;

                if (enabled) {
                    fm.Initialize();
                    fm.IsLoaded = true;
                }

                _fauxMods.Add(fm);
            }
        }

        public void Unload() {
            foreach (FauxMod fm in _fauxMods.Where(x => x.IsLoaded)) {
                fm.Unload();
            }
        }

        public override ModSettings GlobalSettings {
            get => _globalSettings;
            set => _globalSettings = (Settings) value;
        }

        public override string GetVersion() {
            return "0.1";
        }

    }
}