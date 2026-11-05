cmake_minimum_required(VERSION 3.25)
cmake_policy(SET CMP0141 NEW)
# Embedded debug information avoids requiring mspdbsrv during compilation.
set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT Embedded)
project(Promzona VERSION 0.1.0 LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
# One target graph for both the command-line build and the generated VS solution.
set_property(GLOBAL PROPERTY USE_FOLDERS ON)
set_property(GLOBAL PROPERTY PREDEFINED_TARGETS_FOLDER Build)
if(MSVC)
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  add_compile_options(/W4 /permissive- /utf-8 /EHsc)
  if(CMAKE_GENERATOR MATCHES "^Visual Studio")
    add_compile_options(/MP4)
  endif()
  add_compile_definitions(NOMINMAX WIN32_LEAN_AND_MEAN UNICODE _UNICODE)
endif()
add_library(promzona_core src/core.cpp src/physics.cpp src/layout.cpp src/road_levels.cpp src/surfaces.cpp src/city_mesh.cpp src/world.cpp src/combat.cpp src/animation.cpp src/save.cpp src/audio.cpp)
target_sources(promzona_core PRIVATE
  include/promzona/animation.hpp include/promzona/audio.hpp
  include/promzona/city_mesh.hpp include/promzona/combat.hpp
  include/promzona/core.hpp include/promzona/layout.hpp include/promzona/road_levels.hpp
  include/promzona/math.hpp include/promzona/physics.hpp include/promzona/world.hpp)
set_property(TARGET promzona_core PROPERTY FOLDER Libraries)
target_include_directories(promzona_core PUBLIC include PRIVATE third_party)
option(BUILD_TESTING "Build PROMZONA native tests" ON)
if(BUILD_TESTING)
  enable_testing()
  add_executable(promzona_level_tests tests/levels.cpp)
  target_link_libraries(promzona_level_tests PRIVATE promzona_core)
  add_test(NAME native_levels COMMAND promzona_level_tests)
  add_executable(promzona_traffic_tests tests/traffic.cpp)
  target_link_libraries(promzona_traffic_tests PRIVATE promzona_core)
  add_test(NAME native_traffic COMMAND promzona_traffic_tests)
  add_executable(promzona_layout_tests tests/layout.cpp)
  target_link_libraries(promzona_layout_tests PRIVATE promzona_core)
  add_test(NAME native_layout COMMAND promzona_layout_tests "${CMAKE_SOURCE_DIR}/tests/layout-reference.txt")
  add_executable(promzona_tests tests/tests.cpp)
  target_link_libraries(promzona_tests PRIVATE promzona_core)
  add_test(NAME native_core COMMAND promzona_tests "${CMAKE_SOURCE_DIR}/tests/vehicle-reference.txt")
  add_executable(promzona_combat_tests tests/combat.cpp)
  target_link_libraries(promzona_combat_tests PRIVATE promzona_core)
  add_test(NAME native_combat COMMAND promzona_combat_tests)
  set_property(TARGET promzona_tests PROPERTY VS_DEBUGGER_COMMAND_ARGUMENTS
    "\"${CMAKE_SOURCE_DIR}/tests/vehicle-reference.txt\"")
  set_property(TARGET promzona_layout_tests PROPERTY VS_DEBUGGER_COMMAND_ARGUMENTS
    "\"${CMAKE_SOURCE_DIR}/tests/layout-reference.txt\"")
  set_target_properties(promzona_level_tests promzona_tests promzona_traffic_tests promzona_layout_tests promzona_combat_tests
    PROPERTIES FOLDER Tests VS_DEBUGGER_WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}")
endif()
if(WIN32)
  enable_language(RC)
  add_library(promzona_assets src/assets.cpp)
  target_sources(promzona_assets PRIVATE include/promzona/assets.hpp)
  set_property(TARGET promzona_assets PROPERTY FOLDER Libraries)
  target_include_directories(promzona_assets PUBLIC include PRIVATE third_party)
  target_link_libraries(promzona_assets PUBLIC windowscodecs ole32)
  if(BUILD_TESTING)
    add_executable(promzona_asset_tests tests/assets.cpp)
    target_link_libraries(promzona_asset_tests PRIVATE promzona_assets promzona_core)
    add_test(NAME native_assets COMMAND promzona_asset_tests "${CMAKE_SOURCE_DIR}/Content")
    set_target_properties(promzona_asset_tests PROPERTIES
      FOLDER Tests
      VS_DEBUGGER_WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
      VS_DEBUGGER_COMMAND_ARGUMENTS "\"${CMAKE_SOURCE_DIR}/Content\"")
  endif()
  add_executable(Promzona WIN32 src/main.cpp src/renderer.cpp src/audio_device.cpp src/promzona.rc src/app.manifest)
  set(native_shaders
    Content/Shaders/Native/scene.vert Content/Shaders/Native/scene.frag
    Content/Shaders/Native/ui.vert Content/Shaders/Native/ui.frag)
  # GLSL is loaded by the game; listing it here is for editing, not MSBuild compilation.
  set_source_files_properties(${native_shaders} PROPERTIES HEADER_FILE_ONLY TRUE)
  target_sources(Promzona PRIVATE include/promzona/renderer.hpp include/promzona/gl.hpp
    ${native_shaders} Content/Promzona.ico)
  target_include_directories(Promzona PRIVATE third_party)
  target_link_libraries(Promzona PRIVATE promzona_core promzona_assets opengl32 shell32 winmm)
  target_compile_definitions(Promzona PRIVATE PROMZONA_SOURCE_CONTENT=L"${CMAKE_SOURCE_DIR}/Content")
  set_property(DIRECTORY PROPERTY VS_STARTUP_PROJECT Promzona)
  set_target_properties(Promzona PROPERTIES
    FOLDER Game
    VS_DEBUGGER_WORKING_DIRECTORY "${CMAKE_SOURCE_DIR}"
    VS_DEBUGGER_COMMAND_ARGUMENTS "--windowed --content \"${CMAKE_SOURCE_DIR}/Content\"")
  install(TARGETS Promzona RUNTIME DESTINATION .)
  install(DIRECTORY Content/ DESTINATION Content PATTERN "*.py" EXCLUDE PATTERN "*.cs" EXCLUDE)
  install(FILES THIRD-PARTY.txt DESTINATION .)
  install(FILES docs/RUNTIME.txt DESTINATION . RENAME README.txt)
endif()

# Keep the disk layout visible in Solution Explorer; do not duplicate source lists.
get_property(promzona_targets DIRECTORY PROPERTY BUILDSYSTEM_TARGETS)
foreach(target IN LISTS promzona_targets)
  get_target_property(target_sources ${target} SOURCES)
  if(target_sources)
    source_group(TREE "${CMAKE_CURRENT_SOURCE_DIR}" FILES ${target_sources})
  endif()
endforeach()
