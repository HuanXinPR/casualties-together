using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Together;
using UnityEngine;

namespace KrokV5Optimization;

/// <summary>
/// a full hotbar does not drop the craft. AutoPickUpItem parents the result into a bag.
/// the host has that child. the spawn packet is written while the item is still loose,
/// so the client never puts it in the bag. publish the live parent after a move,
/// and parent it here when that byte arrives.
/// a bag insert that cannot name the bag used to be sent as an unload. the host
/// then took the item out, and the client popped it 1.5 up when the ignore ended.
/// that announce is held until the bag has a sync id. the host also rewrites a
/// stored byte that disagrees with the live parent, which is the canvas other
/// machines still had in a mouth slot.
/// </summary>
internal static class BagPlace
{
	private const float WaitSeconds = 30f;
	private const float AnnounceSeconds = 2f;
	private const float AnnounceGap = 0.15f;
	private const byte InBag = 1;
	private const byte Worn = 100;
	private const int SweepBudget = 32;

	private struct Pending
	{
		internal SyncInfo Info;
		internal int ItemId;
		internal float Until;
	}

	private struct Delay
	{
		internal SyncInfo Info;
		internal int ItemId;
		internal float Next;
		internal float Until;
	}

	private static readonly List<Pending> Waiting = new List<Pending>(16);
	private static readonly List<Delay> Announcing = new List<Delay>(8);
	private static readonly List<SyncInfo> SweepList = new List<SyncInfo>(256);
	private static int _sweepAt;
	private static bool _sweeping;
	private static float _sweepNext;
	private static bool _fromRetry;
	private static bool _blocked;
	private static int _warns;

	// load, pickup, drop, wear, unload, the container apply, and the client announce.
	internal static void Install(Harmony harmony)
	{
		if (harmony == null)
			return;

		MethodInfo load = AccessTools.Method(typeof(Container), "LoadItem", new[] { typeof(Item) });
		if (load != null)
			harmony.Patch(load, postfix: new HarmonyMethod(typeof(BagPlace), nameof(AfterLoad)));
		else
			OptimizerMain.Log.LogWarning("[KrokV5Optimization] bag place missed Container.LoadItem");

		HookPublish(harmony, AccessTools.Method(typeof(Body), "PickUpItem", new[] { typeof(Item), typeof(int), typeof(bool) }));
		HookPublish(harmony, AccessTools.Method(typeof(Body), "DropItem", new[] { typeof(Item) }));
		HookPublish(harmony, AccessTools.Method(typeof(Body), "WearWearable", new[] { typeof(Item) }));
		HookPublish(harmony, AccessTools.Method(typeof(Body), "DropWearable", new[] { typeof(Item) }));
		MethodInfo unload = AccessTools.Method(typeof(Container), "UnloadItem", new[] { typeof(Item), typeof(Body) });
		if (unload != null)
			harmony.Patch(unload, postfix: new HarmonyMethod(typeof(BagPlace), nameof(AfterUnload)));
		else
			OptimizerMain.Log.LogWarning("[KrokV5Optimization] bag place missed Container.UnloadItem");

		OptimizerMain.Log.LogDebug("[KrokV5Optimization] bag place publishes the live parent");
	}

	// host walks stored parents. a client retries bag parenting and any announce still waiting.
	internal static void Tick()
	{
		if (!Net.IsRunning)
			return;
		if (Net.IsServer)
		{
			Sweep();
			return;
		}

		RetryAnnounces();
		if (Waiting.Count == 0)
			return;

		float now = Time.realtimeSinceStartup;
		for (int i = Waiting.Count - 1; i >= 0; i--)
		{
			Pending row = Waiting[i];
			if (row.Info == null || row.Until < now || Settle(row.Info))
				Waiting.RemoveAt(i);
		}
	}

	// host. the item is already parented. register both, then store the live parent
	// over the loose byte written when the result was created.
	private static void AfterLoad(Container __instance, Item item)
	{
		Publish(item, __instance != null ? __instance.gameObject : null);
	}

	// pickup, drop, or wear already changed the parent. store that, including a mouth slot.
	private static void AfterSlot(Item item)
	{
		Publish(item, null);
	}

