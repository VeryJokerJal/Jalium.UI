#define JALIUM_MEDIA_EXPORTS
#include "and_media_init.h"
#include "jalium_media.h"

#include <android/log.h>
#include <jni.h>

#include <cstring>

#define ANDLOG_TAG "jalium.native.media.codec"
#define ANDLOGW(...) __android_log_print(ANDROID_LOG_WARN, ANDLOG_TAG, __VA_ARGS__)
#define ANDLOGE(...) __android_log_print(ANDROID_LOG_ERROR, ANDLOG_TAG, __VA_ARGS__)

extern "C" JNIEnv* jalium_android_get_jni_env(void);

namespace jalium::media::android {

// Probe android.media.MediaCodecList for hardware/software video decoders.
// Returns a bitfield of jalium_video_codec_t values.
uint32_t ProbeSupportedCodecsViaJni()
{
    JNIEnv* env = jalium_android_get_jni_env();
    if (!env) {
        ANDLOGW("ProbeSupportedCodecsViaJni: no JNIEnv (jalium_android_set_jni_env not called)");
        return JALIUM_CODEC_H264;  // Sane fallback
    }

    jclass clsList = env->FindClass("android/media/MediaCodecList");
    if (!clsList || env->ExceptionCheck()) {
        env->ExceptionClear();
        return JALIUM_CODEC_H264;
    }

    jmethodID ctor = env->GetMethodID(clsList, "<init>", "(I)V");
    jmethodID getInfos = env->GetMethodID(clsList, "getCodecInfos", "()[Landroid/media/MediaCodecInfo;");
    if (!ctor || !getInfos) {
        env->DeleteLocalRef(clsList);
        return JALIUM_CODEC_H264;
    }

    // MediaCodecList.REGULAR_CODECS = 0
    jobject listObj = env->NewObject(clsList, ctor, 0);
    if (!listObj || env->ExceptionCheck()) {
        env->ExceptionClear();
        env->DeleteLocalRef(clsList);
        return JALIUM_CODEC_H264;
    }

    jobjectArray infos = static_cast<jobjectArray>(env->CallObjectMethod(listObj, getInfos));
    if (!infos || env->ExceptionCheck()) {
        env->ExceptionClear();
        env->DeleteLocalRef(listObj);
        env->DeleteLocalRef(clsList);
        return JALIUM_CODEC_H264;
    }

    jclass clsInfo = env->FindClass("android/media/MediaCodecInfo");
    jmethodID isEncoder      = env->GetMethodID(clsInfo, "isEncoder", "()Z");
    jmethodID getSupported   = env->GetMethodID(clsInfo, "getSupportedTypes", "()[Ljava/lang/String;");

    uint32_t mask = 0;
    jsize n = env->GetArrayLength(infos);
    for (jsize i = 0; i < n; ++i) {
        jobject info = env->GetObjectArrayElement(infos, i);
        if (!info) continue;
        jboolean encoder = env->CallBooleanMethod(info, isEncoder);
        if (encoder) {
            env->DeleteLocalRef(info);
            continue;
        }
        jobjectArray types = static_cast<jobjectArray>(env->CallObjectMethod(info, getSupported));
        if (types) {
            jsize tn = env->GetArrayLength(types);
            for (jsize j = 0; j < tn; ++j) {
                jstring jstr = static_cast<jstring>(env->GetObjectArrayElement(types, j));
                if (!jstr) continue;
                const char* mime = env->GetStringUTFChars(jstr, nullptr);
                if (mime) {
                    if      (std::strcmp(mime, "video/avc") == 0)         mask |= JALIUM_CODEC_H264;
                    else if (std::strcmp(mime, "video/hevc") == 0)        mask |= JALIUM_CODEC_HEVC;
                    else if (std::strcmp(mime, "video/x-vnd.on2.vp9") == 0) mask |= JALIUM_CODEC_VP9;
                    else if (std::strcmp(mime, "video/av01") == 0)        mask |= JALIUM_CODEC_AV1;
                    env->ReleaseStringUTFChars(jstr, mime);
                }
                env->DeleteLocalRef(jstr);
            }
            env->DeleteLocalRef(types);
        }
        env->DeleteLocalRef(info);
    }

    env->DeleteLocalRef(infos);
    env->DeleteLocalRef(listObj);
    env->DeleteLocalRef(clsList);
    env->DeleteLocalRef(clsInfo);

    if (mask == 0) mask = JALIUM_CODEC_H264;  // Sanity fallback
    return mask;
}

} // namespace jalium::media::android
