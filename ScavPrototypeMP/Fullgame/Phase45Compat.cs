#if FULLGAME
using CasualtiesTogetherUtils;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Unity.VisualScripting;
using UnityEngine;

namespace Together;

// phase 4/5 fullgame MP bridges: ascent exit, modifier gravity, host-sim hazards,
// merc/season world flags, cargo/prosthetic health fields (Medical.cs), tutorials.
public static class FullgameWorldCompat {
    public static float HostGravityY = -55f;
    public static byte HostSeason;
    public static bool HostIsMercRun;
    public static bool HostCanStartMercRun;
    public static int HostEndlessLoops;

    public static bool IsAscentLayer() {
        return WorldGeneration.world != null && WorldGeneration.world.biomeDepth == 10;
    }

    public static bool IsWwmrLayer() {
        return WorldGeneration.world != null && WorldGeneration.world.biomeDepth == 9;
    }

    // stock WorldGeneration.Update exit: depth 10 = top, else bottom; WWMR never.
    public static bool IsAtLayerExit(Body body) {
        if (body == null || WorldGeneration.world == null) {
            return false;
        }
        if (IsWwmrLayer()) {
            return false;
        }
        float y = body.transform.position.y;
        uint half = WorldGeneration.world.halfHeight;
        if (IsAscentLayer()) {
            return y > (float)half - 5f;
        }
        return y < (float)(0L - (long)half) + 3.1f;
    }

    public static void WriteWorldExtras(NetDataWriter writer) {
        writer.Put(Physics2D.gravity.y);
        writer.Put(WorldGeneration.world != null && WorldGeneration.world.isMercRun);
        writer.Put(WorldGeneration.world != null && WorldGeneration.world.canStartMercRun);
        writer.Put(WorldGeneration.world != null ? WorldGeneration.world.endlessLoops : 0);
        writer.Put((byte)SeasonalManager.GetSeasonalEvent());
        writer.Put(WorldGeneration.world == null || WorldGeneration.world.allowEarthquakes);
    }

    public static void ReadWorldExtras(NetDataReader reader) {
        if (reader.EndOfData) {
            return;
        }
        reader.Get(out float gravityY);
        HostGravityY = gravityY;
        Physics2D.gravity = new Vector2(0f, gravityY);

        if (reader.EndOfData) {
            return;
        }
        reader.Get(out bool merc);
        reader.Get(out bool canMerc);
        reader.Get(out int endless);
        reader.Get(out byte season);
        bool allowQuakes = true;
        if (!reader.EndOfData) {
            reader.Get(out allowQuakes);
        }

        HostIsMercRun = merc;
        HostCanStartMercRun = canMerc;
        HostEndlessLoops = endless;
        HostSeason = season;

        if (WorldGeneration.world != null) {
            WorldGeneration.world.isMercRun = merc;
            WorldGeneration.world.canStartMercRun = canMerc;
            WorldGeneration.world.endlessLoops = endless;
            WorldGeneration.world.allowEarthquakes = allowQuakes;
        }
        WorldMP.earthquake_enabled = allowQuakes;
    }

    public static void ApplyModifierGravity(float gravityY) {
        HostGravityY = gravityY;
        Physics2D.gravity = new Vector2(0f, gravityY);
    }

    public static byte EncodeProsthetic(Limb limb) {
        if (limb == null || !limb.HasProsthetic()) {
            return 0;
        }
        string id = limb.prostheticInfo.itemId;
        if (id == "peglimb") {
            return 1;
        }
        if (id == "steelprosthesis") {
            return 2;
        }
        if (id == "peo") {
            return 3;
        }
        return 1;
    }

    public static void ApplyProstheticKind(Limb limb, byte kind) {
        if (limb == null) {
            return;
        }
        bool want = kind != 0;
        bool has = limb.HasProsthetic();
        if (!want) {
            if (has) {
                limb.prostheticInfo = default;
                if (limb.mat != null) {
                    limb.mat.SetTexture("_OverlayTex", null);
                }
            }
            return;
        }
        if (has && EncodeProsthetic(limb) == kind) {
            return;
        }
        Limb.ProstheticInfo info = kind switch {
            2 => SteelInfo(),
            3 => PeoInfo(),
            _ => Item.PegLimbInfo(),
        };
        limb.AttachProsthetic(info, isLoad: true);
    }

    static Limb.ProstheticInfo SteelInfo() {
        return new Limb.ProstheticInfo {
            hasProsthetic = true,
            itemId = "steelprosthesis",
            forceChange = 0f,
            armorChange = 0.2f,
            destroyItem = "scrapmetal",
            appearancePath = "Sprites/steelprosthesisoverlay",
            maxHoles = 15,
            r = 0.5f,
            g = 0.5f,
            b = 0.5f,
        };
    }

    static Limb.ProstheticInfo PeoInfo() {
        return new Limb.ProstheticInfo {
            hasProsthetic = true,
            itemId = "peo",
            forceChange = 0.25f,
            armorChange = 0.6f,
            destroyItem = "titaniumsheet",
            appearancePath = "Sprites/peooverlay",
            maxHoles = 20,
            r = 0.361f,
            g = 0.729f,
            b = 0.729f,
        };
    }
}

[HarmonyPatch(typeof(RichModifier), nameof(RichModifier.Initialize))]
static class RichModifier_Initialize_ClientSkip_MultiplayerPatch {
    static bool Prefix() {
        // host already ran GenerateOres; clients must not re-roll ore into a divergent map
        return !Net.IsRunning || Net.IsServer;
    }
}

[HarmonyPatch(typeof(TurbulentModifier), nameof(TurbulentModifier.Initialize))]
static class TurbulentModifier_Initialize_CaptureGravity_MultiplayerPatch {
    static void Postfix() {
        if (Net.IsRunning && Net.IsServer) {
            FullgameWorldCompat.HostGravityY = Physics2D.gravity.y;
        }
    }
}