	// the item is out of the bag. store loose so other machines take it out too.
	private static void AfterUnload(Container __instance, Item item)
	{
		Publish(item, null);
	}

	// false skips the announce. a parented bag with no sync id, or a body whose
	// sync link is missing, would be sent as an unload. hold it and try again.
	internal static bool BeforeAnnounce(SyncInfo si)
	{
		_blocked = false;
		if (!Net.IsRunning || si == null || Net.IsServer)
			return true;
		Item item = ItemOf(si);
		Transform parent = item != null && item.transform != null ? item.transform.parent : null;
		if (parent == null || parent.GetComponent<Container>() == null)
			return true;
		if (!Nameless(item))
			return true;

		_blocked = true;
		if (!_fromRetry)
			HoldAnnounce(si);
		return false;
	}

	// false skips a loose-byte apply while that bag announce is still waiting.
	// the ignore window is what used to end by popping the item out of the bag.
	internal static bool BeforeApply(SyncInfo __instance)
	{
		if (__instance == null || Net.IsServer || !AnnounceHeld(__instance))
			return true;
		if (__instance.container_data1.Value != 0)
			return true;
		Item item = ItemOf(__instance);
		Transform parent = item != null && item.transform != null ? item.transform.parent : null;
		return parent == null || parent.GetComponent<Container>() == null;
	}

	// client. stock skips the bag byte. parent the item if it is not in that bag yet.
	internal static void AfterApply(SyncInfo __instance)
	{
		if (__instance == null || Net.IsServer)
			return;
		if (Settle(__instance))
		{
			// 当帧已直接完成装包，清理 FixInvSync 中刚入队的等待项
			FixInvSync.ClearManaged(__instance);
			return;
		}
		// 若已由 FixInvSync 托管重试，不再重复加入 BagPlace.Waiting，避免双队列重复轮询
		if (FixInvSync.Manages(__instance))
			return;
		Remember(__instance);
	}

	// true when there is nothing left to do for this item.
	private static bool Settle(SyncInfo si)
	{
		Item item = ItemOf(si);
		if (item == null || si.container_data1.Value != InBag)
			return true;

		ushort net = si.container_netId.Value;
		if (net == 0)
			return false;
		if (InThisBag(item, net))
			return true;
		if (!NetObjectRegistry.TryGetSyncInfo((knetid)net, out SyncInfo bag) || bag == null || bag.go == null)
			return false;

		Container box = bag.container;
		if (box == null)
			box = bag.go.GetComponent<Container>();
		if (box == null)
			box = bag.go.GetComponentInChildren<Container>(true);
		if (box == null)
			return false;
		// already spilled. putting it back turns physics off.
		if (ItemSync.BrokenBag(bag, box)) {
			ItemSync.ParentTestChest("chest skip settle", item, bag.go);
			return true;
		}

		try
		{
			// 直接调用同程序集静态方法，消除 MethodInfo.Invoke 数组分配与反射开销
			ItemSync.Container_ForceLoadItem(item, box);
		}
		catch (Exception ex)
		{
			Warn("place", ex);
			return true;
		}
		return InThisBag(item, net);
	}

	// true when this parent is that bag. a container with no sync id yet still counts as placed.
	private static bool InThisBag(Item item, ushort net)
	{
		Transform parent = item.transform != null ? item.transform.parent : null;
		if (parent == null || parent.GetComponent<Container>() == null)
			return false;
		if (!NetObjectRegistry.TryGetSyncInfo(parent.gameObject, out SyncInfo bag) || bag == null)
			return true;
		try
		{
			return (ushort)bag.syncId == net;
		}
		catch (Exception)
		{
			return true;
		}
	}

	// client. the bag byte arrived and the item is not in that bag yet. retry for 30s.
	private static void Remember(SyncInfo si)
	{
		int id = ItemId(si);
		if (id == 0)
			return;

		for (int i = 0; i < Waiting.Count; i++)
		{
			if (Waiting[i].ItemId == id)
				return;
		}
		if (Waiting.Count > 256)
			Waiting.RemoveAt(0);
		Waiting.Add(new Pending
		{
			Info = si,
			ItemId = id,
			Until = Time.realtimeSinceStartup + WaitSeconds,
		});
	}

