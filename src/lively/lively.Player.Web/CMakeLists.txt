cmake_minimum_required(VERSION 3.21)

project(lively-player-web VERSION 0.1.0 LANGUAGES CXX)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_package(Qt6 6.8 REQUIRED COMPONENTS Core Gui Qml Quick WebEngineCore WebEngineQuick)
find_package(LayerShellQt 6.0 REQUIRED)
find_package(PkgConfig REQUIRED)
pkg_check_modules(WaylandClient REQUIRED IMPORTED_TARGET wayland-client)

qt_standard_project_setup(REQUIRES 6.8)
include(GNUInstallDirs)

qt_add_executable(lively-player-web
    main.cpp
    ConsoleLimiter.h ConsoleLimiter.cpp
    Options.h Options.cpp
    PlayerLog.h PlayerLog.cpp
    PosixInput.h PosixInput.cpp
    Protocol.h Protocol.cpp
    Screenshot.h Screenshot.cpp
    WaylandProbe.h WaylandProbe.cpp
)

qt_add_qml_module(lively-player-web
    URI LivelyPlayerWeb
    VERSION 1.0
    QML_FILES Wallpaper.qml
    SOURCES Player.h Player.cpp
)

target_compile_definitions(lively-player-web PRIVATE
    QT_NO_CAST_FROM_ASCII
    QT_NO_URL_CAST_FROM_STRING
)

target_compile_options(lively-player-web PRIVATE -Wall -Wextra)

target_link_libraries(lively-player-web PRIVATE
    Qt6::Core
    Qt6::Gui
    Qt6::Qml
    Qt6::Quick
    Qt6::WebEngineCore
    Qt6::WebEngineQuick
    LayerShellQt::Interface
    PkgConfig::WaylandClient
)

install(TARGETS lively-player-web RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR})
