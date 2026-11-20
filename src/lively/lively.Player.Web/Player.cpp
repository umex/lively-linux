#include "Player.h"

#include "PlayerLog.h"
#include "PosixInput.h"
#include "Protocol.h"

#include <LayerShellQt/Window>

#include <QCoreApplication>
#include <QDir>
#include <QFileInfo>
#include <QGuiApplication>
#include <QJsonDocument>
#include <QMetaEnum>
#include <QQmlComponent>
#include <QQmlEngine>
#include <QQuickWebEngineDownloadRequest>
#include <QQuickWebEngineProfile>
#include <QQuickWindow>
#include <QScreen>

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdio>
#include <utility>

using namespace Qt::StringLiterals;

namespace {

// A url wallpaper restored at login may start before the network is up.
constexpr auto RetryInterval = std::chrono::seconds(15);

// When the compositor closes the layer surface because the output went away, screenRemoved may arrive just
// after; wait for it before treating the close as a plain dismissal.
constexpr auto ClosedGrace = std::chrono::milliseconds(1000);

// A capture right after the load could catch a view that has not painted the page yet.
constexpr auto MinScreenshotDelayAfterLoad = std::chrono::milliseconds(250);

// Reports page console messages dropped by the rate limit at most this often.
constexpr auto SuppressedReportInterval = std::chrono::seconds(1);

// net::ERR_ABORTED: the navigation was replaced by another one (a redirect from script, a new load).
constexpr int NetErrorAborted = -3;

QString screenNames()
{
    QStringList names;
    for (const QScreen *screen : QGuiApplication::screens())
        names << screen->name();
    return names.isEmpty() ? u"none"_s : names.join(u", "_s);
}

}  // namespace

Player::Player(const Options &options, MessageWriter &writer, PlayerLog &log, QObject *parent)
    : QObject(parent), m_options(options), m_writer(writer), m_log(log), m_volume(options.volume)
{
    m_retryTimer.setSingleShot(true);
    m_retryTimer.setInterval(RetryInterval);
    connect(&m_retryTimer, &QTimer::timeout, this, [this] { load(PendingLoad::Retry); });

    m_closedTimer.setSingleShot(true);
    m_closedTimer.setInterval(ClosedGrace);
    connect(&m_closedTimer, &QTimer::timeout, this,
            [this] { quit(0, u"the compositor closed the layer surface"_s); });

    m_screenshotTimer.setSingleShot(true);
    connect(&m_screenshotTimer, &QTimer::timeout, this, &Player::takeScreenshot);

    m_suppressedTimer.setSingleShot(true);
    m_suppressedTimer.setInterval(SuppressedReportInterval);
    connect(&m_suppressedTimer, &QTimer::timeout, this, &Player::reportSuppressedConsole);
    m_clock.start();
}

Player::~Player()
{
    // The window and its WebEngineView go first, then the deferred delete of the page, and only then the
    // profile: QtWebEngine warns when a profile is released while a page still uses it.
    m_window.reset();
    QCoreApplication::sendPostedEvents(nullptr, QEvent::DeferredDelete);
    m_profileOwner.reset();
    m_engine.reset();
}

bool Player::start()
{
    m_signals = new SignalNotifier(this);
    if (!m_signals->isValid())
        m_log.write(u"cannot install the SIGTERM/SIGINT handlers"_s);
    connect(m_signals, &SignalNotifier::received, this, [this](const QString &name) { quit(0, name); });

    m_stdin = new StdinReader(this);
    connect(m_stdin, &StdinReader::lineReceived, this, &Player::handleLine);
    connect(m_stdin, &StdinReader::discarded, this, [this](const QString &reason) {
        m_log.write(reason);
        m_writer.console(u"invalid message: "_s + reason, ConsoleCategory::error);
    });
    connect(m_stdin, &StdinReader::closed, this, [this](const QString &reason) { quit(0, reason); });

    QScreen *screen = findScreen();
    if (!screen)
        return false;

    m_engine = std::make_unique<QQmlEngine>();
    connect(m_engine.get(), &QQmlEngine::warnings, this, [this](const QList<QQmlError> &warnings) {
        for (const QQmlError &warning : warnings)
            m_log.write(u"qml: "_s + warning.toString());
    });
    if (!createProfile() || !createWindow(screen))
        return false;

    connect(qGuiApp, &QGuiApplication::screenRemoved, this, [this](QScreen *removed) {
        if (removed == m_screen)
            fail(u"output %1 was removed"_s.arg(m_options.output));
    });

    m_log.write(u"loading %1%2"_s.arg(m_options.url.toString(), m_options.remote ? u" (remote)"_s : QString()));
    emit loadRequested(m_options.url);
    return true;
}

