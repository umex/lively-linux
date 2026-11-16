using System.Collections.Generic;
using Newtonsoft.Json;

namespace lively.Models.Message
{
    /// <summary>
    /// Line format of the player protocol: one compact JSON object per line, "Type" first as an integer.
    /// </summary>
    public static class IpcMessageJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Include,
            Converters = new List<JsonConverter> { new IpcMessageConverter() },
        };

        /// <summary>
        /// Serializes to a single line (no line breaks, strings are escaped).
        /// </summary>
        public static string Serialize(IpcMessage message)
        {
            return JsonConvert.SerializeObject(message, Settings);
        }

        /// <summary>
        /// Parses one line. Returns null for message types without a class (msg_hwnd, lsp_*) or unknown types.
        /// </summary>
        /// <exception cref="JsonException">Malformed JSON, not an object, or no usable "Type" field.</exception>
        public static IpcMessage Deserialize(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                throw new JsonSerializationException("Empty IPC message.");
            }
            return JsonConvert.DeserializeObject<IpcMessage>(line, Settings);
        }
    }
}
