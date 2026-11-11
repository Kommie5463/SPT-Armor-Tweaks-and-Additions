using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Kommie.SoftArmorSwap
{
    internal static class SoftArmorSwapPlugin
    {
        internal static ManualLogSource Log;

        internal static void Init(ManualLogSource log)
        {
            Log = log;
            try
            {
                if (!SlotAccess.Init())
                {
                    log.LogError("SoftArmorSwap DISABLED: Slot.Locked backing field not found. No patches were applied.");
                    return;
                }
                new SlotCtorPatch().Enable();
                new InsertDropModelPatch().Enable();
                new SlotCompatibilityPatch().Enable();
                new ArmorCollidersPatch().Enable();
                new ArmorPlateMaskPatch().Enable();
                new ItemWeightPatch().Enable();
                new VitalPartsPatch().Enable();
                new PenaltyScalePatch("SpeedPenalty").Enable();
                new PenaltyScalePatch("MousePenalty").Enable();
                new PenaltyScalePatch("WeaponErgonomicPenalty").Enable();
                new PenaltyTooltipPatch().Enable();
            }
            catch (Exception e)
            {
                log.LogError("SoftArmorSwap DISABLED: " + e);
            }
        }
    }

    internal static class SlotAccess
    {
        private static FieldInfo _locked;

        private static readonly ConditionalWeakTable<Slot, object> Marked = new ConditionalWeakTable<Slot, object>();
        private static readonly HashSet<string> Reported = new HashSet<string>();

        internal static readonly HashSet<string> InsertKeys = new HashSet<string>();
        private static readonly List<MongoID> InsertNodes = new List<MongoID>();
        private static MongoID[] _insertArray = new MongoID[0];

        public static bool Init()
        {
            _locked = AccessTools.Field(typeof(Slot), "<Locked>k__BackingField");
            return _locked != null;
        }

        public static float Fill(CompoundItem carrier)
        {
            int total = 0, filled = 0;
            foreach (Slot slot in carrier.Slots)
            {
                if (!IsMarked(slot)) continue;
                total++;
                if (slot.ContainedItem != null) filled++;
            }
            return total == 0 ? 1f : (float)filled / total;
        }

        public static Slot MarkedSlotOf(ArmorComponent armor)
        {
            Item item = armor == null ? null : armor.Item;
            ItemAddress addr = item == null ? null : item.CurrentAddress;
            Slot slot = addr == null ? null : addr.Container as Slot;
            return IsMarked(slot) ? slot : null;
        }

        public static bool IsMarked(Slot slot)
        {
            object o;
            return slot != null && Marked.TryGetValue(slot, out o);
        }

        internal static string Names(IEnumerable<EBodyPartColliderType> c)
        {
            return c == null ? "null" : string.Join(",", c.Select(x => x.ToString()).ToArray());
        }

        private static void Skipped(Slot slot, CompoundItem parent, string reason)
        {
            string id = slot.ID ?? "";
            string low = id.ToLowerInvariant();
            bool armorish = slot.ArmorPlateColliderMask != 0 ||
                (slot.ArmorColliders != null && slot.ArmorColliders.Length > 0) ||
                low.Contains("armor") || low.Contains("plate") || low.Contains("collar") ||
                low.Contains("shoulder") || low.Contains("groin") || low.Contains("side") || low.Contains("insert");
            if (!armorish) return;
            string key = "skip:" + (parent == null ? "?" : parent.StringTemplateId) + "/" + id;
            if (Reported.Add(key))
            {
                SoftArmorSwapPlugin.Log.LogInfo("Skipped locked slot " + id + " on " +
                    (parent == null ? "?" : parent.StringTemplateId) + ": " + reason +
                    " colliders[" + Names(slot.ArmorColliders) + "] plateMask=" + slot.ArmorPlateColliderMask +
                    " type=" + slot.GetType().Name);
            }
        }

        internal static void Learn(Slot slot)
        {
            if (slot.Filters == null) return;
            foreach (ItemFilter f in slot.Filters)
            {
                if (f == null || f.Filter == null) continue;
                foreach (MongoID id in f.Filter)
                    if (InsertKeys.Add(id.ToString())) InsertNodes.Add(id);
            }
            _insertArray = InsertNodes.ToArray();
        }

        public static void TryUnlock(Slot slot, CompoundItem parent)
        {
            if (slot == null || !slot.Locked) return;
            EBodyPartColliderType[] colliders = slot.ArmorColliders;
            bool hasColliders = colliders != null && colliders.Length > 0;
            bool hasPlateMask = slot.ArmorPlateColliderMask != 0;
            string low = (slot.ID ?? "").ToLowerInvariant();

            if (hasPlateMask)
            {

                if (!low.StartsWith("soft_armor")) { Skipped(slot, parent, "plate slot"); return; }
            }
            else if (!hasColliders)
            {
                Skipped(slot, parent, "no armor colliders in filter");
                return;
            }

            _locked.SetValue(slot, false);
            Marked.Add(slot, null);
            Learn(slot);

            string key = (parent == null ? "?" : parent.StringTemplateId) + "/" + slot.ID;
            if (Reported.Add(key))
            {
                SoftArmorSwapPlugin.Log.LogInfo("Unlocked insert slot " + slot.ID + " on " +
                    (parent == null ? "?" : parent.StringTemplateId) + " [" + Names(colliders) + "] plateMask=" + slot.ArmorPlateColliderMask);
            }
        }

        internal static HashSet<string> Zones(IEnumerable<EBodyPartColliderType> colliders, EArmorPlateCollider plates)
        {
            HashSet<string> z = new HashSet<string>();
            if (colliders != null)
            {
                foreach (EBodyPartColliderType c in colliders)
                {
                    if (c == EBodyPartColliderType.HeadCommon) continue;
                    if (c == EBodyPartColliderType.RibcageUp || c == EBodyPartColliderType.RibcageLow) z.Add("front");
                    else if (c == EBodyPartColliderType.SpineTop || c == EBodyPartColliderType.SpineDown) z.Add("back");
                    else if (c == EBodyPartColliderType.LeftSideChestUp || c == EBodyPartColliderType.LeftSideChestDown) z.Add("left");
                    else if (c == EBodyPartColliderType.RightSideChestUp || c == EBodyPartColliderType.RightSideChestDown) z.Add("right");
                    else z.Add(c.ToString());
                }
            }
            if ((plates & (EArmorPlateCollider.Plate_Granit_SAPI_chest | EArmorPlateCollider.Plate_Korund_chest)) != 0) z.Add("front");
            if ((plates & (EArmorPlateCollider.Plate_Granit_SAPI_back | EArmorPlateCollider.Plate_6B13_back)) != 0) z.Add("back");
            if ((plates & (EArmorPlateCollider.Plate_Granit_SSAPI_side_left_high | EArmorPlateCollider.Plate_Granit_SSAPI_side_left_low |
                           EArmorPlateCollider.Plate_Korund_side_left_high | EArmorPlateCollider.Plate_Korund_side_left_low)) != 0) z.Add("left");
            if ((plates & (EArmorPlateCollider.Plate_Granit_SSAPI_side_right_high | EArmorPlateCollider.Plate_Granit_SSAPI_side_right_low |
                           EArmorPlateCollider.Plate_Korund_side_right_high | EArmorPlateCollider.Plate_Korund_side_right_low)) != 0) z.Add("right");
            return z;
        }

        public static bool Fits(Slot slot, Item item)
        {
            ArmorComponent armor;
            if (item == null || !item.TryGetItemComponent<ArmorComponent>(out armor)) return false;

            bool ok;
            string detail;
            if (!ItemFilter.CheckItem(item, _insertArray))
            {
                ok = false;
                detail = "not a built-in insert";
            }
            else
            {

                HashSet<string> slotZones = Zones(slot.ArmorColliders, slot.ArmorPlateColliderMask);
                HashSet<string> itemZones = Zones(armor.ArmorColliders, armor.ArmorPlateCollidersMask);
                ok = slotZones.Overlaps(itemZones);
                detail = "item[" + string.Join(",", itemZones.ToArray()) + "] slot[" + string.Join(",", slotZones.ToArray()) + "]";
            }

            string key = slot.ID + "<-" + item.StringTemplateId + ":" + ok;
            if (Reported.Add(key))
                SoftArmorSwapPlugin.Log.LogInfo((ok ? "Accepted " : "Rejected ") + item.StringTemplateId + " in slot " + slot.ID + " " + detail);
            return ok;
        }
    }

    public class SlotCtorPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Constructor(typeof(Slot), new Type[] { typeof(Slot), typeof(CompoundItem) });
        }

        [PatchPostfix]
        private static void Postfix(Slot __instance, CompoundItem parentItem)
        {
            try { SlotAccess.TryUnlock(__instance, parentItem); }
            catch (Exception e) { SoftArmorSwapPlugin.Log.LogError("SlotCtorPatch: " + e); }
        }
    }

    public class SlotCompatibilityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Slot), nameof(Slot.CheckCompatibility));
        }

        [PatchPostfix]
        private static void Postfix(Slot __instance, Item item, ref bool __result)
        {
            try
            {
                if (SlotAccess.IsMarked(__instance))
                    __result = SlotAccess.Fits(__instance, item);
            }
            catch (Exception e) { SoftArmorSwapPlugin.Log.LogError("SlotCompatibilityPatch: " + e); }
        }
    }

    public class ArmorCollidersPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(ArmorComponent), nameof(ArmorComponent.ArmorColliders));
        }

        [PatchPostfix]
        private static void Postfix(ArmorComponent __instance, ref IEnumerable<EBodyPartColliderType> __result)
        {
            try
            {
                Slot slot = SlotAccess.MarkedSlotOf(__instance);
                if (slot != null) __result = slot.ArmorColliders ?? new EBodyPartColliderType[0];
            }
            catch (Exception e) { SoftArmorSwapPlugin.Log.LogError("ArmorCollidersPatch: " + e); }
        }
    }

    public class ArmorPlateMaskPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(ArmorComponent), nameof(ArmorComponent.ArmorPlateCollidersMask));
        }

        [PatchPostfix]
        private static void Postfix(ArmorComponent __instance, ref EArmorPlateCollider __result)
        {
            try
            {
                Slot slot = SlotAccess.MarkedSlotOf(__instance);
                if (slot != null) __result = slot.ArmorPlateColliderMask;
            }
            catch (Exception e) { SoftArmorSwapPlugin.Log.LogError("ArmorPlateMaskPatch: " + e); }
        }
    }

    internal static class InsertWeights
    {

        private static float ZoneBase(string zone)
        {
            switch (zone)
            {
                case "front": return 0.90f;
                case "back": return 0.80f;
                case "left":
                case "right": return 0.30f;
                case "NeckFront":
                case "NeckBack": return 0.20f;
                case "LeftUpperArm":
                case "RightUpperArm": return 0.15f;
                case "Pelvis": return 0.25f;
                case "PelvisBack": return 0.20f;
                case "ParietalHead": return 0.12f;
                case "BackHead": return 0.10f;
                case "Ears": return 0.06f;
                case "Eyes": return 0.08f;
                case "Jaw": return 0.10f;
                default: return 0.20f;
            }
        }

        private static float MaterialMultiplier(EArmorMaterial m)
        {
            switch (m)
            {
                case EArmorMaterial.UHMWPE: return 0.70f;
                case EArmorMaterial.Aramid: return 1.00f;
                case EArmorMaterial.Combined: return 1.15f;
                case EArmorMaterial.Glass: return 1.40f;
                case EArmorMaterial.Aluminium: return 1.50f;
                case EArmorMaterial.Ceramic: return 2.00f;
                case EArmorMaterial.Titan: return 2.20f;
                case EArmorMaterial.ArmoredSteel: return 2.80f;
                default: return 1.00f;
            }
        }

        private static float TypeMultiplier(EArmorType t)
        {
            return t == EArmorType.Heavy ? 1.30f : 1.00f;
        }

        public static float Compute(EArmorMaterial material, EArmorType type, HashSet<string> zones)
        {
            float zone = 0f;
            foreach (string z in zones) zone = Math.Max(zone, ZoneBase(z));
            if (zone <= 0f) zone = ZoneBase("");
            return (float)Math.Round(zone * MaterialMultiplier(material) * TypeMultiplier(type), 3);
        }
    }

    internal static class TemplateDb
    {
        private static bool _init;
        private static Type _factory;
        private static PropertyInfo _instance;
        private static FieldInfo _templates;

        private static bool Init()
        {
            if (_init) return _factory != null;
            _init = true;
            try
            {
                _factory = typeof(Slot).Assembly.GetTypes().FirstOrDefault(t =>
                    t.GetField("ItemTemplates", BindingFlags.Public | BindingFlags.Instance) != null &&
                    t.GetMethod("FlatItemsToTree") != null);
                Type singleton = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { singleton = a.GetTypes().FirstOrDefault(t => t.FullName == "Comfort.Common.Singleton`1"); }
                    catch { }
                    if (singleton != null) break;
                }
                if (_factory == null || singleton == null) { _factory = null; return false; }
                _instance = singleton.MakeGenericType(_factory).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                _templates = _factory.GetField("ItemTemplates", BindingFlags.Public | BindingFlags.Instance);
                if (_instance == null || _templates == null) { _factory = null; return false; }
                return true;
            }
            catch (Exception e)
            {
                SoftArmorSwapPlugin.Log.LogError("TemplateDb.Init: " + e);
                _factory = null;
                return false;
            }
        }

        public static object Get(MongoID id)
        {
            if (!Init()) return null;
            object factory = _instance.GetValue(null, null);
            if (factory == null) return null;
            object dict = _templates.GetValue(factory);
            if (dict == null) return null;
            MethodInfo tryGet = dict.GetType().GetMethod("TryGetValue");
            if (tryGet == null) return null;
            object[] args = new object[] { id, null };
            return (bool)tryGet.Invoke(dict, args) ? args[1] : null;
        }

        public static T Field<T>(object template, string name, T fallback)
        {
            FieldInfo f = template == null ? null : template.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (f == null) return fallback;
            object v = f.GetValue(template);
            return v is T ? (T)v : fallback;
        }
    }

    public class ItemWeightPatch : ModulePatch
    {

        private static readonly Dictionary<string, float> DefaultDelta = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> InsertCache = new Dictionary<string, float>();
        private static readonly HashSet<string> Logged = new HashSet<string>();

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(Item), "Weight");
        }

        [PatchPostfix]
        private static void Postfix(Item __instance, ref float __result)
        {
            try
            {
                ArmorComponent armor;
                if (SlotAccess.InsertKeys.Contains(__instance.StringTemplateId) && __instance.TryGetItemComponent<ArmorComponent>(out armor))
                {
                    float w = InsertWeight(__instance, armor);
                    if (w >= 0f) __result = w;
                    return;
                }

                CompoundItem carrier = __instance as CompoundItem;
                if (carrier == null) return;
                float delta = 0f;
                foreach (Slot slot in carrier.Slots)
                {
                    if (SlotAccess.IsMarked(slot)) delta += SlotDelta(slot);
                }
                if (delta == 0f) return;
                if (!Logged.Contains("carrier:" + __instance.StringTemplateId))
                    LogOnce("carrier:" + __instance.StringTemplateId, "Carrier " + __instance.StringTemplateId + " own weight " +
                    __result.ToString("0.###") + " kg, insert delta " + delta.ToString("0.###") + " kg, has ArmorComponent " +
                    (__instance.GetItemComponent<ArmorComponent>() != null) + ", template penalties speed/mouse/ergo " + Penalties(__instance.Template));
                float reduced = __result - delta;
                if (reduced < 0f)
                {
                    LogOnce("clamp:" + __instance.StringTemplateId, "Armor " + __instance.StringTemplateId +
                        " is lighter than its inserts (" + delta.ToString("0.###") + " kg); own weight clamped to 0");
                    reduced = 0f;
                }
                __result = reduced;
            }
            catch (Exception e) { SoftArmorSwapPlugin.Log.LogError("ItemWeightPatch: " + e); }
        }

        private static float InsertWeight(Item item, ArmorComponent armor)
        {
            string id = item.StringTemplateId;
            float w;
            if (InsertCache.TryGetValue(id, out w)) return w;

            HashSet<string> zones = SlotAccess.Zones(armor.Template.ArmorColliders, armor.Template.ArmorPlateColliders);
            w = InsertWeights.Compute(armor.Template.ArmorMaterial, armor.Template.ArmorType, zones);
            InsertCache[id] = w;
            LogOnce("ins:" + id, "Insert " + id + " " + armor.Template.ArmorMaterial + "/" + armor.Template.ArmorType +
                " zones[" + string.Join(",", zones.ToArray()) + "] = " + w.ToString("0.###") + " kg");
            return w;
        }

        private static float SlotDelta(Slot slot)
        {
            if (slot.Filters == null || slot.Filters.Length == 0 || slot.Filters[0] == null || slot.Filters[0].Filter == null ||
                slot.Filters[0].Filter.Length == 0) return 0f;
            MongoID stock = slot.Filters[0].Filter[0];
            string key = stock.ToString() + "/" + slot.ID;
            float d;
            if (DefaultDelta.TryGetValue(key, out d)) return d;

            object template = TemplateDb.Get(stock);
            if (template == null) return 0f;

            EArmorMaterial material = TemplateDb.Field(template, "ArmorMaterial", EArmorMaterial.Aramid);
            EArmorType type = TemplateDb.Field(template, "ArmorType", EArmorType.None);
            float vanilla = TemplateDb.Field(template, "Weight", 0f);
            HashSet<string> zones = SlotAccess.Zones(slot.ArmorColliders, slot.ArmorPlateColliderMask);
            float computed = InsertWeights.Compute(material, type, zones);
            d = computed - vanilla;
            DefaultDelta[key] = d;
            LogOnce("slot:" + key, "Stock insert " + stock + " in slot " + slot.ID + " " + material + "/" + type +
                " zones[" + string.Join(",", zones.ToArray()) + "] = " + computed.ToString("0.###") + " kg (vanilla " + vanilla.ToString("0.###") + ")" +
                " penalties speed/mouse/ergo " + Penalties(template));
            return d;
        }

        private static string Penalties(object template)
        {
            return TemplateDb.Field(template, "speedPenaltyPercent", 0f).ToString("0.##") + "/" +
                   TemplateDb.Field(template, "mousePenalty", 0f).ToString("0.##") + "/" +
                   TemplateDb.Field(template, "weaponErgonomicPenalty", 0f).ToString("0.##");
        }

        private static void LogOnce(string key, string msg)
        {
            if (Logged.Add(key)) SoftArmorSwapPlugin.Log.LogInfo(msg);
        }
    }

    public class VitalPartsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(CompoundItem), nameof(CompoundItem.VitalParts));
        }

        [PatchPostfix]
        private static void Postfix(ref IEnumerable<Slot> __result)
        {
            try
            {
                if (__result != null) __result = __result.Where(s => !SlotAccess.IsMarked(s));
            }
            catch (Exception e) { SoftArmorSwapPlugin.Log.LogError("VitalPartsPatch: " + e); }
        }
    }

    public class PenaltyScalePatch : ModulePatch
    {
        private readonly string _property;

        public PenaltyScalePatch(string property)
        {
            _property = property;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(ArmorComponent), _property);
        }

        [PatchPostfix]
        private static void Postfix(ArmorComponent __instance, ref float __result)
        {
            try
            {
                if (__result == 0f) return;

                CompoundItem carrier = null;
                Slot slot = SlotAccess.MarkedSlotOf(__instance);
                if (slot != null) carrier = slot.ParentItem as CompoundItem;
                else if (!SlotAccess.InsertKeys.Contains(__instance.Item.StringTemplateId)) carrier = __instance.Item as CompoundItem;
                if (carrier == null) return;
                __result *= SlotAccess.Fill(carrier);
            }
            catch (Exception e) { SoftArmorSwapPlugin.Log.LogError("PenaltyScalePatch: " + e); }
        }
    }

    public class PenaltyTooltipPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Constructor(typeof(EquipmentPenaltyComponent), new Type[] { typeof(Item), FindTemplateType(), typeof(bool) });
        }

        private static Type FindTemplateType()
        {
            FieldInfo f = typeof(EquipmentPenaltyComponent).GetField("Template", BindingFlags.Public | BindingFlags.Instance);
            return f.FieldType;
        }

        private static readonly Dictionary<string, string> OwnFields = new Dictionary<string, string>
        {
            { "ChangeMovementSpeed", "speedPenaltyPercent" },
            { "ChangeTurningSpeed", "mousePenalty" },
            { "Ergonomics", "weaponErgonomicPenalty" }
        };

        private static object Get(object o, string name)
        {
            Type t = o.GetType();
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null) return p.GetValue(o, null);
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f == null ? null : f.GetValue(o);
        }

        private static void Set(object o, string name, object value)
        {
            Type t = o.GetType();
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null && p.CanWrite) { p.SetValue(o, value, null); return; }
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(o, value);
        }

        [PatchPostfix]
        private static void Postfix(EquipmentPenaltyComponent __instance, Item item)
        {
            try
            {
                CompoundItem carrier = item as CompoundItem;
                if (carrier == null) return;
                foreach (object attr in item.Attributes)
                {
                    object id = Get(attr, "Id");
                    string field;
                    if (id == null || !OwnFields.TryGetValue(id.ToString(), out field)) continue;
                    float own = TemplateDb.Field(__instance.Template, field, 0f);
                    if (own == 0f) continue;

                    Func<float> baseFn = Get(attr, "Base") as Func<float>;
                    Func<string> strFn = Get(attr, "StringValue") as Func<string>;
                    if (baseFn == null) continue;

                    Func<float> scaledBase = () => baseFn() - own * (1f - SlotAccess.Fill(carrier));
                    Set(attr, "Base", scaledBase);
                    if (strFn != null)
                    {
                        Set(attr, "StringValue", (Func<string>)(() =>
                        {
                            string text = strFn();
                            float before = baseFn();
                            float after = scaledBase();
                            return before == after ? text : text.Replace(before.ToString(), after.ToString());
                        }));
                    }
                }
            }
            catch (Exception e) { SoftArmorSwapPlugin.Log.LogError("PenaltyTooltipPatch: " + e); }
        }
    }

    public class InsertDropModelPatch : ModulePatch
    {
        private const string InsertBase = "65649eb40bf0ed77b8044453";
        private const string PlateBase = "644120aa86ffbe10ee032b6f";
        private const string FallbackDonor = "64afdcb83efdfea28601d041";
        private const float SmallScale = 0.5f;
        private static readonly Stack<Action> Restores = new Stack<Action>();
        private static readonly HashSet<string> Reported = new HashSet<string>();

        protected override MethodBase GetTargetMethod()
        {
            foreach (Type type in typeof(Slot).Assembly.GetTypes())
            {
                MethodInfo method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "CreateLootPrefab" && m.GetParameters().Length == 3);
                if (method != null) return method;
            }
            return null;
        }

        private static ItemTemplate LoadedPlate()
        {
            try
            {
                GameWorld world = Comfort.Common.Singleton<GameWorld>.Instance;
                CompoundItem equipment = world == null || world.MainPlayer == null ? null : world.MainPlayer.Inventory.Equipment;
                if (equipment != null)
                {
                    foreach (Item gear in equipment.GetAllItems())
                    {
                        ItemTemplate t = gear.Template;
                        if (t != null && t.Prefab != null && !string.IsNullOrEmpty(t.Prefab.path) && t.IsChildOf(PlateBase)) return t;
                    }
                }
            }
            catch (Exception e)
            {
                SoftArmorSwapPlugin.Log.LogError("InsertDropModelPatch.LoadedPlate: " + e);
            }
            return TemplateDb.Get(new MongoID(FallbackDonor)) as ItemTemplate;
        }

        [PatchPrefix]
        private static void Prefix(Item item, ref int __state)
        {
            __state = 0;
            try
            {
                ItemTemplate template = item == null ? null : item.Template;
                if (template == null || !template.IsChildOf(InsertBase) || template.Prefab == null) return;
                string path = template.Prefab.path ?? "";
                bool large = path.Contains("soft_armor_front") || path.Contains("soft_armor_back");
                ItemTemplate donor = LoadedPlate();
                if (donor == null || donor.Prefab == null) return;
                var original = template.Prefab;
                template.Prefab = donor.Prefab;
                Restores.Push(() => template.Prefab = original);
                __state = large ? 1 : 2;
            }
            catch (Exception e)
            {
                SoftArmorSwapPlugin.Log.LogError("InsertDropModelPatch: " + e);
            }
        }

        [PatchPostfix]
        private static void Postfix(Item item, UnityEngine.GameObject __result, int __state)
        {
            if (__state == 0) return;
            if (Restores.Count > 0) Restores.Pop()();
            if (__result != null && __state == 2) __result.transform.localScale = UnityEngine.Vector3.one * SmallScale;
            if (Reported.Add(item.StringTemplateId))
                SoftArmorSwapPlugin.Log.LogInfo("Insert drop model for " + item.StringTemplateId + ": " + (__result == null ? "NOT created" : "created"));
        }
    }
}