QScreen *Player::findScreen()
{
    for (QScreen *screen : QGuiApplication::screens()) {
        if (screen->name() == m_options.output) {
            m_log.write(u"output %1: %2x%3 at %4,%5, device pixel ratio %6"_s.arg(screen->name())
                            .arg(screen->size().width())
                            .arg(screen->size().height())
                            .arg(screen->geometry().x())
                            .arg(screen->geometry().y())
                            .arg(screen->devicePixelRatio()));
            return screen;
        }
    }
    fail(u"unknown output %1 (outputs: %2)"_s.arg(m_options.output, screenNames()));
    return nullptr;
}

bool Player::createProfile()
{
    // WebEngineProfilePrototype (QML only) creates the profile with its settings already applied. Constructing
    // QQuickWebEngineProfile directly is deprecated since Qt 6.9, and with a storage name it first creates a
    // default directory under ~/.local/share. Without a storage name the profile is off the record.
    QVariantMap properties = {
        // Every permission is denied (Wallpaper.qml); asking each time keeps the denials out of the profile.
        {u"persistentPermissionsPolicy"_s,
         QVariant::fromValue(QQuickWebEngineProfile::PersistentPermissionsPolicy::AskEveryTime)},
    };
    if (!m_options.profilePath.isEmpty()) {
        const QString path = QDir(m_options.profilePath).absolutePath();
        if (!QDir().mkpath(path)) {
            fail(u"cannot create the profile directory %1"_s.arg(path));
            return false;
        }
        properties.insert(u"storageName"_s, u"lively-player-web"_s);
        properties.insert(u"persistentStoragePath"_s, path);
        properties.insert(u"cachePath"_s, path);
    }

    QQmlComponent component(m_engine.get());
    component.loadFromModule("QtWebEngine", "WebEngineProfilePrototype");
    std::unique_ptr<QObject> prototype(component.createWithInitialProperties(properties));
    if (prototype)
        QMetaObject::invokeMethod(prototype.get(), "instance", qReturnArg(m_profile));
    if (!m_profile) {
        fail(u"cannot create the web profile: "_s + component.errorString().trimmed());
        return false;
    }
    m_profileOwner = std::move(prototype);
    if (m_profile->isOffTheRecord())
        m_log.write(u"profile: off the record"_s);
    else
        m_log.write(u"profile: "_s + m_profile->persistentStoragePath());

    connect(m_profile, &QQuickWebEngineProfile::downloadRequested, this,
            [this](QQuickWebEngineDownloadRequest *download) {
                download->cancel();
                refused(u"download of "_s + download->url().toString());
            });
    return true;
}

