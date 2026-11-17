#pragma once

#include <QFile>
#include <QString>

#include <mutex>

// Optional log file (--web-log): appended, UTF-8, one timestamped line per entry, flushed after each.
// Thread-safe; never throws. Without a path it writes nothing.
class PlayerLog
{
public:
    explicit PlayerLog(const QString &path);

    void write(const QString &text);

private:
    std::mutex m_mutex;
    QFile m_file;
};
