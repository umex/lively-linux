#pragma once

#include <QString>

// Checks on a short-lived connection of its own, before Qt starts, that a Wayland compositor is reachable
// and offers zwlr_layer_shell_v1. Qt would otherwise abort without a usable reason, or show a normal
// window instead of a background surface. Returns the failure reason, or an empty string.
QString probeWaylandLayerShell();