bool Player::createWindow(QScreen *screen)
{
    QQmlComponent component(m_engine.get());
    component.loadFromModule("LivelyPlayerWeb", "Wallpaper");
    QObject *root = component.createWithInitialProperties({
        {u"player"_s, QVariant::fromValue(this)},
        {u"profile"_s, QVariant::fromValue(m_profile)},
    });
    m_window.reset(qobject_cast<QQuickWindow *>(root));
    if (!m_window) {
        delete root;
        fail(u"cannot create the wallpaper window: "_s + component.errorString().trimmed());
        return false;
    }

    m_screen = screen;
    m_window->setScreen(screen);
    m_window->resize(m_options.geometry.isEmpty() ? screen->size() : m_options.geometry);

    // Must be configured before the platform window exists, i.e. before show().
    auto *layer = LayerShellQt::Window::get(m_window.get());
    layer->setLayer(LayerShellQt::Window::LayerBackground);
    layer->setAnchors({LayerShellQt::Window::AnchorTop, LayerShellQt::Window::AnchorBottom,
                       LayerShellQt::Window::AnchorLeft, LayerShellQt::Window::AnchorRight});
    layer->setExclusiveZone(-1);
    layer->setScope(u"lively-wallpaper"_s);
    layer->setKeyboardInteractivity(LayerShellQt::Window::KeyboardInteractivityNone);
    layer->setActivateOnShow(false);
    layer->setScreen(screen);

    m_firstFrame = connect(m_window.get(), &QQuickWindow::frameSwapped, this, &Player::onFirstFrame,
                           Qt::QueuedConnection);
    connect(m_window.get(), &QWindow::visibleChanged, this, [this](bool visible) {
        if (!visible && !m_exiting && !m_closedTimer.isActive())
            m_closedTimer.start();
    });
    m_window->show();
    return true;
}

void Player::onFirstFrame()
{
    disconnect(m_firstFrame);
    if (m_surfaceShown)
        return;  // already queued when disconnected
    m_surfaceShown = true;
    m_log.write(u"surface shown, %1x%2"_s.arg(m_window->width()).arg(m_window->height()));
    if (m_options.remote || m_pageLoaded)
        markReady();
    armScreenshot();
}

void Player::markReady()
{
    if (m_ready)
        return;
    m_ready = true;
    m_writer.loaded(true);
    m_log.write(u"loaded %1 (%2) on %3"_s.arg(m_options.url.toString(), m_options.type, m_options.output));
    updateSuspended();
}

void Player::handleLine(const QByteArray &line)
{
    const ParseResult result = parseCommand(line);
    if (!result.error.isEmpty()) {
        m_log.write(u"invalid message: "_s + QString::fromUtf8(line));
        m_writer.console(u"invalid message: "_s + result.error, ConsoleCategory::error);
        return;
    }
    if (!result.command) {
        m_log.write(u"ignored message: "_s + QString::fromUtf8(line));
        return;
    }
    handleCommand(*result.command);
}

void Player::handleCommand(const Command &command)
{
    const QString name = messageTypeName(command.type);
    m_log.write(u"received "_s + name);
    m_writer.console(u"received "_s + name);

    switch (command.type) {
    case MessageType::cmd_close:
        quit(0, name);
        break;
    case MessageType::cmd_reload:
        load(PendingLoad::Reload);
        break;
    case MessageType::cmd_suspend:
    case MessageType::cmd_resume:
        m_suspendRequested = command.type == MessageType::cmd_suspend;
        updateSuspended();
        break;
    case MessageType::cmd_screenshot:
        requestScreenshot(command);
        break;
    case MessageType::cmd_volume: {
        const std::optional<int> volume = command.volume();
        if (!volume) {
            const QByteArray json = QJsonDocument(command.fields).toJson(QJsonDocument::Compact);
            m_log.write(u"invalid cmd_volume: "_s + QString::fromUtf8(json));
            m_writer.console(u"invalid cmd_volume: Volume must be a number"_s, ConsoleCategory::error);
            break;
        }
        const bool wasMuted = muted();
        m_volume = *volume;
        // QtWebEngine has no per-page volume, only mute.
        m_log.write(u"volume %1, %2"_s.arg(m_volume).arg(muted() ? u"muted"_s : u"unmuted"_s));
        if (muted() != wasMuted)
            emit mutedChanged();
        break;
    }
    default:
        m_log.write(u"ignored "_s + name);
        break;
    }
}

void Player::updateSuspended()
{
    const bool suspended = m_suspendRequested && m_ready;
    if (suspended == m_suspended)
        return;
    m_suspended = suspended;
    m_log.write(suspended ? u"suspending"_s : u"resuming"_s);
    emit suspendedChanged();
    if (!suspended && m_pendingLoad != PendingLoad::None)
        load(std::exchange(m_pendingLoad, PendingLoad::None));
}

