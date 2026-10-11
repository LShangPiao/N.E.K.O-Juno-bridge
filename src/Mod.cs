// N.E.K.O-Juno-bridge — BepInEx 插件入口
//
// 把《朱诺新起源 Juno: New Origins》的飞行遥测通过本地 HTTP 暴露出去，
// 供外部程序（例如 N.E.K.O. 的猫娘）读取。
//
// 为什么是 BepInEx 而不是游戏官方的 .sr2-mod？
//   游戏的官方 mod 必须以 Unity 的 AssetBundle 形式打包（扩展名 .sr2-mod），
//   而构建它需要 Unity 编辑器和官方 ModTools。任何不经过 Unity 的构建
//   都无法产出合法的 AssetBundle，所以这里改走社区通用的 BepInEx 注入 ——
//   它不修改游戏原文件，卸载就是删掉几个文件。
//
// 设计原则（与 N.E.K.O-KSP-bridge 一致）
//   1. **可读写（默认开启，可被配置关掉）** —— 通过 /command 接口真正驱动飞船
//      （油门 / 姿态 / 平移 / 分级 / 激活组）。命令只在玩家明确发出时才动飞船，
//      且 zero / cut 可随时把操纵权交还玩家。
//   2. **公用** —— 不依赖 N.E.K.O.，任何程序都能读（和写）这个 HTTP 接口。
//   3. **不打扰** —— 不弹窗、不改 UI；控制走 ModApi 内部通道，不注入 OS 级输入。
//
// 生命周期（实测踩过的坑）
//   BepInEx 挂在它自己的 manager 对象上的插件组件，在这个游戏里会在
//   启动场景切换时被销毁 —— 所以 Start() / Update() 永远不会被派发。
//   因此这里做了两件事：
//     · HTTP 服务由**独立后台线程**持有，不随任何 Unity 对象消亡；
//     · 遥测采集挂在一个自己创建、显式 DontDestroyOnLoad 的运行器上。
//
// 线程模型
//   Unity API 只能在主线程调用，所以：
//     主线程    BridgeRunner.Update() -> Telemetry.Capture() 渲染快照
//     后台线程  HTTP 服务只读那份已渲染好的字符串，完全不碰 Unity 对象
//
//   ⚠️ 将来如果加**控制**接口（分级/油门/姿态），必须改成
//      「HTTP 线程入队 → 主线程 Update 里执行」。在后台线程调用
//      Unity API 会直接把游戏搞崩 —— 这是姊妹项目踩过的坑。

using System;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;

namespace NekoJunoBridge
{
    [BepInPlugin(JunoBridgePlugin.PluginGuid, JunoBridgePlugin.ModName, JunoBridgePlugin.PluginVersion)]
    public class JunoBridgePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "top.xhth.neko.juno-bridge";
        public const string ModName = "N.E.K.O-Juno-bridge";
        public const string PluginVersion = "0.2.0";

        private static GameObject runnerObject;

        private void Awake()
        {
            try
            {
                string dir = PluginDirectory();
                BridgeLog.Init(dir, "=== " + ModName + " v" + PluginVersion + " ===");
                BridgeConfig.Load(dir);

                BridgeLog.Write("插件目录: " + (dir ?? "(未知)"));
                BridgeLog.Write("配置: " + BridgeConfig.Describe());

                // 控制接口默认跟随配置开关（neko-juno-bridge.ini 里的 control_enabled）
                ControlApi.Enabled = BridgeConfig.ControlEnabled;
                BridgeLog.Write("控制接口: " + (BridgeConfig.ControlEnabled ? "已启用" : "已禁用"));
            }
            catch (Exception ex)
            {
                BridgeLog.Error("初始化失败: " + BridgeLog.Safe(ex));
            }

            // 服务是后台线程，先把它拉起来 —— 这样即使 Unity 侧的组件
            // 被销毁，接口也还在（只是快照不再更新）。
            StartServer();

            // 采集运行器：显式 DontDestroyOnLoad，避免和 BepInEx 的 manager
            // 一起被启动场景切换带走。
            CreateRunner();
        }

        private void StartServer()
        {
            if (BridgeServer.IsRunning)
            {
                return;
            }
            try
            {
                if (BridgeServer.Start(BridgeConfig.Port))
                {
                    BridgeLog.Write("HTTP 服务已启动: http://127.0.0.1:" + BridgeConfig.Port + "/");
                }
                else
                {
                    BridgeLog.Error("HTTP 服务未能启动，端口 " + BridgeConfig.Port
                        + " 可能被占用。改 neko-juno-bridge.ini 里的 port 再重启游戏。");
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Error("启动 HTTP 服务失败: " + BridgeLog.Safe(ex));
            }
        }

        private static void CreateRunner()
        {
            if (runnerObject != null)
            {
                return;
            }
            try
            {
                runnerObject = new GameObject("NekoJunoBridge_Runner");
                runnerObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(runnerObject);
                runnerObject.AddComponent<BridgeRunner>();
                BridgeLog.Write("采集运行器已创建");
            }
            catch (Exception ex)
            {
                BridgeLog.Error("创建采集运行器失败: " + BridgeLog.Safe(ex));
            }
        }

        // 保留 Update 作为额外保险：如果 BepInEx 的对象恰好活着，
        // 由它来驱动采集（BridgeRunner 里会去重）。
        private void Update()
        {
            BridgeRunner.Pump();
        }

        private void OnDestroy()
        {
            // 注意：这里**故意不停掉 HTTP 服务**。
            // 这个对象被销毁并不代表游戏退出，服务应当继续可用。
            BridgeLog.Write("插件宿主对象被销毁（HTTP 服务保持运行）");
        }

        private static string PluginDirectory()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(location))
                {
                    return null;
                }
                return Path.GetDirectoryName(location);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 遥测采集运行器。挂在自建的常驻对象上，负责按间隔把游戏状态
    /// 渲染成 JSON 快照。同一个插件里只应存在一个。
    /// </summary>
    public class BridgeRunner : MonoBehaviour
    {
        private static BridgeRunner active;
        private static float nextCapture;
        private static bool loggedStart;

