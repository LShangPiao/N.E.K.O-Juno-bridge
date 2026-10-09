// N.E.K.O-Juno-bridge — 调试探针（只在 -DefineConstants:NEKO_DEBUG 时编译进包）
//
// 用途：在没有 OS 级输入注入的情况下驱动游戏界面，以便自动验证飞行遥测。
// 这个游戏用 Rewired + Unity 的 RawInput 读取鼠标，SendInput / mouse_event
// 合成的事件它收不到，所以只能从**游戏内部**驱动 UI。
//
// 做法（也正是将来加控制接口必须遵循的线程模型）：
//   HTTP 线程   -> Enqueue(...) 把请求塞进队列，然后轮询 LastReport
//   主线程 Pump -> 出队、执行（这时才允许碰 Unity 对象）、写回结果
//
// ⚠️ 正式发布**不包含**这个文件的内容。

#if NEKO_DEBUG

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NekoJunoBridge
{
    public static class DebugProbe
    {
        private sealed class Command
        {
            public string Op;
            public float X;
            public float Y;
            public string Text;
        }

        private static readonly object Gate = new object();
        private static readonly Queue<Command> Pending = new Queue<Command>();
        private static string lastReport = "{\"ok\":false,\"error\":\"还没有结果\"}";
        private static long lastTick;

        public static string LastReport
        {
            get { lock (Gate) { return lastReport; } }
        }

        public static void EnqueueUi()
        {
            lock (Gate)
            {
                Pending.Enqueue(new Command { Op = "ui" });
            }
        }

        public static void EnqueueClick(float x, float y)
        {
            lock (Gate)
            {
                Pending.Enqueue(new Command { Op = "click", X = x, Y = y });
            }
        }

        public static void EnqueuePress(string text)
        {
            lock (Gate)
            {
                Pending.Enqueue(new Command { Op = "press", Text = text });
            }
        }

        public static void EnqueueTap(string name)
        {
            lock (Gate)
            {
                Pending.Enqueue(new Command { Op = "tap", Text = name });
            }
        }

        public static void EnqueueInfo()
        {
            lock (Gate)
            {
                Pending.Enqueue(new Command { Op = "info" });
            }
        }

        /// <summary>**只能在主线程调用。**</summary>
        public static void Pump()
        {
            Command command = null;
            lock (Gate)
            {
                if (Pending.Count > 0)
                {
                    command = Pending.Dequeue();
                }
            }
            if (command == null)
            {
                return;
            }

            string report;
            try
            {
                switch (command.Op)
                {
                    case "ui":
                        report = DescribeUi();
                        break;
                    case "click":
                        report = ClickAt(command.X, command.Y);
                        break;
                    case "press":
                        report = PressByText(command.Text);
                        break;
                    case "tap":
                        report = TapByName(command.Text);
                        break;
                    case "info":
                        report = DescribeInfo();
                        break;
                    default:
                        report = "{\"ok\":false,\"error\":\"unknown op\"}";
                        break;
                }
            }
            catch (Exception ex)
            {
                report = "{\"ok\":false,\"error\":" + BridgeJson.Str(BridgeLog.Safe(ex)) + "}";
            }

            lock (Gate)
            {
                lastReport = report;
                lastTick = DateTime.Now.Ticks;
            }
        }

        private static string DescribeUi()
        {
            BridgeJson.Writer w = new BridgeJson.Writer();
            w.StartObject()
                .Key("ok").Boolean(true)
                .Key("screen_width").Integer(Screen.width)
                .Key("screen_height").Integer(Screen.height)
                .Key("event_system").Boolean(EventSystem.current != null);

            w.Key("selectables").StartArray();
            Selectable[] all = UnityEngine.Object.FindObjectsOfType<Selectable>();
            for (int i = 0; i < all.Length; i++)
            {
                Selectable s = all[i];
                if (s == null)
                {
                    continue;
                }
                string label = null;
                try
                {
                    Text t = s.GetComponentInChildren<Text>();
                    if (t != null)
                    {
                        label = t.text;
                    }
                }
                catch { }

                Vector2 screen = new Vector2(-1, -1);
                try
                {
                    RectTransform rt = s.transform as RectTransform;
                    if (rt != null)
                    {
                        Vector3 world = rt.TransformPoint(rt.rect.center);
                        screen = RectTransformUtility.WorldToScreenPoint(null, world);
                    }
                }
                catch { }

                w.StartObject()
                    .Key("name").Text(TryStr(() => s.gameObject.name))
                    .Key("type").Text(s.GetType().Name)
                    .Key("label").Text(label)
                    .Key("interactable").Boolean(TryBool(() => s.interactable))
                    .Key("active").Boolean(TryBool(() => s.gameObject.activeInHierarchy))
                    .Key("shot_x").Number(screen.x)
                    .Key("shot_y").Number(Screen.height - screen.y)
                    .EndObject();
            }
            w.EndArray();
            w.EndObject();
            return w.ToString();
        }

        private static string ClickAt(float x, float y)
        {
            if (EventSystem.current == null)
            {
                return "{\"ok\":false,\"error\":\"no EventSystem\"}";
            }

            // 传进来的是「截图像素坐标，左上原点」；转成 Unity 的屏幕坐标（左下原点）
            Vector2 screenPoint = new Vector2(x, Screen.height - y);

            PointerEventData data = new PointerEventData(EventSystem.current);
            data.position = screenPoint;
            data.pressPosition = screenPoint;
            data.button = PointerEventData.InputButton.Left;
            data.clickCount = 1;

            List<RaycastResult> hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);

            StringBuilder names = new StringBuilder();
            for (int i = 0; i < hits.Count; i++)
            {
                if (i > 0)
                {
                    names.Append(" > ");
                }
                names.Append(hits[i].gameObject.name);
            }

            GameObject target = hits.Count > 0 ? hits[0].gameObject : null;
            bool clicked = false;
            if (target != null)
            {
                ExecuteEvents.Execute(target, data, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(target, data, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
                ExecuteEvents.Execute(target, data, ExecuteEvents.submitHandler);
                clicked = true;
            }

            BridgeJson.Writer w = new BridgeJson.Writer();
            w.StartObject()
                .Key("ok").Boolean(clicked)
                .Key("raw_x").Number(x)
                .Key("raw_y").Number(y)
                .Key("screen_x").Number(screenPoint.x)
                .Key("screen_y").Number(screenPoint.y)
                .Key("target").Text(target != null ? target.name : null)
                .Key("hits").Text(names.ToString())
                .EndObject();
            return w.ToString();
        }

        private static string PressByText(string wanted)
        {
            Selectable[] all = UnityEngine.Object.FindObjectsOfType<Selectable>();
            for (int i = 0; i < all.Length; i++)
            {
                Selectable s = all[i];
                if (s == null)
                {
                    continue;
                }
                string label = null;
                try
                {
                    Text t = s.GetComponentInChildren<Text>();
                    if (t != null)
                    {
                        label = t.text;
                    }
                }
                catch { }

                if (label != null && label.Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    bool ok = true;
                    try
                    {
                        Button b = s as Button;
                        if (b != null)
                        {
                            b.onClick.Invoke();
                        }
                        else
                        {
                            ok = false;
                        }
                    }
                    catch
                    {
                        ok = false;
                    }
                    return "{\"ok\":" + (ok ? "true" : "false") + ",\"pressed\":" + BridgeJson.Str(label) + "}";
                }
            }
            return "{\"ok\":false,\"error\":\"button not found: " + BridgeJson.Str(wanted) + "\"}";
        }

        /// <summary>当前处在哪个场景、有没有飞行场景 —— 用来判断该点哪里。</summary>
        private static string DescribeInfo()
        {
            string sceneName = null;
            try
            {
                sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }
            catch { }

            BridgeJson.Writer w = new BridgeJson.Writer();
            w.StartObject()
                .Key("ok").Boolean(true)
                .Key("scene").Text(sceneName)
                .Key("has_flight_scene").Boolean(GameAccess.Scene != null)
                .Key("time_scale").Number(Time.timeScale)
                .Key("in_flight").Boolean(Telemetry.InFlight)
                .Key("offline_reason").Text(Telemetry.OfflineReason)
                .EndObject();
            return w.ToString();
        }

        /// <summary>按 GameObject 名字触发一个 Selectable —— 不依赖坐标，最稳。</summary>
        private static string TapByName(string wanted)
        {
            Selectable[] all = UnityEngine.Object.FindObjectsOfType<Selectable>();
            for (int i = 0; i < all.Length; i++)
            {
                Selectable s = all[i];
                if (s == null || s.gameObject == null)
                {
                    continue;
                }
                if (s.gameObject.name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                PointerEventData data = new PointerEventData(EventSystem.current);
                data.button = PointerEventData.InputButton.Left;
                data.clickCount = 1;

                ExecuteEvents.Execute(s.gameObject, data, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(s.gameObject, data, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(s.gameObject, data, ExecuteEvents.pointerClickHandler);
                ExecuteEvents.Execute(s.gameObject, data, ExecuteEvents.submitHandler);

                bool invoked = false;
                try
                {
                    Button b = s as Button;
                    if (b != null)
                    {
                        b.onClick.Invoke();
                        invoked = true;
                    }
                }
                catch { }

                return "{\"ok\":true,\"tapped\":" + BridgeJson.Str(s.gameObject.name)
                    + ",\"type\":" + BridgeJson.Str(s.GetType().Name)
                    + ",\"onClickInvoked\":" + (invoked ? "true" : "false") + "}";
            }
            return "{\"ok\":false,\"error\":\"selectable not found: " + BridgeJson.Str(wanted) + "\"}";
        }

        private static string TryStr(Func<string> getter)
        {
            try { return getter(); } catch { return null; }
        }

        private static bool TryBool(Func<bool> getter)
        {
            try { return getter(); } catch { return false; }
        }
    }
}

#endif