[HarmonyPatch(typeof(TurbulentModifier), nameof(TurbulentModifier.Disable))]
static class TurbulentModifier_Disable_CaptureGravity_MultiplayerPatch {
    static void Postfix() {
        if (Net.IsRunning && Net.IsServer) {
            FullgameWorldCompat.HostGravityY = Physics2D.gravity.y;
        }
    }
}

[HarmonyPatch(typeof(StableModifier), nameof(StableModifier.Initialize))]
static class StableModifier_Initialize_EarthquakeFlag_MultiplayerPatch {
    static void Postfix() {
        if (Net.IsRunning) {
            WorldMP.earthquake_enabled = false;
        }
    }
}

[HarmonyPatch(typeof(StableModifier), nameof(StableModifier.Disable))]
static class StableModifier_Disable_EarthquakeFlag_MultiplayerPatch {
    static void Postfix() {
        if (Net.IsRunning) {
            WorldMP.earthquake_enabled = true;
        }
    }
}

[HarmonyPatch(typeof(SeasonalManager), nameof(SeasonalManager.GetSeasonalEvent))]
static class SeasonalManager_GetSeasonalEvent_HostAuthority_MultiplayerPatch {
    static bool Prefix(ref SeasonalManager.SeasonalEventType __result) {
        if (!Net.IsRunning || Net.IsServer) {
            return true;
        }
        __result = (SeasonalManager.SeasonalEventType)FullgameWorldCompat.HostSeason;
        return false;
    }
}

internal sealed class FanClientFollow : MonoBehaviour {
    public float HostSpin = -1f;
    public bool NotedDead;
}

/// <summary>
/// 风扇陷阱（<see cref="FanTrap"/>）转速、期望转速与扇叶断裂状态的 <see cref="SyncInfo.specialdata1"/> / <see cref="SyncInfo.specialdata2"/> 编解码及客机推人模拟。
/// </summary>
internal static class FanTrapSync {
    internal static void Write(FanTrap fan, SyncInfo si, bool force = false) {
        if (fan == null || si == null) {
            return;
        }
        byte brokenBits = 0;
        if (fan.fan1 != null && !fan.fan1.gameObject.activeSelf) {
            brokenBits |= 1;
        }
        if (fan.fan2 != null && !fan.fan2.gameObject.activeSelf) {
            brokenBits |= 2;
        }
        int multEncoded = Mathf.Clamp(Mathf.RoundToInt(fan.speedMult * 50f), 0, 63);
        byte raw = (byte)(brokenBits | (multEncoded << 2));
        float packed = PackSpeeds(fan.spinSpeed, fan.desiredSpinSpeed);
        if (force || si.specialdata1.Value != raw) {
            si.specialdata1.Value = raw;
        }
        if (force || Mathf.Abs(si.specialdata2.Value - packed) > 4f) {
            si.specialdata2.Value = packed;
        }
    }

    internal static void Push(FanTrap fan, bool force = false) {
        if (fan != null && Net.IsRunning && Net.IsServer && NetObjectRegistry.TryGetSyncInfo(fan.gameObject, out SyncInfo si) && si != null) {
            Write(fan, si, force);
        }
    }

    internal static bool Pull(FanTrap fan) {
        if (fan == null || !Net.IsRunning || Net.IsServer) {
            return false;
        }
        if (!NetObjectRegistry.TryGetSyncInfo(fan.gameObject, out SyncInfo si) || si == null) {
            return false;
        }
        return Apply(fan, si.specialdata1.Value, si.specialdata2.Value);
    }

    static float PackSpeeds(float spin, float desired) {
        int s = Mathf.Clamp(Mathf.RoundToInt(spin * 100f), 0, 2000);
        int d = Mathf.Clamp(Mathf.RoundToInt(desired * 100f), 0, 2000);
        return s + d * 4096f;
    }

    static void UnpackSpeeds(float packed, out float spin, out float desired) {
        int d = Mathf.FloorToInt(packed / 4096f);
        int s = Mathf.RoundToInt(packed - d * 4096f);
        if (s < 0) {
            s = 0;
        }
        spin = s / 100f;
        desired = d / 100f;
    }

    internal static bool Apply(FanTrap fan, byte raw, float packed) {
        if (fan == null) {
            return false;
        }
        int brokenBits = raw & 3;
        int multEncoded = (raw >> 2) & 0x3F;
        if (multEncoded == 0 && brokenBits == 0 && packed < 1f) {
            return false;
        }
        UnpackSpeeds(packed, out float spin, out float desired);
        fan.speedMult = multEncoded / 50f;
        fan.desiredSpinSpeed = desired;

        FanClientFollow follow = fan.GetComponent<FanClientFollow>();
        if (follow == null) {
            follow = fan.gameObject.AddComponent<FanClientFollow>();
        }
        bool announce = follow.HostSpin >= 0f;
        if (follow.HostSpin < 0f || Mathf.Abs(follow.HostSpin - spin) > 0.04f) {
            fan.spinSpeed = spin;
            follow.HostSpin = spin;
        }
        MirrorBlade(fan.fan1, (brokenBits & 1) != 0, fan.transform.position, announce);
        MirrorBlade(fan.fan2, (brokenBits & 2) != 0, fan.transform.position, announce);
        if (fan.speedMult <= 0f && !follow.NotedDead) {
            follow.NotedDead = true;
            BuildingEntity build = fan.GetComponent<BuildingEntity>();
            if (build != null) {
                build.description = Locale.GetBuilding("fandsc2");
            }
        }
        return true;
    }

    static void MirrorBlade(SpriteRenderer blade, bool broken, Vector3 pos, bool announce) {
        if (blade == null) {
            return;
        }
        if (broken) {
            if (blade.gameObject.activeSelf) {
                BuildingEntity.CreateBreakParticle(blade);
                blade.gameObject.SetActive(false);
                if (announce) {
                    Sound.Play("fansnap", pos);
                }
            }
        } else if (!blade.gameObject.activeSelf) {
            blade.gameObject.SetActive(true);
        }
    }

