#include "sha256_lib.hpp"

EXPORT_DLL void SHA256Ex(uint8_t* buffer, uint8_t* output) {
    //std::array<uint8_t, 32> ret;
    SHA256_CTX sha256;
    SHA256_Init(&sha256);
    SHA256_Update(&sha256, buffer, 64);
    SHA256_Final(output, &sha256);
    //return ret;
}
