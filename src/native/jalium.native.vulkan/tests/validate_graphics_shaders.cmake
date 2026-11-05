execute_process(COMMAND "${SHADER_TEST}" "${OUTPUT_DIR}"
    RESULT_VARIABLE generation_result)
if(NOT generation_result EQUAL 0)
    message(FATAL_ERROR "Graphics shader relocation failed: ${generation_result}")
endif()
file(GLOB modules "${OUTPUT_DIR}/*.spv")
if(NOT modules)
    message(FATAL_ERROR "Graphics shader relocation produced no modules")
endif()
foreach(module IN LISTS modules)
    execute_process(COMMAND "${SPIRV_VAL}" --target-env vulkan1.0 "${module}"
        RESULT_VARIABLE validation_result
        OUTPUT_VARIABLE validation_output ERROR_VARIABLE validation_error)
    if(NOT validation_result EQUAL 0)
        message(FATAL_ERROR "${module}: ${validation_output}${validation_error}")
    endif()
endforeach()
list(LENGTH modules module_count)
message(STATUS "Validated ${module_count} relocated shaders against Vulkan 1.0")