    internal static void DrawBlades(FanTrap fan) {
        if (fan == null) {
            return;
        }
        Pull(fan);
        if (fan.speedMult <= 0f) {
            fan.src?.Stop();
            return;
        }
        SpriteRenderer[] blades = { fan.fan1, fan.fan2 };
        for (int i = 0; i < blades.Length; i++) {
            SpriteRenderer blade = blades[i];
            if (blade == null || !blade.gameObject.activeSelf) {
                continue;
            }
            float sin = fan.SinForSpin(fan.fanProgress + (float)i / blades.Length);
            float depth = fan.DepthForSpin(fan.fanProgress + (float)i / blades.Length);
            blade.transform.localPosition = Vector3.right * 3.375f * sin;
            float scaleX = Mathf.Abs(sin) <= 0.07f ? Mathf.Sign(sin) * 0.07f : sin;
            blade.transform.localScale = new Vector3(scaleX, 1f + depth * 0.25f);
            blade.sortingOrder = depth > 0f ? 500 : -500;
            float shade = Mathf.Lerp(0.4f, 1f, Mathf.InverseLerp(-1f, 1f, depth));
            blade.color = new Color(shade, shade, shade);
            blade.flipY = sin < 0f;
            bool wasFront = fan.front != null && i < fan.front.Length && fan.front[i];
            bool isFront = depth > 0f;
            if (fan.front != null && i < fan.front.Length) {
                fan.front[i] = isFront;
            }
            if (wasFront != isFront) {
                ShoveLocal(fan, blade);
            }
        }
        if (fan.src != null) {
            fan.src.pitch = fan.spinSpeed * 0.62f * fan.speedMult;
            fan.src.volume = Mathf.Clamp01(fan.spinSpeed * 0.25f) * 0.66f;
        }
    }

    static void ShoveLocal(FanTrap fan, SpriteRenderer blade) {
        Vector2 dir = blade.transform.right.normalized * blade.transform.localScale.x;
        if (dir == Vector2.zero) {
            dir = Vector2.right;
        }
        RaycastHit2D[] hits = Physics2D.RaycastAll(fan.transform.position, dir, 6.5f, LayerMask.GetMask("Body", "Limb"));
        for (int i = 0; i < hits.Length; i++) {
            RaycastHit2D hit = hits[i];
            if (hit.collider == null || !Utils.GetBody(hit.collider.gameObject, out Body body) || !body.IsBodyLocal()) {
                continue;
            }
            body.DoGoreSound();
            if (fan.spinSpeed >= 2f) {
                body.Ragdoll();
            } else {
                body.crouchAmount += 0.3f;
                body.SetVelocity(dir * fan.spinSpeed * 7.5f);
                body.liquidSlipTime += 0.35f;
            }
            Limb closest = body.GetClosestLimb(hit.point);
            if (closest != null && closest.rb != null) {
                closest.rb.velocity = dir * fan.spinSpeed * 5f;
                Limb[] connected = closest.connectedLimbs;
                if (connected != null) {
                    for (int k = 0; k < connected.Length; k++) {
                        if (connected[k] != null && connected[k].rb != null) {
                            connected[k].rb.velocity = dir * fan.spinSpeed * 5f;
                        }
                    }
                }
                if (body.limbs != null) {
                    for (int l = 0; l < body.limbs.Length; l++) {
                        closest.rb.velocity += dir * fan.spinSpeed * 2.5f;
                    }
                }
            }
            if (fan.spinSpeed > 2.25f) {
                Sound.Play("loudstab", hit.point);
            }
            break;
        }
    }
}

[HarmonyPatch(typeof(FanTrap), "OnWillRenderObject")]
static class FanTrap_OnWillRenderObject_HostSim_MultiplayerPatch {
    static bool Prefix(FanTrap __instance) {
        if (!Net.IsRunning || Net.IsServer) {
            return true;
        }
        FanTrapSync.DrawBlades(__instance);
        return false;
    }

    static void Postfix(FanTrap __instance) {
        if (Net.IsRunning && Net.IsServer && __instance != null) {
            FanTrapSync.Push(__instance);
        }
    }
}

[HarmonyPatch(typeof(FanTrap), nameof(FanTrap.BreakFan))]
static class FanTrap_BreakFan_Publish_MultiplayerPatch {
    static void Postfix(FanTrap __instance) {
        if (Net.IsRunning && Net.IsServer && __instance != null) {
            FanTrapSync.Push(__instance, force: true);
        }
    }
}

[HarmonyPatch(typeof(FanTrap), "Start")]
static class FanTrap_Start_PublishSpeed_MultiplayerPatch {
    static void Postfix(FanTrap __instance) {
        if (!Net.IsRunning || __instance == null) {
            return;
        }
        if (Net.IsServer) {
            FanTrapSync.Push(__instance, force: true);
        } else if (!FanTrapSync.Pull(__instance)) {
            __instance.spinSpeed = 0f;
            __instance.desiredSpinSpeed = 0f;
        }
    }
}

[HarmonyPatch(typeof(FanTrap), "Update")]
static class FanTrap_Update_SpeedFollow_MultiplayerPatch {
    static void Prefix(FanTrap __instance) {
        if (Net.IsRunning && !Net.IsServer && __instance != null) {
            FanTrapSync.Pull(__instance);
        }
    }

    static void Postfix(FanTrap __instance) {
        if (Net.IsRunning && Net.IsServer && __instance != null) {
            FanTrapSync.Push(__instance);
        }
    }
}


#if FULLGAME
// client-side bloodsucker tentacle / suck timer visuals (host owns damage).
internal static class BloodSuckerClientVisual {
    internal static void Tick(BloodSuckerScript sucker) {
        if (sucker == null) {
            return;
        }
        Traverse tr = Traverse.Create(sucker);
        Limb limb = tr.Field("limb").GetValue<Limb>();
        if (limb == null) {
            return;
        }
        float suckTime = tr.Field("suckTime").GetValue<float>() - Time.deltaTime;
        tr.Field("suckTime").SetValue(suckTime);
        if (suckTime < 0f || limb.body == null
            || Vector2.Distance(limb.body.transform.position, sucker.transform.position) > 12f) {
            tr.Field("limb").SetValue(null);
        }
    }

