using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PepperDash.Essentials.Plugin.Config
{
    /// <summary>
    /// Deserializes string (or integer) enum values, but falls back to the default value
    /// (<c>null</c> for nullable enum targets, the zero value otherwise) when the token does not
    /// match a defined enum member — instead of throwing.
    /// <para>
    /// This prevents a single unrecognized enum string in a device config (for example a legacy
    /// <c>"Enabled"</c> value for a routing-mode property) from aborting deserialization of the
    /// entire owning object and silently discarding every other property.
    /// </para>
    /// </summary>
    public class TolerantStringEnumConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            var underlying = Nullable.GetUnderlyingType(objectType) ?? objectType;
            return underlying.IsEnum;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var isNullable = Nullable.GetUnderlyingType(objectType) != null;
            var enumType = Nullable.GetUnderlyingType(objectType) ?? objectType;

            if (reader.TokenType == JsonToken.Null)
                return null;

            var token = JToken.Load(reader);

            try
            {
                if (token.Type == JTokenType.Integer)
                {
                    var intValue = token.Value<long>();
                    var boxed = Enum.ToObject(enumType, intValue);
                    if (Enum.IsDefined(enumType, boxed))
                        return boxed;
                }
                else if (token.Type == JTokenType.String)
                {
                    var raw = token.Value<string>();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        foreach (var name in Enum.GetNames(enumType))
                        {
                            if (string.Equals(name, raw.Trim(), StringComparison.OrdinalIgnoreCase))
                                return Enum.Parse(enumType, name);
                        }
                    }
                }
            }
            catch
            {
                // Fall through to the default below.
            }

            return isNullable ? null : Enum.ToObject(enumType, 0);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteValue(value.ToString());
        }
    }
}