        private void Awake()
        {
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            if (active != null && active != this)
            {
                BridgeLog.Warn("检测到重复的采集运行器，销毁多余的那个");
                UnityEngine.Object.Destroy(this);
                return;
            }
            active = this;
        }

        private void Update()
        {
            if (!loggedStart)
            {
                loggedStart = true;
                BridgeLog.Write("Update 循环已启动");
            }
            Pump();
        }

        /// <summary>可以由任意常驻组件驱动；自带节流，重复调用无害。</summary>
        public static void Pump()
        {
            try
            {
#if NEKO_DEBUG
                // 调试命令必须在主线程执行，且不该被采集节流拖慢
                DebugProbe.Pump();
#endif
                GameAccess.Tick();

                // 控制接口：出队执行一次性命令，并把 held setpoint 每帧写回 Controls。
                // 内部按 Enabled 开关；只有 engaged 时才每帧写回（松手后把操纵权交还玩家）。
                ControlApi.Pump();

                if (!BridgeServer.IsRunning)
                {
                    return;
                }

                float now = Time.realtimeSinceStartup;
                if (now < nextCapture)
                {
                    return;
                }
                double interval = BridgeConfig.PollIntervalMs / 1000.0;
                if (interval < 0.05)
                {
                    interval = 0.05;
                }
                nextCapture = now + (float)interval;
                Telemetry.Capture();
            }
            catch (Exception ex)
            {
                BridgeLog.Error("采集异常: " + BridgeLog.Safe(ex));
            }
        }
    }

    /// <summary>
    /// 配置。放在插件目录下的 neko-juno-bridge.ini，文件不存在会自动生成一份带注释的。
    /// 改完需要重启游戏。
    /// </summary>
    public static class BridgeConfig
    {
        public static int Port = 21577;
        public static double PollIntervalMs = 200;
        public static bool LogToFile = true;
        public static bool ControlEnabled = true;

        public static string ConfigPath { get; private set; }

        public static void Load(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir))
                {
                    return;
                }
                ConfigPath = Path.Combine(dir, "neko-juno-bridge.ini");
                if (!File.Exists(ConfigPath))
                {
                    WriteDefault(ConfigPath);
                    return;
                }

                string[] lines = File.ReadAllLines(ConfigPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                    {
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();

                    if (key == "port")
                    {
                        int parsed;
                        if (int.TryParse(value, out parsed) && parsed > 1024 && parsed < 65536)
                        {
                            Port = parsed;
                        }
                    }
                    else if (key == "poll_interval_ms")
                    {
                        double parsed;
                        if (double.TryParse(value, out parsed) && parsed >= 50 && parsed <= 60000)
                        {
                            PollIntervalMs = parsed;
                        }
                    }
                    else if (key == "log_to_file")
                    {
                        LogToFile = value == "1" || value.ToLowerInvariant() == "true";
                    }
                    else if (key == "control_enabled")
                    {
                        ControlEnabled = value == "1" || value.ToLowerInvariant() == "true";
                    }
                }
            }
            catch (Exception ex)
            {
                BridgeLog.Error("读配置失败: " + BridgeLog.Safe(ex));
            }
        }

        private static void WriteDefault(string path)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# N.E.K.O-Juno-bridge 配置");
                sb.AppendLine("# 改完要重启《朱诺新起源》才生效");
                sb.AppendLine();
                sb.AppendLine("# HTTP 端口，只监听 127.0.0.1（不对局域网开放），范围 1025-65535");
                sb.AppendLine("# 注意：改这里要同时改 N.E.K.O. 插件配置里的 bridge_url，只改一处两边就连不上了");
                sb.AppendLine("port=21577");
                sb.AppendLine();
                sb.AppendLine("# 遥测采集间隔（毫秒），范围 50-60000");
                sb.AppendLine("# 调小更实时但更耗 CPU；调大更省。默认 200（每秒 5 次）");
                sb.AppendLine("poll_interval_ms=200");
                sb.AppendLine();
                sb.AppendLine("# 是否写日志文件（juno-bridge.log，超过 2 MB 自动截断）");
                sb.AppendLine("log_to_file=true");
                sb.AppendLine();
                sb.AppendLine("# 是否开放控制接口（/command、/controls）");
                sb.AppendLine("# 设 0 即回到纯只读；设 1 才允许外部程序真正驱动飞船");
                sb.AppendLine("control_enabled=true");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        public static string Describe()
        {
            return "port=" + Port + " poll=" + PollIntervalMs + "ms log=" + LogToFile + " control=" + ControlEnabled;
        }
    }
}