	// wait up to 2s. ignore server updates for that long so a loose byte cannot pop the item.
	private static void HoldAnnounce(SyncInfo si)
	{
		int id = ItemId(si);
		if (id == 0)
			return;
		float now = Time.realtimeSinceStartup;
		for (int i = 0; i < Announcing.Count; i++)
		{
			if (Announcing[i].ItemId != id)
				continue;
			Delay row = Announcing[i];
			row.Info = si;
			row.Next = now + AnnounceGap;
			row.Until = now + AnnounceSeconds;
			Announcing[i] = row;
			si.SetIgnoreTimeForRoundTrip(AnnounceSeconds);
			return;
		}
		if (Announcing.Count > 64)
			Announcing.RemoveAt(0);
		Announcing.Add(new Delay
		{
			Info = si,
			ItemId = id,
			Next = now + AnnounceGap,
			Until = now + AnnounceSeconds,
		});
		si.SetIgnoreTimeForRoundTrip(AnnounceSeconds);
	}

	// true while this item's bag announce is still waiting for a sync id.
	private static bool AnnounceHeld(SyncInfo si)
	{
		int id = ItemId(si);
		if (id == 0)
			return false;
		for (int i = 0; i < Announcing.Count; i++)
		{
			if (Announcing[i].ItemId == id)
				return true;
		}
		return false;
	}

	// try the held announce again. a real send drops it. a bag that is still nameless stays until the wait ends, and is never sent as an unload.
	private static void RetryAnnounces()
	{
		if (Announcing.Count == 0)
			return;
		float now = Time.realtimeSinceStartup;
		for (int i = Announcing.Count - 1; i >= 0; i--)
		{
			Delay row = Announcing[i];
			if (row.Info == null || row.Until < now)
			{
				Announcing.RemoveAt(i);
				continue;
			}
			if (now < row.Next)
				continue;
			row.Next = now + AnnounceGap;
			Announcing[i] = row;
			_fromRetry = true;
			try
			{
				_blocked = false;
				// 直接调用同程序集 ItemSync.Client_AnnounceContainerChange，移除反射调用
				ItemSync.Client_AnnounceContainerChange(row.Info);
			}
			catch (Exception ex)
			{
				Warn("announce", ex);
				_blocked = false;
			}
			finally
			{
				_fromRetry = false;
			}
			if (!_blocked)
				Announcing.RemoveAt(i);
		}
	}

	// host. a few synced items per frame. a stored mouth or bag byte that no
	// longer matches the parent is written again, and other clients follow.
	private static void Sweep()
	{
		if (!_sweeping)
		{
			if (Time.realtimeSinceStartup < _sweepNext)
				return;
			SweepList.Clear();
			// 直接遍历泛型注册表 Values，消除非泛型 IDictionary 枚举装箱与反射查字段开销
			Dictionary<GameObject, SyncInfo> map = NetObjectRegistry.SyncRegistry;
			if (map != null)
			{
				foreach (SyncInfo si in map.Values)
				{
					if (si != null)
						SweepList.Add(si);
				}
			}
			_sweepAt = 0;
			_sweeping = SweepList.Count > 0;
			if (!_sweeping)
				_sweepNext = Time.realtimeSinceStartup + 1f;
			return;
		}

		int budget = SweepBudget;
		while (budget > 0 && _sweepAt < SweepList.Count)
		{
			PublishIfStale(SweepList[_sweepAt]);
			_sweepAt++;
			budget--;
		}
		if (_sweepAt >= SweepList.Count)
		{
			_sweeping = false;
			_sweepNext = Time.realtimeSinceStartup + 1f;
		}
	}

	// write only when the stored byte disagrees with the live parent. a missing body link is left alone, or stock would store 0.
	private static void PublishIfStale(SyncInfo si)
	{
		Item item = ItemOf(si);
		if (item == null || LinkMissing(item) || !Stale(si, item))
			return;
		try
		{
			si.WriteContainerInfo();
		}
		catch (Exception ex)
		{
			Warn("sweep", ex);
		}
	}