    internal static void DrawTentacle(BloodSuckerScript sucker) {
        Traverse tr = Traverse.Create(sucker);
        LineRenderer line = tr.Field("line").GetValue<LineRenderer>();
        if (line == null) {
            return;
        }
        Limb limb = tr.Field("limb").GetValue<Limb>();
        Vector2 linePos = tr.Field("linePos").GetValue<Vector2>();
        int segments = sucker.segments;
        Vector2 target = limb != null
            ? (Vector2)sucker.transform.InverseTransformPoint(limb.transform.position)
            : Vector2.zero;
        linePos = Vector2.Lerp(linePos, target, Time.deltaTime * 5f * (limb != null ? 3.5f : 1f));
        tr.Field("linePos").SetValue(linePos);
        for (int i = 0; i < segments; i++) {
            float t01 = (float)i / (float)(segments - 1);
            line.SetPosition(i, Vector2.Lerp(Vector2.zero, linePos, t01));
        }
        if (limb != null) {
            AnimationCurve curve = new AnimationCurve();
            float wave = Time.time * (-sucker.waveSpeed);
            for (int k = 0; k < segments; k++) {
                float t01 = (float)k / (float)(segments - 1);
                float pulse = Mathf.Sin(t01 * Mathf.PI * 2f * sucker.waveFrequency - wave);
                float width = 1.6f + pulse * sucker.pulseStrength;
                curve.AddKey(t01, 0.6f * width);
            }
            line.widthCurve = curve;
        } else {
            line.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.6f),
                new Keyframe(0f, 0.6f));
        }
    }
}
#endif

[HarmonyPatch(typeof(BloodSuckerScript), "OnTriggerEnter2D")]
static class BloodSuckerScript_OnTriggerEnter2D_HostSim_MultiplayerPatch {
    static bool Prefix(BloodSuckerScript __instance, Collider2D collision) {
        if (!Net.IsRunning || Net.IsServer) {
            return true;
        }
        // the host owns the suck. a client walking into the vent still has to tell it, or nobody sees the tentacle.
        if (__instance == null || !Utils.GetBody(collision.gameObject, out Body body) || !Util.IsBodyLocal(body)) {
            return false;
        }
        if (!NetObjectRegistry.TryGetSyncInfo(__instance.gameObject, out SyncInfo si) || si == null) {
            return false;
        }
        NetDataWriter writer = Multiplayer.CreateNamedWriter("CustomCommand");
        writer.Put("bloodsucker_touch " + si.syncId.id.ToString());
        Net.Client_Send(DeliveryMethod.ReliableUnordered, writer);
        return false;
    }

    [CTPostInit]
    static void Init() {
        ServerMain.ServerCustomCommandsDict["bloodsucker_touch"] = (plr, cmd, args) => {
            if (!Net.IsServer || plr == null || plr.body == null || args == null || args.Length < 2) {
                return;
            }
            if (!ushort.TryParse(args[1], out ushort syncId)) {
                return;
            }
            if (!NetObjectRegistry.TryGetSyncInfo(syncId, out SyncInfo si) || si == null || si.go == null) {
                return;
            }
            if (!si.go.TryGetComponent(out BloodSuckerScript sucker) || sucker == null) {
                return;
            }
            float suckTime = Traverse.Create(sucker).Field("suckTime").GetValue<float>();
            if (suckTime >= -10f) {
                return;
            }
            if (Vector2.Distance(plr.body.transform.position, sucker.transform.position) > 6f) {
                return;
            }
            Limb limb = null;
            if (plr.body.limbs != null) {
                for (int i = 0; i < plr.body.limbs.Length; i++) {
                    Limb candidate = plr.body.limbs[i];
                    if (candidate != null && !candidate.dismembered) {
                        limb = candidate;
                        break;
                    }
                }
            }
            if (limb == null) {
                return;
            }
            sucker.BeginSucc(limb);
        };
    }
}

// host owns BeginSucc + drain. remotes need limb/suckTime for the tentacle line + scream/gore.
[HarmonyPatch(typeof(BloodSuckerScript), nameof(BloodSuckerScript.BeginSucc))]
static class BloodSuckerScript_BeginSucc_Relay_MultiplayerPatch {
    static void Postfix(BloodSuckerScript __instance, Limb lm) {
        if (!Net.IsRunning || !Net.IsServer || lm == null || lm.body == null) {
            return;
        }
        if (!NetObjectRegistry.TryGetSyncInfo(__instance.gameObject, out SyncInfo si)) {
            return;
        }
        if (!lm.body.TryGetNetBody(out NetBody nb)) {
            return;
        }
        NetDataWriter writer = Net.CreateWriter(NetmsgId.CLIENT_BloodSuckerSucc);
        writer.Put(si.syncId);
        writer.Put(nb.netId);
        writer.Put(Util.GetLimbIndex(lm));
        Net.Server_SendToClients(DeliveryMethod.ReliableUnordered, writer, ServerMain.AllClientIdsExceptHost);
    }

    [ClientReceiver((byte)NetmsgId.CLIENT_BloodSuckerSucc, false)]
    static void Client_BloodSuckerSucc(knetid _, NetDataReader reader) {
        reader.Get(out knetid denId);
        reader.Get(out knetid bodyNetId);
        reader.Get(out byte limbIndex);
        if (!NetObjectRegistry.TryGetSyncInfo(denId, out SyncInfo si) || si.go == null) {
            return;
        }
        if (!si.go.TryGetComponent(out BloodSuckerScript sucker)) {
            return;
        }
        if (!NetBody.TryGetNetBodyFromId(bodyNetId, out NetBody nb) || nb.body == null) {
            return;
        }
        if (limbIndex >= nb.body.limbs.Length) {
            return;
        }
        Limb lm = nb.body.limbs[limbIndex];
        if (lm == null || lm.dismembered) {
            return;
        }
        // visual-only. host owns damage and bloodVolume drain.
        Traverse tr = Traverse.Create(sucker);
        tr.Field("limb").SetValue(lm);
        tr.Field("suckTime").SetValue(1.25f);
        lm.body.eyeScareTime = 2.5f;
        lm.body.Scream();
        lm.body.DoGoreSound();
    }
}

