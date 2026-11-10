using System;
using System.Collections;
using System.Globalization;
using System.Text;

namespace Supercent.LunaTextureOptimizer.Editor
{
    // JSON writer for the browser page's API: dictionaries, lists, strings, numbers, bools and null, nested to any
    // depth (JsonUtility cannot write recursive trees such as the Used by chain).
    public static class MiniJson
    {
        public static string Serialize(object value)
        {
            var builder = new StringBuilder();
            Write(builder, value);
            return builder.ToString();
        }

        static void Write(StringBuilder builder, object value)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }
            var text = value as string;
            if (text != null)
            {
                WriteString(builder, text);
                return;
            }
            if (value is bool)
            {
                builder.Append((bool)value ? "true" : "false");
                return;
            }
            if (value is Enum)
            {
                WriteString(builder, value.ToString());
                return;
            }
            if (value is IConvertible && !(value is char))
            {
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                builder.Append(double.IsNaN(number) || double.IsInfinity(number) ? "null" : number.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var map = value as IDictionary;
            if (map != null)
            {
                builder.Append('{');
                var first = true;
                foreach (DictionaryEntry entry in map)
                {
                    if (!first)
                        builder.Append(',');
                    first = false;
                    WriteString(builder, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                    builder.Append(':');
                    Write(builder, entry.Value);
                }
                builder.Append('}');
                return;
            }
            var list = value as IEnumerable;
            if (list != null)
            {
                builder.Append('[');
                var first = true;
                foreach (var item in list)
                {
                    if (!first)
                        builder.Append(',');
                    first = false;
                    Write(builder, item);
                }
                builder.Append(']');
                return;
            }
            WriteString(builder, value.ToString());
        }

        static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == '<' || c == '>' || c == '&')
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
