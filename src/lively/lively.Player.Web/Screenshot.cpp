#include "Screenshot.h"

#include "Protocol.h"

#include <QDir>
#include <QFileInfo>
#include <QImageWriter>
#include <QJsonValue>
#include <QSaveFile>

#include <array>
#include <limits>
#include <optional>

using namespace Qt::StringLiterals;

namespace {

// Numbers match lively.Models.Message.ScreenshotFormat.
constexpr std::array<QLatin1StringView, 3> formats = {"jpeg"_L1, "png"_L1, "bmp"_L1};

constexpr int JpegQuality = 85;

// A non-negative integer field; missing or null gives 0.
std::optional<int> intField(const QJsonObject &fields, QLatin1StringView name)
{
    const QJsonValue value = fields.value(name);
    if (value.isUndefined() || value.isNull())
        return 0;
    const double number = value.toDouble(-1);
    if (!value.isDouble() || number < 0 || number > std::numeric_limits<int>::max() || number != int(number))
        return std::nullopt;
    return int(number);
}

}  // namespace

QString ScreenshotRequest::fileName() const
{
    return QFileInfo(filePath).fileName();
}

std::variant<ScreenshotRequest, QString> ScreenshotRequest::fromCommand(const Command &command)
{
    ScreenshotRequest request;
    request.filePath = command.fields.value("FilePath"_L1).toString();
    if (!QDir::isAbsolutePath(request.filePath) || request.fileName().isEmpty())
        return u"FilePath must be an absolute file path, got \"%1\""_s.arg(request.filePath);

    const QJsonValue format = command.fields.value("Format"_L1);
    if (format.isUndefined() || format.isNull()) {
        request.format = QByteArray(formats[0].data(), formats[0].size());
    } else if (format.isDouble()) {
        const double index = format.toDouble();
        if (index < 0 || index >= double(formats.size()) || index != int(index))
            return u"unknown Format %1"_s.arg(index);
        request.format = QByteArray(formats[int(index)].data(), formats[int(index)].size());
    } else if (format.isString()) {
        const QString name = format.toString();
        for (QLatin1StringView known : formats) {
            if (name == known)
                request.format = QByteArray(known.data(), known.size());
        }
        if (request.format.isEmpty())
            return u"unknown Format \"%1\""_s.arg(name);
    } else {
        return u"Format must be an integer or a string"_s;
    }

    const std::optional<int> delay = intField(command.fields, "Delay"_L1);
    const std::optional<int> width = intField(command.fields, "Width"_L1);
    if (!delay)
        return u"Delay must be a non-negative integer (milliseconds)"_s;
    if (!width)
        return u"Width must be a non-negative integer (pixels)"_s;
    request.delayMs = *delay;
    request.width = *width;
    return request;
}

QString saveScreenshot(const QImage &grabbed, const ScreenshotRequest &request)
{
    if (grabbed.isNull())
        return u"the window could not be grabbed"_s;
    QImage image = grabbed;
    if (request.width > 0 && request.width < image.width())
        image = image.scaledToWidth(request.width, Qt::SmoothTransformation);
    // The surface is opaque; JPEG and BMP have no alpha anyway.
    image = image.convertToFormat(QImage::Format_RGB32);

    QSaveFile file(request.filePath);
    if (!file.open(QIODevice::WriteOnly))
        return u"cannot write %1: %2"_s.arg(request.filePath, file.errorString());
    QImageWriter writer(&file, request.format);
    if (request.format == "jpeg")
        writer.setQuality(JpegQuality);
    if (!writer.write(image)) {
        file.cancelWriting();
        return u"cannot encode %1: %2"_s.arg(request.filePath, writer.errorString());
    }
    if (!file.commit())
        return u"cannot write %1: %2"_s.arg(request.filePath, file.errorString());
    return {};
}