// clients draw the tentacle without blood/pain. host still runs OnWillRenderObject.
[HarmonyPatch(typeof(BloodSuckerScript), "OnWillRenderObject")]
static class BloodSuckerScript_OnWillRenderObject_HostDrain_MultiplayerPatch {
    static bool Prefix(BloodSuckerScript __instance) {
        if (!Net.IsRunning || Net.IsServer) {
            return true;
        }
        BloodSuckerClientVisual.DrawTentacle(__instance);
        return false;
    }
}

// geothermal heaters (hot rock and the cold amogus vent) only lerp PlayerCamera.main.body.
// the host applies that lerp to every body in range and syncs it.
[HarmonyPatch(typeof(Heater), "OnWillRenderObject")]
static class Heater_OnWillRenderObject_AllBodies_MultiplayerPatch {
    static bool Prefix(Heater __instance) {
        if (!Net.IsRunning || __instance == null) {
            return true;
        }
        if (!Net.IsServer) {
            return false;
        }
        Traverse tr = Traverse.Create(__instance);
        float last = tr.Field("lastTime").GetValue<float>();
        if (Time.time - last <= 0.5f) {
            return false;
        }
        tr.Field("lastTime").SetValue(Time.time);
        float desired = __instance.desiredTemp;
        bool heating = desired >= 37f;
        float reach = __instance.maxDistance;
        if (reach <= 0f) {
            return false;
        }
        Vector2 pos = __instance.transform.position;
        foreach (NetBody nb in NetBody.GetBodiesInRadius(pos, reach)) {
            Body body = nb != null ? nb.body : null;
            if (body == null) {
                continue;
            }
            bool towards = (heating && body.temperature < desired) || (!heating && body.temperature > desired);
            if (!towards) {
                continue;
            }
            body.temperature = Mathf.Lerp(body.temperature, desired, __instance.tempLerpStrength);
            MedicalSync.Server_QueueSendCharacterHealth(nb, true);
        }
        return false;
    }
}

// coldPuff: every peer runs Update so health-sync can Blow; Blow itself is local-body only (TrapClaim/Phase45).
[HarmonyPatch(typeof(ColdPuffScript), nameof(ColdPuffScript.Blow))]
static class ColdPuffScript_Blow_LocalBody_MultiplayerPatch {
    static bool Prefix(ColdPuffScript __instance) {
        if (!Net.IsRunning) {
            return true;
        }
        return TrapClaim.ColdPuffBlowLocal(__instance);
    }

    [CTPostInit]
    static void Init() {
        ServerMain.ServerCustomCommandsDict["coldpuff_blow"] = (plr, cmd, args) => {
            if (!Net.IsServer || args == null || args.Length < 2) {
                return;
            }
            if (!ushort.TryParse(args[1], out ushort syncId)) {
                return;
            }
            if (!NetObjectRegistry.TryGetSyncInfo(syncId, out SyncInfo si) || si == null || si.go == null) {
                return;
            }
            if (!si.go.TryGetComponent(out ColdPuffScript puff) || puff == null) {
                return;
            }
            TrapClaim.ChillBodies(puff, puff.transform.position, 8f);
        };
    }
}

[HarmonyPatch(typeof(PusSackScript), "OnWillRenderObject")]
static class PusSackScript_OnWillRenderObject_HostSim_MultiplayerPatch {
    // vanilla only shoves PlayerCamera.main.body. skipping this on clients meant only the host felt the pop.
    static bool Prefix() => true;
}

// cryodrone: host owns AI/chase/FireShot; clients follow open bit; hits are victim-TrapClaim.
[HarmonyPatch(typeof(LaserDroneScript), "Update")]
static class LaserDroneScript_Update_HostAuth_MultiplayerPatch {
    static bool Prefix(LaserDroneScript __instance) {
        if (!Net.IsRunning) {
            return true;
        }
        if (Net.IsClient) {
            TrapClaim.ClientCryoFollowHost(__instance);
            return false;
        }
        BodyGetterOverrider bgo = __instance.GetOrAddComponent<BodyGetterOverrider>();
        bgo.SetNearestBody();
        if (bgo.body == null && PlayerCamera.main != null) {
            bgo.body = PlayerCamera.main.body;
        }
        // wake when any player is close - don't wait for host camera OnWillRender
        if (__instance.hidden && bgo.body != null
            && Vector2.Distance(__instance.transform.position, bgo.body.transform.position) < 10f) {
            __instance.Open();
        }
        return true;
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod) {
        return BodyGetterOverrider.TranspileAll(instructions, generator, __originalMethod.DeclaringType);
    }
}

[HarmonyPatch(typeof(LaserDroneScript), "FixedUpdate")]
static class LaserDroneScript_FixedUpdate_HostSim_MultiplayerPatch {
    static bool Prefix(LaserDroneScript __instance) {
        if (!Net.IsRunning) {
            return true;
        }
        if (!Net.IsServer) {
            return false;
        }
        BodyGetterOverrider bgo = __instance.GetOrAddComponent<BodyGetterOverrider>();
        bgo.SetNearestBody();
        if (bgo.body == null && PlayerCamera.main != null) {
            bgo.body = PlayerCamera.main.body;
        }
        return true;
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod) {
        return BodyGetterOverrider.TranspileAll(instructions, generator, __originalMethod.DeclaringType);
    }
}

[HarmonyPatch(typeof(LaserDroneScript), nameof(LaserDroneScript.FireShot))]
static class LaserDroneScript_FireShot_TrapClaim_MultiplayerPatch {
    static bool Prefix(LaserDroneScript __instance) {
        if (!Net.IsRunning) {
            return true;
        }
        if (!Net.IsServer) {
            return false;
        }
        TrapClaim.HostCryoFire(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(LaserDroneScript), "OnWillRenderObject")]
static class LaserDroneScript_OnWillRenderObject_Bgo_MultiplayerPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod) {
        return BodyGetterOverrider.TranspileAll(instructions, generator, __originalMethod.DeclaringType);
    }
}

