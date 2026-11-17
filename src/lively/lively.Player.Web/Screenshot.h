#pragma once

#include <QByteArray>
#include <QImage>
#include <QString>

#include <variant>

struct Command;

// A cmd_screenshot: {"Type":6,"Format":0,"FilePath":"/abs/thumbnail.jpg","Delay":3000,"Width":480}.
struct ScreenshotRequest
{
    QString filePath;   // absolute; its folder must exist
    QByteArray format;  // "jpeg", "png" or "bmp"
    int delayMs = 0;    // counted from the page's successful load
    int width = 0;      // scale down to this width; 0 keeps the native size

    QString fileName() const;

    // Format is 0/1/2 or "jpeg"/"png"/"bmp" (default jpeg); Delay and Width default to 0. Returns the
    // request, or the reason it is unusable.
    static std::variant<ScreenshotRequest, QString> fromCommand(const Command &command);
};

// Scales and writes the image through a temporary file in the same folder that is renamed over FilePath,
// so a reader never sees a partial file. Returns the failure reason, or an empty string.
QString saveScreenshot(const QImage &image, const ScreenshotRequest &request);
