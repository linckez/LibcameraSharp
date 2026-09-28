cmake_minimum_required(VERSION 3.16)
project(libcamera-shim LANGUAGES C CXX)

# Three native libraries for .NET:
#   libcamera-shim.so  the extern "C" surface of libcamera. Sources are vendored from libcamera-rs; see VENDORED.md.
#   libtiff-shim.so    libtiff for .NET: every libtiff call the SDK makes, with fixed signatures (tiff/, ours).
#   libexif-shim.so    libexif for .NET: building a JPEG's EXIF block (exif/, ours).
# The C# imports for each are generated from their headers (src/LibcameraSharp.Core/Generate/regenerate.sh).
option(SHIM_SANITIZE "Build with AddressSanitizer + UBSan (test images only)" OFF)

find_package(PkgConfig REQUIRED)
pkg_check_modules(LIBCAMERA REQUIRED IMPORTED_TARGET libcamera)
message(STATUS "libcamera ${LIBCAMERA_VERSION} from ${LIBCAMERA_LIBDIR}")

file(GLOB SHIM_SOURCES CONFIGURE_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/c_api/*.cpp)

add_library(libcamera-shim SHARED ${SHIM_SOURCES})
target_include_directories(libcamera-shim PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/c_api)
target_link_libraries(libcamera-shim PRIVATE PkgConfig::LIBCAMERA)
target_compile_features(libcamera-shim PUBLIC cxx_std_17)
target_compile_options(libcamera-shim PRIVATE -Wall -Wextra)

# Export only the extern "C" libcamera_* functions (version script; the vendored
# sources carry no visibility attributes and are not edited).
set_target_properties(libcamera-shim PROPERTIES
    OUTPUT_NAME "camera-shim"      # -> libcamera-shim.so, matching the C# LibraryImport name
    PREFIX "lib"
    LINK_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/exports.map
)
target_link_options(libcamera-shim PRIVATE "-Wl,--version-script=${CMAKE_CURRENT_SOURCE_DIR}/exports.map")

if(SHIM_SANITIZE)
    target_compile_options(libcamera-shim PRIVATE -fsanitize=address,undefined -fno-omit-frame-pointer -g)
    target_link_options(libcamera-shim PRIVATE -fsanitize=address,undefined)
endif()

install(TARGETS libcamera-shim LIBRARY DESTINATION lib)

# The C shims over system C libraries, one per library so that a missing one only affects what uses it
# (opening a camera never needs libtiff or libexif). Each: tiff/ or exif/ sources, its own exports.map.
function(add_c_shim name dir pkg)
    pkg_check_modules(${name}_DEP REQUIRED IMPORTED_TARGET ${pkg})
    message(STATUS "${pkg} ${${name}_DEP_VERSION} for lib${name}-shim")
    add_library(${name}-shim SHARED ${CMAKE_CURRENT_SOURCE_DIR}/${dir}/${dir}_shim.c)
    target_include_directories(${name}-shim PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/${dir})
    target_link_libraries(${name}-shim PRIVATE PkgConfig::${name}_DEP)
    target_compile_options(${name}-shim PRIVATE -Wall -Wextra)
    set_target_properties(${name}-shim PROPERTIES
        OUTPUT_NAME "${name}-shim"      # -> lib<name>-shim.so, matching the C# LibraryImport name
        PREFIX "lib"
        LINK_DEPENDS ${CMAKE_CURRENT_SOURCE_DIR}/${dir}/exports.map
    )
    target_link_options(${name}-shim PRIVATE "-Wl,--version-script=${CMAKE_CURRENT_SOURCE_DIR}/${dir}/exports.map")
    if(SHIM_SANITIZE)
        target_compile_options(${name}-shim PRIVATE -fsanitize=address,undefined -fno-omit-frame-pointer -g)
        target_link_options(${name}-shim PRIVATE -fsanitize=address,undefined)
    endif()
    install(TARGETS ${name}-shim LIBRARY DESTINATION lib)
endfunction()

add_c_shim(tiff tiff libtiff-4)
add_c_shim(exif exif libexif)