	// 0 loose, 1 bag, 100 worn, 200 plus the slot. a canvas in the mouth is slot 2, byte 202.
	private static bool Stale(SyncInfo si, Item item)
	{
		byte stored = si.container_data1.Value;
		Transform parent = item.transform != null ? item.transform.parent : null;
		if (parent == null)
			return stored != 0;
		InventorySlot slot = parent.GetComponent<InventorySlot>();
		if (slot != null)
			return stored != (byte)(200 + slot.slot);
		if (parent.GetComponent<Container>() != null)
			return stored != InBag;
		if (parent.GetComponent<Limb>() != null && item.Stats != null && item.Stats.wearable)
			return stored != Worn;
		return false;
	}

	// stock ItemGetContainerInfo returns a blank record here, and the announce becomes an unload.
	private static bool Nameless(Item item)
	{
		if (LinkMissing(item))
			return true;
		Transform parent = item.transform != null ? item.transform.parent : null;
		if (parent == null)
			return false;
		return !NetObjectRegistry.TryGetSyncInfo(parent.gameObject, out SyncInfo bag) || bag == null;
	}

	// slot or bag whose body has no sync link yet. a real drop has no parent.
	private static bool LinkMissing(Item item)
	{
		Transform parent = item.transform != null ? item.transform.parent : null;
		if (parent == null)
			return false;
		InventorySlot slot = parent.GetComponent<InventorySlot>();
		if (slot != null)
			return slot.body != null && slot.body.TryGetNetBody(out NetBody body) && body.syncBody == null;
		if (parent.GetComponent<Container>() == null)
			return false;
		NetBody owner = item.GetComponentInParent<NetBody>();
		return owner != null && owner.syncBody == null;
	}

	// host. register the item and the bag, then store the live parent. client NewGO deletes an unregistered object, so this does not run there.
	private static void Publish(Item item, GameObject box)
	{
		if (!Net.IsRunning || !Net.IsServer || item == null)
			return;
		if (ItemSync.SuppressContainerWrite > 0) {
			if (ItemSync.ParentTestSwap)
				ItemSync.ParentTest("swap block bag " + ItemSync.ParentTestItem(item));
			return;
		}
		try
		{
			Ensure(box);
			Ensure(item.gameObject);
			if (!NetObjectRegistry.TryGetSyncInfo(item.gameObject, out SyncInfo si) || si == null)
				return;
			if (LinkMissing(item))
				return;
			// 直接调用 internal SyncInfo.WriteContainerInfo，消除反射调用
			si.WriteContainerInfo();
		}
		catch (Exception ex)
		{
			Warn("publish", ex);
		}
	}

	// one postfix for pickup, drop, or wear. a missing method is skipped.
	private static void HookPublish(Harmony harmony, MethodInfo method)
	{
		if (method == null)
		{
			OptimizerMain.Log.LogWarning("[KrokV5Optimization] bag place missed a parent hook");
			return;
		}
		harmony.Patch(method, postfix: new HarmonyMethod(typeof(BagPlace), nameof(AfterSlot)));
	}

	// host registers an object the spatial pass has not found. already registered objects are left alone.
	private static void Ensure(GameObject go)
	{
		if (!Net.IsRunning || go == null || !Net.IsServer)
			return;
		if (NetObjectRegistry.IsRegistered(go) || !NetObjectRegistry.ObjectShouldBeSynced(go))
			return;
		NetObjectRegistry.NewGO(go);
	}

	// SyncInfo.item throws when the object is not an item. those entries are skipped.
	private static Item ItemOf(SyncInfo si)
	{
		if (si == null)
			return null;
		try
		{
			return si.item;
		}
		catch (Exception)
		{
			return null;
		}
	}

	// 0 when the item is already gone, so it is not queued.
	private static int ItemId(SyncInfo si)
	{
		Item item = ItemOf(si);
		if (item == null)
			return 0;
		try
		{
			return item.GetInstanceID();
		}
		catch (Exception)
		{
			return 0;
		}
	}

	// first four failures. a repeating error would fill the log.
	private static void Warn(string where, Exception ex)
	{
		if (_warns >= 4)
			return;
		_warns++;
		string message = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
		OptimizerMain.Log.LogWarning("[KrokV5Optimization] bag place " + where + ": " + message);
	}
}
