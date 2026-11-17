#include "WaylandProbe.h"

#include <wayland-client.h>

#include <cerrno>
#include <cstring>

using namespace Qt::StringLiterals;

namespace {

void onGlobal(void *data, wl_registry *, uint32_t, const char *interface, uint32_t)
{
    if (std::strcmp(interface, "zwlr_layer_shell_v1") == 0)
        *static_cast<bool *>(data) = true;
}

void onGlobalRemove(void *, wl_registry *, uint32_t) { }

constexpr wl_registry_listener registryListener = {.global = onGlobal, .global_remove = onGlobalRemove};

}  // namespace

QString probeWaylandLayerShell()
{
    const QByteArray displayName = qgetenv("WAYLAND_DISPLAY");
    if (qEnvironmentVariableIsSet("WAYLAND_SOCKET")) {
        // An inherited socket can be connected only once; leave it to Qt.
        return {};
    }
    if (displayName.isEmpty())
        return u"not a Wayland session: WAYLAND_DISPLAY is not set"_s;

    wl_display *display = wl_display_connect(nullptr);
    if (!display) {
        return u"cannot connect to the Wayland display %1: %2"_s.arg(QString::fromLocal8Bit(displayName),
                                                                     QString::fromLocal8Bit(std::strerror(errno)));
    }
    bool layerShell = false;
    wl_registry *registry = wl_display_get_registry(display);
    wl_registry_add_listener(registry, &registryListener, &layerShell);
    const int result = wl_display_roundtrip(display);
    wl_registry_destroy(registry);
    wl_display_disconnect(display);

    if (result < 0)
        return u"the Wayland connection to %1 failed"_s.arg(QString::fromLocal8Bit(displayName));
    if (!layerShell)
        return u"the compositor does not support zwlr_layer_shell_v1 (wlr-layer-shell)"_s;
    return {};
}
