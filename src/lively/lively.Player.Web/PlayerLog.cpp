#include "PlayerLog.h"

#include <QDateTime>
#include <QDir>
#include <QFileInfo>

#include <cstdio>

using namespace Qt::StringLiterals;

PlayerLog::PlayerLog(const QString &path)
{
    if (path.isEmpty())
        return;
    QDir().mkpath(QFileInfo(path).absolutePath());
    m_file.setFileName(path);
    if (!m_file.open(QIODevice::WriteOnly | QIODevice::Append)) {
        std::fprintf(stderr, "lively-player-web: cannot open log %s: %s\n", qPrintable(path),
                     qPrintable(m_file.errorString()));
    }
}

void PlayerLog::write(const QString &text)
{
    const std::lock_guard lock(m_mutex);
    if (!m_file.isOpen())
        return;
    const QString line = QDateTime::currentDateTime().toString(u"yyyy-MM-dd HH:mm:ss.zzz"_s) + u' ' + text + u'\n';
    m_file.write(line.toUtf8());
    m_file.flush();
}
