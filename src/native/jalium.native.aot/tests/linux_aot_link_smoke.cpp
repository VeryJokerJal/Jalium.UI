#include "jalium_aot.h"

extern "C" {

// The media-link regression deliberately does not pull the renderer archives.
// Their initialization is orthogonal to proving the Linux media archive is a
// complete AOT dependency, so provide inert symbols for this focused fixture.
void jalium_software_init(void) {}
void jalium_vulkan_init(void) {}

}

int main()
{
    // Linking this executable is the regression gate: aot_register.cpp has
    // strong references to jalium_media_initialize and jalium_audio_initialize,
    // which Linux must resolve from jalium.native.media.linux.
    jalium_aot_register_all_backends();
    return 0;
}