[HarmonyPatch(typeof(LaserDroneScript), "OnCollisionEnter2D")]
static class LaserDroneScript_OnCollisionEnter2D_LocalOnly_MultiplayerPatch {
    static bool Prefix(Collision2D collision) {
        return !TrapClaim.SkipRemote(collision?.collider);
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod) {
        return BodyGetterOverrider.TranspileAll(instructions, generator, __originalMethod.DeclaringType);
    }
}

// piston: host owns flip timing; clients follow specialdata1 `up` 1:1 (piggyback-safe).
[HarmonyPatch(typeof(PistonScript), "Update")]
static class PistonScript_Update_HostAuth_MultiplayerPatch {
    static bool Prefix(PistonScript __instance) {
        if (!Net.IsRunning) {
            return true;
        }
        if (Net.IsClient) {
            TrapClaim.ClientPistonFollowHost(__instance);
            return false;
        }
        return true;
    }
}

// onWillRenderObject only runs for a camera that can see the turret. skip it in MP.
[HarmonyPatch(typeof(CeilingTurret), "OnWillRenderObject")]
static class CeilingTurret_OnWillRenderObject_HostSim_MultiplayerPatch {
    static bool Prefix() => !Net.IsRunning;
}

internal sealed class CeilingTurretClient : MonoBehaviour {
    public bool Seeing;
    public float TimeSaw;
}

// host replacement for Update + OnWillRenderObject. picks any living player below the gun.
internal static class CeilingTurretHostSim {
    private struct SentAim {
        public float Angle;
        public float Next;
        public bool Seeing;
    }

    private struct MoveSample {
        public Vector2 Pos;
        public float Time;
        public bool Fast;
    }

    static readonly Dictionary<int, SentAim> Sent = new Dictionary<int, SentAim>();
    static CeilingTurret[] _cached;
    static int _cachedFrame = -1;
    static readonly Dictionary<int, MoveSample> _moveSample = new Dictionary<int, MoveSample>();

    internal static void Tick(CeilingTurret ct) {
        if (ct == null) {
            return;
        }
        Traverse tr = Traverse.Create(ct);
        float timeSinceShot = tr.Field("timeSinceShot").GetValue<float>() + Time.deltaTime;
        tr.Field("timeSinceShot").SetValue(timeSinceShot);
        Transform lowerGun = ct.lowerGun;
        if (lowerGun != null && ct.barrelAnimCurve != null) {
            lowerGun.localPosition = new Vector2(0f, ct.barrelAnimCurve.Evaluate(timeSinceShot));
        }
        Body target = PickTarget(ct, out bool seeing);
        float timeSaw = tr.Field("timeSawMovement").GetValue<float>();
        float desired = tr.Field("desiredAngle").GetValue<float>();
        float gunVelocity = tr.Field("gunVelocity").GetValue<float>();
        float gunAngle = tr.Field("gunAngle").GetValue<float>();
        AudioSource source = tr.Field("source").GetValue<AudioSource>();
        if (ct.alwaysSee && ct.openLight != null) {
            ct.openLight.intensity = Mathf.PingPong(Time.time * 10f, 1f) * timeSaw * 5f;
        }
        if (seeing && target != null) {
            Vector2 toBody = target.transform.position - ct.transform.position;
            desired = Vector2.SignedAngle(Vector2.down, toBody.normalized);
            if (ct.lights != null) {
                ct.lights.enabled = true;
            }
            timeSaw += Time.deltaTime * (ct.alwaysSee ? 0.3f : 1f);
            if (WorldGeneration.world != null && WorldGeneration.world.biomeDepth == 11
                && PlayerCamera.main != null) {
                PlayerCamera.main.threatMusicTime = 2f;
            }
            if (source != null) {
                source.volume = Mathf.MoveTowards(source.volume, 1f, Time.deltaTime * 3f);
            }
        } else {
            if (!ct.alwaysSee) {
                timeSaw = 0f;
            }
            if (ct.lights != null) {
                ct.lights.enabled = Mathf.PingPong(Time.time, 0.2f) > 0.1f;
            }
            if (source != null) {
                source.volume = Mathf.MoveTowards(source.volume, 0f, Time.deltaTime * 3f);
            }
        }
        if (source != null) {
            if (source.volume > 0f && !source.isPlaying) {
                source.Play();
            }
            if (source.volume <= 0f && source.isPlaying) {
                source.Stop();
            }
            source.pitch = 0.5f + timeSaw * 1.5f;
        }
        float delta = Mathf.Clamp(desired - gunAngle, -30f, 30f);
        gunVelocity += delta * Time.deltaTime * 15f;
        gunVelocity *= 1f / (1f + Time.deltaTime);
        gunAngle += gunVelocity * Time.deltaTime;
        gunAngle = Mathf.Clamp(gunAngle, -80f, 80f);
        if (ct.upperGun != null) {
            ct.upperGun.eulerAngles = new Vector3(0f, 0f, gunAngle);
            ct.upperGun.localPosition = ct.upperGun.up * -0.75f;
        }
        tr.Field("desiredAngle").SetValue(desired);
        tr.Field("gunVelocity").SetValue(gunVelocity);
        tr.Field("gunAngle").SetValue(gunAngle);
        bool fired = false;
        if (seeing && target != null && timeSaw > 0.7f && target.conscious) {
            timeSaw = 0f;
            tr.Field("timeSinceShot").SetValue(0f);
            fired = true;
            if (lowerGun != null) {
                Sound.Play("rifleshot", lowerGun.position, true, false, null, 1f, 1f, false, false);
                TurretScript.Shoot(new FireInfo {
                    pos = lowerGun.position,
                    dir = -lowerGun.up,
                    ignoreTrans = ct.transform,
                    playerDamageMultiplier = 1f,
                });
            }
        }
        tr.Field("timeSawMovement").SetValue(timeSaw);
        Vector2 shotPos = lowerGun != null ? (Vector2)lowerGun.position : (Vector2)ct.transform.position;
        Vector2 shotDir = lowerGun != null ? (Vector2)(-lowerGun.up) : Vector2.down;
        Publish(ct, gunAngle, fired, seeing, timeSaw, shotPos, shotDir);
    }

