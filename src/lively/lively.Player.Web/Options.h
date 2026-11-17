#pragma once

#include <QSize>
#include <QString>
#include <QStringList>
#include <QUrl>

// Command line of lively-player-web (docs/ipc.md, "Launch").
//
// --wallpaper-url <absolute path, file: URL or http(s) URL> is required, and --wallpaper-output or
// --wallpaper-display names the output. --wallpaper-geometry <W>x<H>, --wallpaper-type, --wallpaper-volume
// <0-100> (default 0, muted), --wallpaper-backend, --wallpaper-desktop, --web-log <path> and --web-profile
// <dir> are optional. Unknown --wallpaper-* options are ignored (their value too, when the next token does
// not start with "--"); other unknown arguments are reported in warnings.
struct Options
{
    QString source;  // --wallpaper-url as given
    QUrl url;        // what the page loads
    bool remote = false;  // http(s) or a "url" wallpaper: reports loaded when shown, retries failed loads
    QString type;
    QString display;
    QSize geometry;  // empty when not given
    int volume = 0;
    QString backend;
    QString output;  // defaults to display
    QString desktop;
    QString logPath;
    QString profilePath;  // empty: off-the-record profile
    QStringList warnings;
    QString error;  // set when the arguments are unusable

    static Options parse(const QStringList &args);
};
