add_library(ffgl-plugin-isfeffect STATIC)
add_library(ffgl::plugin::isfeffect ALIAS ffgl-plugin-isfeffect)
target_sources(ffgl-plugin-isfeffect PRIVATE
    ISFEffect.h
    ISFEffect.cpp
    IsfJson.h
    IsfJson.cpp
)
target_link_libraries(ffgl-plugin-isfeffect PRIVATE ffgl::sdk)
target_compile_features(ffgl-plugin-isfeffect PRIVATE cxx_std_14)

install(
    TARGETS     ffgl-plugin-isfeffect
    EXPORT      ffgl--plugin-isfeffect-targets
    DESTINATION ${CMAKE_INSTALL_LIBDIR}
)
