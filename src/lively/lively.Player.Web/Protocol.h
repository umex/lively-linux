#pragma once

#include <QByteArray>
#include <QJsonObject>
#include <QJsonValue>
#include <QLatin1StringView>
#include <QString>

#include <atomic>
#include <mutex>
#include <optional>
#include <utility>
#include <vector>

// The player protocol (docs/ipc.md): one compact JSON object per line, "Type" first as an integer.
// Numbers match lively.Models.Message.MessageType; do not reorder.
enum class MessageType {
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
};

enum class ConsoleCategory { log = 0, error = 1, console = 2 };

QLatin1StringView messageTypeName(MessageType type);

// A message from the core. The fields stay in the JSON object; accessors read the ones a command uses.
struct Command
{
    MessageType type;
    QJsonObject fields;

    // cmd_volume "Volume", clamped to 0-100; missing gives 0 like the .NET reader.
    std::optional<int> volume() const;
};

struct ParseResult
{
    std::optional<Command> command;  // empty for a valid line with an unknown "Type"
    QString error;                   // malformed line
};

// Parses one line. "Type" may be the integer value or the enum name.
ParseResult parseCommand(const QByteArray &line);

// Writes messages to the core on a file descriptor (the original stdout). Thread-safe; write errors are
// ignored because stdin EOF ends the process when the core goes away.
class MessageWriter
{
public:
    explicit MessageWriter(int fd) : m_fd(fd) { }

    using Fields = std::vector<std::pair<QLatin1StringView, QJsonValue>>;

    void send(MessageType type, const Fields &fields = {});
    void console(const QString &text, ConsoleCategory category = ConsoleCategory::log);
    // Sends msg_wploaded once; later calls do nothing. Returns whether this call sent it.
    bool loaded(bool success);
    bool loadedSent() const { return m_loadedSent.load(); }

private:
    int m_fd;
    std::mutex m_mutex;
    std::atomic<bool> m_loadedSent = false;
};
