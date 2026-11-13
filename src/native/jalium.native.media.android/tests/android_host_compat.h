#pragma once
// Only for dispatch tests compiled against real NDK declarations on a Linux
// host. Production and target Android tests retain the NDK availability checks.
#include <sys/cdefs.h>
#include <sys/types.h>
#define __INTRODUCED_IN(x)
#define __INTRODUCED_IN_32(x)
#define __INTRODUCED_IN_64(x)
#define _Nonnull
#define _Nullable
#define __NDK_FPABI__
#define __RENAME_IF_FILE_OFFSET64(x)
