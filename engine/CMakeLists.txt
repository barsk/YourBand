cmake_minimum_required(VERSION 3.22)

project(GuitarTutorEngine VERSION 0.1.0 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# JUCE 9.0.4, fetched into third_party/JUCE (see third_party/README.md)
add_subdirectory(third_party/JUCE)

juce_add_gui_app(gt_engine
    PRODUCT_NAME "GuitarTutor Engine"
    COMPANY_NAME "GuitarTutor"
    NEEDS_WEB_BROWSER FALSE
    NEEDS_CURL FALSE)

target_sources(gt_engine
    PRIVATE
        src/Main.cpp
        src/MainComponent.cpp
        src/EngineCore.cpp
        src/Processors.cpp
        src/Footswitch.cpp)

target_compile_definitions(gt_engine
    PRIVATE
        JUCE_WEB_BROWSER=0
        JUCE_USE_CURL=0
        JUCE_ASIO=1                 # bundled Steinberg ASIO headers (Steinberg ASIO licence or GPLv3)
        JUCE_WASAPI=1
        JUCE_DIRECTSOUND=0
        JUCE_PLUGINHOST_VST3=1      # hosts the TONE3000 plugin
        JUCE_DISPLAY_SPLASH_SCREEN=0
        JUCE_APPLICATION_NAME_STRING="$<TARGET_PROPERTY:gt_engine,JUCE_PRODUCT_NAME>"
        JUCE_APPLICATION_VERSION_STRING="$<TARGET_PROPERTY:gt_engine,JUCE_VERSION>")

target_link_libraries(gt_engine
    PRIVATE
        juce::juce_audio_utils
        juce::juce_audio_processors
        winmm                       # footswitch: Windows joystick API
    PUBLIC
        juce::juce_recommended_config_flags
        juce::juce_recommended_lto_flags
        juce::juce_recommended_warning_flags)