    static void Publish(CeilingTurret ct, float angle, bool fired, bool seeing, float timeSaw, Vector2 shotPos, Vector2 shotDir) {
        if (ct == null || ServerMain.AllClientIdsExceptHost == null || ServerMain.AllClientIdsExceptHost.Count == 0) {
            return;
        }
        int id = ct.GetInstanceID();
        bool known = Sent.TryGetValue(id, out SentAim prev);
        bool lostSight = known && prev.Seeing && !seeing;
        bool aimDue = fired || lostSight || (seeing && (!known || Mathf.Abs(Mathf.DeltaAngle(prev.Angle, angle)) > 3f || Time.time >= prev.Next));
        if (!aimDue) {
            return;
        }
        Sent[id] = new SentAim {
            Angle = angle,
            Next = Time.time + (fired ? 0.05f : 0.2f),
            Seeing = seeing,
        };
        ushort syncId = 0;
        if (NetObjectRegistry.TryGetSyncInfo(ct.gameObject, out SyncInfo si) && si != null) {
            syncId = si.syncId;
        }
        NetDataWriter writer = Multiplayer.CreateNamedWriter("CustomCommand");
        writer.Put("ceiling_gun");
        writer.Put(syncId);
        writer.Put((Vector2)ct.transform.position);
        writer.Put(angle);
        writer.Put(seeing);
        writer.Put(timeSaw);
        writer.Put(fired);
        if (fired) {
            writer.Put(shotPos);
            writer.Put(shotDir);
        }
        Net.Server_SendToClients(fired ? DeliveryMethod.ReliableUnordered : DeliveryMethod.Unreliable, writer, ServerMain.AllClientIdsExceptHost);
    }

    public static void ApplyRemote(ushort syncId, Vector2 pos, float angle, bool seeing, float timeSaw, bool fired, Vector2 shotPos, Vector2 shotDir) {
        CeilingTurret ct = FindById(syncId) ?? FindNear(pos);
        if (ct != null) {
            Pose(ct, angle);
            CeilingTurretClient clientState = ct.GetComponent<CeilingTurretClient>();
            if (clientState == null) {
                clientState = ct.gameObject.AddComponent<CeilingTurretClient>();
            }
            clientState.Seeing = seeing;
            clientState.TimeSaw = timeSaw;
            if (fired) {
                Traverse.Create(ct).Field("timeSinceShot").SetValue(0f);
                if (ct.lowerGun != null && ct.barrelAnimCurve != null) {
                    ct.lowerGun.localPosition = new Vector2(0f, ct.barrelAnimCurve.Evaluate(0f));
                }
            }
        }
        if (!fired) {
            return;
        }
        if (shotDir.sqrMagnitude < 0.01f) {
            shotDir = Vector2.down;
        }
        Sound.Play("rifleshot", shotPos, true, false, null, 1f, 1f, false, false);
        if (WorldGeneration.world != null && WorldGeneration.world.biomeDepth == 11 && PlayerCamera.main != null) {
            PlayerCamera.main.threatMusicTime = 2f;
        }
        TurretScript_Shoot_MultiplayerPatch.SuppressBodyDamage = true;
        try {
            TurretScript.Shoot(new FireInfo {
                pos = shotPos,
                dir = shotDir,
                ignoreTrans = ct != null ? ct.transform : null,
                playerDamageMultiplier = 1f,
            });
        } finally {
            TurretScript_Shoot_MultiplayerPatch.SuppressBodyDamage = false;
        }
    }

    static void Pose(CeilingTurret ct, float angle) {
        Traverse.Create(ct).Field("gunAngle").SetValue(angle);
        if (ct.upperGun == null) {
            return;
        }
        ct.upperGun.eulerAngles = new Vector3(0f, 0f, angle);
        ct.upperGun.localPosition = ct.upperGun.up * -0.75f;
    }

    internal static void ClientBarrel(CeilingTurret ct) {
        if (ct == null) {
            return;
        }
        CeilingTurretClient clientState = ct.GetComponent<CeilingTurretClient>();
        float timeSaw = clientState != null ? clientState.TimeSaw : 0f;
        bool seeing = clientState != null && clientState.Seeing;
        if (ct.upperGun != null) {
            float angle = Traverse.Create(ct).Field("gunAngle").GetValue<float>();
            ct.upperGun.eulerAngles = new Vector3(0f, 0f, angle);
            ct.upperGun.localPosition = ct.upperGun.up * -0.75f;
        }
        if (ct.lowerGun != null && ct.barrelAnimCurve != null) {
            Traverse tr = Traverse.Create(ct);
            float t = tr.Field("timeSinceShot").GetValue<float>() + Time.deltaTime;
            tr.Field("timeSinceShot").SetValue(t);
            ct.lowerGun.localPosition = new Vector2(0f, ct.barrelAnimCurve.Evaluate(t));
        }
        if (ct.alwaysSee && ct.openLight != null) {
            ct.openLight.intensity = Mathf.PingPong(Time.time * 10f, 1f) * timeSaw * 5f;
        }
        if (ct.lights != null) {
            ct.lights.enabled = seeing || Mathf.PingPong(Time.time, 0.2f) > 0.1f;
        }
        AudioSource source = Traverse.Create(ct).Field("source").GetValue<AudioSource>();
        if (source != null) {
            source.volume = Mathf.MoveTowards(source.volume, seeing ? 1f : 0f, Time.deltaTime * 3f);
            if (source.volume > 0f && !source.isPlaying) {
                source.Play();
            }
            if (source.volume <= 0f && source.isPlaying) {
                source.Stop();
            }
            source.pitch = 0.5f + timeSaw * 1.5f;
        }
    }

