#include "PosixInput.h"

#include <cerrno>
#include <csignal>
#include <cstring>

#include <fcntl.h>
#include <unistd.h>

using namespace Qt::StringLiterals;

namespace {

// Lines from the core are small; this only bounds memory if something writes garbage without newlines.
constexpr qsizetype MaxLineLength = 1 << 20;

}  // namespace

StdinReader::StdinReader(QObject *parent)
    : QObject(parent), m_notifier(STDIN_FILENO, QSocketNotifier::Read)
{
    connect(&m_notifier, &QSocketNotifier::activated, this, &StdinReader::readAvailable);
}

void StdinReader::readAvailable()
{
    char chunk[16384];
    const ssize_t n = ::read(STDIN_FILENO, chunk, sizeof chunk);
    if (n < 0 && (errno == EINTR || errno == EAGAIN))
        return;
    if (n <= 0) {
        m_notifier.setEnabled(false);
        emit closed(n == 0 ? u"stdin closed"_s
                           : u"stdin read error: "_s + QString::fromLocal8Bit(std::strerror(errno)));
        return;
    }

    m_buffer.append(chunk, n);
    qsizetype start = 0;
    for (qsizetype end; (end = m_buffer.indexOf('\n', start)) >= 0; start = end + 1) {
        const QByteArray line = m_buffer.sliced(start, end - start).trimmed();
        if (!line.isEmpty())
            emit lineReceived(line);
    }
    m_buffer.remove(0, start);
    if (m_buffer.size() > MaxLineLength) {
        m_buffer.clear();
        emit discarded(u"discarded a line longer than %1 bytes"_s.arg(MaxLineLength));
    }
}

namespace {

// Written once before the handlers are installed; read by the handler.
int signalWriteFd = -1;

extern "C" void onSignal(int signo)
{
    const int saved = errno;
    const unsigned char byte = static_cast<unsigned char>(signo);
    [[maybe_unused]] const ssize_t ignored = ::write(signalWriteFd, &byte, 1);
    errno = saved;
}

}  // namespace

SignalNotifier::SignalNotifier(QObject *parent) : QObject(parent)
{
    if (::pipe2(m_fds, O_CLOEXEC | O_NONBLOCK) != 0)
        return;
    signalWriteFd = m_fds[1];

    struct sigaction action = {};
    action.sa_handler = onSignal;
    sigemptyset(&action.sa_mask);
    action.sa_flags = SA_RESTART;
    sigaction(SIGTERM, &action, nullptr);
    sigaction(SIGINT, &action, nullptr);

    m_notifier = new QSocketNotifier(m_fds[0], QSocketNotifier::Read, this);
    connect(m_notifier, &QSocketNotifier::activated, this, &SignalNotifier::readSignal);
}

SignalNotifier::~SignalNotifier()
{
    if (m_fds[0] < 0)
        return;
    std::signal(SIGTERM, SIG_DFL);
    std::signal(SIGINT, SIG_DFL);
    signalWriteFd = -1;
    ::close(m_fds[0]);
    ::close(m_fds[1]);
}

void SignalNotifier::readSignal()
{
    unsigned char byte = 0;
    while (::read(m_fds[0], &byte, 1) == 1) {
        emit received(byte == SIGTERM ? u"SIGTERM"_s : byte == SIGINT ? u"SIGINT"_s : u"signal %1"_s.arg(byte));
    }
}
