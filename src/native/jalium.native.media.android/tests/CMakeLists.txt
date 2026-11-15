project(jalium.native.media.android.tests)
add_executable(jalium.native.media.android.decoder.tests
    android_video_decoder_tests.cpp
    ../src/and_yuv_scalar.cpp ../src/and_yuv_neon.cpp ../src/and_yuv_sse2.cpp)
target_include_directories(jalium.native.media.android.decoder.tests PRIVATE
    ../include ../src ../../jalium.native.media.core/include)
target_link_libraries(jalium.native.media.android.decoder.tests PRIVATE
    jalium.native.media.core ${CMAKE_DL_LIBS})
set_target_properties(jalium.native.media.android.decoder.tests PROPERTIES ENABLE_EXPORTS ON CXX_VISIBILITY_PRESET default)
if(NOT ANDROID)
    set(JALIUM_ANDROID_NDK_ROOT "$ENV{ANDROID_NDK_ROOT}" CACHE PATH "NDK used for Android decoder host regression declarations")
    set(_ndk_headers "${JALIUM_ANDROID_NDK_ROOT}/toolchains/llvm/prebuilt/linux-x86_64/sysroot/usr/include")
    if(NOT EXISTS "${_ndk_headers}/media/NdkImageReader.h")
        message(FATAL_ERROR "Set JALIUM_ANDROID_NDK_ROOT to run Android decoder host tests with real NDK declarations")
    endif()
    target_compile_options(jalium.native.media.android.decoder.tests PRIVATE
        -include "${CMAKE_CURRENT_SOURCE_DIR}/android_host_compat.h"
        -idirafter "${_ndk_headers}")
endif()
add_test(NAME jalium.native.media.android.decoder COMMAND jalium.native.media.android.decoder.tests)
