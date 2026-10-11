// N.E.K.O-Juno-bridge — 控制接口（"支持操作"）
//
// 此前 bridge 是只读的：只报遥测、不碰飞船。这一层把"读取"补成"读写"——
// 通过 ModApi 的 ICommandPod.Controls（CraftControls）真正驱动飞船：
//   油门 / 俯仰 / 偏航 / 滚转 / 平移 / 刹车 / 激活组 / 分级 / 平移模式。
//
// 线程模型（和 DebugProbe 完全一致，这是姊妹项目踩过的坑）：
//   HTTP 线程   -> Enqueue(...) 只把命令塞进队列，绝不碰 Unity 对象
//   主线程 Pump -> 出队执行、把持续型 setpoint 每帧写回 Controls
//
// 为什么持续型 setpoint 要每帧写：
//   SimpleRockets2 每帧会从玩家输入刷新 Controls；如果只在收到命令的那一帧
//   写一次，下一帧就被清零了。所以"hold"类的指令会记进 held，由 Pump 每帧
//   重新写回，直到收到 zero / cut 才松手，把操纵权交还玩家。

using System;
using System.Collections.Generic;
using ModApi.Craft;
using ModApi.Craft.Parts;
using ModApi.Flight;
using UnityEngine;

namespace NekoJunoBridge
{
    public static class ControlApi
    {
        private enum Op
        {
            Stage,
            AgSet,
            AgToggle,
            TransMode,
            Hold,
            Zero,
            Cut
        }

        private sealed class Command
        {
            public Op Action;
            public string Axis;
            public float Value;
            public int Group;
            public int State; // 0/1 —— ag_set / trans_mode 用
        }

        // 持续保持的 setpoint（engaged 时每帧写回 Controls）
        private sealed class HeldState
        {
            public float Throttle;
            public float Pitch;
            public float Yaw;
            public float Roll;
            public float TranslateForward;
            public float TranslateRight;
            public float TranslateUp;
            public float Brake;
        }

        private static readonly HeldState held = new HeldState();
        private static volatile bool enabled;
        private static volatile bool engaged;
        private static string lastError;

        private static readonly object Gate = new object();
        private static readonly Queue<Command> pending = new Queue<Command>();
        private static string snapshotJson = "{\"ok\":false,\"engaged\":false,\"enabled\":false}";

