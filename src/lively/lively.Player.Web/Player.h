#pragma once

#include "ConsoleLimiter.h"
#include "Options.h"
#include "Screenshot.h"

#include <QElapsedTimer>
#include <QObject>
#include <QPointer>
#include <QQmlEngine>
#include <QQuickWindow>
#include <QTimer>
#include <QWebEngineLoadingInfo>
#include <QWebEnginePermission>

#include <memory>
#include <optional>

class MessageWriter;
class PlayerLog;
class QQuickWebEngineProfile;
class QScreen;
class SignalNotifier;
class StdinReader;
struct Command;

// One web wallpaper on one output: owns the layer-shell window (Wallpaper.qml), its WebEngine profile and the
// protocol state. Wallpaper.qml does the page mechanics and refuses whatever the page asks for; it reports
// back through the invokable methods, and follows the muted/suspended properties and the load signals.
class Player : public QObject
{
    Q_OBJECT
    QML_ELEMENT
    QML_UNCREATABLE("Created by main()")

    Q_PROPERTY(bool muted READ muted NOTIFY mutedChanged FINAL)
    Q_PROPERTY(bool suspended READ suspended NOTIFY suspendedChanged FINAL)

public:
    Player(const Options &options, MessageWriter &writer, PlayerLog &log, QObject *parent = nullptr);
    ~Player() override;

    // Places the window on the output and starts loading. Returns false after reporting a failure.
    bool start();

    bool muted() const { return m_volume == 0; }
    // Effective state: a cmd_suspend that arrives before the wallpaper loaded takes effect once it has.
    bool suspended() const { return m_suspended; }

    Q_INVOKABLE void pageLoadingChanged(const QWebEngineLoadingInfo &info);
    Q_INVOKABLE void pageConsoleMessage(int level, const QString &message, int line, const QString &source);
    Q_INVOKABLE void webGlProbed(const QString &result);
    Q_INVOKABLE void suspendApplied(bool frozen, bool still);
    Q_INVOKABLE void permissionDenied(const QWebEnginePermission &permission);
    Q_INVOKABLE void refused(const QString &what);
    Q_INVOKABLE void renderProcessTerminated(int status, int exitCode);

signals:
    void mutedChanged();
    void suspendedChanged();
    void loadRequested(const QUrl &url);
    void reloadRequested();

private:
    enum class PendingLoad { None, Reload, Retry };

    QScreen *findScreen();
    bool createProfile();
    bool createWindow(QScreen *screen);
    void handleLine(const QByteArray &line);
    void handleCommand(const Command &command);
    void onFirstFrame();
    void markReady();
    void updateSuspended();
    void load(PendingLoad kind);
    void retryLater(const QString &reason);
    void requestScreenshot(const Command &command);
    void armScreenshot();
    void takeScreenshot();
    void screenshotFailed(const QString &fileName, const QString &reason);
    void reportSuppressedConsole();
    void fail(const QString &reason);
    void quit(int code, const QString &reason);

    const Options m_options;
    MessageWriter &m_writer;
    PlayerLog &m_log;

    // Released in reverse order by ~Player: pages must be gone before their profile.
    std::unique_ptr<QQmlEngine> m_engine;
    std::unique_ptr<QObject> m_profileOwner;  // the WebEngineProfilePrototype that owns the profile
    QQuickWebEngineProfile *m_profile = nullptr;
    std::unique_ptr<QQuickWindow> m_window;
    QPointer<QScreen> m_screen;
    StdinReader *m_stdin = nullptr;
    SignalNotifier *m_signals = nullptr;
    QMetaObject::Connection m_firstFrame;
    QTimer m_retryTimer;
    QTimer m_closedTimer;
    QTimer m_screenshotTimer;
    QTimer m_suppressedTimer;
    QElapsedTimer m_clock;
    QElapsedTimer m_contentSince;
    ConsoleLimiter m_consoleLimiter;
    std::optional<ScreenshotRequest> m_screenshot;  // waiting for its delay or for a successful load

    int m_volume;
    bool m_surfaceShown = false;
    bool m_pageLoaded = false;     // some load succeeded
    bool m_contentLoaded = false;  // the last load succeeded: the view shows the page, not an error page
    bool m_ready = false;
    bool m_suspendRequested = false;
    bool m_suspended = false;
    bool m_exiting = false;
    PendingLoad m_pendingLoad = PendingLoad::None;
};
