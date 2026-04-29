#define JALIUM_MEDIA_EXPORTS
#include "and_image_decoder.h"
#include "and_media_init.h"
#include "jalium_media_internal.h"

#include <android/bitmap.h>
#include <android/log.h>
#include <jni.h>

#include <atomic>
#include <cstring>
#include <mutex>

#define ANDLOG_TAG "jalium.native.media.image.jni"
#define ANDLOGW(...) __android_log_print(ANDROID_LOG_WARN, ANDLOG_TAG, __VA_ARGS__)
#define ANDLOGE(...) __android_log_print(ANDROID_LOG_ERROR, ANDLOG_TAG, __VA_ARGS__)

// Exports from jalium.native.platform.
extern "C" JNIEnv* jalium_android_get_jni_env(void);

namespace jalium::media::android {

namespace {

std::mutex g_jniCacheMutex;
jclass     g_BitmapFactoryClass    = nullptr;   // global ref
jmethodID  g_decodeByteArray       = nullptr;   // signature with Options
jclass     g_BitmapClass           = nullptr;   // global ref (for recycle)
jmethodID  g_recycleMethod         = nullptr;
jclass     g_OptionsClass          = nullptr;   // global ref (BitmapFactory$Options)
jmethodID  g_OptionsCtor           = nullptr;
jfieldID   g_OptionsInPreferredCfg = nullptr;
jobject    g_ConfigArgb8888        = nullptr;   // global ref to Bitmap$Config.ARGB_8888

bool EnsureJniCache(JNIEnv* env)
{
    std::lock_guard<std::mutex> lock(g_jniCacheMutex);
    if (g_BitmapFactoryClass) return true;

    auto findGlobal = [&](const char* name, jclass* out) -> bool {
        jclass local = env->FindClass(name);
        if (!local || env->ExceptionCheck()) {
            env->ExceptionClear();
            ANDLOGE("Failed to find %s", name);
            return false;
        }
        *out = static_cast<jclass>(env->NewGlobalRef(local));
        env->DeleteLocalRef(local);
        return *out != nullptr;
    };

    if (!findGlobal("android/graphics/BitmapFactory", &g_BitmapFactoryClass)) return false;
    if (!findGlobal("android/graphics/Bitmap", &g_BitmapClass)) return false;
    if (!findGlobal("android/graphics/BitmapFactory$Options", &g_OptionsClass)) return false;

    // Decode method that accepts Options so we can force RGBA_8888 output.
    g_decodeByteArray = env->GetStaticMethodID(
        g_BitmapFactoryClass,
        "decodeByteArray",
        "([BIILandroid/graphics/BitmapFactory$Options;)Landroid/graphics/Bitmap;");
    if (!g_decodeByteArray || env->ExceptionCheck()) {
        env->ExceptionClear();
        ANDLOGE("Failed to find BitmapFactory.decodeByteArray(Options)");
        return false;
    }

    g_recycleMethod = env->GetMethodID(g_BitmapClass, "recycle", "()V");

    g_OptionsCtor = env->GetMethodID(g_OptionsClass, "<init>", "()V");
    g_OptionsInPreferredCfg = env->GetFieldID(g_OptionsClass, "inPreferredConfig",
                                              "Landroid/graphics/Bitmap$Config;");

    // Resolve Bitmap$Config.ARGB_8888 (static enum constant).
    jclass localCfg = env->FindClass("android/graphics/Bitmap$Config");
    if (localCfg && !env->ExceptionCheck()) {
        jfieldID fid = env->GetStaticFieldID(localCfg, "ARGB_8888", "Landroid/graphics/Bitmap$Config;");
        if (fid && !env->ExceptionCheck()) {
            jobject local = env->GetStaticObjectField(localCfg, fid);
            if (local) {
                g_ConfigArgb8888 = env->NewGlobalRef(local);
                env->DeleteLocalRef(local);
            }
        }
        env->DeleteLocalRef(localCfg);
    }
    env->ExceptionClear();
    return true;
}

// Build a fresh BitmapFactory.Options with inPreferredConfig=ARGB_8888.
// Returns a local ref the caller must delete; nullptr on failure.
jobject CreateOptionsArgb8888(JNIEnv* env)
{
    if (!g_OptionsClass || !g_OptionsCtor) return nullptr;
    jobject opts = env->NewObject(g_OptionsClass, g_OptionsCtor);
    if (!opts || env->ExceptionCheck()) {
        env->ExceptionClear();
        return nullptr;
    }
    if (g_OptionsInPreferredCfg && g_ConfigArgb8888) {
        env->SetObjectField(opts, g_OptionsInPreferredCfg, g_ConfigArgb8888);
    }
    return opts;
}

} // anonymous

jalium_media_status_t DecodeImageMemoryViaJni(
    const uint8_t*        data,
    size_t                size,
    jalium_pixel_format_t requested_format,
    jalium_image_t*       out_image)
{
    if (!IsInitialized()) return JALIUM_MEDIA_E_NOT_INITIALIZED;
    if (!data || size == 0 || !out_image) return JALIUM_MEDIA_E_INVALID_ARG;
    if (size > static_cast<size_t>(INT32_MAX)) return JALIUM_MEDIA_E_INVALID_ARG;

    *out_image = {};

    JNIEnv* env = jalium_android_get_jni_env();
    if (!env) {
        ANDLOGW("DecodeImageMemoryViaJni: no JNIEnv (jalium_android_set_jni_env not called)");
        return JALIUM_MEDIA_E_PLATFORM;
    }

    if (!EnsureJniCache(env)) {
        return JALIUM_MEDIA_E_PLATFORM;
    }

    jbyteArray jbytes = env->NewByteArray(static_cast<jsize>(size));
    if (!jbytes || env->ExceptionCheck()) {
        env->ExceptionClear();
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    env->SetByteArrayRegion(jbytes, 0, static_cast<jsize>(size),
                            reinterpret_cast<const jbyte*>(data));
    if (env->ExceptionCheck()) {
        env->ExceptionClear();
        env->DeleteLocalRef(jbytes);
        return JALIUM_MEDIA_E_PLATFORM;
    }

    jobject opts = CreateOptionsArgb8888(env);
    jobject bitmap = env->CallStaticObjectMethod(
        g_BitmapFactoryClass, g_decodeByteArray,
        jbytes, 0, static_cast<jint>(size), opts);
    env->DeleteLocalRef(jbytes);
    if (opts) env->DeleteLocalRef(opts);
    if (env->ExceptionCheck()) {
        env->ExceptionClear();
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }
    if (!bitmap) {
        return JALIUM_MEDIA_E_DECODE_FAILED;
    }

    AndroidBitmapInfo info{};
    if (AndroidBitmap_getInfo(env, bitmap, &info) != ANDROID_BITMAP_RESULT_SUCCESS) {
        env->CallVoidMethod(bitmap, g_recycleMethod);
        env->ExceptionClear();
        env->DeleteLocalRef(bitmap);
        return JALIUM_MEDIA_E_PLATFORM;
    }

    if (info.format != ANDROID_BITMAP_FORMAT_RGBA_8888) {
        ANDLOGW("Unexpected bitmap format %d (expected RGBA_8888)", info.format);
        env->CallVoidMethod(bitmap, g_recycleMethod);
        env->DeleteLocalRef(bitmap);
        return JALIUM_MEDIA_E_UNSUPPORTED_FORMAT;
    }

    void* srcPixels = nullptr;
    if (AndroidBitmap_lockPixels(env, bitmap, &srcPixels) != ANDROID_BITMAP_RESULT_SUCCESS || !srcPixels) {
        env->CallVoidMethod(bitmap, g_recycleMethod);
        env->DeleteLocalRef(bitmap);
        return JALIUM_MEDIA_E_PLATFORM;
    }

    const uint32_t width  = info.width;
    const uint32_t height = info.height;
    const uint32_t stride = info.stride;
    const size_t   size_bytes = static_cast<size_t>(stride) * height;

    auto* pixels = static_cast<uint8_t*>(jalium_media_aligned_alloc(size_bytes));
    if (!pixels) {
        AndroidBitmap_unlockPixels(env, bitmap);
        env->CallVoidMethod(bitmap, g_recycleMethod);
        env->DeleteLocalRef(bitmap);
        return JALIUM_MEDIA_E_OUT_OF_MEMORY;
    }
    std::memcpy(pixels, srcPixels, size_bytes);
    AndroidBitmap_unlockPixels(env, bitmap);
    env->CallVoidMethod(bitmap, g_recycleMethod);
    env->DeleteLocalRef(bitmap);

    if (requested_format == JALIUM_PF_BGRA8) {
        jalium_media_swap_rb_inplace(pixels, width, height, stride);
    }

    out_image->width        = width;
    out_image->height       = height;
    out_image->stride_bytes = stride;
    out_image->format       = requested_format;
    out_image->pixels       = pixels;
    out_image->_reserved    = nullptr;
    return JALIUM_MEDIA_OK;
}

} // namespace jalium::media::android
