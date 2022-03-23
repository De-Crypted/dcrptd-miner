#define SHA256_DIGEST_SIZE 32
#define POW_MAX_ITERATIONS 10
#define POW_INPUT_SIZE 32
#define POW_NONCE_SIZE 32
#define POW_BUFF_SIZE (POW_INPUT_SIZE + POW_NONCE_SIZE)
#define MAX_SOURCE_SIZE 10000000

#ifndef WORK_SIZE
#define WORK_SIZE 1024
#endif

#pragma OPENCL EXTENSION cl_khr_int64_base_atomics: enable

/* Elementary functions used by SHA256 */
//#define ch(x, y, z)     ((x & (y ^ z)) ^ z)
//#define maj(x, y, z)    ((x & (y | z)) | (y & z))
#define maj(x, y, z) (bitselect ((x), (y), ((x) ^ (z))))
#define ch(x, y, z) (bitselect ((z), (y), (x)))

#define rotr(x, n)      rotate (x, (uint)(32 - n))
//#define rotr(x, n)      ((x >> n) | (x << (32 - n)))
#define sigma0(x)       (rotr(x, 2) ^ rotr(x, 13) ^ rotr(x, 22))
#define sigma1(x)       (rotr(x, 6) ^ rotr(x, 11) ^ rotr(x, 25))
#define gamma0(x)       (rotr(x, 7) ^ rotr(x, 18) ^ (x >> 3))
#define gamma1(x)       (rotr(x, 17) ^ rotr(x, 19) ^ (x >> 10))

__constant const uint K[64]={
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1,
    0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
    0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786,
    0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147,
    0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
    0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b,
    0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a,
    0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
    0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
  };

void inline sha256_crypt_subkernel(uint* input, uint *digest) {

  digest[0] = 0x6a09e667;
  digest[1] = 0xbb67ae85;
  digest[2] = 0x3c6ef372;
  digest[3] = 0xa54ff53a;
  digest[4] = 0x510e527f;
  digest[5] = 0x9b05688c;
  digest[6] = 0x1f83d9ab;
  digest[7] = 0x5be0cd19;

  uint A,B,C,D,E,F,G,H,T1,T2, t;

  A = digest[0];
  B = digest[1];
  C = digest[2];
  D = digest[3];
  E = digest[4];
  F = digest[5];
  G = digest[6];
  H = digest[7];

  uint W[80] = { 
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000
  };

  #pragma unroll
  for (t = 0 ; t < 16 ; t++){
    /*W[t] = input[t * 4] << 24;
    W[t] |= input[t * 4 + 1] << 16;
    W[t] |= input[t * 4 + 2] << 8;
    W[t] |= input[t * 4 + 3];*/
    W[t] = (input[t] << 24)
                | ((input[t] <<  8) & 0x00ff0000)
                | ((input[t] >>  8) & 0x0000ff00)
                | ((input[t] >> 24) & 0x000000ff);
  }

  W[t] =  0x80000000;

  #pragma unroll
  for (t = 0; t < 16; t++) {
    T1 = H + sigma1(E) + ch(E, F, G) + K[t] + W[t];
    T2 = sigma0(A) + maj(A, B, C);
    H = G; G = F; F = E; E = D + T1; D = C; C = B; B = A; A = T1 + T2;
  }

  #pragma unroll
  for (t = 16; t < 64; t++) {
    W[t] = gamma1(W[t - 2]) + W[t - 7] + gamma0(W[t - 15]) + W[t - 16];
    T1 = H + sigma1(E) + ch(E, F, G) + K[t] + W[t];
    T2 = sigma0(A) + maj(A, B, C);
    H = G; G = F; F = E; E = D + T1; D = C; C = B; B = A; A = T1 + T2;
  }

  digest[0] += A;
  digest[1] += B;
  digest[2] += C;
  digest[3] += D;
  digest[4] += E;
  digest[5] += F;
  digest[6] += G;
  digest[7] += H;

  A = digest[0];
  B = digest[1];
  C = digest[2];
  D = digest[3];
  E = digest[4];
  F = digest[5];
  G = digest[6];
  H = digest[7];

  uint W2[80] = { 
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000,
    0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000, 0x00000000
  };

  W2[0]=0x80000000;
  W2[15] = POW_BUFF_SIZE * 8;

  #pragma unroll
  for (t = 0; t < 16; t++) {
    T1 = H + sigma1(E) + ch(E, F, G) + K[t] + W2[t];
    T2 = sigma0(A) + maj(A, B, C);
    H = G; G = F; F = E; E = D + T1; D = C; C = B; B = A; A = T1 + T2;
  }

  #pragma unroll
  for (t = 16; t < 64; t++) {
    W2[t] = gamma1(W2[t - 2]) + W2[t - 7] + gamma0(W2[t - 15]) + W2[t - 16];
    T1 = H + sigma1(E) + ch(E, F, G) + K[t] + W2[t];
    T2 = sigma0(A) + maj(A, B, C);
    H = G; G = F; F = E; E = D + T1; D = C; C = B; B = A; A = T1 + T2;
  }

  digest[0] += A;
  digest[1] += B;
  digest[2] += C;
  digest[3] += D;
  digest[4] += E;
  digest[5] += F;
  digest[6] += G;
  digest[7] += H;

  // Convert result to big-endian.
  #pragma unroll
  for (int i = 0; i < 8; i++) {
    digest[i] = (digest[i] << 24)
                | ((digest[i] <<  8) & 0x00ff0000)
                | ((digest[i] >>  8) & 0x0000ff00)
                | ((digest[i] >> 24) & 0x000000ff);
  }
}

bool inline checkLeadingZeroBits(char* hash, int challengeBytes, int remainingBits) {
    for (int i = 0; i < challengeBytes; i++) {
        if (hash[i] != 0) return false;
    }

    if (remainingBits > 0) 
        return hash[challengeBytes]>>(8 - remainingBits) == 0;
    
    return true;
}

int inline checkLeadingZeroBitsEx(uchar* hash, int difficulty) {
    uint zeros = hash[0] + hash[1] + hash[2] + hash[3];

    return zeros == 0;
}

__kernel void sha256_pow_kernel(__global const char* concat,
                                __global const int* difficulty,
                                __global char* results,
                                __global volatile uint* found) {
    uint gx = get_global_id(0);

    // Prepare input buffer and convenience byte pointer to it.
    //__uint128_t local_input[4];
    //__uint128_t local_digest[2];
    ulong local_input[8];
    ulong local_digest[4];

    char* local_input_bytes = (char*)local_input;

    #pragma unroll
    for (int i = 0; i < 64; i++) {
      local_input_bytes[i] = concat[i];
    }

    //ulong* increment = (ulong*)&local_input[2];
    //*increment += gx * 1024;
    local_input[6] += gx * 1024;

    //int attempts_num;
    int diff;
    int challengeBytes;
    int remainingBits;

    diff = *difficulty;
    challengeBytes = diff / 8;
    remainingBits = (diff - (8 * challengeBytes));

    for (int i = 0; i < WORK_SIZE; i++) { 
      sha256_crypt_subkernel((uint*)local_input, (uint*)local_digest);
      bool res = checkLeadingZeroBits((char*)local_digest, challengeBytes, remainingBits);

      if (res) {
        int f = atomic_add((&found[0]), 1);

        #pragma unroll
        for (int x = 0; x < POW_INPUT_SIZE; x++) {
            results[(f * POW_INPUT_SIZE) + x] = local_input_bytes[POW_INPUT_SIZE + x];
        }
      }

      ++local_input[7];
    }
}
