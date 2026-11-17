#include "Options.h"

#include <QDir>
#include <QRegularExpression>

#include <algorithm>

using namespace Qt::StringLiterals;

namespace {

QSize parseGeometry(const QString &text)
{
    static const QRegularExpression pattern(u"^(\\d{1,6})[xX](\\d{1,6})$"_s);
    const auto match = pattern.match(text);
    if (!match.hasMatch())
        return {};
    const QSize size(match.captured(1).toInt(), match.captured(2).toInt());
    return size.width() > 0 && size.height() > 0 ? size : QSize();
}

}  // namespace

Options Options::parse(const QStringList &args)
{
    Options o;
    QString geometry;
    bool haveGeometry = false;
    for (qsizetype i = 0; i < args.size(); ++i) {
        const QString &arg = args[i];
        auto value = [&]() -> QString {
            if (i + 1 < args.size() && !args[i + 1].startsWith(u"--"_s))
                return args[++i];
            return {};
        };
        if (arg == u"--wallpaper-url"_s) {
            o.source = value();
        } else if (arg == u"--wallpaper-type"_s) {
            o.type = value();
        } else if (arg == u"--wallpaper-display"_s) {
            o.display = value();
        } else if (arg == u"--wallpaper-geometry"_s) {
            geometry = value();
            haveGeometry = true;
        } else if (arg == u"--wallpaper-volume"_s) {
            const QString text = value();
            bool ok = false;
            const int volume = text.toInt(&ok);
            if (ok) {
                o.volume = std::clamp(volume, 0, 100);
            } else {
                o.warnings << u"invalid --wallpaper-volume \"%1\", using 0"_s.arg(text);
                o.volume = 0;
            }
        } else if (arg == u"--wallpaper-backend"_s) {
            o.backend = value();
        } else if (arg == u"--wallpaper-output"_s) {
            o.output = value();
        } else if (arg == u"--wallpaper-desktop"_s) {
            o.desktop = value();
        } else if (arg == u"--web-log"_s) {
            o.logPath = value();
        } else if (arg == u"--web-profile"_s) {
            o.profilePath = value();
        } else if (arg.startsWith(u"--wallpaper-"_s)) {
            // Players must ignore --wallpaper-* arguments they do not know.
            value();
        } else if (arg.startsWith(u"--"_s)) {
            const QString v = value();
            o.warnings << u"ignored unknown option "_s + arg + (v.isNull() ? QString() : u" "_s + v);
        } else {
            o.warnings << u"ignored argument "_s + arg;
        }
    }

    if (o.output.isEmpty())
        o.output = o.display;

    if (haveGeometry) {
        o.geometry = parseGeometry(geometry);
        if (o.geometry.isEmpty()) {
            o.error = u"invalid --wallpaper-geometry \"%1\", expected <width>x<height>"_s.arg(geometry);
            return o;
        }
    }

    if (o.source.isEmpty()) {
        o.error = u"missing --wallpaper-url"_s;
        return o;
    }
    if (o.output.isEmpty()) {
        o.error = u"missing --wallpaper-output and --wallpaper-display"_s;
        return o;
    }

    const QUrl url(o.source, QUrl::StrictMode);
    const QString scheme = url.scheme().toLower();
    if (scheme == u"http"_s || scheme == u"https"_s) {
        if (!url.isValid() || url.host().isEmpty()) {
            o.error = u"invalid --wallpaper-url \"%1\""_s.arg(o.source);
            return o;
        }
        o.url = url;
        o.remote = true;
    } else if (scheme == u"file"_s) {
        if (!url.isValid() || !QDir::isAbsolutePath(url.toLocalFile())) {
            o.error = u"invalid --wallpaper-url \"%1\""_s.arg(o.source);
            return o;
        }
        o.url = url;
    } else if (QDir::isAbsolutePath(o.source)) {
        o.url = QUrl::fromLocalFile(QDir::cleanPath(o.source));
    } else {
        o.error = u"--wallpaper-url must be an absolute path or an http(s) URL, got \"%1\""_s.arg(o.source);
        return o;
    }
    o.remote = o.remote || o.type == u"url"_s;
    return o;
}
