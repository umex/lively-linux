using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace lively.Models.Message
{
    /// <summary>
    /// Deserializes an <see cref="IpcMessage"/> into its concrete class based on the "Type" field.
    /// "Type" may be the integer value or the enum name. Message types without a class
    /// (msg_hwnd, lsp_*) and unknown values give null. Writing uses the default serializer.
    /// </summary>
    public class IpcMessageConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType) => objectType == typeof(IpcMessage);

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            var obj = JObject.Load(reader);
            var typeToken = obj["Type"];
            if (typeToken == null)
            {
                throw new JsonSerializationException("IPC message has no \"Type\" field.");
            }

            MessageType type;
            switch (typeToken.Type)
            {
                case JTokenType.Integer:
                    var number = typeToken.Value<long>();
                    if (number < int.MinValue || number > int.MaxValue || !Enum.IsDefined(typeof(MessageType), (int)number))
                    {
                        return null;
                    }
                    type = (MessageType)(int)number;
                    break;
                case JTokenType.String:
                    var name = typeToken.Value<string>();
                    // Enum.TryParse also accepts numeric strings, IsDefined filters out-of-range values.
                    if (!Enum.TryParse(name, false, out type) || !Enum.IsDefined(typeof(MessageType), type))
                    {
                        return null;
                    }
                    break;
                default:
                    throw new JsonSerializationException("IPC message \"Type\" must be an integer or a string.");
            }

            var message = Create(type);
            if (message != null)
            {
                using (var objReader = obj.CreateReader())
                {
                    serializer.Populate(objReader, message);
                }
            }
            return message;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }

        private static IpcMessage Create(MessageType type)
        {
            switch (type)
            {
                case MessageType.msg_console: return new LivelyMessageConsole();
                case MessageType.msg_wploaded: return new LivelyMessageWallpaperLoaded();
                case MessageType.msg_screenshot: return new LivelyMessageScreenshot();
                case MessageType.cmd_reload: return new LivelyReloadCmd();
                case MessageType.cmd_close: return new LivelyCloseCmd();
                case MessageType.cmd_screenshot: return new LivelyScreenshotCmd();
                case MessageType.cmd_suspend: return new LivelySuspendCmd();
                case MessageType.cmd_resume: return new LivelyResumeCmd();
                case MessageType.cmd_volume: return new LivelyVolumeCmd();
                case MessageType.lp_slider: return new LivelySlider();
                case MessageType.lp_textbox: return new LivelyTextBox();
                case MessageType.lp_dropdown: return new LivelyDropdown();
                case MessageType.lp_fdropdown: return new LivelyFolderDropdown();
                case MessageType.lp_button: return new LivelyButton();
                case MessageType.lp_cpicker: return new LivelyColorPicker();
                case MessageType.lp_chekbox: return new LivelyCheckbox();
                case MessageType.lp_dropdown_scaler: return new LivelyDropdownScaler();
                default: return null; // msg_hwnd, lsp_perfcntr, lsp_nowplaying: no class yet.
            }
        }
    }
}
