add_library(ffgl-plugin-isfbrowser STATIC)
add_library(ffgl::plugin::isfbrowser ALIAS ffgl-plugin-isfbrowser)
target_sources(ffgl-plugin-isfbrowser PRIVATE
    ISFBrowser.h
    ISFBrowser.cpp
    IsfJson.h
    IsfJson.cpp
)
target_link_libraries(ffgl-plugin-isfbrowser PRIVATE ffgl::sdk)
target_compile_features(ffgl-plugin-isfbrowser PRIVATE cxx_std_14)

install(
    TARGETS     ffgl-plugin-isfbrowser
    EXPORT      ffgl--plugin-isfbrowser-targets
    DESTINATION ${CMAKE_INSTALL_LIBDIR}
)
