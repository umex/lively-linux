#include "Protocol.h"

#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonParseError>

#include <algorithm>
#include <array>
#include <cerrno>
#include <cmath>

#include <unistd.h>

using namespace Qt::StringLiterals;

namespace {

constexpr std::array<QLatin1StringView, 20> typeNames = {
    "msg_hwnd"_L1,     "msg_console"_L1,    "msg_wploaded"_L1,  "msg_screenshot"_L1, "cmd_reload"_L1,
    "cmd_close"_L1,    "cmd_screenshot"_L1, "cmd_suspend"_L1,   "cmd_resume"_L1,     "cmd_volume"_L1,
    "lsp_perfcntr"_L1, "lsp_nowplaying"_L1, "lp_slider"_L1,     "lp_textbox"_L1,     "lp_dropdown"_L1,
    "lp_fdropdown"_L1, "lp_button"_L1,      "lp_cpicker"_L1,    "lp_chekbox"_L1,     "lp_dropdown_scaler"_L1,
};

std::optional<MessageType> typeFromNumber(double number)
{
    if (number < 0 || number >= double(typeNames.size()) || number != std::floor(number))
        return std::nullopt;
    return MessageType(int(number));
}

std::optional<MessageType> typeFromName(const QString &name)
{
    // Enum.TryParse on the .NET side also accepts a number in a string.
    bool isNumber = false;
    const int number = name.trimmed().toInt(&isNumber);
    if (isNumber)
        return typeFromNumber(number);
    const auto it = std::find(typeNames.begin(), typeNames.end(), name);
    if (it == typeNames.end())
        return std::nullopt;
    return MessageType(it - typeNames.begin());
}

// QJsonDocument only serializes arrays and objects, and QJsonObject sorts its keys, which would move
// "Type" away from the front. Each value is written on its own inside an array and unwrapped.
QByteArray jsonValue(const QJsonValue &value)
{
    const QByteArray array = QJsonDocument(QJsonArray{value}).toJson(QJsonDocument::Compact);
    return array.mid(1, array.size() - 2);
}

}  // namespace

QLatin1StringView messageTypeName(MessageType type)
{
    const auto index = std::size_t(type);
    return index < typeNames.size() ? typeNames[index] : "unknown"_L1;
}

std::optional<int> Command::volume() const
{
    const QJsonValue value = fields.value("Volume"_L1);
    if (value.isUndefined() || value.isNull())
        return 0;
    if (!value.isDouble())
        return std::nullopt;
    return std::clamp(value.toInt(), 0, 100);
}

ParseResult parseCommand(const QByteArray &line)
{
    QJsonParseError error;
    const QJsonDocument document = QJsonDocument::fromJson(line, &error);
    if (error.error != QJsonParseError::NoError)
        return {.command = std::nullopt, .error = error.errorString()};
    if (!document.isObject())
        return {.command = std::nullopt, .error = u"not a JSON object"_s};

    const QJsonObject object = document.object();
    const QJsonValue type = object.value("Type"_L1);
    std::optional<MessageType> parsed;
    if (type.isDouble())
        parsed = typeFromNumber(type.toDouble());
    else if (type.isString())
        parsed = typeFromName(type.toString());
    else if (type.isUndefined())
        return {.command = std::nullopt, .error = u"no \"Type\" field"_s};
    else
        return {.command = std::nullopt, .error = u"\"Type\" must be an integer or a string"_s};

    if (!parsed)
        return {};
    return {.command = Command{.type = *parsed, .fields = object}, .error = {}};
}

void MessageWriter::send(MessageType type, const Fields &fields)
{
    QByteArray line = "{\"Type\":" + QByteArray::number(int(type));
    for (const auto &[name, value] : fields) {
        line += ",\"";
        line += QByteArrayView(name.data(), name.size());
        line += "\":";
        line += jsonValue(value);
    }
    line += "}\n";

    const std::lock_guard lock(m_mutex);
    const char *data = line.constData();
    qsizetype left = line.size();
    while (left > 0) {
        const ssize_t written = ::write(m_fd, data, size_t(left));
        if (written < 0) {
            if (errno == EINTR)
                continue;
            return;
        }
        data += written;
        left -= written;
    }
}

void MessageWriter::console(const QString &text, ConsoleCategory category)
{
    send(MessageType::msg_console, {{"Message"_L1, text}, {"Category"_L1, int(category)}});
}

bool MessageWriter::loaded(bool success)
{
    if (m_loadedSent.exchange(true))
        return false;
    send(MessageType::msg_wploaded, {{"Success"_L1, success}});
    return true;
}
