using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SPT.Reflection.Patching;
using UnityEngine.EventSystems;

namespace Kommie.ArmorRefurbishment.Client
{

    internal static class ArmorRefurbishmentPlugin
    {

        internal const string SoftKitId = "6723a1c4e8b2d90a7c5f1e01";
        internal const string HelmetKitId = "6723a1c4e8b2d90a7c5f1e02";
        internal const string Route = "/armorrefurbishment/apply";

        internal static ManualLogSource Log;

        internal static void Init(ManualLogSource log)
        {
            Log = log;
            new KitDropPatch().Enable();
            log.LogInfo("Armor Refurbishment Client loaded");
        }

        internal static bool IsKit(Item item)
        {
            string id = item.TemplateId.ToString();
            return id == SoftKitId || id == HelmetKitId;
        }
    }

    internal class KitDropPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(ItemView).GetMethod("ProcessItem",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(DragItemContext), typeof(PointerEventData) }, null);
        }

        [PatchPrefix]
        private static bool Prefix(DragItemContext dragItemContext, PointerEventData eventData)
        {
            Item kit = dragItemContext.Item;
            if (!ArmorRefurbishmentPlugin.IsKit(kit)) return true;
            if (eventData.pointerEnter == null) return true;

            ItemView view = eventData.pointerEnter.GetComponentInParent<ItemView>();
            Item target = view != null && view.ItemContext != null ? view.ItemContext.Item : null;
            if (target == null) return true;

            dragItemContext.DragCancelled();

            if (target.Id == kit.Id) return Reject("A kit cannot be used on itself");
            if (target.Owner == null || target.Owner.OwnerType != EOwnerType.Profile)
                return Reject("Refurbishment only works on gear in your inventory");
            if (!kit.TryGetItemComponent(out RepairKitComponent kitComponent))
                return Reject("This kit has no uses left");

            try
            {
                JObject reply = Send(kit.Id.ToString(), target.Id.ToString());
                JObject data = reply["data"] as JObject;
                if (data == null) throw new InvalidOperationException("Empty server reply");

                if (!(bool)data["ok"]) return Reject((string)data["message"]);

                Apply(data, target, kit, kitComponent);
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.RepairComplete);
                NotificationManager.DisplayMessageNotification((string)data["message"]);
            }
            catch (Exception e)
            {
                ArmorRefurbishmentPlugin.Log.LogError(e);
                Reject("Refurbishment failed: server error");
            }
            return false;
        }

        private static bool Reject(string message)
        {
            Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.ErrorMessage);
            NotificationManager.DisplayMessageNotification(message, ENotificationDurationType.Default, ENotificationIconType.Alert);
            return false;
        }

        private static MethodInfo _post;

        private static JObject Send(string kitId, string targetId)
        {
            if (_post == null)
            {
                Type handler = AccessTools.TypeByName("SPT.Common.Http.RequestHandler");
                _post = AccessTools.Method(handler, "PostJson", new[] { typeof(string), typeof(string) });
            }
            string body = new JObject { ["kitId"] = kitId, ["targetId"] = targetId }.ToString();
            string reply;
            try { reply = (string)_post.Invoke(null, new object[] { ArmorRefurbishmentPlugin.Route, body }); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
            return JObject.Parse(reply);
        }

        private static void Apply(JObject data, Item target, Item kit, RepairKitComponent kitComponent)
        {
            List<Item> pool = target.GetAllItems().ToList();
            foreach (JToken piece in data["pieces"])
            {
                Item item = pool.FirstOrDefault(i => i.Id == (string)piece["id"]);
                if (item == null || !item.TryGetItemComponent(out RepairableComponent repairable)) continue;
                repairable.MaxDurability = (float)piece["maxDurability"];
                repairable.Durability = (float)piece["durability"];
                item.UpdateAttributes();
                item.RaiseRefreshEvent(true, true);
            }
            target.UpdateAttributes();
            target.RaiseRefreshEvent(true, true);

            float left = (float)data["kitResource"];
            kitComponent.Resource = left;
            kit.UpdateAttributes();
            kit.RaiseRefreshEvent(true, true);
            if (left <= 0f)
            {
                ItemController owner = kit.Parent != null ? kit.Parent.GetOwner() as ItemController : null;
                if (owner != null) owner.ThrowItem(kit);
            }
        }
    }
}
