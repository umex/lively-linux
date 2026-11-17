using lively.Common;
using lively.Common.Helpers.Storage;
using lively.Models;

namespace lively.Tests;

public static class WallpaperFixture
{
    /// <summary>
    /// Writes a wallpaper folder (LivelyInfo.json, wallpaper file, thumbnail) and returns its path.
    /// </summary>
    public static string Create(TempDirectory root, string folderName, string title, WallpaperType type = WallpaperType.video)
    {
        var dir = root.CreateDirectory(folderName);
        File.WriteAllText(Path.Combine(dir, "wallpaper.mp4"), "video");
        File.WriteAllText(Path.Combine(dir, "thumb.jpg"), "image");
        JsonStorage<LivelyInfoModel>.StoreData(Path.Combine(dir, "LivelyInfo.json"), new LivelyInfoModel
        {
            Title = title,
            Desc = "description of " + title,
            Type = type,
            FileName = "wallpaper.mp4",
            Thumbnail = "thumb.jpg",
        });
        return dir;
    }

    /// <summary>Console line the <see cref="CreateWeb"/> page logs once it has drawn a frame.</summary>
    public const string WebPageReadyMessage = "fixture page drawing";

    /// <summary>
    /// Writes a web wallpaper folder (LivelyInfo.json without a thumbnail, index.html) and returns its path. The
    /// page animates a WebGL gradient with a highp fragment shader and logs <see cref="WebPageReadyMessage"/>.
    /// </summary>
    public static string CreateWeb(TempDirectory root, string folderName, string title) => CreateWeb(root.Path, folderName, title);

    /// <inheritdoc cref="CreateWeb(TempDirectory, string, string)"/>
    public static string CreateWeb(string parent, string folderName, string title)
    {
        var dir = Path.Combine(parent, folderName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.html"), WebPage);
        JsonStorage<LivelyInfoModel>.StoreData(Path.Combine(dir, "LivelyInfo.json"), new LivelyInfoModel
        {
            Title = title,
            Desc = "web page " + title,
            Type = WallpaperType.web,
            FileName = "index.html",
            Thumbnail = null,
        });
        return dir;
    }

    private const string WebPage = """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <title>lively test page</title>
        <style>html, body { margin: 0; height: 100%; overflow: hidden; background: #000; } canvas { display: block; width: 100vw; height: 100vh; }</style>
        </head>
        <body>
        <canvas id="c"></canvas>
        <script>
        const canvas = document.getElementById('c');
        const gl = canvas.getContext('webgl');
        const vertex = 'attribute vec2 p; void main() { gl_Position = vec4(p, 0.0, 1.0); }';
        const fragment = 'precision highp float; uniform float t; uniform vec2 r;'
            + 'void main() { vec2 uv = gl_FragCoord.xy / r;'
            + ' gl_FragColor = vec4(0.5 + 0.5 * cos(t + uv.x * 3.0), 0.3 + 0.3 * sin(t * 0.7 + uv.y * 4.0), 0.6, 1.0); }';
        function compile(type, source) {
          const shader = gl.createShader(type);
          gl.shaderSource(shader, source);
          gl.compileShader(shader);
          return shader;
        }
        const program = gl.createProgram();
        gl.attachShader(program, compile(gl.VERTEX_SHADER, vertex));
        gl.attachShader(program, compile(gl.FRAGMENT_SHADER, fragment));
        gl.linkProgram(program);
        gl.useProgram(program);
        gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
        const position = gl.getAttribLocation(program, 'p');
        gl.enableVertexAttribArray(position);
        gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0);
        const time = gl.getUniformLocation(program, 't');
        const resolution = gl.getUniformLocation(program, 'r');
        let logged = false;
        function frame(ms) {
          canvas.width = innerWidth * devicePixelRatio;
          canvas.height = innerHeight * devicePixelRatio;
          gl.viewport(0, 0, canvas.width, canvas.height);
          gl.uniform1f(time, ms / 1000);
          gl.uniform2f(resolution, canvas.width, canvas.height);
          gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
          if (!logged) {
            logged = true;
            console.log('fixture page drawing');
          }
          requestAnimationFrame(frame);
        }
        requestAnimationFrame(frame);
        </script>
        </body>
        </html>
        """;
}
