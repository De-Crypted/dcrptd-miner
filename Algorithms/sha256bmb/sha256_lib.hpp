#ifndef MYDLL_H
#define MYDLL_H

#include <array>
#include "openssl/sha.h"

#ifdef WIN32
#define EXPORT_DLL extern "C" __declspec(dllexport)
#else
#define EXPORT_DLL extern "C"
#endif

EXPORT_DLL void SHA256Ex(uint8_t* buffer, uint8_t* output);

#endif