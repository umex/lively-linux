import QtQuick
import QtWebEngine

// The wallpaper surface: a full-window WebEngineView, and a still frame that replaces it while suspended.
// Player (C++) places the window on its output as a layer-shell background surface and shows it.
Window {
    id: root

    required property Player player
    required property WebEngineProfile profile

    // Bumped on every suspend and resume, so a grab that finishes after a resume is dropped.
    property int generation: 0

    color: "black"
    flags: Qt.FramelessWindowHint | Qt.WindowDoesNotAcceptFocus
    visible: false

    function suspend() {
        const current = ++generation;
        stillRelease.stop();
        const grabbing = view.grabToImage(result => {
            if (current === generation)
                freeze(result.url);
        });
        if (!grabbing)
            freeze("");
    }

    // Qt only accepts the Frozen lifecycle state on a view that is not visible.
    function freeze(stillUrl) {
        still.source = stillUrl;
        still.visible = stillUrl !== "";
        view.visible = false;
        view.lifecycleState = WebEngineView.LifecycleState.Frozen;
        player.suspendApplied(view.lifecycleState === WebEngineView.LifecycleState.Frozen, still.visible);
    }

    function resume() {
        ++generation;
        view.lifecycleState = WebEngineView.LifecycleState.Active;
        view.visible = true;
        // The view needs a moment for its first new frame; the still covers it until then.
        stillRelease.restart();
    }

    Connections {
        target: root.player

        function onSuspendedChanged() {
            if (root.player.suspended)
                root.suspend();
            else
                root.resume();
        }

        function onLoadRequested(url) {
            view.url = url;
        }

        function onReloadRequested() {
            view.reload();
        }
    }

    WebEngineView {
        id: view

        // Reports the WebGL context the page would get and the unmasked GL renderer. Runs in the application
        // world, so the page's own scripts cannot interfere.
        readonly property string webGlProbe: `(() => {
            try {
                const canvas = document.createElement('canvas');
                let kind = 'webgl2';
                let gl = canvas.getContext(kind);
                if (!gl) { kind = 'webgl'; gl = canvas.getContext(kind); }
                if (!gl) return 'no context';
                const info = gl.getExtension('WEBGL_debug_renderer_info');
                const renderer = info ? gl.getParameter(info.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
                const result = kind + ', ' + gl.getParameter(gl.VERSION) + ', renderer ' + renderer;
                const lose = gl.getExtension('WEBGL_lose_context');
                if (lose) lose.loseContext();
                return result;
            } catch (e) {
                return 'probe failed: ' + e;
            }
        })()`

        anchors.fill: parent
        profile: root.profile
        backgroundColor: "black"
        audioMuted: root.player.muted || root.player.suspended
        activeFocusOnPress: false

        settings.webGLEnabled: true
        settings.accelerated2dCanvasEnabled: true
        settings.playbackRequiresUserGesture: false
        settings.localContentCanAccessRemoteUrls: true
        settings.localContentCanAccessFileUrls: true
        settings.javascriptCanOpenWindows: false
        settings.allowWindowActivationFromJavaScript: false
        settings.focusOnNavigationEnabled: false
        settings.fullScreenSupportEnabled: false
        settings.screenCaptureEnabled: false
        settings.navigateOnDropEnabled: false
        settings.unknownUrlSchemePolicy: WebEngineSettings.DisallowUnknownUrlSchemes

        onLoadingChanged: info => {
            root.player.pageLoadingChanged(info);
            if (info.status === WebEngineLoadingInfo.LoadSucceededStatus) {
                runJavaScript(webGlProbe, WebEngineScript.ApplicationWorld,
                              result => root.player.webGlProbed(String(result)));
            }
        }

        onJavaScriptConsoleMessage: (level, message, lineNumber, sourceID) => {
            root.player.pageConsoleMessage(level, message, lineNumber, sourceID);
        }

        onRenderProcessTerminated: (terminationStatus, exitCode) => {
            root.player.renderProcessTerminated(terminationStatus, exitCode);
        }

        // Everything a page can ask for is refused. Handlers that set accepted take over the request, so Qt
        // shows no dialog or menu of its own.

        onJavaScriptDialogRequested: request => {
            request.accepted = true;
            // Rejecting "leave page?" would also block cmd_reload.
            if (request.type === JavaScriptDialogRequest.DialogTypeBeforeUnload)
                request.dialogAccept();
            else
                request.dialogReject();
            root.player.refused("JavaScript dialog: " + request.message);
        }

        onNewWindowRequested: request => {
            // Not calling openIn() refuses it.
            root.player.refused("new window for " + request.requestedUrl);
        }

        onPermissionRequested: permission => {
            permission.deny();
            root.player.permissionDenied(permission);
        }

        onFileDialogRequested: request => {
            request.accepted = true;
            request.dialogReject();
            root.player.refused("file dialog");
        }

        onAuthenticationDialogRequested: request => {
            request.accepted = true;
            request.dialogReject();
            root.player.refused("authentication for " + request.url);
        }

        onColorDialogRequested: request => {
            request.accepted = true;
            request.dialogReject();
            root.player.refused("colour dialog");
        }

        onContextMenuRequested: request => {
            request.accepted = true;
        }

        onTooltipRequested: request => {
            request.accepted = true;
        }

        onTouchSelectionMenuRequested: request => {
            request.accepted = true;
        }

        onFullScreenRequested: request => {
            request.reject();
            root.player.refused("full screen");
        }

        onRegisterProtocolHandlerRequested: request => {
            request.reject();
            root.player.refused("protocol handler " + request.scheme);
        }

        onFileSystemAccessRequested: request => {
            request.reject();
            root.player.refused("file system access to " + request.filePath);
        }

        onDesktopMediaRequested: request => {
            request.cancel();
            root.player.refused("screen sharing");
        }

        onWebAuthUxRequested: request => {
            request.cancel();
            root.player.refused("web authentication");
        }

        onSelectClientCertificate: selection => {
            selection.selectNone();
            root.player.refused("client certificate for " + selection.host);
        }

        onCertificateError: error => {
            error.rejectCertificate();
            root.player.refused("certificate for " + error.url + ": " + error.description);
        }

        onWindowCloseRequested: {
            root.player.refused("window.close()");
        }
    }

    Image {
        id: still

        anchors.fill: parent
        visible: false
        cache: false
        fillMode: Image.Stretch
    }

    Timer {
        id: stillRelease

        interval: 250
        onTriggered: {
            still.visible = false;
            still.source = "";
        }
    }
}
