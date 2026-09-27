add_library(ffgl-plugin-framediff STATIC)
add_library(ffgl::plugin::framediff ALIAS ffgl-plugin-framediff)
target_sources(ffgl-plugin-framediff PRIVATE FrameDiff.h FrameDiff.cpp)
target_link_libraries(ffgl-plugin-framediff PRIVATE ffgl::sdk)

install(
    TARGETS     ffgl-plugin-framediff
    EXPORT      ffgl--plugin-framediff-targets
    DESTINATION ${CMAKE_INSTALL_LIBDIR}
)
