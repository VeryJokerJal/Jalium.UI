#pragma once

#if JALIUM_HAS_GSTREAMER

#include <gst/app/gstappsink.h>
#include <gst/app/gstappsrc.h>
#include <gst/audio/audio.h>
#include <gst/allocators/gstdmabuf.h>
#include <gst/gst.h>
#include <gst/pbutils/pbutils.h>
#include <gst/video/video.h>

// Keep the public Linux media library free of DT_NEEDED entries for optional
// GStreamer/GLib components.  The build still consumes the development headers
// for ABI-correct types, while every function below is resolved after dlopen.
#define JALIUM_GSTREAMER_FUNCTIONS(X) \
    X(g_error_free) \
    X(g_free) \
    X(g_list_free_full) \
    X(g_list_length) \
    X(g_object_get) \
    X(g_object_set) \
    X(g_object_unref) \
    X(g_signal_connect_data) \
    X(g_str_has_prefix) \
    X(gst_app_sink_is_eos) \
    X(gst_app_sink_try_pull_sample) \
    X(gst_app_src_end_of_stream) \
    X(gst_app_src_push_buffer) \
    X(gst_audio_info_from_caps) \
    X(gst_bin_add_many) \
    X(gst_buffer_fill) \
    X(gst_buffer_find_memory) \
    X(gst_buffer_get_video_meta) \
    X(gst_buffer_map) \
    X(gst_buffer_n_memory) \
    X(gst_buffer_new_allocate) \
    X(gst_buffer_peek_memory) \
    X(gst_buffer_unmap) \
    X(gst_bus_pop_filtered) \
    X(gst_bus_timed_pop_filtered) \
    X(gst_caps_get_size) \
    X(gst_caps_get_structure) \
    X(gst_caps_get_features) \
    X(gst_caps_features_contains) \
    X(gst_caps_from_string) \
    X(gst_caps_new_empty_simple) \
    X(gst_caps_new_simple) \
    X(gst_device_create_element) \
    X(gst_device_get_caps) \
    X(gst_device_get_display_name) \
    X(gst_device_get_properties) \
    X(gst_device_monitor_add_filter) \
    X(gst_device_monitor_get_devices) \
    X(gst_device_monitor_new) \
    X(gst_device_monitor_start) \
    X(gst_device_monitor_stop) \
    X(gst_discoverer_discover_uri) \
    X(gst_discoverer_audio_info_get_channels) \
    X(gst_discoverer_audio_info_get_sample_rate) \
    X(gst_discoverer_info_get_audio_streams) \
    X(gst_discoverer_info_get_duration) \
    X(gst_discoverer_info_get_subtitle_streams) \
    X(gst_discoverer_info_get_video_streams) \
    X(gst_discoverer_new) \
    X(gst_discoverer_stream_info_get_caps) \
    X(gst_discoverer_stream_info_get_stream_id) \
    X(gst_discoverer_stream_info_get_tags) \
    X(gst_discoverer_stream_info_list_free) \
    X(gst_discoverer_video_info_get_framerate_denom) \
    X(gst_discoverer_video_info_get_framerate_num) \
    X(gst_discoverer_video_info_get_height) \
    X(gst_discoverer_video_info_get_width) \
    X(gst_element_factory_list_filter) \
    X(gst_element_factory_list_get_elements) \
    X(gst_element_factory_make) \
    X(gst_element_get_bus) \
    X(gst_element_get_state) \
    X(gst_element_get_static_pad) \
    X(gst_element_link) \
    X(gst_element_link_many) \
    X(gst_element_query_duration) \
    X(gst_element_seek_simple) \
    X(gst_element_set_state) \
    X(gst_event_new_eos) \
    X(gst_filename_to_uri) \
    X(gst_init_check) \
    X(gst_is_dmabuf_memory) \
    X(gst_dmabuf_memory_get_fd) \
    X(gst_memory_get_sizes) \
    X(gst_message_parse_error) \
    X(gst_mini_object_ref) \
    X(gst_mini_object_unref) \
    X(gst_object_ref) \
    X(gst_object_unref) \
    X(gst_pad_get_current_caps) \
    X(gst_pad_is_linked) \
    X(gst_pad_link) \
    X(gst_pad_query_caps) \
    X(gst_pad_send_event) \
    X(gst_pipeline_new) \
    X(gst_plugin_feature_list_free) \
    X(gst_sample_get_buffer) \
    X(gst_sample_get_caps) \
    X(gst_stream_error_quark) \
    X(gst_structure_free) \
    X(gst_structure_get_fraction) \
    X(gst_structure_get_int) \
    X(gst_structure_get_name) \
    X(gst_structure_get_string) \
    X(gst_structure_set) \
    X(gst_tag_list_get_string) \
    X(gst_util_double_to_fraction) \
    X(gst_video_frame_map) \
    X(gst_video_frame_unmap) \
    X(gst_video_info_from_caps)

