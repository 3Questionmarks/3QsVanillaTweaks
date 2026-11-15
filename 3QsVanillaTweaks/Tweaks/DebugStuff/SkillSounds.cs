using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;

namespace Tweaks.DebugStuff
{
    internal static class SkillSounds
    {
        private sealed class SkillWindow
        {
            internal string Name;
            internal string State;
            internal float Start;
            internal int Number;
        }

        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<bool> includeMovement;
        private static ConfigEntry<bool> includeInvalid;
        private static ConfigEntry<int> captureSeconds;

        private static readonly Dictionary<GameObject, SkillWindow> recent = new Dictionary<GameObject, SkillWindow>();
        private static int nextWindow;

        internal static void Init()
        {
            enabled = C.BindBool("Debug - Skill Sounds", "Log Player Skill Sounds", true,
                "Log sound events played by the player around each skill activation. Requires Debug Options.", clientSide: true);
            includeMovement = C.BindBool("Debug - Skill Sounds", "Include Movement Sounds", true,
                "Also log footsteps, landings and other movement sounds.", clientSide: true);
            includeInvalid = C.BindBool("Debug - Skill Sounds", "Include Failed Sound Events", true,
                "Include empty sound names and events with playing ID 0. Useful for diagnosing missing sounds.", clientSide: true);
            captureSeconds = C.Bind("Debug - Skill Sounds", "Skill Sound Capture Seconds", 20,
                "Record sound events for this many seconds after a player skill activation (default 20). Longer windows can include unrelated sounds.", clientSide: true);

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Debug.SkillSounds");
            harmony.Patch(AccessTools.Method(typeof(CharacterBody), nameof(CharacterBody.OnSkillActivated)),
                postfix: new HarmonyMethod(typeof(SkillSounds), nameof(Activated)));

            // Util.PlaySound normally delegates to AkSoundEngine.PostEvent. Log the Wwise
            // call only, otherwise the same sound appears twice with the same playing ID.
            var post = AccessTools.Method(typeof(AkSoundEngine), "PostEvent",
                new[] { typeof(string), typeof(GameObject) });
            if (post != null)
                harmony.Patch(post, postfix: new HarmonyMethod(typeof(SkillSounds), nameof(Posted)));
            else
                ThreeQsVanillaTweaks.Log.Warning("[Skill Sounds] Could not hook AkSoundEngine.PostEvent(string, GameObject).");
        }

        private static bool Active => C.DebugOptionsEnabled && C.Get(enabled);

        private static void Activated(CharacterBody __instance, GenericSkill skill)
        {
            if (!Active || !__instance || !__instance.isPlayerControlled || !skill) return;

            var definition = skill.skillDef;
            string name = definition ? definition.skillName : "<unknown>";
            string state = definition && definition.activationState.stateType != null
                ? definition.activationState.stateType.FullName : "<unknown>";
            var window = new SkillWindow
            {
                Name = name,
                State = state,
                Start = Time.realtimeSinceStartup,
                Number = ++nextWindow
            };
            recent[__instance.gameObject] = window;
            ThreeQsVanillaTweaks.Log.Info(
                $"[Skill Sounds] #{window.Number} START t={window.Start:F2}s body={__instance.name} skill={name} state={state}");
        }

        private static void Posted(string in_pszEventName, GameObject in_gameObjectID, uint __result)
        {
            if (!Active || !in_gameObjectID) return;
            string sound = in_pszEventName;
            if (!C.Get(includeInvalid) && (string.IsNullOrEmpty(sound) || __result == 0)) return;
            if (!C.Get(includeMovement) && IsMovement(sound)) return;

            CharacterBody body = in_gameObjectID.GetComponent<CharacterBody>()
                ?? in_gameObjectID.GetComponentInParent<CharacterBody>();
            if (!body || !body.isPlayerControlled) return;

            if (!recent.TryGetValue(body.gameObject, out var window)) return;
            float elapsed = Time.realtimeSinceStartup - window.Start;
            if (elapsed < 0f || elapsed > Mathf.Max(1, C.Get(captureSeconds))) return;

            // These are sounds observed on this body *after* the activation, not a
            // guarantee that the skill itself caused each sound.
            ThreeQsVanillaTweaks.Log.Info(
                $"[Skill Sounds] #{window.Number} +{elapsed:F2}s skill={window.Name} " +
                $"event={sound ?? "<null>"} playingId={__result} source={in_gameObjectID.name}");
        }

        private static bool IsMovement(string sound)
        {
            if (string.IsNullOrEmpty(sound)) return false;
            return sound.IndexOf("step", StringComparison.OrdinalIgnoreCase) >= 0
                || sound.IndexOf("land", StringComparison.OrdinalIgnoreCase) >= 0
                || sound.IndexOf("foot", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