void Player::load(PendingLoad kind)
{
    if (m_suspended) {
        // A frozen page does not load; do it on resume. A reload covers a pending retry and vice versa.
        m_log.write(kind == PendingLoad::Reload ? u"reload deferred until resume"_s
                                                : u"retry deferred until resume"_s);
        m_pendingLoad = kind;
        return;
    }
    m_retryTimer.stop();
    if (kind == PendingLoad::Reload) {
        emit reloadRequested();
    } else {
        m_log.write(u"retrying "_s + m_options.url.toString());
        m_writer.console(u"retrying "_s + m_options.url.toString());
        emit loadRequested(m_options.url);
    }
}

void Player::retryLater(const QString &reason)
{
    const QString text = u"%1, retrying in %2 s"_s.arg(reason).arg(std::chrono::seconds(RetryInterval).count());
    m_log.write(text);
    m_writer.console(text, ConsoleCategory::error);
    m_retryTimer.start();
}

void Player::pageLoadingChanged(const QWebEngineLoadingInfo &info)
{
    const QString url = info.url().toString();
    switch (info.status()) {
    case QWebEngineLoadingInfo::LoadStartedStatus:
        m_log.write(u"load started: "_s + url);
        break;
    case QWebEngineLoadingInfo::LoadStoppedStatus:
        m_log.write(u"load stopped: "_s + url);
        break;
    case QWebEngineLoadingInfo::LoadSucceededStatus:
        m_log.write(u"load finished: "_s + url);
        m_retryTimer.stop();
        m_pageLoaded = true;
        m_contentLoaded = !info.isErrorPage();
        m_contentSince.start();
        if (m_surfaceShown)
            markReady();
        if (!m_screenshotTimer.isActive())
            armScreenshot();
        break;
    case QWebEngineLoadingInfo::LoadFailedStatus: {
        if (info.errorCode() == NetErrorAborted) {
            m_log.write(u"load aborted: "_s + url);
            break;
        }
        // The view now shows Chromium's error page; a pending screenshot waits for the next successful load.
        m_contentLoaded = false;
        m_screenshotTimer.stop();
        const QString reason = u"cannot load %1: %2 (error %3)"_s.arg(url, info.errorString()).arg(info.errorCode());
        if (m_options.remote)
            retryLater(reason);
        else if (!m_ready)
            fail(reason);
        else {
            m_log.write(reason);
            m_writer.console(reason, ConsoleCategory::error);
        }
        break;
    }
    }
}

void Player::pageConsoleMessage(int level, const QString &message, int line, const QString &source)
{
    // WebEngineView.JavaScriptConsoleMessageLevel
    static constexpr std::array<QStringView, 3> levels = {u"info", u"warning", u"error"};
    const QStringView name = level >= 0 && level < int(levels.size()) ? levels[level] : u"log";
    if (!m_consoleLimiter.allow(m_clock.elapsed())) {
        if (!m_suppressedTimer.isActive())
            m_suppressedTimer.start();
        return;
    }
    const QString text =
        ConsoleLimiter::truncate(u"console %1: %2 (%3:%4)"_s.arg(name, message, source).arg(line));
    m_log.write(text);
    m_writer.console(text, ConsoleCategory::console);
}

void Player::reportSuppressedConsole()
{
    const int suppressed = m_consoleLimiter.takeSuppressed();
    if (suppressed == 0)
        return;
    const QString text = u"%1 page console messages suppressed (more than %2 per second)"_s.arg(suppressed)
                             .arg(ConsoleLimiter::PerSecond);
    m_log.write(text);
    m_writer.console(text);
}

void Player::requestScreenshot(const Command &command)
{
    auto parsed = ScreenshotRequest::fromCommand(command);
    if (const QString *error = std::get_if<QString>(&parsed)) {
        screenshotFailed(QFileInfo(command.fields.value("FilePath"_L1).toString()).fileName(), *error);
        return;
    }
    // One pending request at a time: a newer one replaces it and the replaced one is answered as failed.
    if (m_screenshot) {
        m_screenshotTimer.stop();
        screenshotFailed(m_screenshot->fileName(), u"replaced by a newer cmd_screenshot"_s);
    }
    m_screenshot = std::get<ScreenshotRequest>(std::move(parsed));
    m_log.write(u"screenshot to %1 (%2, delay %3 ms, width %4) requested"_s
                    .arg(m_screenshot->filePath, QString::fromLatin1(m_screenshot->format))
                    .arg(m_screenshot->delayMs)
                    .arg(m_screenshot->width));
    armScreenshot();
}

