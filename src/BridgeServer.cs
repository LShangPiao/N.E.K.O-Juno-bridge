// N.E.K.O-Juno-bridge — HTTP 服务
//
// 手写 TcpListener 而不是 HttpListener，理由和 KSP 那边一样：
// Unity 的 Mono 对 System.Net.HttpListener 支持不稳（很多平台被裁掉），
// 而这里的 HTTP 需求极简 —— 只读、GET、JSON。
//
// 只监听 127.0.0.1，不接受局域网连接。这是个**公用接口**：
// 任何程序（网页、仪表盘、Discord 机器人）都能读，N.E.K.O. 只是第一个消费者。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace NekoJunoBridge
{
    public static class BridgeServer
    {
        private static TcpListener listener;
        private static Thread acceptThread;
        private static volatile bool running;
        private static int activeClients;

        public static bool IsRunning
        {
            get { return running; }
        }

        public static int Port { get; private set; }

        public static bool Start(int port)
        {
            if (running)
            {
                return true;
            }
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                Port = port;
                running = true;
                acceptThread = new Thread(AcceptLoop);
                acceptThread.IsBackground = true;
                acceptThread.Name = "NekoJunoBridge-HTTP";
                acceptThread.Start();
                return true;
            }
            catch (Exception ex)
            {
                BridgeLog.Error("HTTP 服务启动失败 (端口 " + port + "): " + BridgeLog.Safe(ex));
                running = false;
                return false;
            }
        }

        public static void Stop()
        {
            running = false;
            try
            {
                if (listener != null)
                {
                    listener.Stop();
                }
            }
            catch
            {
            }
            listener = null;
        }

        private static void AcceptLoop()
        {
            while (running)
            {
                TcpClient client = null;
                try
                {
                    if (listener == null || !listener.Server.IsBound)
                    {
                        break;
                    }
                    client = listener.AcceptTcpClient();
                }
                catch
                {
                    if (!running)
                    {
                        break;
                    }
                    Thread.Sleep(50);
                    continue;
                }

                // 简单限流：同时最多 16 个连接，超了直接关掉，避免被拖垮
                if (Interlocked.Increment(ref activeClients) > 16)
                {
                    Interlocked.Decrement(ref activeClients);
                    try { client.Close(); }
                    catch { }
                    continue;
                }

                TcpClient captured = client;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        HandleClient(captured);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeClients);
                        try { captured.Close(); }
                        catch { }
                    }
                });
            }
        }

        private static void HandleClient(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;
                NetworkStream stream = client.GetStream();

                string requestLine = ReadLine(stream);
                if (string.IsNullOrEmpty(requestLine))
                {
                    return;
                }

                // 丢弃请求头（我们只处理 GET，没有 body）
                int guard = 0;
                while (guard++ < 64)
                {
                    string header = ReadLine(stream);
                    if (header == null || header.Length == 0)
                    {
                        break;
                    }
                }

                string method;
                string path;
                ParseRequestLine(requestLine, out method, out path);

                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResponse(stream, 405, "text/plain; charset=utf-8", "只支持 GET");
                    return;
                }

                string body;
                string contentType;
                int status = Route(path, out body, out contentType);
                WriteResponse(stream, status, contentType, body);
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------
        // 路由
        // ------------------------------------------------------------------

        private static int Route(string path, out string body, out string contentType)
        {
            contentType = "application/json; charset=utf-8";

            string route = path;
            string query = "";
            int q = path.IndexOf('?');
            if (q >= 0)
            {
                route = path.Substring(0, q);
                query = path.Substring(q + 1);
            }
            route = route.TrimEnd('/');
            if (route.Length == 0)
            {
                route = "/";
            }

            switch (route)
            {
                case "/":
                    contentType = "text/html; charset=utf-8";
                    body = IndexPage();
                    return 200;
                case "/ping":
                    body = PingJson();
                    return 200;
                case "/state":
                    body = Telemetry.StateJson;
                    return 200;
                case "/vessel":
                    body = Telemetry.VesselJson;
                    return 200;
                case "/orbit":
                    body = Telemetry.OrbitJson;
                    return 200;
                case "/flight":
                    body = Telemetry.FlightJson;
                    return 200;
                case "/resources":
                    body = Telemetry.ResourcesJson;
                    return 200;
                case "/target":
                    body = Telemetry.TargetJson;
                    return 200;
                case "/bodies":
                    body = Telemetry.BodiesJson;
                    return 200;
                case "/vessels":
                    body = Telemetry.VesselsJson;
                    return 200;
                case "/scene":
                    body = Telemetry.SceneJson;
                    return 200;
                case "/config":
                    body = ConfigJson();
                    return 200;
                case "/log":
                    body = LogJson(QueryInt(query, "n", 100));
                    return 200;
#if NEKO_DEBUG
                case "/debug/ui":
                    DebugProbe.EnqueueUi();
                    body = WaitDebug();
                    return 200;
                case "/debug/click":
                    DebugProbe.EnqueueClick(QueryFloat(query, "x", 0f), QueryFloat(query, "y", 0f));
                    body = WaitDebug();
                    return 200;
                case "/debug/press":
                    DebugProbe.EnqueuePress(QueryString(query, "text", ""));
                    body = WaitDebug();
                    return 200;
                case "/debug/tap":
                    DebugProbe.EnqueueTap(QueryString(query, "name", ""));
                    body = WaitDebug();
                    return 200;
                case "/debug/info":
                    DebugProbe.EnqueueInfo();
                    body = WaitDebug();
                    return 200;
#endif
                default:
                    body = "{\"ok\":false,\"error\":\"未知接口\",\"path\":" + BridgeJson.Str(path) + "}";
                    return 404;
            }
        }

        private static string PingJson()
        {
            BridgeJson.Writer w = new BridgeJson.Writer();
            w.StartObject()
                .Key("ok").Boolean(true)
                .Key("bridge").Text(JunoBridgePlugin.ModName)
                .Key("version").Text(JunoBridgePlugin.PluginVersion)
                .Key("mode").Text("read-only")
                .Key("in_flight").Boolean(Telemetry.InFlight)
                .Key("port").Integer(Port)
                .Key("game").Text(GameVersion());
            return w.ToString();
        }

        private static string ConfigJson()
        {
            BridgeJson.Writer w = new BridgeJson.Writer();
            w.StartObject()
                .Key("ok").Boolean(true)
                .Key("port").Integer(BridgeConfig.Port)
                .Key("poll_interval_ms").Number(BridgeConfig.PollIntervalMs)
                .Key("log_to_file").Boolean(BridgeConfig.LogToFile)
                .Key("config_path").Text(BridgeConfig.ConfigPath)
                .Key("log_path").Text(BridgeLog.LogPath)
                .Key("read_only").Boolean(true);
            return w.ToString();
        }

        private static string LogJson(int count)
        {
            List<string> lines = BridgeLog.Tail(count);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"ok\":true,\"lines\":[");
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(BridgeJson.Str(lines[i]));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string GameVersion()
        {
            try
            {
                return UnityEngine.Application.version;
            }
            catch
            {
                return null;
            }
        }

        private static string IndexPage()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            sb.Append("<title>N.E.K.O-Juno-bridge</title>");
            sb.Append("<style>body{font-family:system-ui,Segoe UI,sans-serif;max-width:760px;margin:40px auto;");
            sb.Append("padding:0 16px;line-height:1.7;color:#222}code{background:#f3f3f5;padding:2px 5px;border-radius:4px}");
            sb.Append("h1{font-size:22px}h2{font-size:16px;margin-top:28px}li{margin:4px 0}</style></head><body>");
            sb.Append("<h1>N.E.K.O-Juno-bridge <small>v").Append(JunoBridgePlugin.PluginVersion).Append("</small></h1>");
            sb.Append("<p>《朱诺新起源》只读飞行遥测接口。任何程序都可以读，不依赖 N.E.K.O.。</p>");
            sb.Append("<p><b>只读</strong></b> —— 它不会控制飞船、不动油门、不碰姿态。</p>");
            sb.Append("<h2>接口</h2><ul>");
            AppendRoute(sb, "/ping", "存活检查 + 版本 + 是否在飞行");
            AppendRoute(sb, "/state", "一次性拿到全部数据");
            AppendRoute(sb, "/vessel", "飞船基础信息与姿态");
            AppendRoute(sb, "/orbit", "轨道根数与入轨判断");
            AppendRoute(sb, "/flight", "速度、大气、受力");
            AppendRoute(sb, "/resources", "燃料 / 电量 / Delta-V / 推重比");
            AppendRoute(sb, "/target", "锁定目标");
            AppendRoute(sb, "/bodies", "星系全部天体");
            AppendRoute(sb, "/vessels", "本次飞行里的所有飞船");
            AppendRoute(sb, "/scene", "场景与时间加速状态");
            AppendRoute(sb, "/config", "当前配置");
            AppendRoute(sb, "/log?n=100", "最近日志");
            sb.Append("</ul><h2>状态</h2><p>");
            sb.Append(Telemetry.InFlight ? "正在飞行，遥测可用。" : "当前不在飞行场景。");
            sb.Append("</p></body></html>");
            return sb.ToString();
        }

        private static void AppendRoute(StringBuilder sb, string path, string description)
        {
            sb.Append("<li><code>").Append(path).Append("</code> — ").Append(description).Append("</li>");
        }

        // ------------------------------------------------------------------
        // HTTP 细节
        // ------------------------------------------------------------------

        private static string ReadLine(NetworkStream stream)
        {
            MemoryStream buffer = new MemoryStream();
            int b;
            while ((b = stream.ReadByte()) >= 0)
            {
                if (b == '\n')
                {
                    break;
                }
                if (b != '\r')
                {
                    buffer.WriteByte((byte)b);
                }
                if (buffer.Length > 8192)
                {
                    break;
                }
            }
            if (b < 0 && buffer.Length == 0)
            {
                return null;
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        private static void ParseRequestLine(string line, out string method, out string path)
        {
            method = "";
            path = "/";
            string[] parts = line.Split(' ');
            if (parts.Length >= 1)
            {
                method = parts[0];
            }
            if (parts.Length >= 2)
            {
                path = parts[1];
            }
        }

        private static void WriteResponse(NetworkStream stream, int status, string contentType, string body)
        {
            byte[] payload = Encoding.UTF8.GetBytes(body ?? "");
            StringBuilder header = new StringBuilder();
            header.Append("HTTP/1.1 ").Append(status).Append(' ').Append(StatusText(status)).Append("\r\n");
            header.Append("Content-Type: ").Append(contentType).Append("\r\n");
            header.Append("Content-Length: ").Append(payload.Length).Append("\r\n");
            header.Append("Access-Control-Allow-Origin: *\r\n");
            header.Append("Cache-Control: no-store\r\n");
            header.Append("Connection: close\r\n\r\n");

            byte[] head = Encoding.ASCII.GetBytes(header.ToString());
            stream.Write(head, 0, head.Length);
            if (payload.Length > 0)
            {
                stream.Write(payload, 0, payload.Length);
            }
            stream.Flush();
        }

        private static string StatusText(int status)
        {
            switch (status)
            {
                case 200: return "OK";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                default: return "OK";
            }
        }

        private static int QueryInt(string query, string key, int fallback)
        {
            string raw = QueryString(query, key, null);
            if (raw == null)
            {
                return fallback;
            }
            int value;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return fallback;
        }

        private static float QueryFloat(string query, string key, float fallback)
        {
            string raw = QueryString(query, key, null);
            if (raw == null)
            {
                return fallback;
            }
            float value;
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return fallback;
        }

        private static string QueryString(string query, string key, string fallback)
        {
            if (string.IsNullOrEmpty(query))
            {
                return fallback;
            }
            string[] pairs = query.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                int eq = pairs[i].IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                if (pairs[i].Substring(0, eq) == key)
                {
                    return Uri.UnescapeDataString(pairs[i].Substring(eq + 1).Replace('+', ' '));
                }
            }
            return fallback;
        }

#if NEKO_DEBUG
        /// <summary>
        /// 调试命令是异步执行的（HTTP 线程入队、主线程出队执行），
        /// 这里等一小会儿再取结果 —— 仅调试构建使用。
        /// </summary>
        private static string WaitDebug()
        {
            System.Threading.Thread.Sleep(800);
            return DebugProbe.LastReport;
        }
#endif
    }
}
