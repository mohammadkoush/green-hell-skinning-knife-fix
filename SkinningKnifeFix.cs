// SkinningKnifeFix - the skinning animation shows the knife you are actually holding.
//
// THE QUIRK. When you harvest a large animal, Green Hell plays the skinning animation with a stone
// blade in hand, whatever you were holding. A Steam bug report says it plainly: "harvesting with
// metal blade has the stone blade animation... kinda breaks the immersion." It is documented on the
// wiki as a known behaviour and no mod on Nexus or the ModAPI hub changes it.
//
// WHY IT HAPPENS, read from the game's own class rather than assumed. HarvestingAnimalController -
// the controller that runs the big-animal skinning - carries a fixed prop:
//
//     GameObject m_StoneBlade          the blade shown during the animation
//     Transform  m_StoneBladeHolder    where it sits, on the hand bone
//
// It is a prefab child of the controller, independent of the item in your hand. The animation was
// authored around a stone blade and nobody wired the held item into it.
//
// WHAT THIS DOES. When the controller switches on, it looks at the item in your right hand. If that
// is a knife or a machete, the stone blade is hidden and a visual copy of your blade is put in the
// same holder, in the same pose. When the controller switches off, the copy is destroyed and the
// stone blade is shown again. A copy of the VISUAL only - meshes and materials - never the Item
// itself: no collider, no trigger, no replication, nothing the game could mistake for a second
// knife. If you hold anything else, or nothing, the game's own blade is left exactly alone.
//
// The blade's model origin is not guaranteed to match the stone blade's, so the pose has an offset
// you can tune - three positions and three angles - rather than a number I chose in the dark.
//
// It reads two private fields by name. If a game update renames them, this logs once and does
// nothing, and the animation is the game's own again.
//
// Language level is C# 5 (stock Framework csc.exe) - no ?., no $"", no ??=.

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SkinningKnifeFix
{
    [BepInPlugin(Guid, Name, Version)]
    public class SkinningKnifeFixPlugin : BaseUnityPlugin
    {
        public const string Guid    = "com.mohammadkoush.skinningknifefix";
        public const string Name    = "SkinningKnifeFix";
        public const string Version = "1.0.0";

        private static SkinningKnifeFixPlugin s_Self;
        private static FieldInfo s_BladeFI, s_HolderFI;
        private static bool s_FieldsMissingReported;

        private ConfigEntry<bool>  _enabled;
        private ConfigEntry<bool>  _machetes;
        private ConfigEntry<Vector3> _posOffset;
        private ConfigEntry<Vector3> _rotOffset;
        private ConfigEntry<bool>  _logSwaps;

        // What is currently standing in for the stone blade, and the blade it replaced.
        private static GameObject s_Copy;
        private static GameObject s_HiddenBlade;

        private void Awake()
        {
            s_Self = this;

            _enabled = Config.Bind("Knife", "Enabled", true,
                "Show the knife you are holding in the skinning animation instead of the game's " +
                "fixed stone blade.");
            _machetes = Config.Bind("Knife", "IncludeMachetes", true,
                "Also swap in a machete when that is what you are holding. Off: only knives.");
            _posOffset = Config.Bind("Pose", "PositionOffset", Vector3.zero,
                "Nudge the swapped blade in the hand, in metres, if your knife's model sits off " +
                "from where the stone blade did. x y z.");
            _rotOffset = Config.Bind("Pose", "RotationOffset", Vector3.zero,
                "Turn the swapped blade, in degrees, if it points the wrong way. x y z.");
            _logSwaps = Config.Bind("Diagnostics", "LogSwaps", true,
                "Write a line to the log each time a blade is swapped, naming the item and how " +
                "many meshes were copied.");

            try
            {
                Harmony h = new Harmony(Guid);
                h.PatchAll(typeof(SkinningKnifeFixPlugin).Assembly);
                Logger.LogInfo(Name + " " + Version + " loaded.");
            }
            catch (Exception ex)
            {
                Logger.LogError("could not patch the skinning controller: " + ex.Message);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Finding the blade the game uses
        // -----------------------------------------------------------------------------------------

        private static bool ResolveFields()
        {
            if (s_BladeFI != null && s_HolderFI != null) return true;
            s_BladeFI  = AccessTools.Field(typeof(HarvestingAnimalController), "m_StoneBlade");
            s_HolderFI = AccessTools.Field(typeof(HarvestingAnimalController), "m_StoneBladeHolder");
            if (s_BladeFI != null && s_HolderFI != null) return true;
            if (!s_FieldsMissingReported && s_Self != null)
            {
                s_FieldsMissingReported = true;
                s_Self.Logger.LogWarning("the game's skinning controller no longer has m_StoneBlade / "
                    + "m_StoneBladeHolder - nothing swapped, the animation is the game's own.");
            }
            return false;
        }

        /// <summary>The blade in the right hand, if it is one this mod should show.</summary>
        private static Item HeldBlade()
        {
            try
            {
                Player p = Player.Get();
                if (p == null) return null;
                Item it = p.GetCurrentItem(Enums.Hand.Right);
                if (it == null || it.m_Info == null) it = p.GetCurrentItem(Enums.Hand.Left);
                if (it == null || it.m_Info == null) return null;
                if (it.m_Info.IsKnife()) return it;
                if (s_Self._machetes.Value && it.m_Info.IsMachete()) return it;
            }
            catch (Exception) { }
            return null;
        }

        // -----------------------------------------------------------------------------------------
        // The swap
        // -----------------------------------------------------------------------------------------

        private static void Swap(HarvestingAnimalController ctl)
        {
            Undo();
            if (s_Self == null || !s_Self._enabled.Value) return;
            if (!ResolveFields()) return;

            Item blade = HeldBlade();
            if (blade == null) return;                    // not a knife: the game's blade stays

            GameObject stone  = s_BladeFI.GetValue(ctl) as GameObject;
            Transform  holder = s_HolderFI.GetValue(ctl) as Transform;
            if (stone == null || holder == null) return;

            // The copy sits exactly where the stone blade sits, plus his offsets.
            GameObject copy = new GameObject("SkinningKnifeFix_" + blade.m_Info.m_ID);
            copy.transform.SetParent(holder, false);
            copy.transform.localPosition = stone.transform.localPosition + s_Self._posOffset.Value;
            copy.transform.localRotation = stone.transform.localRotation
                                           * Quaternion.Euler(s_Self._rotOffset.Value);
            copy.transform.localScale    = stone.transform.localScale;

            // Visual only. Each renderer on the held item becomes a plain mesh child, placed where
            // it sits relative to the item's own root - so a knife made of a blade and a handle
            // keeps its shape. No Item, no collider, no trigger comes across.
            int meshes = 0;
            Transform root = blade.transform;
            Renderer[] rends = blade.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < rends.Length; i++)
            {
                Renderer r = rends[i];
                if (r == null || !r.enabled) continue;

                Mesh mesh = null;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
                SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
                if (smr != null) mesh = smr.sharedMesh;
                if (mesh == null) continue;

                GameObject part = new GameObject(r.gameObject.name);
                part.transform.SetParent(copy.transform, false);

                // Where this piece sits inside the knife, carried over as local pose.
                Matrix4x4 rel = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                part.transform.localPosition = rel.GetColumn(3);
                part.transform.localRotation = Quaternion.LookRotation(rel.GetColumn(2), rel.GetColumn(1));
                part.transform.localScale    = new Vector3(rel.GetColumn(0).magnitude,
                                                           rel.GetColumn(1).magnitude,
                                                           rel.GetColumn(2).magnitude);

                MeshFilter pf = part.AddComponent<MeshFilter>();
                pf.sharedMesh = mesh;
                MeshRenderer pr = part.AddComponent<MeshRenderer>();
                pr.sharedMaterials = r.sharedMaterials;
                pr.shadowCastingMode = r.shadowCastingMode;
                pr.receiveShadows = r.receiveShadows;
                part.layer = stone.layer;
                meshes++;
            }

            if (meshes == 0)
            {
                // Nothing to show: leave the game's blade rather than an empty hand.
                UnityEngine.Object.Destroy(copy);
                if (s_Self._logSwaps.Value)
                    s_Self.Logger.LogInfo("held " + blade.m_Info.m_ID + " has no mesh to copy - stone blade kept");
                return;
            }

            stone.SetActive(false);
            s_Copy = copy;
            s_HiddenBlade = stone;
            if (s_Self._logSwaps.Value)
                s_Self.Logger.LogInfo("skinning with " + blade.m_Info.m_ID + " - " + meshes
                                      + " mesh(es) in place of the stone blade");
        }

        private static void Undo()
        {
            try
            {
                if (s_Copy != null) UnityEngine.Object.Destroy(s_Copy);
                if (s_HiddenBlade != null) s_HiddenBlade.SetActive(true);
            }
            catch (Exception) { }
            s_Copy = null;
            s_HiddenBlade = null;
        }

        // -----------------------------------------------------------------------------------------
        // Hooks - after the game has set itself up, and after it has torn itself down
        // -----------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(HarvestingAnimalController), "OnEnable")]
        private static class Patch_OnEnable
        {
            private static void Postfix(HarvestingAnimalController __instance)
            {
                try { Swap(__instance); }
                catch (Exception ex)
                {
                    // Never let a cosmetic swap break the harvest. Put the game's blade back.
                    Undo();
                    if (s_Self != null) s_Self.Logger.LogWarning("blade swap failed: " + ex.Message);
                }
            }
        }

        [HarmonyPatch(typeof(HarvestingAnimalController), "OnDisable")]
        private static class Patch_OnDisable
        {
            private static void Postfix()
            {
                Undo();
            }
        }
    }
}