        public static bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                if (!value)
                {
                    engaged = false;
                }
            }
        }

        public static bool Engaged
        {
            get { return engaged; }
        }

        public static string SnapshotJson
        {
            get { lock (Gate) { return snapshotJson; } }
        }

        public static string LastError
        {
            get { return lastError; }
        }

        // ------------------------------------------------------------------
        // HTTP 线程入口：只入队
        // ------------------------------------------------------------------

        public static bool Enqueue(string action, string axis, float value, int group, int state)
        {
            if (!enabled)
            {
                lastError = "control disabled (set control_enabled=1 in neko-juno-bridge.ini)";
                return false;
            }

            Op op;
            switch (action)
            {
                case "stage": op = Op.Stage; break;
                case "ag_set": op = Op.AgSet; break;
                case "ag_toggle": op = Op.AgToggle; break;
                case "trans_mode": op = Op.TransMode; break;
                case "hold": op = Op.Hold; break;
                case "zero": op = Op.Zero; break;
                case "cut": op = Op.Cut; break;
                default:
                    lastError = "unknown action: " + action;
                    return false;
            }

            if (op == Op.Hold && !IsKnownAxis(axis))
            {
                lastError = "unknown axis: " + axis;
                return false;
            }

            lock (Gate)
            {
                pending.Enqueue(new Command
                {
                    Action = op,
                    Axis = axis,
                    Value = value,
                    Group = group,
                    State = state < 0 ? 1 : state
                });
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 主线程入口：出队执行 + 每帧写回 held
        // ------------------------------------------------------------------

        /// <summary>**只能在主线程调用。**</summary>
        public static void Pump()
        {
            if (!enabled)
            {
                return;
            }

            // 1) 一次性命令（分级 / 激活组 / 平移模式 / 松手）
            Command cmd = null;
            lock (Gate)
            {
                if (pending.Count > 0)
                {
                    cmd = pending.Dequeue();
                }
            }
            if (cmd != null)
            {
                ExecuteOneShot(cmd);
            }

            // 2) 持续型 setpoint：只要 engaged 就每帧写回
            if (engaged)
            {
                ApplyHeld();
            }

            // 3) 刷新快照（HTTP 线程只读这份字符串）
            RefreshSnapshot();
        }

        private static void ExecuteOneShot(Command cmd)
        {
            ICommandPod pod = GetActivePod();
            if (pod == null)
            {
                lastError = "no active command pod (not in flight?)";
                return;
            }

            try
            {
                switch (cmd.Action)
                {
                    case Op.Stage:
                        pod.ActivateStage();
                        BridgeLog.Write("control: ActivateStage()");
                        break;
                    case Op.AgSet:
                        if (pod.Controls != null)
                        {
                            pod.Controls.SetActivationGroup(cmd.Group, cmd.State != 0);
                        }
                        BridgeLog.Write("control: activation group " + cmd.Group + " -> " + (cmd.State != 0));
                        break;
                    case Op.AgToggle:
                        if (pod.Controls != null)
                        {
                            pod.Controls.ToggleActivationGroup(cmd.Group);
                        }
                        BridgeLog.Write("control: toggle activation group " + cmd.Group);
                        break;
                    case Op.TransMode:
                        if (pod.Controls != null)
                        {
                            pod.Controls.TranslationModeEnabled = cmd.State != 0;
                        }
                        BridgeLog.Write("control: translation mode -> " + (cmd.State != 0));
                        break;
                    case Op.Hold:
                        SetHeld(cmd.Axis, cmd.Value);
                        engaged = true;
                        break;
                    case Op.Zero:
                        ZeroAll(pod);
                        engaged = false;
                        break;
                    case Op.Cut:
                        if (pod.Controls != null)
                        {
                            pod.Controls.Throttle = 0f;
                        }
                        ZeroAll(pod);
                        engaged = false;
                        break;
                }
            }
            catch (Exception ex)
            {
                lastError = BridgeLog.Safe(ex);
                BridgeLog.Error("control command failed: " + lastError);
            }
        }

        // ------------------------------------------------------------------
        // held setpoint 的读写
        // ------------------------------------------------------------------

        private static void SetHeld(string axis, float value)
        {
            switch (axis)
            {
                case "throttle":
                    held.Throttle = Clamp01(value);
                    break;
                case "pitch":
                    held.Pitch = Clamp11(value);
                    break;
                case "yaw":
                    held.Yaw = Clamp11(value);
                    break;
                case "roll":
                    held.Roll = Clamp11(value);
                    break;
                case "translate_forward":
                    held.TranslateForward = Clamp11(value);
                    break;
                case "translate_right":
                    held.TranslateRight = Clamp11(value);
                    break;
                case "translate_up":
                    held.TranslateUp = Clamp11(value);
                    break;
                case "brake":
                    held.Brake = Clamp01(value);
                    break;
                default:
                    lastError = "unknown axis: " + axis;
                    break;
            }
        }

        private static void ApplyHeld()
        {
            ICommandPod pod = GetActivePod();
            if (pod == null || pod.Controls == null)
            {
                return;
            }
            CraftControls c = pod.Controls;
            c.Throttle = held.Throttle;
            c.Pitch = held.Pitch;
            c.Yaw = held.Yaw;
            c.Roll = held.Roll;
            c.TranslateForward = held.TranslateForward;
            c.TranslateRight = held.TranslateRight;
            c.TranslateUp = held.TranslateUp;
            c.Brake = held.Brake;
        }

        private static void ZeroAll(ICommandPod pod)
        {
            held.Throttle = 0f;
            held.Pitch = 0f;
            held.Yaw = 0f;
            held.Roll = 0f;
            held.TranslateForward = 0f;
            held.TranslateRight = 0f;
            held.TranslateUp = 0f;
            held.Brake = 0f;

            if (pod != null && pod.Controls != null)
            {
                CraftControls c = pod.Controls;
                c.Throttle = 0f;
                c.Pitch = 0f;
                c.Yaw = 0f;
                c.Roll = 0f;
                c.TranslateForward = 0f;
                c.TranslateRight = 0f;
                c.TranslateUp = 0f;
                c.Brake = 0f;
            }
        }

        // ------------------------------------------------------------------
        // 取数兜底
        // ------------------------------------------------------------------

        private static ICommandPod GetActivePod()
        {
            try
            {
                IFlightScene scene = GameAccess.Scene;
                if (scene == null)
                {
                    return null;
                }
                ICraftNode craft = scene.CraftNode;
                if (craft == null || craft.CraftScript == null)
                {
                    return null;
                }
                return craft.CraftScript.ActiveCommandPod;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsKnownAxis(string axis)
        {
            return axis == "throttle" || axis == "pitch" || axis == "yaw" || axis == "roll"
                || axis == "translate_forward" || axis == "translate_right" || axis == "translate_up"
                || axis == "brake";
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static float Clamp11(float v)
        {
            if (v < -1f) return -1f;
            if (v > 1f) return 1f;
            return v;
        }

        // ------------------------------------------------------------------
        // 快照（每帧刷新；HTTP 线程只读）
        // ------------------------------------------------------------------

        private static void RefreshSnapshot()
        {
            BridgeJson.Writer w = new BridgeJson.Writer();
            w.StartObject()
                .Key("ok").Boolean(true)
                .Key("engaged").Boolean(engaged)
                .Key("enabled").Boolean(enabled);

            ICommandPod pod = GetActivePod();
            if (pod == null)
            {
                w.Key("in_flight").Boolean(false)
                 .Key("is_player_controlled").Value("null")
                 .Key("current_stage").Value("null")
                 .Key("num_stages").Value("null")
                 .Key("translation_mode").Value("null");
            }
            else
            {
                w.Key("in_flight").Boolean(true)
                 .Key("is_player_controlled").Boolean(TryB(() => pod.IsPlayerControlled) ?? false)
                 .Key("current_stage").Integer(TryI(() => pod.CurrentStage))
                 .Key("num_stages").Integer(TryI(() => pod.NumStages))
                 .Key("translation_mode").Boolean(TryB(() => pod.Controls != null && pod.Controls.TranslationModeEnabled) ?? false);
            }

            w.Key("held").StartObject()
                .Key("throttle").Number(held.Throttle)
                .Key("pitch").Number(held.Pitch)
                .Key("yaw").Number(held.Yaw)
                .Key("roll").Number(held.Roll)
                .Key("translate_forward").Number(held.TranslateForward)
                .Key("translate_right").Number(held.TranslateRight)
                .Key("translate_up").Number(held.TranslateUp)
                .Key("brake").Number(held.Brake)
                .EndObject();

            w.Key("last_error").Text(string.IsNullOrEmpty(lastError) ? null : lastError);
            w.EndObject();

            lock (Gate)
            {
                snapshotJson = w.ToString();
            }
        }

        private static int TryI(Func<int> getter)
        {
            try { return getter(); }
            catch { return -1; }
        }

        private static bool? TryB(Func<bool> getter)
        {
            try { return getter(); }
            catch { return null; }
        }
    }
}
