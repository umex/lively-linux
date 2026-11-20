#pragma once

#include <QByteArray>
#include <QObject>
#include <QSocketNotifier>

// Reads lines from stdin on the event loop. Works for pipes, FIFOs, terminals and /dev/null; each
// notification does one read(), which cannot block because the descriptor is readable, so stdin stays in
// blocking mode (O_NONBLOCK would leak into whoever shares the open file description).
class StdinReader : public QObject
{
    Q_OBJECT

public:
    explicit StdinReader(QObject *parent = nullptr);

signals:
    void lineReceived(const QByteArray &line);
    void discarded(const QString &reason);
    // End of file or a read error; emitted once.
    void closed(const QString &reason);

private:
    void readAvailable();

    QSocketNotifier m_notifier;
    QByteArray m_buffer;
};

// Turns SIGTERM and SIGINT into a Qt signal through a self-pipe: the handler only write()s the signal
// number, which is async-signal-safe.
class SignalNotifier : public QObject
{
    Q_OBJECT

public:
    explicit SignalNotifier(QObject *parent = nullptr);
    ~SignalNotifier() override;

    bool isValid() const { return m_notifier != nullptr; }

signals:
    void received(const QString &name);

private:
    void readSignal();

    int m_fds[2] = {-1, -1};
    QSocketNotifier *m_notifier = nullptr;
};
