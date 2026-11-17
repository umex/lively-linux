#pragma once

#include <QString>
#include <QtGlobal>

// Rate limit for the page's console messages, which go to msg_console and the log: a token bucket of
// Burst messages that refills at PerSecond. The player's own messages never pass through it.
class ConsoleLimiter
{
public:
    static constexpr int Burst = 20;
    static constexpr int PerSecond = 5;
    // Longer messages are cut and end with an ellipsis.
    static constexpr qsizetype MaxLength = 4096;

    // Whether a message at nowMs (a monotonic clock) may pass; counts it as suppressed otherwise.
    bool allow(qint64 nowMs);

    // Messages suppressed since the last call.
    int takeSuppressed();

    static QString truncate(const QString &text);

private:
    double m_tokens = Burst;
    qint64 m_lastMs = -1;
    int m_suppressed = 0;
};
