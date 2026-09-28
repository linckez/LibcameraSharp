// libtiff for .NET, the way c_api/ is libcamera for .NET: every libtiff call LibcameraSharp makes, with
// a fixed signature. TIFFSetField takes a variable argument list, which .NET cannot pass on Linux
// (dotnet/runtime#48796), so it gets one function per way libtiff reads a tag's value (the
// TIFF_SETGET_* kinds in tif_dirinfo.c). The C# imports are generated from this header by
// src/LibcameraSharp.Core/Generate/gen_pinvoke.py; tiff_shim.c is compiled against tiffio.h.

#ifndef LIBCAMERASHARP_TIFF_SHIM_H
#define LIBCAMERASHARP_TIFF_SHIM_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

struct tiff;
struct _TIFFField;

/** Opens a TIFF file (TIFFOpen); mode "w" creates it. Returns NULL on failure. */
struct tiff *tiff_open(const char *path, const char *mode);
/** Flushes and closes the file (TIFFClose). */
void tiff_close(struct tiff *tif);
/** Writes one row of the current directory's image (TIFFWriteScanline, sample 0). Returns 1 on success. */
int tiff_write_scanline(struct tiff *tif, void *row, uint32_t index);
/** Writes the current directory and starts a new one (TIFFWriteDirectory). Returns 1 on success. */
int tiff_write_directory(struct tiff *tif);
/** Writes the current directory so far, keeping it current (TIFFCheckpointDirectory). Returns 1 on success. */
int tiff_checkpoint_directory(struct tiff *tif);
/** The current directory's offset in the file (TIFFCurrentDirOffset). */
uint64_t tiff_current_directory_offset(struct tiff *tif);
/** Starts an EXIF directory (TIFFCreateEXIFDirectory). Returns 0 on success. */
int tiff_create_exif_directory(struct tiff *tif);
/** Makes directory index (from 0) current again (TIFFSetDirectory). Returns 1 on success. */
int tiff_set_directory(struct tiff *tif, uint32_t index);
/** Removes directory number (from 1) from the chain (TIFFUnlinkDirectory). Returns 1 on success. */
int tiff_unlink_directory(struct tiff *tif, uint32_t number);

/**
 * Registers the CFARepeatPatternDim and CFAPattern tags when this libtiff lacks them: libtiff 4.5.1 lost
 * them from its field table. Returns 0 when they were already known or are now registered.
 */
int tiff_register_cfa_fields(struct tiff *tif);

/** libtiff's description of a tag in the current directory (TIFFFieldWithTag), or NULL if unknown. */
const struct _TIFFField *tiff_field(struct tiff *tif, uint32_t tag);
/** The tag's TIFFDataType (TIFFFieldDataType). */
int tiff_field_data_type(const struct _TIFFField *field);
/** Non-zero when TIFFSetField expects a count before the values (TIFFFieldPassCount). */
int tiff_field_passes_count(const struct _TIFFField *field);
/** How many values the tag holds, or a negative marker for variable (TIFFFieldReadCount). */
int tiff_field_read_count(const struct _TIFFField *field);
/** Bytes per value as TIFFSetField reads it (TIFFFieldSetGetSize). */
int tiff_field_value_size(const struct _TIFFField *field);
/** Bytes of the count TIFFSetField reads first: 2 or 4, else 0 (TIFFFieldSetGetCountSize). */
int tiff_field_count_size(const struct _TIFFField *field);

/** TIFFSetField for UINT16 and UINT32 tags: a 16-bit value is read as a promoted int. Returns 1 on success. */
int tiff_set_field_uint32(struct tiff *tif, uint32_t tag, uint32_t value);
/** TIFFSetField for IFD8 tags, such as TIFFTAG_EXIFIFD. Returns 1 on success. */
int tiff_set_field_uint64(struct tiff *tif, uint32_t tag, uint64_t value);
/** TIFFSetField for FLOAT and DOUBLE tags: a float is read as a promoted double. Returns 1 on success. */
int tiff_set_field_double(struct tiff *tif, uint32_t tag, double value);
/** TIFFSetField for ASCII tags. Returns 1 on success. */
int tiff_set_field_string(struct tiff *tif, uint32_t tag, const char *value);
/** TIFFSetField for C0 tags: an array whose length the tag fixes, such as TIFFTAG_DNGVERSION. Returns 1 on success. */
int tiff_set_field_array(struct tiff *tif, uint32_t tag, const void *values);
/** TIFFSetField for C16 and C32 tags: a count, then the array. Returns 1 on success. */
int tiff_set_field_counted_array(struct tiff *tif, uint32_t tag, int count, const void *values);

#ifdef __cplusplus
}
#endif

#endif