    static CeilingTurret FindNear(Vector2 pos) {
        if (_cached == null || Time.frameCount - _cachedFrame > 45) {
            _cached = UnityEngine.Object.FindObjectsByType<CeilingTurret>(FindObjectsSortMode.None);
            _cachedFrame = Time.frameCount;
        }
        CeilingTurret best = null;
        float bestSqr = 64f;
        for (int i = 0; i < _cached.Length; i++) {
            CeilingTurret ct = _cached[i];
            if (ct == null) {
                continue;
            }
            float sqr = ((Vector2)ct.transform.position - pos).sqrMagnitude;
            if (sqr < bestSqr) {
                bestSqr = sqr;
                best = ct;
            }
        }
        return best;
    }

    static CeilingTurret FindById(ushort syncId) {
        if (syncId == 0 || !NetObjectRegistry.TryGetSyncInfo(syncId, out SyncInfo si) || si == null || si.go == null) {
            return null;
        }
        CeilingTurret ct = si.go.GetComponent<CeilingTurret>();
        if (ct == null) {
            ct = si.go.GetComponentInChildren<CeilingTurret>(includeInactive: true);
        }
        return ct;
    }

    [CTPostInit]
    static void InitGuns() {
        ClientMain.ClientCustomCommandsDict["ceiling_gun"] = (cmd, reader) => {
            reader.Get(out ushort syncId);
            reader.Get(out Vector2 pos);
            reader.Get(out float angle);
            reader.Get(out bool seeing);
            reader.Get(out float timeSaw);
            reader.Get(out bool fired);
            Vector2 shotPos = pos;
            Vector2 shotDir = Vector2.down;
            if (fired) {
                reader.Get(out shotPos);
                reader.Get(out shotDir);
            }
            ApplyRemote(syncId, pos, angle, seeing, timeSaw, fired, shotPos, shotDir);
        };
    }

    static Body PickTarget(CeilingTurret ct, out bool seeing) {
        seeing = false;
        Body best = null;
        float bestSqr = float.MaxValue;
        int mask = LayerMask.GetMask("Ground");
        foreach (ScavPlayer plr in ScavPlayer.AllLivingPlayers) {
            if (plr == null || plr.body == null) {
                continue;
            }
            Body body = plr.body;
            if (body.transform.position.y >= ct.transform.position.y || (!ct.alwaysSee && !MovingFast(body))) {
                continue;
            }
            bool clear = true;
            RaycastHit2D[] hits = Physics2D.LinecastAll(ct.transform.position, body.transform.position, mask);
            for (int i = 0; i < hits.Length; i++) {
                if (hits[i].transform != ct.transform) {
                    clear = false;
                    break;
                }
            }
            if (!clear) {
                continue;
            }
            Vector2 delta = (Vector2)body.transform.position - (Vector2)ct.transform.position;
            float sqr = delta.sqrMagnitude;
            if (sqr < bestSqr) {
                bestSqr = sqr;
                best = body;
            }
        }
        seeing = best != null;
        return best;
    }

    static bool MovingFast(Body body) {
        if (body == null) {
            return false;
        }
        int id = body.GetInstanceID();
        Vector2 pos = body.transform.position;
        float now = Time.time;
        if (body.grounded && body.moveDir.sqrMagnitude < 0.04f) {
            _moveSample[id] = new MoveSample { Pos = pos, Time = now, Fast = false };
            return false;
        }
        if (!_moveSample.TryGetValue(id, out MoveSample prev)) {
            _moveSample[id] = new MoveSample { Pos = pos, Time = now, Fast = false };
            return false;
        }
        float dt = now - prev.Time;
        if (dt < 0.25f) {
            return prev.Fast;
        }
        bool fast = (pos - prev.Pos).magnitude > 15f * dt;
        _moveSample[id] = new MoveSample { Pos = pos, Time = now, Fast = fast };
        return fast;
    }
}

[HarmonyPatch(typeof(CeilingTurret), "Update")]
static class CeilingTurret_Update_HostSim_MultiplayerPatch {
    static bool Prefix(CeilingTurret __instance) {
        if (!Net.IsRunning) {
            return true;
        }
        if (!Net.IsServer) {
            CeilingTurretHostSim.ClientBarrel(__instance);
            return false;
        }
        CeilingTurretHostSim.Tick(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(TechballScript), "Update")]
static class TechballScript_Update_HostSim_MultiplayerPatch {
    static bool Prefix() => !Net.IsRunning || Net.IsServer;
}

[HarmonyPatch(typeof(Body), nameof(Body.EmbedCargo))]
static class Body_EmbedCargo_MultiplayerPatch {
    static void Postfix(Body __instance) {
        if (!Net.IsRunning || __instance == null) {
            return;
        }
        if (__instance.TryGetNetBody(out NetBody nb)) {
            MedicalSync.Server_QueueSendCharacterHealth(nb, true);
        }
    }
}

[HarmonyPatch(typeof(Body), nameof(Body.RemoveCargo))]
static class Body_RemoveCargo_MultiplayerPatch {
    static void Postfix(Body __instance) {
        if (!Net.IsRunning || __instance == null) {
            return;
        }
        // clients must not call the server queue - that throws. the host copy is cleared by cargo_off.
        MacroFleshRebuilderMp.NoteLocalCargoRemoved(__instance);
        if (!Net.IsServer) {
            return;
        }
        if (__instance.TryGetNetBody(out NetBody nb)) {
            MedicalSync.Server_QueueSendCharacterHealth(nb, true);
        }
    }
}

[HarmonyPatch(typeof(Limb), nameof(Limb.AttachProsthetic))]
static class Limb_AttachProsthetic_HealthPush_MultiplayerPatch {
    static void Postfix(Limb __instance) {
        if (!Net.IsRunning || !Net.IsServer || __instance == null || __instance.body == null) {
            return;
        }
        if (__instance.body.TryGetNetBody(out NetBody nb)) {
            MedicalSync.Server_QueueSendCharacterHealth(nb, true);
        }
    }
}

// movement course is SP tutorial content - don't run the parkour controller in a lobby
[HarmonyPatch(typeof(MovementCourse), "Start")]
static class MovementCourse_Start_MultiplayerPatch {
    static bool Prefix() => !Net.IsRunning;
}

#endif