using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.Health;
using SPT.Reflection.Patching;
using TMPro;
using UnityEngine;

namespace Kommie.ArmorTweaks
{
    internal static class ArmorHealthReadout
    {
        internal static BepInEx.Logging.ManualLogSource Log;

        internal static void Init(BepInEx.Logging.ManualLogSource log)
        {
            Log = log;
            try
            {
                new HealthPanelPatch().Enable();
                log.LogInfo("Armor health readout enabled");
            }
            catch (Exception e)
            {
                log.LogError("Armor health readout DISABLED: " + e);
            }
        }
    }

    public class HealthPanelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            MethodBase method = typeof(InventoryScreenHealthPanel).GetMethod("Show", BindingFlags.Instance | BindingFlags.Public);
            ArmorHealthReadout.Log.LogInfo("Armor health readout target: " + (method == null ? "NOT FOUND" : method.ToString()));
            return method;
        }

        [PatchPostfix]
        private static void Postfix(InventoryScreenHealthPanel __instance, Inventory inventory)
        {
            try
            {
                Dictionary<EBodyPart, BodyPartView> views = null;
                foreach (FieldInfo field in typeof(InventoryScreenHealthPanel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (field.FieldType == typeof(Dictionary<EBodyPart, BodyPartView>))
                    {
                        views = field.GetValue(__instance) as Dictionary<EBodyPart, BodyPartView>;
                        break;
                    }
                }
                if (views == null)
                {
                    ArmorHealthReadout.Log.LogWarning("Armor health readout: body part views not found");
                    return;
                }

                FieldInfo textField = null;
                foreach (FieldInfo field in typeof(BodyPartView).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (typeof(TMP_Text).IsAssignableFrom(field.FieldType)) { textField = field; break; }
                }
                if (textField == null)
                {
                    ArmorHealthReadout.Log.LogWarning("Armor health readout: value text field not found");
                    return;
                }

                foreach (KeyValuePair<EBodyPart, BodyPartView> pair in views)
                {
                    if (pair.Value == null) continue;
                    TMP_Text valueText = textField.GetValue(pair.Value) as TMP_Text;
                    if (valueText == null) continue;

                    Transform existing = valueText.transform.parent.Find("ArmorTweaksArmorLabel");
                    ArmorLabel label = existing == null ? null : existing.GetComponent<ArmorLabel>();
                    if (label == null)
                    {
                        GameObject clone = UnityEngine.Object.Instantiate(valueText.gameObject, valueText.transform.parent);
                        clone.name = "ArmorTweaksArmorLabel";
                        TMP_Text text = clone.GetComponent<TMP_Text>();
                        text.fontSize = valueText.fontSize * 0.8f;
                        text.color = new Color(0.65f, 0.82f, 1f);
                        text.enableWordWrapping = false;
                        text.overflowMode = TextOverflowModes.Overflow;
                        text.alignment = TextAlignmentOptions.Top;
                        RectTransform rect = clone.GetComponent<RectTransform>();
                        rect.anchoredPosition = rect.anchoredPosition + new Vector2(0f, -valueText.fontSize * 1.9f);
                        label = clone.AddComponent<ArmorLabel>();
                        label.Text = text;
                    }
                    label.Inventory = inventory;
                    label.Part = pair.Key;
                    label.Refresh();
                    ArmorHealthReadout.Log.LogInfo("Armor health readout: " + pair.Key + " = " + ArmorTotals.Zones(inventory, pair.Key).Count);
                }
            }
            catch (Exception e)
            {
                ArmorHealthReadout.Log.LogError("Armor health readout failed: " + e);
            }
        }
    }

    public class ArmorLabel : MonoBehaviour
    {
        public TMP_Text Text;
        public Inventory Inventory;
        public EBodyPart Part;
        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            Refresh();
        }

        public void Refresh()
        {
            if (Text == null || Inventory == null) return;
            List<KeyValuePair<string, float>> zones = ArmorTotals.Zones(Inventory, Part);
            List<string> entries = new List<string>();
            foreach (KeyValuePair<string, float> zone in zones)
                if (zone.Value > 0.5f) entries.Add(zone.Key + " " + Mathf.RoundToInt(zone.Value));
            List<string> lines = new List<string>();
            for (int i = 0; i < entries.Count; i += 2)
                lines.Add(i + 1 < entries.Count ? entries[i] + "  " + entries[i + 1] : entries[i]);
            Text.text = string.Join("\n", lines.ToArray());
        }
    }

    internal static class ArmorTotals
    {
        private const string LegHolderTpl = "5e9c4f1d8a2b4c3d7f0e1a8c";

        private static readonly HashSet<string> WornSlots = new HashSet<string> { "ArmorVest", "TacticalVest", "Headwear", "FaceCover", "Eyewear", "Earpiece" };

        internal static List<KeyValuePair<string, float>> Zones(Inventory inventory, EBodyPart part)
        {
            Dictionary<string, float> totals = new Dictionary<string, float>();
            List<string> order = new List<string>();
            CompoundItem equipment = inventory.Equipment;
            if (equipment == null) return new List<KeyValuePair<string, float>>();
            foreach (Item item in equipment.GetAllItems())
            {
                ArmorComponent armor;
                if (!item.TryGetItemComponent(out armor) || armor.Repairable == null) continue;
                if (!Worn(item, equipment)) continue;
                float durability = armor.Repairable.Durability;
                HashSet<string> seen = new HashSet<string>();
                foreach (EBodyPartColliderType collider in armor.ArmorColliders)
                {
                    string zone;
                    if (ZoneOf(collider, part, out zone) && seen.Add(zone)) Add(totals, order, zone, durability);
                }
                EArmorPlateCollider mask = armor.ArmorPlateCollidersMask;
                if (mask == 0) continue;
                foreach (EArmorPlateCollider flag in Enum.GetValues(typeof(EArmorPlateCollider)))
                {
                    if ((mask & flag) == 0) continue;
                    string zone;
                    if (PlateZone(flag.ToString(), part, out zone) && seen.Add(zone)) Add(totals, order, zone, durability);
                }
            }
            List<KeyValuePair<string, float>> result = new List<KeyValuePair<string, float>>();
            foreach (string name in Order)
                if (totals.ContainsKey(name)) result.Add(new KeyValuePair<string, float>(name, totals[name]));
            return result;
        }

        private static readonly string[] Order = { "Front", "Back", "Left", "Right", "Groin", "R.Groin", "Top", "Nape", "Ears", "Jaw", "Eyes", "Face", "Throat", "Neck", "Arm", "Thigh", "Calf" };

        private static void Add(Dictionary<string, float> totals, List<string> order, string zone, float value)
        {
            float current;
            totals.TryGetValue(zone, out current);
            totals[zone] = current + value;
        }

        private static bool PlateZone(string name, EBodyPart part, out string zone)
        {
            EBodyPart target = name.EndsWith("_low") ? EBodyPart.Stomach : EBodyPart.Chest;
            zone = name.Contains("side_left") ? "Left" : name.Contains("side_right") ? "Right" : name.EndsWith("_back") ? "Back" : "Front";
            return target == part;
        }

        private static bool ZoneOf(EBodyPartColliderType collider, EBodyPart part, out string zone)
        {
            zone = null;
            switch (collider)
            {
                case EBodyPartColliderType.ParietalHead: zone = "Top"; return part == EBodyPart.Head;
                case EBodyPartColliderType.BackHead: zone = "Nape"; return part == EBodyPart.Head;
                case EBodyPartColliderType.Ears: zone = "Ears"; return part == EBodyPart.Head;
                case EBodyPartColliderType.Jaw: zone = "Jaw"; return part == EBodyPart.Head;
                case EBodyPartColliderType.Eyes: zone = "Eyes"; return part == EBodyPart.Head;
                case EBodyPartColliderType.HeadCommon: zone = "Face"; return part == EBodyPart.Head;
                case EBodyPartColliderType.NeckFront: zone = "Throat"; return part == EBodyPart.Head;
                case EBodyPartColliderType.NeckBack: zone = "Neck"; return part == EBodyPart.Head;
                case EBodyPartColliderType.RibcageUp: zone = "Front"; return part == EBodyPart.Chest;
                case EBodyPartColliderType.SpineTop: zone = "Back"; return part == EBodyPart.Chest;
                case EBodyPartColliderType.LeftSideChestUp: zone = "Left"; return part == EBodyPart.Chest;
                case EBodyPartColliderType.RightSideChestUp: zone = "Right"; return part == EBodyPart.Chest;
                case EBodyPartColliderType.RibcageLow: zone = "Front"; return part == EBodyPart.Stomach;
                case EBodyPartColliderType.SpineDown: zone = "Back"; return part == EBodyPart.Stomach;
                case EBodyPartColliderType.LeftSideChestDown: zone = "Left"; return part == EBodyPart.Stomach;
                case EBodyPartColliderType.RightSideChestDown: zone = "Right"; return part == EBodyPart.Stomach;
                case EBodyPartColliderType.Pelvis: zone = "Groin"; return part == EBodyPart.Stomach;
                case EBodyPartColliderType.PelvisBack: zone = "R.Groin"; return part == EBodyPart.Stomach;
                case EBodyPartColliderType.LeftThigh: zone = "Thigh"; return part == EBodyPart.LeftLeg;
                case EBodyPartColliderType.LeftCalf: zone = "Calf"; return part == EBodyPart.LeftLeg;
                case EBodyPartColliderType.RightThigh: zone = "Thigh"; return part == EBodyPart.RightLeg;
                case EBodyPartColliderType.RightCalf: zone = "Calf"; return part == EBodyPart.RightLeg;
                case EBodyPartColliderType.LeftUpperArm:
                case EBodyPartColliderType.LeftForearm: zone = "Arm"; return part == EBodyPart.LeftArm;
                case EBodyPartColliderType.RightUpperArm:
                case EBodyPartColliderType.RightForearm: zone = "Arm"; return part == EBodyPart.RightArm;
                default: return false;
            }
        }

        private static bool Worn(Item item, CompoundItem equipment)
        {
            Item current = item;
            for (int depth = 0; depth < 8 && current != null && current != equipment; depth++)
            {
                if (current.Parent == null) return false;
                Slot slot = current.Parent.Container as Slot;
                if (slot == null) return false;
                if (slot.ParentItem == equipment) return WornSlots.Contains(slot.ID);
                current = slot.ParentItem;
                if (current != null && current.TemplateId.ToString() == LegHolderTpl) return true;
            }
            return false;
        }
    }
}
