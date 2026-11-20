// lively-player-web: web wallpaper player. QtWebEngine (Chromium) renders a local page or an http(s) URL
// into a wlr-layer-shell background surface on one Wayland output, through LayerShellQt. Speaks the player
// protocol (docs/ipc.md) on stdin/stdout.
//
// Exits 0 on cmd_close, stdin EOF, SIGTERM, SIGINT and when the compositor closes the surface; on a failure
// it sends msg_console (error), msg_wploaded Success:false if it had not loaded, and exits 1.

#include "Options.h"
#include "Player.h"
#include "PlayerLog.h"
#include "Protocol.h"
#include "WaylandProbe.h"

#include <QCoreApplication>
#include <QFileInfo>
#include <QGuiApplication>
#include <QtWebEngineQuick/qtwebenginequickglobal.h>

#include <csignal>
#include <cstdio>
#include <cstdlib>

#include <fcntl.h>
#include <unistd.h>

using namespace Qt::StringLiterals;

namespace {

// qInstallMessageHandler takes a plain function, so the handler reaches these through file statics. Set
// once in main() before the handler is installed.
MessageWriter *messageWriter = nullptr;
PlayerLog *playerLog = nullptr;

void handleQtMessage(QtMsgType type, const QMessageLogContext &context, const QString &message)
{
    const QString category = QString::fromLatin1(context.category ? context.category : "default");
    const QString text = category == u"default"_s ? message : category + u": "_s + message;
    playerLog->write(u"qt: "_s + text);
    if (type == QtWarningMsg || type == QtCriticalMsg || type == QtFatalMsg)
        std::fprintf(stderr, "lively-player-web: %s\n", qPrintable(text));
    if (type == QtFatalMsg) {
        messageWriter->console(text, ConsoleCategory::error);
        messageWriter->loaded(false);
        std::_Exit(1);
    }
}

int fail(MessageWriter &writer, PlayerLog &log, const QString &reason)
{
    log.write(u"failed: "_s + reason);
    log.write(u"exit code 1"_s);
    std::fprintf(stderr, "lively-player-web: %s\n", qPrintable(reason));
    writer.console(reason, ConsoleCategory::error);
    writer.loaded(false);
    return 1;
}

}  // namespace

int main(int argc, char *argv[])
{
    // A closed standard descriptor would be reused by the next open(); a closed stdin then reads as EOF.
    for (int fd = STDIN_FILENO; fd <= STDERR_FILENO; ++fd) {
        if (::fcntl(fd, F_GETFD) < 0)
            ::open("/dev/null", O_RDWR);
    }
    // stdout carries only protocol lines. Keep the original on a private descriptor and point fd 1 at stderr,
    // so anything else that prints there (Qt, Chromium, its child processes, GL drivers) cannot corrupt it.
    const int protocolFd = ::fcntl(STDOUT_FILENO, F_DUPFD_CLOEXEC, 3);
    if (protocolFd < 0 || ::dup2(STDERR_FILENO, STDOUT_FILENO) < 0) {
        std::perror("lively-player-web: cannot redirect stdout");
        return 1;
    }
    // A core that went away must not kill the player mid-write; stdin EOF ends it.
    std::signal(SIGPIPE, SIG_IGN);

    MessageWriter writer(protocolFd);
    QStringList args;
    for (int i = 1; i < argc; ++i)
        args << QString::fromLocal8Bit(argv[i]);
    const Options options = Options::parse(args);
    PlayerLog log(options.logPath);
    log.write(u"started pid %1 args: %2"_s.arg(::getpid()).arg(args.join(u' ')));
    for (const QString &warning : options.warnings)
        log.write(warning);
    writer.console(u"started "_s + args.join(u' '));

    if (!options.error.isEmpty())
        return fail(writer, log, options.error);
    if (options.url.isLocalFile() && !QFileInfo(options.url.toLocalFile()).isFile())
        return fail(writer, log, u"file not found: "_s + options.url.toLocalFile());
    if (const QString reason = probeWaylandLayerShell(); !reason.isEmpty())
        return fail(writer, log, reason);

    messageWriter = &writer;
    playerLog = &log;
    qInstallMessageHandler(handleQtMessage);

    qputenv("QT_QPA_PLATFORM", "wayland");
    // The surface never has keyboard focus, so an input method is of no use. Without this, a click on a text
    // field in the page sends text-input show_input_panel, which KWin honours for whatever window is focused.
    // "compose" (dead keys only) keeps Qt from using text-input at all.
    qputenv("QT_IM_MODULES", "compose");
    QCoreApplication::setApplicationName(u"lively-player-web"_s);
    QCoreApplication::setOrganizationName(u"lively-wallpaper"_s);
    // Before the application object: sets up context sharing and the graphics API for Chromium.
    QtWebEngineQuick::initialize();

    // Qt and Chromium read their options from the command line; the player's own arguments are not for them.
    int qtArgc = 1;
    char *qtArgv[] = {argv[0], nullptr};
    QGuiApplication app(qtArgc, qtArgv);
    app.setQuitOnLastWindowClosed(false);

    int code = 1;
    {
        Player player(options, writer, log);
        if (player.start())
            code = app.exec();
    }
    log.write(u"exit code %1"_s.arg(code));
    qInstallMessageHandler(nullptr);
    return code;
}
