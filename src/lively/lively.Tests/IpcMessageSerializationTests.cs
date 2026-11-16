using lively.Models.Message;
using Newtonsoft.Json;

namespace lively.Tests;

public class IpcMessageSerializationTests
{
    [Fact]
    public void Serialize_WritesTypeFirstAsInteger()
    {
        Assert.Equal("{\"Type\":7}", IpcMessageJson.Serialize(new LivelySuspendCmd()));
        Assert.Equal("{\"Type\":2,\"Success\":true}", IpcMessageJson.Serialize(new LivelyMessageWallpaperLoaded { Success = true }));
        Assert.Equal("{\"Type\":9,\"Volume\":40}", IpcMessageJson.Serialize(new LivelyVolumeCmd { Volume = 40 }));
    }

    [Fact]
    public void Serialize_IsASingleLine()
    {
        var json = IpcMessageJson.Serialize(new LivelyMessageConsole { Message = "line one\nline two\r\n", Category = ConsoleMessageType.error });

        Assert.DoesNotContain('\n', json);
        Assert.DoesNotContain('\r', json);
        var back = Assert.IsType<LivelyMessageConsole>(IpcMessageJson.Deserialize(json));
        Assert.Equal("line one\nline two\r\n", back.Message);
        Assert.Equal(ConsoleMessageType.error, back.Category);
    }

    [Fact]
    public void MessageType_NumbersMatchUpstream()
    {
        Assert.Equal(0, (int)MessageType.msg_hwnd);
        Assert.Equal(5, (int)MessageType.cmd_close);
        Assert.Equal(12, (int)MessageType.lp_slider);
        Assert.Equal(19, (int)MessageType.lp_dropdown_scaler);
        Assert.Equal(20, Enum.GetValues<MessageType>().Length);
    }

    public static TheoryData<IpcMessage> AllMessages() =>
    [
        new LivelyCloseCmd(),
        new LivelySuspendCmd(),
        new LivelyResumeCmd(),
        new LivelyReloadCmd(),
        new LivelyVolumeCmd { Volume = 55 },
        new LivelyScreenshotCmd { Format = ScreenshotFormat.png, FilePath = "/tmp/shot.png", Delay = 250 },
        new LivelyMessageConsole { Message = "hello", Category = ConsoleMessageType.console },
        new LivelyMessageWallpaperLoaded { Success = true },
        new LivelyMessageScreenshot { FileName = "/tmp/shot.png", Success = true },
        new LivelySlider { Name = "speed", Value = 1.5, Step = 0.1 },
        new LivelyTextBox { Name = "text", Value = "hi" },
        new LivelyDropdown { Name = "list", Value = 2 },
        new LivelyFolderDropdown { Name = "images", Value = "a.jpg" },
        new LivelyButton { Name = "reset", IsDefault = true },
        new LivelyColorPicker { Name = "color", Value = "#FF0000" },
        new LivelyCheckbox { Name = "check", Value = true },
        new LivelyDropdownScaler { Name = "scaler", Value = 3 },
    ];

    [Theory]
    [MemberData(nameof(AllMessages))]
    public void RoundTrip_KeepsClassAndValues(IpcMessage message)
    {
        var json = IpcMessageJson.Serialize(message);
        var back = IpcMessageJson.Deserialize(json);

        Assert.NotNull(back);
        Assert.IsType(message.GetType(), back);
        Assert.Equal(message.Type, back.Type);
        Assert.Equal(json, IpcMessageJson.Serialize(back));
    }

    [Fact]
    public void Deserialize_AcceptsEnumName()
    {
        var msg = IpcMessageJson.Deserialize("{\"Type\":\"cmd_volume\",\"Volume\":10}");

        Assert.Equal(10, Assert.IsType<LivelyVolumeCmd>(msg).Volume);
    }

    [Fact]
    public void Deserialize_TypeNeedNotBeFirst()
    {
        var msg = IpcMessageJson.Deserialize("{\"Success\":false,\"Type\":2}");

        Assert.False(Assert.IsType<LivelyMessageWallpaperLoaded>(msg).Success);
    }

    [Theory]
    [InlineData("{\"Type\":0}")]
    [InlineData("{\"Type\":10}")]
    [InlineData("{\"Type\":11,\"Title\":\"song\"}")]
    [InlineData("{\"Type\":999}")]
    [InlineData("{\"Type\":\"not_a_type\"}")]
    public void Deserialize_TypesWithoutClass_GiveNull(string json)
    {
        Assert.Null(IpcMessageJson.Deserialize(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("42")]
    [InlineData("[1,2]")]
    [InlineData("{\"Volume\":5}")]
    [InlineData("{\"Type\":true}")]
    [InlineData("{\"Type\":7} trailing")]
    public void Deserialize_Malformed_ThrowsJsonException(string json)
    {
        Assert.ThrowsAny<JsonException>(() => IpcMessageJson.Deserialize(json));
    }
}
