
# Lively Linux   ![Badge Contributions]   [![Badge License]][License]

*Experimental Linux version of **[Lively Wallpaper]**.*

<br>

<div align = center>

<img
    src = 'resources/main.png'
    height = 400
/>

</div>

<br>
<br>

## Progress

The project is in its very early stages and <br>
wallpapers do not work yet, however the <br>
basic user interface works by now.

See the **[Revival Plan]** for the roadmap.

<br>

### Plans

*Everything is subject to change.*

- Language / Runtime : **[.NET 10 SDK]**

- Video Player : **[MPV]** / **[MPV.Net]**

- Interface : **[AvaloniaUI]**

- Web Browser : ***???***

- Desktop : ***KDE ???***

<br>
<br>

## Data

Wallpapers are saved in:

`~/.local/share/lively-wallpaper/library/wallpapers`

*`$XDG_DATA_HOME` is used instead of `~/.local/share` when set.*

<br>
<br>

## Building

1.  Install the **[.NET 10 SDK]**

2. Navigate to `src/lively`

3. Build the project with:

    ```shell
    dotnet build
    ```

4. Run the app with:

    ```shell
    dotnet run --project lively
    ```

5. Run the tests with:

    ```shell
    dotnet test
    ```

<br>


<!----------------------------------------------------------------------------->

[Lively Wallpaper]: https://github.com/rocksdanister/lively
[AvaloniaUI]: https://github.com/AvaloniaUI/Avalonia
[.NET 10 SDK]: https://dotnet.microsoft.com/download
[Revival Plan]: REVIVAL_PLAN.md
[MPV.Net]: https://github.com/mireo91/Mpv.NET-lib-
[MPV]: https://github.com/mpv-player/mpv

[License]: LICENSE


<!---------------------------------[ Badges ]---------------------------------->

[Badge Contributions]: https://img.shields.io/badge/Contributions-Welcome-327c25?style=for-the-badge&logoColor=white&logo=GitHub&labelColor=44A833
[Badge License]: https://img.shields.io/badge/License-GPL_3-02639a.svg?style=for-the-badge&labelColor=blue