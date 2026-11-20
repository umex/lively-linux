#include "ConsoleLimiter.h"

#include <algorithm>
#include <utility>

bool ConsoleLimiter::allow(qint64 nowMs)
{
    if (m_lastMs >= 0)
        m_tokens = std::min<double>(Burst, m_tokens + double(nowMs - m_lastMs) * PerSecond / 1000.0);
    m_lastMs = nowMs;
    if (m_tokens >= 1.0) {
        m_tokens -= 1.0;
        return true;
    }
    ++m_suppressed;
    return false;
}

int ConsoleLimiter::takeSuppressed()
{
    return std::exchange(m_suppressed, 0);
}

QString ConsoleLimiter::truncate(const QString &text)
{
    if (text.size() <= MaxLength)
        return text;
    qsizetype keep = MaxLength - 1;
    if (text.at(keep - 1).isHighSurrogate())
        --keep;
    return text.first(keep) + QChar(0x2026);
}
