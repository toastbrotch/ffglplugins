add_library(ffgl-plugin-colorreduce STATIC)
add_library(ffgl::plugin::colorreduce ALIAS ffgl-plugin-colorreduce)
target_sources(ffgl-plugin-colorreduce PRIVATE ColorReduce.h ColorReduce.cpp)
target_link_libraries(ffgl-plugin-colorreduce PRIVATE ffgl::sdk)

install(
    TARGETS     ffgl-plugin-colorreduce
    EXPORT      ffgl--plugin-colorreduce-targets
    DESTINATION ${CMAKE_INSTALL_LIBDIR}
)
