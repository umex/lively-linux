namespace lively.Models.Message
{
    /// <summary>
    /// Message identifiers of the core ↔ player protocol, serialized as integers.
    /// Names and numbers match upstream Lively; do not reorder.
    /// </summary>
    public enum MessageType
    {
        msg_hwnd = 0,
        msg_console = 1,
        msg_wploaded = 2,
        msg_screenshot = 3,
        cmd_reload = 4,
        cmd_close = 5,
        cmd_screenshot = 6,
        cmd_suspend = 7,
        cmd_resume = 8,
        cmd_volume = 9,
        lsp_perfcntr = 10,
        lsp_nowplaying = 11,
        lp_slider = 12,
        lp_textbox = 13,
        lp_dropdown = 14,
        lp_fdropdown = 15,
        lp_button = 16,
        lp_cpicker = 17,
        lp_chekbox = 18,
        lp_dropdown_scaler = 19,
    }

    public enum ConsoleMessageType
    {
        log,
        error,
        console,
    }

    public enum ScreenshotFormat
    {
        jpeg,
        png,
        bmp,
    }
}
