using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace kommie.armorstacking
{
    internal static class ArmorStackingPlugin
    {
        internal static bool Active;
        internal static ConfigEntry<bool> CarryToShot;
        internal static ConfigEntry<bool> DebugLog;
        internal static ConfigEntry<float> ExtraAbsorption;
        internal static ManualLogSource Log;
        internal static bool SoftPatchActive;

        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static |
                                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        internal static void Init(ManualLogSource log)
        {
            Log = log;

            Assembly asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (asm == null) { Log.LogError("Assembly-CSharp not found."); return; }

            Type[] types = SafeTypes(asm);
            if (!ResistanceAccess.Init(types))
            {
                Log.LogError("ArmorStacking DISABLED: the game's RealResistance(...) helper was not found " +
                                "(the game version probably changed). No patches were applied.");
                return;
            }
            Type player = types.FirstOrDefault(t => t.FullName == "EFT.Player");
            Type armor = types.FirstOrDefault(t => t.Name == "ArmorComponent");
            Type inventory = types.FirstOrDefault(t => t.FullName == "EFT.InventoryLogic.Inventory");

            MethodInfo setShot = player == null ? null : AccessTools.DeclaredMethod(player, "SetShotStatus");
            MethodInfo setPen = armor == null ? null : AccessTools.DeclaredMethod(armor, "SetPenetrationStatus");
            if (setShot == null || setPen == null)
            {
                Log.LogError("Could not find Player.SetShotStatus and/or ArmorComponent.SetPenetrationStatus.");
                return;
            }

            var harmony = new Harmony("kommie.armorstacking");
            harmony.Patch(setShot,
                new HarmonyMethod(typeof(Patches), nameof(Patches.ShotPre)),
                new HarmonyMethod(typeof(Patches), nameof(Patches.ShotPost)),
                null,
                new HarmonyMethod(typeof(Patches), nameof(Patches.ShotFin)),
                null);
            harmony.Patch(setPen,
                new HarmonyMethod(typeof(Patches), nameof(Patches.PenPre)),
                new HarmonyMethod(typeof(Patches), nameof(Patches.PenPost)),
                null, null, null);
            Active = true;
            Log.LogInfo("Hooked Player.SetShotStatus and ArmorComponent.SetPenetrationStatus.");

            Try("physical armor order", () =>
            {
                MethodInfo[] ms = Declared(inventory, "GetPutOnArmorsNonAlloc");
                if (ms.Length == 0) throw new Exception("Inventory.GetPutOnArmorsNonAlloc not found");
                foreach (MethodInfo m in ms)
                    harmony.Patch(m, null, new HarmonyMethod(typeof(Patches), nameof(Patches.SortPost)), null, null, null);
                Log.LogInfo("Hooked Inventory.GetPutOnArmorsNonAlloc (physical layer order).");
            });

            Try("soft armor in plate hits", () =>
            {
                MethodInfo[] matches = Declared(armor, "ShotMatches");
                MethodInfo[] applies = Declared(player, "ApplyShot");
                if (matches.Length == 0 || applies.Length == 0) throw new Exception("ShotMatches/ApplyShot not found");
                foreach (MethodInfo m in matches)
                    harmony.Patch(m, null, new HarmonyMethod(typeof(Patches), nameof(Patches.MatchPost)), null, null, null);
                foreach (MethodInfo m in applies)
                    harmony.Patch(m, null, null, null, new HarmonyMethod(typeof(Patches), nameof(Patches.ApplyFin)), null);
                SoftPatchActive = true;
                Log.LogInfo("Hooked ArmorComponent.ShotMatches and Player.ApplyShot (soft armor in plate hits).");
            });

            Try("body armor with plate carrier", () =>
            {
                Type blocker = types.FirstOrDefault(t => t.FullName == "EFT.InventoryLogic.SlotBlockerComponent");
                if (blocker == null) throw new Exception("SlotBlockerComponent not found");
                ConstructorInfo[] ctors = blocker.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (ctors.Length == 0) throw new Exception("SlotBlockerComponent constructor not found");
                foreach (ConstructorInfo c in ctors)
                    harmony.Patch(c, null, new HarmonyMethod(typeof(Patches), nameof(Patches.BlockerPost)), null, null, null);
                Log.LogInfo("Hooked SlotBlockerComponent (body armor + plate carrier rig can be worn together).");
            });
        }

        private static void Try(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.LogError("Could not hook '" + what + "': " + e.Message); }
        }

        private static MethodInfo[] Declared(Type t, string name)
        {
            if (t == null) return new MethodInfo[0];
            return t.GetMethods(All).Where(m => m.Name == name && !m.IsAbstract && !m.ContainsGenericParameters).ToArray();
        }

        private static Type[] SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
        }
    }

    public static class Patches
    {
        private sealed class Ctx
        {
            public object Shot;
            public float Orig;
            public float Factor = 1f;
            public int Layers;
        }

        private sealed class Pend
        {
            public object SkipSoftType;
        }

        [ThreadStatic] private static Ctx _ctx;
        [ThreadStatic] private static Pend _pending;

        public static void ShotPre(object bodypart, object shot)
        {
            _ctx = null;
            _pending = null;
            try
            {
                if (!ArmorStackingPlugin.Active || shot == null || !ShotAccess.Init(shot)) return;

                _ctx = new Ctx { Shot = shot, Orig = ShotAccess.GetPen(shot) };

                if (ArmorStackingPlugin.SoftPatchActive &&
                    bodypart != null && !ArmorInfo.HasBase(bodypart.GetType(), "ArmorPlateCollider"))
                {
                    object parent = ShotAccess.Parent(shot);
                    object parentHit = parent == null ? null : ShotAccess.HitCollider(parent);
                    if (parentHit != null && ArmorInfo.HasBase(parentHit.GetType(), "ArmorPlateCollider") &&
                        ReferenceEquals(ArmorInfo.Get(parentHit, "Player"), ArmorInfo.Get(bodypart, "Player")))
                    {
                        object zone = ArmorInfo.Get(parentHit, "BodyPartColliderType");
                        if (zone != null) _pending = new Pend { SkipSoftType = zone };
                    }
                }
            }
            catch (Exception e) { ArmorStackingPlugin.Log.LogError("ShotPre: " + e); }
        }

        public static void PenPre(object shot, out float __state)
        {
            __state = -1f;
            try
            {
                Ctx c = _ctx;
                if (c == null || !ReferenceEquals(c.Shot, shot) || c.Factor >= 0.9999f) return;
                __state = c.Orig;
                ShotAccess.SetPen(shot, c.Orig * c.Factor);
            }
            catch (Exception e) { ArmorStackingPlugin.Log.LogError("PenPre: " + e); }
        }

        public static void PenPost(object __instance, object shot, float __state)
        {
            try
            {
                Ctx c = _ctx;
                if (c == null || !ReferenceEquals(c.Shot, shot)) return;

                if (__state >= 0f) ShotAccess.SetPen(shot, __state);

                bool blocked = ShotAccess.Blocked(shot);
                bool plate = ArmorInfo.IsPlate(__instance);
                float used = c.Orig * c.Factor;

                if (ArmorStackingPlugin.DebugLog.Value)
                {
                    ArmorStackingPlugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                        "[{0}] {1}: pen used {2:0.0} (shot {3:0.0}, stack x{4:0.00}) -> {5}",
                        plate ? "plate" : "soft", ArmorInfo.Describe(__instance), used, c.Orig, c.Factor,
                        blocked ? "BLOCKED" : "passed"));
                }

                if (blocked) return;

                float dur = ArmorInfo.DurabilityFraction(__instance);
                if (dur >= 0f && dur <= 0.0001f) return;

                float resistance, layerFactor;
                if (!ResistanceAccess.TryGet(__instance, used, out resistance, out layerFactor)) return;

                float vanillaCf = layerFactor;
                float extra = ArmorStackingPlugin.ExtraAbsorption.Value / 100f;
                if (extra > 0f)
                {
                    layerFactor *= 1f - extra * resistance / (resistance + used);
                    if (layerFactor < 0f) layerFactor = 0f;
                }

                if (ArmorStackingPlugin.DebugLog.Value)
                {
                    ArmorStackingPlugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                        "[{0}] resistance R={1:0.0} vs pen {2:0.0} -> vanilla CF x{3:0.000}, total x{4:0.000} (pen out {5:0.0})",
                        plate ? "plate" : "soft", resistance, used, vanillaCf, layerFactor, used * layerFactor));
                }

                c.Factor *= layerFactor;
                c.Layers++;
            }
            catch (Exception e) { ArmorStackingPlugin.Log.LogError("PenPost: " + e); }
        }

        public static void ShotPost(object shot, bool __result)
        {
            try
            {
                Ctx c = _ctx;
                if (c == null || !ReferenceEquals(c.Shot, shot) || c.Layers == 0) return;

                bool blocked = ShotAccess.Blocked(shot);
                if (ArmorStackingPlugin.CarryToShot.Value && !__result && !blocked)
                    ShotAccess.SetPen(shot, c.Orig * c.Factor);

                if (ArmorStackingPlugin.DebugLog.Value)
                {
                    ArmorStackingPlugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                        "[hit] {0} layer(s) defeated, pen {1:0.0} -> {2:0.0}{3}",
                        c.Layers, c.Orig, ShotAccess.GetPen(shot), blocked ? ", bullet blocked" : ""));
                }
            }
            catch (Exception e) { ArmorStackingPlugin.Log.LogError("ShotPost: " + e); }
        }

        public static Exception ShotFin(Exception __exception)
        {
            _ctx = null;
            return __exception;
        }

        public static void SortPost(IList armorComponents)
        {
            try
            {
                if (!ArmorStackingPlugin.Active ||
                    armorComponents == null || armorComponents.Count < 2) return;

                var items = new List<object>(armorComponents.Count);
                foreach (object a in armorComponents) items.Add(a);

                var ordered = items
                    .Select((a, idx) => new
                    {
                        a,
                        idx,
                        carrier = ArmorInfo.CarrierPriority(a),
                        kind = ArmorInfo.VanillaOrder(a)
                    })
                    .OrderBy(x => x.carrier).ThenBy(x => x.kind).ThenBy(x => x.idx)
                    .ToList();

                armorComponents.Clear();
                foreach (var x in ordered) armorComponents.Add(x.a);
            }
            catch (Exception e) { ArmorStackingPlugin.Log.LogError("SortPost: " + e); }
        }

        public static void MatchPost(object __instance, object colliderType, object armorPlateCollider, ref bool __result)
        {
            try
            {
                if (!ArmorStackingPlugin.Active) return;

                if (Convert.ToInt32(armorPlateCollider, CultureInfo.InvariantCulture) != 0)
                {
                    if (!__result && ArmorInfo.IsSoft(__instance) && ArmorInfo.CoversCollider(__instance, colliderType))
                        __result = true;
                }
                else
                {
                    Pend p = _pending;
                    if (__result && p != null && p.SkipSoftType != null &&
                        ArmorInfo.IsSoft(__instance) && ArmorInfo.CoversCollider(__instance, p.SkipSoftType))
                        __result = false;
                }
            }
            catch (Exception e) { ArmorStackingPlugin.Log.LogError("MatchPost: " + e); }
        }

        private static FieldInfo _blockerNames;

        public static void BlockerPost(object __instance)
        {
            try
            {
                if (!ArmorStackingPlugin.Active || __instance == null) return;

                if (_blockerNames == null)
                    _blockerNames = __instance.GetType().GetField("ConflictingSlotNames",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_blockerNames == null) return;

                string[] names = _blockerNames.GetValue(__instance) as string[];
                if (names == null || !names.Contains("ArmorVest")) return;

                _blockerNames.SetValue(__instance, names.Where(n => n != "ArmorVest").ToArray());
            }
            catch (Exception e) { ArmorStackingPlugin.Log.LogError("BlockerPost: " + e); }
        }

        public static Exception ApplyFin(Exception __exception)
        {
            _pending = null;
            return __exception;
        }
    }

    internal static class ResistanceAccess
    {
        private static MethodInfo _method;
        private static FieldInfo _real;
        private static FieldInfo _cf;
        private static bool _warned;

        public static bool Init(Type[] types)
        {
            try
            {
                foreach (Type t in types)
                {
                    if (t == null || !t.IsAbstract || !t.IsSealed) continue;
                    foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (m.Name != "RealResistance") continue;
                        ParameterInfo[] p = m.GetParameters();
                        if (p.Length != 4 || p[0].ParameterType != typeof(float) || p[1].ParameterType != typeof(float) ||
                            p[2].ParameterType != typeof(int) || p[3].ParameterType != typeof(float)) continue;
                        FieldInfo f = m.ReturnType.GetField("RealResistance", BindingFlags.Instance | BindingFlags.Public);
                        FieldInfo cf = m.ReturnType.GetField("CF", BindingFlags.Instance | BindingFlags.Public);
                        if (f == null || f.FieldType != typeof(float) || cf == null || cf.FieldType != typeof(float)) continue;
                        _method = m;
                        _real = f;
                        _cf = cf;
                        ArmorStackingPlugin.Log.LogInfo("Found resistance helper " + t.FullName + "." + m.Name +
                                                        " (vanilla class/durability based pen carry-over active).");
                        return true;
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                ArmorStackingPlugin.Log.LogError("ResistanceAccess.Init: " + e);
                return false;
            }
        }

        public static bool TryGet(object armor, float pen, out float resistance, out float cf)
        {
            resistance = 0f;
            cf = 1f;
            if (_method == null) return false;
            try
            {
                object d = ArmorInfo.Get(armor, "Repairable.Durability");
                object m = ArmorInfo.Get(armor, "Repairable.TemplateDurability");
                object c = ArmorInfo.Get(armor, "ArmorClass");
                if (d == null || m == null || c == null) return false;

                object result = _method.Invoke(null, new object[]
                {
                    Convert.ToSingle(d, CultureInfo.InvariantCulture),
                    Convert.ToSingle(m, CultureInfo.InvariantCulture),
                    Convert.ToInt32(c, CultureInfo.InvariantCulture),
                    pen
                });
                resistance = (float)_real.GetValue(result);
                cf = (float)_cf.GetValue(result);
                return resistance > 0f && !float.IsNaN(resistance) && !float.IsInfinity(resistance) &&
                       cf > 0f && cf <= 1f && !float.IsNaN(cf);
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    ArmorStackingPlugin.Log.LogError("ResistanceAccess.TryGet: " + e.Message + " (layer not slowed)");
                }
                return false;
            }
        }
    }

    internal static class ShotAccess
    {
        private static FieldInfo _pen;
        private static FieldInfo _blocked;
        private static FieldInfo _parent;
        private static FieldInfo _hit;

        public static bool Init(object shot)
        {
            if (_pen != null && _blocked != null) return true;
            Type t = shot.GetType();
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public;
            _pen = t.GetField("PenetrationPower", f);
            _blocked = t.GetField("BlockedBy", f);
            _parent = t.GetField("Parent", f);
            _hit = t.GetField("HittedBallisticCollider", f);
            return _pen != null && _blocked != null;
        }

        public static float GetPen(object shot) { return (float)_pen.GetValue(shot); }
        public static void SetPen(object shot, float v) { _pen.SetValue(shot, v); }
        public static bool Blocked(object shot) { return _blocked.GetValue(shot) != null; }
        public static object Parent(object shot) { return _parent == null ? null : _parent.GetValue(shot); }
        public static object HitCollider(object shot) { return _hit == null ? null : _hit.GetValue(shot); }
    }

    internal static class ArmorInfo
    {
        public static bool HasBase(Type t, string name)
        {
            for (; t != null; t = t.BaseType)
                if (t.Name == name) return true;
            return false;
        }

        public static bool IsSoft(object armor)
        {
            object mask = Member(armor, "ArmorPlateCollidersMask");
            return mask == null || Convert.ToInt32(mask, CultureInfo.InvariantCulture) == 0;
        }

        public static bool IsPlate(object armor) { return !IsSoft(armor); }

        public static bool CoversCollider(object armor, object colliderType)
        {
            IEnumerable colliders = Member(armor, "ArmorColliders") as IEnumerable;
            if (colliders == null || colliderType == null) return false;
            foreach (object c in colliders)
                if (c != null && c.Equals(colliderType)) return true;
            return false;
        }

        public static int VanillaOrder(object armor)
        {
            object item = Member(armor, "Item");
            object addr = item == null ? null : Member(item, "CurrentAddress");
            object slot = addr == null ? null : Member(addr, "Slot");
            if (slot == null || !HasBase(slot.GetType(), "ArmorSlot")) return 1;
            object locked = Member(slot, "Locked");
            return (locked is bool b && !b) ? -1 : 1;
        }

        public static string CarrierSlotId(object armor)
        {
            object item = Member(armor, "Item");
            for (int i = 0; i < 8 && item != null; i++)
            {
                object addr = Member(item, "CurrentAddress");
                object slot = addr == null ? null : Member(addr, "Slot");
                if (slot == null) return null;
                object parent = Member(slot, "ParentItem");
                if (parent == null || parent.GetType().Name == "InventoryEquipment")
                    return Member(slot, "ID") as string;
                item = parent;
            }
            return null;
        }

        public static int CarrierPriority(object armor)
        {
            string id = CarrierSlotId(armor);
            if (id == "TacticalVest") return 0;
            if (id == "ArmorVest") return 1;
            return 2;
        }

        public static float DurabilityFraction(object armor)
        {
            object d = Get(armor, "Repairable.Durability");
            object m = Get(armor, "Repairable.TemplateDurability");
            if (d == null || m == null) return -1f;
            float df = Convert.ToSingle(d, CultureInfo.InvariantCulture);
            float mf = Convert.ToSingle(m, CultureInfo.InvariantCulture);
            return mf <= 0f ? -1f : df / mf;
        }

        public static string Describe(object armor)
        {
            object name = Get(armor, "Item.Template._name") ?? Get(armor, "Item.Template.Name");
            object cls = Get(armor, "ArmorClass");
            float dur = DurabilityFraction(armor);
            return (name ?? "armor") + " (class " + (cls ?? "?") + ", dur " +
                   (dur < 0f ? "?" : (dur * 100f).ToString("0", CultureInfo.InvariantCulture) + "%") +
                   ", " + (CarrierSlotId(armor) ?? "?") + ")";
        }

        public static object Get(object o, string path)
        {
            foreach (string part in path.Split('.'))
            {
                if (o == null) return null;
                o = Member(o, part);
            }
            return o;
        }

        private static object Member(object o, string name)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            try
            {
                for (Type t = o.GetType(); t != null && t != typeof(object); t = t.BaseType)
                {
                    FieldInfo fi = t.GetField(name, f);
                    if (fi != null) return fi.GetValue(o);
                    PropertyInfo pi = t.GetProperty(name, f);
                    if (pi != null && pi.CanRead && pi.GetIndexParameters().Length == 0) return pi.GetValue(o, null);
                }
            }
            catch { }
            return null;
        }
    }
}