void Player::armScreenshot()
{
    if (!m_screenshot || !m_contentLoaded || !m_surfaceShown)
        return;
    // Delay counts from the successful load, or from the request when the page had loaded before it.
    const auto sinceLoad = std::chrono::milliseconds(m_contentSince.elapsed());
    const auto delay = std::max(std::chrono::milliseconds(m_screenshot->delayMs),
                                MinScreenshotDelayAfterLoad - sinceLoad);
    m_screenshotTimer.start(delay);
}

void Player::takeScreenshot()
{
    if (!m_screenshot)
        return;
    const ScreenshotRequest request = *std::exchange(m_screenshot, std::nullopt);
    // Grabs what the window shows: the page, or the still frame while suspended.
    QElapsedTimer took;
    took.start();
    const QString error = saveScreenshot(m_window->grabWindow(), request);
    if (!error.isEmpty()) {
        screenshotFailed(request.fileName(), error);
        return;
    }
    m_log.write(u"screenshot written to %1 in %2 ms%3"_s.arg(request.filePath)
                    .arg(took.elapsed())
                    .arg(m_suspended ? u" (still frame)"_s : QString()));
    m_writer.send(MessageType::msg_screenshot, {{"FileName"_L1, request.fileName()}, {"Success"_L1, true}});
}

void Player::screenshotFailed(const QString &fileName, const QString &reason)
{
    const QString text = u"cmd_screenshot failed: "_s + reason;
    m_log.write(text);
    m_writer.console(text, ConsoleCategory::error);
    m_writer.send(MessageType::msg_screenshot, {{"FileName"_L1, fileName}, {"Success"_L1, false}});
}

void Player::webGlProbed(const QString &result)
{
    m_log.write(u"WebGL: "_s + result);
    m_writer.console(u"WebGL: "_s + result);
}

void Player::suspendApplied(bool frozen, bool still)
{
    m_log.write(u"suspended: %1, %2"_s.arg(frozen ? u"page frozen"_s : u"page hidden but not frozen"_s,
                                           still ? u"still frame shown"_s : u"no still frame"_s));
}

void Player::permissionDenied(const QWebEnginePermission &permission)
{
    const char *type = QMetaEnum::fromType<QWebEnginePermission::PermissionType>().valueToKey(
        int(permission.permissionType()));
    refused(u"permission %1 for %2"_s.arg(type ? QString::fromLatin1(type) : u"unknown"_s,
                                         permission.origin().toString()));
}

void Player::refused(const QString &what)
{
    m_log.write(ConsoleLimiter::truncate(u"refused "_s + what));
}

void Player::renderProcessTerminated(int status, int exitCode)
{
    if (m_exiting)
        return;
    // WebEngineView.RenderProcessTerminationStatus
    static constexpr std::array<QStringView, 4> statuses = {u"exited", u"exited abnormally", u"crashed",
                                                            u"was killed"};
    const QStringView what = status >= 0 && status < int(statuses.size()) ? statuses[status] : u"ended";
    fail(u"the web render process %1 (exit code %2)"_s.arg(what).arg(exitCode));
}

void Player::fail(const QString &reason)
{
    if (m_exiting)
        return;
    m_log.write(u"failed: "_s + reason);
    std::fprintf(stderr, "lively-player-web: %s\n", qPrintable(reason));
    m_writer.console(reason, ConsoleCategory::error);
    m_writer.loaded(false);
    quit(1, reason);
}

void Player::quit(int code, const QString &reason)
{
    if (m_exiting)
        return;
    m_exiting = true;
    m_log.write(u"exiting: "_s + reason);
    QCoreApplication::exit(code);
}
