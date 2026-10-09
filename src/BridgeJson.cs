// N.E.K.O-Juno-bridge — JSON 拼装与转义
//
// 手写 JSON 的理由和 KSP 那边一样：输出结构固定、可控，
// 且不依赖游戏 Mono 环境里行为不确定的序列化库。
//
// **最重要的一条**：所有数字都经 Num()，NaN / Infinity 一律输出 null，
// 保证任何情况下输出的都是合法 JSON，外部解析器永远不会崩。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NekoJunoBridge
{
    public static class BridgeJson
    {
        public static string Num(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "null";
            }
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        public static string Num(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "null";
            }
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>可空数字：null → JSON null。</summary>
        public static string Num(double? value)
        {
            return value.HasValue ? Num(value.Value) : "null";
        }

        public static string Num(float? value)
        {
            return value.HasValue ? Num(value.Value) : "null";
        }

        public static string Int(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Int(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        public static string Str(string value)
        {
            if (value == null)
            {
                return "null";
            }
            StringBuilder sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ' || c == '\u007f')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Raw(string value)
        {
            return value == null ? "null" : value;
        }

        /// <summary>
        /// 极简对象/数组构造器 —— 只是为了让拼装代码读起来像结构体，
        /// 不做任何解析。
        /// </summary>
        public sealed class Writer
        {
            private readonly StringBuilder sb = new StringBuilder(512);
            private readonly Stack<bool> firstStack = new Stack<bool>();
            private bool pendingKey;

            public Writer StartObject()
            {
                Separate();
                sb.Append('{');
                firstStack.Push(true);
                return this;
            }

            public Writer EndObject()
            {
                sb.Append('}');
                if (firstStack.Count > 0)
                {
                    firstStack.Pop();
                }
                return this;
            }

            public Writer StartArray()
            {
                Separate();
                sb.Append('[');
                firstStack.Push(true);
                return this;
            }

            public Writer EndArray()
            {
                sb.Append(']');
                if (firstStack.Count > 0)
                {
                    firstStack.Pop();
                }
                return this;
            }

            public Writer Key(string name)
            {
                Separate();
                sb.Append(BridgeJson.Str(name)).Append(':');
                pendingKey = true;
                return this;
            }

            public Writer Value(string rawJson)
            {
                Separate();
                sb.Append(rawJson);
                return this;
            }

            public Writer Number(double value)
            {
                return Value(Num(value));
            }

            public Writer Number(float value)
            {
                return Value(Num(value));
            }

            /// <summary>null → JSON null，用于可能缺失的遥测字段。</summary>
            public Writer Number(double? value)
            {
                return Value(value.HasValue ? Num(value.Value) : "null");
            }

            public Writer Number(float? value)
            {
                return Value(value.HasValue ? Num(value.Value) : "null");
            }

            public Writer Integer(int value)
            {
                return Value(Int(value));
            }

            public Writer Boolean(bool value)
            {
                return Value(Bool(value));
            }

            public Writer Text(string value)
            {
                return Value(Str(value));
            }

            private void Separate()
            {
                if (pendingKey)
                {
                    pendingKey = false;
                    return;
                }
                if (firstStack.Count == 0)
                {
                    return;
                }
                if (firstStack.Peek())
                {
                    firstStack.Pop();
                    firstStack.Push(false);
                }
                else
                {
                    sb.Append(',');
                }
            }

            public override string ToString()
            {
                return sb.ToString();
            }
        }

        /// <summary>从任意对象取一个安全字符串（异常/空都兜住）。</summary>
        public static string Quote(Func<string> getter)
        {
            try
            {
                return Str(getter());
            }
            catch
            {
                return "null";
            }
        }
    }
}
