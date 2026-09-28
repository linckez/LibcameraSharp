// See tiff_shim.h. Each function is one libtiff call; compiling against tiffio.h checks the types.

#include "tiff_shim.h"

#include <tiffio.h>

TIFF *tiff_open(const char *path, const char *mode) { return TIFFOpen(path, mode); }
void tiff_close(TIFF *tif) { TIFFClose(tif); }
int tiff_write_scanline(TIFF *tif, void *row, uint32_t index) { return TIFFWriteScanline(tif, row, index, 0); }
int tiff_write_directory(TIFF *tif) { return TIFFWriteDirectory(tif); }
int tiff_checkpoint_directory(TIFF *tif) { return TIFFCheckpointDirectory(tif); }
uint64_t tiff_current_directory_offset(TIFF *tif) { return TIFFCurrentDirOffset(tif); }
int tiff_create_exif_directory(TIFF *tif) { return TIFFCreateEXIFDirectory(tif); }
int tiff_set_directory(TIFF *tif, uint32_t index) { return TIFFSetDirectory(tif, (tdir_t)index); }
int tiff_unlink_directory(TIFF *tif, uint32_t number) { return TIFFUnlinkDirectory(tif, (tdir_t)number); }

// libtiff 4.5.1 dropped the CFA tags from its field table (libtiff commit 738e0409, fixed by
// 49856998), so register them when missing.
int tiff_register_cfa_fields(TIFF *tif)
{
	if (TIFFFindField(tif, TIFFTAG_CFAREPEATPATTERNDIM, TIFF_ANY))
		return 0;
	static const TIFFFieldInfo infos[] = {
		{ TIFFTAG_CFAREPEATPATTERNDIM, 2, 2, TIFF_SHORT, FIELD_CUSTOM, 1, 0, (char *)"CFARepeatPatternDim" },
		{ TIFFTAG_CFAPATTERN, -1, -1, TIFF_BYTE, FIELD_CUSTOM, 1, 1, (char *)"CFAPattern" },
	};
	return TIFFMergeFieldInfo(tif, infos, 2);
}

const TIFFField *tiff_field(TIFF *tif, uint32_t tag) { return TIFFFieldWithTag(tif, tag); }
int tiff_field_data_type(const TIFFField *field) { return (int)TIFFFieldDataType(field); }
int tiff_field_passes_count(const TIFFField *field) { return TIFFFieldPassCount(field); }
int tiff_field_read_count(const TIFFField *field) { return TIFFFieldReadCount(field); }
int tiff_field_value_size(const TIFFField *field) { return TIFFFieldSetGetSize(field); }
int tiff_field_count_size(const TIFFField *field) { return TIFFFieldSetGetCountSize(field); }

int tiff_set_field_uint32(TIFF *tif, uint32_t tag, uint32_t value) { return TIFFSetField(tif, tag, value); }
int tiff_set_field_uint64(TIFF *tif, uint32_t tag, uint64_t value) { return TIFFSetField(tif, tag, value); }
int tiff_set_field_double(TIFF *tif, uint32_t tag, double value) { return TIFFSetField(tif, tag, value); }
int tiff_set_field_string(TIFF *tif, uint32_t tag, const char *value) { return TIFFSetField(tif, tag, value); }
int tiff_set_field_array(TIFF *tif, uint32_t tag, const void *values) { return TIFFSetField(tif, tag, values); }
int tiff_set_field_counted_array(TIFF *tif, uint32_t tag, int count, const void *values)
{
	return TIFFSetField(tif, tag, count, values);
}
