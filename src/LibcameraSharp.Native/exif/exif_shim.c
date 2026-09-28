// See exif_shim.h. An entry is found or created, added and initialised, then its data replaced with
// the value, in the format the caller gives.

#include "exif_shim.h"

#include <stdlib.h>
#include <string.h>

#include <libexif/exif-data.h>

_Static_assert((int)EXIF_BLOCK_IFD_0 == (int)EXIF_IFD_0, "exif_block_ifd must match libexif's ExifIfd");
_Static_assert((int)EXIF_BLOCK_IFD_EXIF == (int)EXIF_IFD_EXIF, "exif_block_ifd must match libexif's ExifIfd");

ExifData *exif_block_new(void)
{
	ExifData *exif = exif_data_new();
	if (exif)
		exif_data_set_byte_order(exif, EXIF_BYTE_ORDER_INTEL);
	return exif;
}

int exif_block_set(ExifData *exif, enum exif_block_ifd ifd, uint16_t tag, uint16_t format, uint32_t components,
		   const uint8_t *value, uint32_t size)
{
	// Find the entry, or create, add and initialise it; the content keeps the reference.
	ExifEntry *entry = exif_content_get_entry(exif->ifd[ifd], (ExifTag)tag);
	if (!entry) {
		entry = exif_entry_new();
		if (!entry)
			return -1;
		entry->tag = (ExifTag)tag;
		exif_content_add_entry(exif->ifd[ifd], entry);
		exif_entry_initialize(entry, entry->tag);
		exif_entry_unref(entry);
	}

	// Replace its data; libexif frees entry data with free().
	unsigned char *data = malloc(size ? size : 1);
	if (!data)
		return -1;
	if (size)
		memcpy(data, value, size);
	free(entry->data);
	entry->data = data;
	entry->size = size;
	entry->components = components;
	entry->format = (ExifFormat)format;
	return 0;
}

uint8_t *exif_block_save(ExifData *exif, uint32_t *length)
{
	unsigned char *bytes = NULL;
	unsigned int count = 0;
	exif_data_save_data(exif, &bytes, &count);
	*length = count;
	return bytes;
}

void exif_block_free_bytes(uint8_t *bytes) { free(bytes); }

void exif_block_free(ExifData *exif) { exif_data_unref(exif); }
