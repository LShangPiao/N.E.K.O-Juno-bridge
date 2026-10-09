// N.E.K.O-Juno-bridge — 日志
//
// 双写：Unity 日志（Player.log / BepInEx 控制台）+ 自己的文件。
// 文件超过 2 MB 时截掉前半段，避免长期挂机把磁盘写满。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NekoJunoBridge
{
    public static class BridgeLog
    {
        private static readonly object Gate = new object();
        private static string logPath;

        public static string LogPath
        {
            get { return logPath; }
        }

        public static void Init(string dir, string header)
        {
            try
            {
                if (string.IsNullOrEmpty(dir))
                {
                    return;
                }
                Directory.CreateDirectory(dir);
                logPath = Path.Combine(dir, "juno-bridge.log");
                Trim();
                Write(header);
            }
            catch
            {
            }
        }

        public static void Write(string message)
        {
            Append("INFO ", message);
        }

        public static void Warn(string message)
        {
            Append("WARN ", message);
        }

        public static void Error(string message)
        {
            Append("ERROR", message);
        }

        private static void Append(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message;
            try
            {
                UnityEngine.Debug.Log("[NekoJunoBridge] " + message);
            }
            catch
            {
            }
            if (logPath == null)
            {
                return;
            }
            lock (Gate)
            {
                try
                {
                    File.AppendAllText(logPath, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch
                {
                }
            }
        }

        /// <summary>把二进制垃圾转成可读文本，方便折进日志。</summary>
        public static string Safe(Exception ex)
        {
            if (ex == null)
            {
                return "(null)";
            }
            return ex.GetType().Name + ": " + ex.Message;
        }

        /// <summary>读取最近 N 行日志，供 /log 接口使用。</summary>
        public static List<string> Tail(int count)
        {
            List<string> result = new List<string>();
            if (count <= 0)
            {
                count = 100;
            }
            if (count > 2000)
            {
                count = 2000;
            }
            if (logPath == null || !File.Exists(logPath))
            {
                return result;
            }
            lock (Gate)
            {
                try
                {
                    string[] lines = File.ReadAllLines(logPath, Encoding.UTF8);
                    int start = lines.Length - count;
                    if (start < 0)
                    {
                        start = 0;
                    }
                    for (int i = start; i < lines.Length; i++)
                    {
                        result.Add(lines[i]);
                    }
                }
                catch
                {
                }
            }
            return result;
        }

        private static void Trim()
        {
            try
            {
                if (!File.Exists(logPath) || new FileInfo(logPath).Length <= 2 * 1024 * 1024)
                {
                    return;
                }
                string[] lines = File.ReadAllLines(logPath, Encoding.UTF8);
                int keep = lines.Length / 2;
                List<string> tail = new List<string>();
                tail.Add("# (旧日志已截断)");
                for (int i = keep; i < lines.Length; i++)
                {
                    tail.Add(lines[i]);
                }
                File.WriteAllLines(logPath, tail.ToArray(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}
