using System;
using Newtonsoft.Json;

namespace lively.Models.Message
{
    /// <summary>
    /// Base of every message exchanged with a player process (one JSON object per line).
    /// </summary>
    [Serializable]
    public abstract class IpcMessage
    {
        [JsonProperty(Order = -2)]
        public MessageType Type { get; }

        protected IpcMessage(MessageType type)
        {
            Type = type;
        }
    }

    #region commands (core → player)

    [Serializable]
    public class LivelyCloseCmd : IpcMessage
    {
        public LivelyCloseCmd() : base(MessageType.cmd_close) { }
    }

    [Serializable]
    public class LivelySuspendCmd : IpcMessage
    {
        public LivelySuspendCmd() : base(MessageType.cmd_suspend) { }
    }

    [Serializable]
    public class LivelyResumeCmd : IpcMessage
    {
        public LivelyResumeCmd() : base(MessageType.cmd_resume) { }
    }

    [Serializable]
    public class LivelyReloadCmd : IpcMessage
    {
        public LivelyReloadCmd() : base(MessageType.cmd_reload) { }
    }

    [Serializable]
    public class LivelyVolumeCmd : IpcMessage
    {
        /// <summary>0-100.</summary>
        public int Volume { get; set; }
        public LivelyVolumeCmd() : base(MessageType.cmd_volume) { }
    }

    [Serializable]
    public class LivelyScreenshotCmd : IpcMessage
    {
        public ScreenshotFormat Format { get; set; }
        public string FilePath { get; set; }
        /// <summary>Delay in milliseconds before capture.</summary>
        public uint Delay { get; set; }
        public LivelyScreenshotCmd() : base(MessageType.cmd_screenshot) { }
    }

    #endregion commands

    #region messages (player → core)

    [Serializable]
    public class LivelyMessageConsole : IpcMessage
    {
        public string Message { get; set; }
        public ConsoleMessageType Category { get; set; }
        public LivelyMessageConsole() : base(MessageType.msg_console) { }
    }

    [Serializable]
    public class LivelyMessageWallpaperLoaded : IpcMessage
    {
        public bool Success { get; set; }
        public LivelyMessageWallpaperLoaded() : base(MessageType.msg_wploaded) { }
    }

    [Serializable]
    public class LivelyMessageScreenshot : IpcMessage
    {
        public string FileName { get; set; }
        public bool Success { get; set; }
        public LivelyMessageScreenshot() : base(MessageType.msg_screenshot) { }
    }

    #endregion messages

    #region LivelyProperties.json controls (core → player)

    [Serializable]
    public class LivelySlider : IpcMessage
    {
        public string Name { get; set; }
        public double Value { get; set; }
        public double Step { get; set; }
        public LivelySlider() : base(MessageType.lp_slider) { }
    }

    [Serializable]
    public class LivelyTextBox : IpcMessage
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public LivelyTextBox() : base(MessageType.lp_textbox) { }
    }

    [Serializable]
    public class LivelyDropdown : IpcMessage
    {
        public string Name { get; set; }
        public int Value { get; set; }
        public LivelyDropdown() : base(MessageType.lp_dropdown) { }
    }

    [Serializable]
    public class LivelyFolderDropdown : IpcMessage
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public LivelyFolderDropdown() : base(MessageType.lp_fdropdown) { }
    }

    [Serializable]
    public class LivelyButton : IpcMessage
    {
        public string Name { get; set; }
        /// <summary>True for the "restore default" button.</summary>
        public bool IsDefault { get; set; }
        public LivelyButton() : base(MessageType.lp_button) { }
    }

    [Serializable]
    public class LivelyColorPicker : IpcMessage
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public LivelyColorPicker() : base(MessageType.lp_cpicker) { }
    }

    [Serializable]
    public class LivelyCheckbox : IpcMessage
    {
        public string Name { get; set; }
        public bool Value { get; set; }
        public LivelyCheckbox() : base(MessageType.lp_chekbox) { }
    }

    [Serializable]
    public class LivelyDropdownScaler : IpcMessage
    {
        public string Name { get; set; }
        public int Value { get; set; }
        public LivelyDropdownScaler() : base(MessageType.lp_dropdown_scaler) { }
    }

    #endregion controls
}
