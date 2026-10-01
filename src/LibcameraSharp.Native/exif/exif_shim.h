// libexif for .NET, the way c_api/ is libcamera for .NET: the calls LibcameraSharp makes to build a
// JPEG's EXIF block, with fixed signatures and no libexif struct crossing into .NET. Setting a tag
// writes libexif's ExifEntry fields directly, so that part lives here in C, compiled against
// libexif's headers. The C# imports are generated from
// this header by src/LibcameraSharp.Core/Generate/gen_pinvoke.py.

#ifndef LIBCAMERASHARP_EXIF_SHIM_H
#define LIBCAMERASHARP_EXIF_SHIM_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

struct _ExifData;

/** The directories a tag can go in; the values are libexif's ExifIfd (checked in exif_shim.c). */
enum exif_block_ifd {
	EXIF_BLOCK_IFD_0 = 0,
	EXIF_BLOCK_IFD_EXIF = 2,
	EXIF_BLOCK_IFD_GPS = 3,
};

/** A new, empty EXIF block in little-endian ("Intel") byte order, or NULL. Release with exif_block_free. */
struct _ExifData *exif_block_new(void);
/**
 * Sets tag in directory ifd to size bytes of value, stored as components values of TIFF type format,
 * replacing any value it had. Returns 0 on success, -1 on failure.
 */
int exif_block_set(struct _ExifData *exif, enum exif_block_ifd ifd, uint16_t tag, uint16_t format,
		   uint32_t components, const uint8_t *value, uint32_t size);
/** The block's bytes, starting with "Exif\0\0", or NULL; length receives their count. Release with exif_block_free_bytes. */
uint8_t *exif_block_save(struct _ExifData *exif, uint32_t *length);
/** Releases bytes from exif_block_save. */
void exif_block_free_bytes(uint8_t *bytes);
/** Releases a block from exif_block_new. */
void exif_block_free(struct _ExifData *exif);

#ifdef __cplusplus
}
#endif

#endif