namespace jalium::media::gst_runtime {

#define JALIUM_DECLARE_GSTREAMER_FUNCTION(name) \
    extern decltype(&::name) name##_ptr;
JALIUM_GSTREAMER_FUNCTIONS(JALIUM_DECLARE_GSTREAMER_FUNCTION)
#undef JALIUM_DECLARE_GSTREAMER_FUNCTION

extern GType* gst_fraction_type_ptr;

bool Load() noexcept;
void Unload() noexcept;
bool IsLoaded() noexcept;

} // namespace jalium::media::gst_runtime

#ifndef JALIUM_GSTREAMER_LOADER_IMPLEMENTATION

#define g_error_free (*::jalium::media::gst_runtime::g_error_free_ptr)
#define g_free (*::jalium::media::gst_runtime::g_free_ptr)
#define g_list_free_full (*::jalium::media::gst_runtime::g_list_free_full_ptr)
#define g_list_length (*::jalium::media::gst_runtime::g_list_length_ptr)
#define g_object_get (*::jalium::media::gst_runtime::g_object_get_ptr)
#define g_object_set (*::jalium::media::gst_runtime::g_object_set_ptr)
#define g_object_unref (*::jalium::media::gst_runtime::g_object_unref_ptr)
#define g_signal_connect_data (*::jalium::media::gst_runtime::g_signal_connect_data_ptr)
#undef g_str_has_prefix
#define g_str_has_prefix (*::jalium::media::gst_runtime::g_str_has_prefix_ptr)
#define gst_app_sink_is_eos (*::jalium::media::gst_runtime::gst_app_sink_is_eos_ptr)
#define gst_app_sink_try_pull_sample (*::jalium::media::gst_runtime::gst_app_sink_try_pull_sample_ptr)
#define gst_app_src_end_of_stream (*::jalium::media::gst_runtime::gst_app_src_end_of_stream_ptr)
#define gst_app_src_push_buffer (*::jalium::media::gst_runtime::gst_app_src_push_buffer_ptr)
#define gst_audio_info_from_caps (*::jalium::media::gst_runtime::gst_audio_info_from_caps_ptr)
#define gst_bin_add_many (*::jalium::media::gst_runtime::gst_bin_add_many_ptr)
#define gst_buffer_fill (*::jalium::media::gst_runtime::gst_buffer_fill_ptr)
#define gst_buffer_find_memory (*::jalium::media::gst_runtime::gst_buffer_find_memory_ptr)
#define gst_buffer_get_video_meta (*::jalium::media::gst_runtime::gst_buffer_get_video_meta_ptr)
#define gst_buffer_map (*::jalium::media::gst_runtime::gst_buffer_map_ptr)
#define gst_buffer_n_memory (*::jalium::media::gst_runtime::gst_buffer_n_memory_ptr)
#define gst_buffer_new_allocate (*::jalium::media::gst_runtime::gst_buffer_new_allocate_ptr)
#define gst_buffer_peek_memory (*::jalium::media::gst_runtime::gst_buffer_peek_memory_ptr)
#define gst_buffer_unmap (*::jalium::media::gst_runtime::gst_buffer_unmap_ptr)
#define gst_bus_pop_filtered (*::jalium::media::gst_runtime::gst_bus_pop_filtered_ptr)
#define gst_bus_timed_pop_filtered (*::jalium::media::gst_runtime::gst_bus_timed_pop_filtered_ptr)
#define gst_caps_get_size (*::jalium::media::gst_runtime::gst_caps_get_size_ptr)
#define gst_caps_get_structure (*::jalium::media::gst_runtime::gst_caps_get_structure_ptr)
#define gst_caps_get_features (*::jalium::media::gst_runtime::gst_caps_get_features_ptr)
#define gst_caps_features_contains (*::jalium::media::gst_runtime::gst_caps_features_contains_ptr)
#define gst_caps_from_string (*::jalium::media::gst_runtime::gst_caps_from_string_ptr)
#define gst_caps_new_empty_simple (*::jalium::media::gst_runtime::gst_caps_new_empty_simple_ptr)
#define gst_caps_new_simple (*::jalium::media::gst_runtime::gst_caps_new_simple_ptr)
#define gst_device_create_element (*::jalium::media::gst_runtime::gst_device_create_element_ptr)
#define gst_device_get_caps (*::jalium::media::gst_runtime::gst_device_get_caps_ptr)
#define gst_device_get_display_name (*::jalium::media::gst_runtime::gst_device_get_display_name_ptr)
#define gst_device_get_properties (*::jalium::media::gst_runtime::gst_device_get_properties_ptr)
#define gst_device_monitor_add_filter (*::jalium::media::gst_runtime::gst_device_monitor_add_filter_ptr)
#define gst_device_monitor_get_devices (*::jalium::media::gst_runtime::gst_device_monitor_get_devices_ptr)
#define gst_device_monitor_new (*::jalium::media::gst_runtime::gst_device_monitor_new_ptr)
#define gst_device_monitor_start (*::jalium::media::gst_runtime::gst_device_monitor_start_ptr)
#define gst_device_monitor_stop (*::jalium::media::gst_runtime::gst_device_monitor_stop_ptr)
#define gst_discoverer_discover_uri (*::jalium::media::gst_runtime::gst_discoverer_discover_uri_ptr)
#define gst_discoverer_audio_info_get_channels (*::jalium::media::gst_runtime::gst_discoverer_audio_info_get_channels_ptr)
#define gst_discoverer_audio_info_get_sample_rate (*::jalium::media::gst_runtime::gst_discoverer_audio_info_get_sample_rate_ptr)
#define gst_discoverer_info_get_audio_streams (*::jalium::media::gst_runtime::gst_discoverer_info_get_audio_streams_ptr)
#define gst_discoverer_info_get_duration (*::jalium::media::gst_runtime::gst_discoverer_info_get_duration_ptr)
#define gst_discoverer_info_get_subtitle_streams (*::jalium::media::gst_runtime::gst_discoverer_info_get_subtitle_streams_ptr)
#define gst_discoverer_info_get_video_streams (*::jalium::media::gst_runtime::gst_discoverer_info_get_video_streams_ptr)
#define gst_discoverer_new (*::jalium::media::gst_runtime::gst_discoverer_new_ptr)
#define gst_discoverer_stream_info_get_caps (*::jalium::media::gst_runtime::gst_discoverer_stream_info_get_caps_ptr)
#define gst_discoverer_stream_info_get_stream_id (*::jalium::media::gst_runtime::gst_discoverer_stream_info_get_stream_id_ptr)
#define gst_discoverer_stream_info_get_tags (*::jalium::media::gst_runtime::gst_discoverer_stream_info_get_tags_ptr)
#define gst_discoverer_stream_info_list_free (*::jalium::media::gst_runtime::gst_discoverer_stream_info_list_free_ptr)
#define gst_discoverer_video_info_get_framerate_denom (*::jalium::media::gst_runtime::gst_discoverer_video_info_get_framerate_denom_ptr)
#define gst_discoverer_video_info_get_framerate_num (*::jalium::media::gst_runtime::gst_discoverer_video_info_get_framerate_num_ptr)
#define gst_discoverer_video_info_get_height (*::jalium::media::gst_runtime::gst_discoverer_video_info_get_height_ptr)
#define gst_discoverer_video_info_get_width (*::jalium::media::gst_runtime::gst_discoverer_video_info_get_width_ptr)
#define gst_element_factory_list_filter (*::jalium::media::gst_runtime::gst_element_factory_list_filter_ptr)
#define gst_element_factory_list_get_elements (*::jalium::media::gst_runtime::gst_element_factory_list_get_elements_ptr)
#define gst_element_factory_make (*::jalium::media::gst_runtime::gst_element_factory_make_ptr)
#define gst_element_get_bus (*::jalium::media::gst_runtime::gst_element_get_bus_ptr)
#define gst_element_get_state (*::jalium::media::gst_runtime::gst_element_get_state_ptr)
#define gst_element_get_static_pad (*::jalium::media::gst_runtime::gst_element_get_static_pad_ptr)
#define gst_element_link (*::jalium::media::gst_runtime::gst_element_link_ptr)
#define gst_element_link_many (*::jalium::media::gst_runtime::gst_element_link_many_ptr)
#define gst_element_query_duration (*::jalium::media::gst_runtime::gst_element_query_duration_ptr)
#define gst_element_seek_simple (*::jalium::media::gst_runtime::gst_element_seek_simple_ptr)
#define gst_element_set_state (*::jalium::media::gst_runtime::gst_element_set_state_ptr)
#define gst_event_new_eos (*::jalium::media::gst_runtime::gst_event_new_eos_ptr)
#define gst_filename_to_uri (*::jalium::media::gst_runtime::gst_filename_to_uri_ptr)
#define gst_init_check (*::jalium::media::gst_runtime::gst_init_check_ptr)
#define gst_is_dmabuf_memory (*::jalium::media::gst_runtime::gst_is_dmabuf_memory_ptr)
#define gst_dmabuf_memory_get_fd (*::jalium::media::gst_runtime::gst_dmabuf_memory_get_fd_ptr)
#define gst_memory_get_sizes (*::jalium::media::gst_runtime::gst_memory_get_sizes_ptr)
#define gst_message_parse_error (*::jalium::media::gst_runtime::gst_message_parse_error_ptr)
#define gst_mini_object_ref (*::jalium::media::gst_runtime::gst_mini_object_ref_ptr)
#define gst_mini_object_unref (*::jalium::media::gst_runtime::gst_mini_object_unref_ptr)
#define gst_object_ref (*::jalium::media::gst_runtime::gst_object_ref_ptr)
#define gst_object_unref (*::jalium::media::gst_runtime::gst_object_unref_ptr)
#define gst_pad_get_current_caps (*::jalium::media::gst_runtime::gst_pad_get_current_caps_ptr)
#define gst_pad_is_linked (*::jalium::media::gst_runtime::gst_pad_is_linked_ptr)
#define gst_pad_link (*::jalium::media::gst_runtime::gst_pad_link_ptr)
#define gst_pad_query_caps (*::jalium::media::gst_runtime::gst_pad_query_caps_ptr)
#define gst_pad_send_event (*::jalium::media::gst_runtime::gst_pad_send_event_ptr)
#define gst_pipeline_new (*::jalium::media::gst_runtime::gst_pipeline_new_ptr)
#define gst_plugin_feature_list_free (*::jalium::media::gst_runtime::gst_plugin_feature_list_free_ptr)
#define gst_sample_get_buffer (*::jalium::media::gst_runtime::gst_sample_get_buffer_ptr)
#define gst_sample_get_caps (*::jalium::media::gst_runtime::gst_sample_get_caps_ptr)
#define gst_stream_error_quark (*::jalium::media::gst_runtime::gst_stream_error_quark_ptr)
#define gst_structure_free (*::jalium::media::gst_runtime::gst_structure_free_ptr)
#define gst_structure_get_fraction (*::jalium::media::gst_runtime::gst_structure_get_fraction_ptr)
#define gst_structure_get_int (*::jalium::media::gst_runtime::gst_structure_get_int_ptr)
#define gst_structure_get_name (*::jalium::media::gst_runtime::gst_structure_get_name_ptr)
#define gst_structure_get_string (*::jalium::media::gst_runtime::gst_structure_get_string_ptr)
#define gst_structure_set (*::jalium::media::gst_runtime::gst_structure_set_ptr)
#define gst_tag_list_get_string (*::jalium::media::gst_runtime::gst_tag_list_get_string_ptr)
#define gst_util_double_to_fraction (*::jalium::media::gst_runtime::gst_util_double_to_fraction_ptr)
#define gst_video_frame_map (*::jalium::media::gst_runtime::gst_video_frame_map_ptr)
#define gst_video_frame_unmap (*::jalium::media::gst_runtime::gst_video_frame_unmap_ptr)
#define gst_video_info_from_caps (*::jalium::media::gst_runtime::gst_video_info_from_caps_ptr)

// These are static inline wrappers in the public headers. Their bodies were
// compiled before the symbol aliases above were visible, so call the resolved
// GstMiniObject primitive directly instead of leaving an ELF undefined symbol.
#define gst_buffer_unref(buffer) \
    gst_mini_object_unref(GST_MINI_OBJECT_CAST(buffer))
#define gst_caps_unref(caps) \
    gst_mini_object_unref(GST_MINI_OBJECT_CAST(caps))
#define gst_message_unref(message) \
    gst_mini_object_unref(GST_MINI_OBJECT_CAST(message))
#define gst_sample_ref(sample) \
    reinterpret_cast<GstSample*>(gst_mini_object_ref(GST_MINI_OBJECT_CAST(sample)))
#define gst_sample_unref(sample) \
    gst_mini_object_unref(GST_MINI_OBJECT_CAST(sample))

#undef GST_TYPE_FRACTION
#define GST_TYPE_FRACTION (*::jalium::media::gst_runtime::gst_fraction_type_ptr)

#endif // !JALIUM_GSTREAMER_LOADER_IMPLEMENTATION
#endif // JALIUM_HAS_GSTREAMER
