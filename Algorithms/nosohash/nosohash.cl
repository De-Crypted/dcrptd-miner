#define MD5_F_S(x,y,z)  ((z) ^ ((x) & ((y) ^ (z))))
#define MD5_G_S(x,y,z)  ((y) ^ ((z) & ((x) ^ (y))))
#define MD5_H_S(x,y,z)  ((x) ^ (y) ^ (z))
#define MD5_I_S(x,y,z)  ((y) ^ ((x) | ~(z)))

#define MD5_F(x,y,z)    ((z) ^ ((x) & ((y) ^ (z))))
#define MD5_G(x,y,z)    ((y) ^ ((z) & ((x) ^ (y))))
#define MD5_H(x,y,z)    ((x) ^ (y) ^ (z))
#define MD5_H1(x,y,z)   ((t = (x) ^ (y)) ^ (z))
#define MD5_H2(x,y,z)   ((x) ^ t)
#define MD5_I(x,y,z)    ((y) ^ ((x) | ~(z)))

#ifdef USE_BITSELECT
#define MD5_Fo(x,y,z)   (bitselect ((z), (y), (x)))
#define MD5_Go(x,y,z)   (bitselect ((y), (x), (z)))
#else
#define MD5_Fo(x,y,z)   (MD5_F((x), (y), (z)))
#define MD5_Go(x,y,z)   (MD5_G((x), (y), (z)))
#endif

#define MD5_STEP_S(f,a,b,c,d,x,K,s)   \
{                                     \
  a += K;                             \
  a  = hc_add3_S (a, x, f (b, c, d)); \
  a  = hc_rotl32_S (a, s);            \
  a += b;                             \
}

#define MD5_STEP(f,a,b,c,d,x,K,s)   \
{                                   \
  a += make_u32x (K);               \
  a  = hc_add3 (a, x, f (b, c, d)); \
  a  = hc_rotl32 (a, s);            \
  a += b;                           \
}

#define MD5_STEP0(f,a,b,c,d,K,s)    \
{                                   \
  a  = hc_add3 (a, K, f (b, c, d)); \
  a  = hc_rotl32 (a, s);            \
  a += b;                           \
}

typedef struct md5_ctx
{
  uint h[4];

  uint w0[4];
  uint w1[4];
  uint w2[4];
  uint w3[4];

  int len;

} md5_ctx_t;

typedef struct md5_hmac_ctx
{
  md5_ctx_t ipad;
  md5_ctx_t opad;

} md5_hmac_ctx_t;

typedef struct md5_ctx_vector
{
  uint h[4];

  uint w0[4];
  uint w1[4];
  uint w2[4];
  uint w3[4];

  int  len;

} md5_ctx_vector_t;

typedef struct md5_hmac_ctx_vector
{
  md5_ctx_vector_t ipad;
  md5_ctx_vector_t opad;

} md5_hmac_ctx_vector_t;


__constant const char filler_chars[] = "%)+/5;=CGIOSYaegk%)+/5;=CGIOSYaegk%)+/5;=CGIOSYaegk%)+/5;=CGIOSYaegk%)+/5;=CGIOSYaegk";
__constant const char hex_dec2char_table[] = {'0','1','2','3','4','5','6','7','8','9','A','B','C','D','E','F' };

__constant const char hex_char2dec_table[] = {
 0,
 0,   0,   0,   0,   0,   0,   0,   0,   0,   0,
 0,   0,   0,   0,   0,   0,   0,   0,   0,   0,
 0,   0,   0,   0,   0,   0,   0,   0,   0,   0,
 0,   0,   0,   0,   0,   0,   0,   0,   0,   0,
 0,   0,   0,   0,   0,   0,   0,   0,   1,   2,
 3,   4,   5,   6,   7,   8,   9,   0,   0,   0,
 0,   0,   0,   0,  10,  11,  12,  13,  14,  15 };

 __constant const char nosohash_chars_table[] = {
  0,
  0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,   0,
  0,   0,   0,   0,   0,   0,   0,  32,  33,  34,  35,  36,  37,  38,  39,  40,  41,  42,  43,  44,  45,  46,  47,  48,
 49,  50,  51,  52,  53,  54,  55,  56,  57,  58,  59,  60,  61,  62,  63,  64,  65,  66,  67,  68,  69,  70,  71,  72,
 73,  74,  75,  76,  77,  78,  79,  80,  81,  82,  83,  84,  85,  86,  87,  88,  89,  90,  91,  92,  93,  94,  95,  96,
 97,  98,  99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120,
121, 122, 123, 124, 125, 126,  32,  33,  34,  35,  36,  37,  38,  39,  40,  41,  42,  43,  44,  45,  46,  47,  48,  49,
 50,  51,  52,  53,  54,  55,  56,  57,  58,  59,  60,  61,  62,  63,  64,  65,  66,  67,  68,  69,  70,  71,  72,  73,
 74,  75,  76,  77,  78,  79,  80,  81,  82,  83,  84,  85,  86,  87,  88,  89,  90,  91,  92,  93,  94,  95,  96,  97,
 98,  99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121,
122, 123, 124, 125, 126,  32,  33,  34,  35,  36,  37,  38,  39,  40,  41,  42,  43,  44,  45,  46,  47,  48,  49,  50,
 51,  52,  53,  54,  55,  56,  57,  58,  59,  60,  61,  62,  63,  64,  65,  66,  67,  68,  69,  70,  71,  72,  73,  74,
 75,  76,  77,  78,  79,  80,  81,  82,  83,  84,  85,  86,  87,  88,  89,  90,  91,  92,  93,  94,  95,  96,  97,  98,
 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122,
123, 124, 125, 126,  32,  33,  34,  35,  36,  37,  38,  39,  40,  41,  42,  43,  44,  45,  46,  47,  48,  49,  50,  51,
 52,  53,  54,  55,  56,  57,  58,  59,  60,  61,  62,  63,  64,  65,  66,  67,  68,  69,  70,  71,  72,  73,  74,  75,
 76,  77,  78,  79,  80,  81,  82,  83,  84,  85,  86,  87,  88,  89,  90,  91,  92,  93,  94,  95,  96,  97,  98,  99,
100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123,
124, 125, 126,  32,  33,  34,  35,  36,  37,  38,  39,  40,  41,  42,  43,  44,  45,  46,  47,  48,  49,  50,  51,  52,
 53,  54,  55,  56,  57,  58,  59,  60,  61,  62,  63,  64,  65,  66,  67,  68,  69,  70,  71,  72,  73,  74,  75,  76,
 77,  78,  79,  80,  81,  82,  83,  84,  85,  86,  87,  88,  89,  90,  91,  92,  93,  94,  95,  96,  97,  98,  99, 100,
101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124,
};

uint hc_add3 (const uint a, const uint b, const uint c)
{
  return a + b + c;
}

uint hc_add3_S (const uint a, const uint b, const uint c)
{
  return a + b + c;
}

uint hc_rotl32_S (const uint a, const int n)
{
  #if   defined _CPU_OPENCL_EMU_H
  return rotl32 (a, n);
  #elif defined IS_CUDA || defined IS_HIP
  return rotl32_S (a, n);
  #else
  #ifdef USE_ROTATE
  return rotate (a, (u32) (n));
  #else
  return ((a << n) | (a >> (32 - n)));
  #endif
  #endif
}

typedef enum md5_constants
{
  MD5M_A=0x67452301U,
  MD5M_B=0xefcdab89U,
  MD5M_C=0x98badcfeU,
  MD5M_D=0x10325476U,

  MD5S00=7,
  MD5S01=12,
  MD5S02=17,
  MD5S03=22,
  MD5S10=5,
  MD5S11=9,
  MD5S12=14,
  MD5S13=20,
  MD5S20=4,
  MD5S21=11,
  MD5S22=16,
  MD5S23=23,
  MD5S30=6,
  MD5S31=10,
  MD5S32=15,
  MD5S33=21,

  MD5C00=0xd76aa478U,
  MD5C01=0xe8c7b756U,
  MD5C02=0x242070dbU,
  MD5C03=0xc1bdceeeU,
  MD5C04=0xf57c0fafU,
  MD5C05=0x4787c62aU,
  MD5C06=0xa8304613U,
  MD5C07=0xfd469501U,
  MD5C08=0x698098d8U,
  MD5C09=0x8b44f7afU,
  MD5C0a=0xffff5bb1U,
  MD5C0b=0x895cd7beU,
  MD5C0c=0x6b901122U,
  MD5C0d=0xfd987193U,
  MD5C0e=0xa679438eU,
  MD5C0f=0x49b40821U,
  MD5C10=0xf61e2562U,
  MD5C11=0xc040b340U,
  MD5C12=0x265e5a51U,
  MD5C13=0xe9b6c7aaU,
  MD5C14=0xd62f105dU,
  MD5C15=0x02441453U,
  MD5C16=0xd8a1e681U,
  MD5C17=0xe7d3fbc8U,
  MD5C18=0x21e1cde6U,
  MD5C19=0xc33707d6U,
  MD5C1a=0xf4d50d87U,
  MD5C1b=0x455a14edU,
  MD5C1c=0xa9e3e905U,
  MD5C1d=0xfcefa3f8U,
  MD5C1e=0x676f02d9U,
  MD5C1f=0x8d2a4c8aU,
  MD5C20=0xfffa3942U,
  MD5C21=0x8771f681U,
  MD5C22=0x6d9d6122U,
  MD5C23=0xfde5380cU,
  MD5C24=0xa4beea44U,
  MD5C25=0x4bdecfa9U,
  MD5C26=0xf6bb4b60U,
  MD5C27=0xbebfbc70U,
  MD5C28=0x289b7ec6U,
  MD5C29=0xeaa127faU,
  MD5C2a=0xd4ef3085U,
  MD5C2b=0x04881d05U,
  MD5C2c=0xd9d4d039U,
  MD5C2d=0xe6db99e5U,
  MD5C2e=0x1fa27cf8U,
  MD5C2f=0xc4ac5665U,
  MD5C30=0xf4292244U,
  MD5C31=0x432aff97U,
  MD5C32=0xab9423a7U,
  MD5C33=0xfc93a039U,
  MD5C34=0x655b59c3U,
  MD5C35=0x8f0ccc92U,
  MD5C36=0xffeff47dU,
  MD5C37=0x85845dd1U,
  MD5C38=0x6fa87e4fU,
  MD5C39=0xfe2ce6e0U,
  MD5C3a=0xa3014314U,
  MD5C3b=0x4e0811a1U,
  MD5C3c=0xf7537e82U,
  MD5C3d=0xbd3af235U,
  MD5C3e=0x2ad7d2bbU,
  MD5C3f=0xeb86d391U

} md5_constants_t;

void set_mark_1x4_S (uint *v, const uint offset)
{
  const uint c = (offset & 15) / 4;
  const uint r = 0xff << ((offset & 3) * 8);

  v[0] = (c == 0) ? r : 0;
  v[1] = (c == 1) ? r : 0;
  v[2] = (c == 2) ? r : 0;
  v[3] = (c == 3) ? r : 0;
}

void append_helper_1x4_S (uint *r, const uint v, const uint *m)
{
  r[0] |= v & m[0];
  r[1] |= v & m[1];
  r[2] |= v & m[2];
  r[3] |= v & m[3];
}

void append_0x80_4x4_S (uint *w0, uint *w1, uint *w2, uint *w3, const uint offset)
{
  uint v[4];

  set_mark_1x4_S (v, offset);

  const uint offset16 = offset / 16;

  append_helper_1x4_S (w0, ((offset16 == 0) ? 0x80808080 : 0), v);
  append_helper_1x4_S (w1, ((offset16 == 1) ? 0x80808080 : 0), v);
  append_helper_1x4_S (w2, ((offset16 == 2) ? 0x80808080 : 0), v);
  append_helper_1x4_S (w3, ((offset16 == 3) ? 0x80808080 : 0), v);
}

void md5_init (md5_ctx_t *ctx)
{
  ctx->h[0] = MD5M_A;
  ctx->h[1] = MD5M_B;
  ctx->h[2] = MD5M_C;
  ctx->h[3] = MD5M_D;

  ctx->w0[0] = 0;
  ctx->w0[1] = 0;
  ctx->w0[2] = 0;
  ctx->w0[3] = 0;
  ctx->w1[0] = 0;
  ctx->w1[1] = 0;
  ctx->w1[2] = 0;
  ctx->w1[3] = 0;
  ctx->w2[0] = 0;
  ctx->w2[1] = 0;
  ctx->w2[2] = 0;
  ctx->w2[3] = 0;
  ctx->w3[0] = 0;
  ctx->w3[1] = 0;
  ctx->w3[2] = 0;
  ctx->w3[3] = 0;

  ctx->len = 0;
}

void md5_transform (const uint *w0, const uint *w1, const uint *w2, const uint *w3, uint *digest)
{
  uint a = digest[0];
  uint b = digest[1];
  uint c = digest[2];
  uint d = digest[3];

  uint w0_t = w0[0];
  uint w1_t = w0[1];
  uint w2_t = w0[2];
  uint w3_t = w0[3];
  uint w4_t = w1[0];
  uint w5_t = w1[1];
  uint w6_t = w1[2];
  uint w7_t = w1[3];
  uint w8_t = w2[0];
  uint w9_t = w2[1];
  uint wa_t = w2[2];
  uint wb_t = w2[3];
  uint wc_t = w3[0];
  uint wd_t = w3[1];
  uint we_t = w3[2];
  uint wf_t = w3[3];

  MD5_STEP_S (MD5_Fo, a, b, c, d, w0_t, MD5C00, MD5S00);
  MD5_STEP_S (MD5_Fo, d, a, b, c, w1_t, MD5C01, MD5S01);
  MD5_STEP_S (MD5_Fo, c, d, a, b, w2_t, MD5C02, MD5S02);
  MD5_STEP_S (MD5_Fo, b, c, d, a, w3_t, MD5C03, MD5S03);
  MD5_STEP_S (MD5_Fo, a, b, c, d, w4_t, MD5C04, MD5S00);
  MD5_STEP_S (MD5_Fo, d, a, b, c, w5_t, MD5C05, MD5S01);
  MD5_STEP_S (MD5_Fo, c, d, a, b, w6_t, MD5C06, MD5S02);
  MD5_STEP_S (MD5_Fo, b, c, d, a, w7_t, MD5C07, MD5S03);
  MD5_STEP_S (MD5_Fo, a, b, c, d, w8_t, MD5C08, MD5S00);
  MD5_STEP_S (MD5_Fo, d, a, b, c, w9_t, MD5C09, MD5S01);
  MD5_STEP_S (MD5_Fo, c, d, a, b, wa_t, MD5C0a, MD5S02);
  MD5_STEP_S (MD5_Fo, b, c, d, a, wb_t, MD5C0b, MD5S03);
  MD5_STEP_S (MD5_Fo, a, b, c, d, wc_t, MD5C0c, MD5S00);
  MD5_STEP_S (MD5_Fo, d, a, b, c, wd_t, MD5C0d, MD5S01);
  MD5_STEP_S (MD5_Fo, c, d, a, b, we_t, MD5C0e, MD5S02);
  MD5_STEP_S (MD5_Fo, b, c, d, a, wf_t, MD5C0f, MD5S03);

  MD5_STEP_S (MD5_Go, a, b, c, d, w1_t, MD5C10, MD5S10);
  MD5_STEP_S (MD5_Go, d, a, b, c, w6_t, MD5C11, MD5S11);
  MD5_STEP_S (MD5_Go, c, d, a, b, wb_t, MD5C12, MD5S12);
  MD5_STEP_S (MD5_Go, b, c, d, a, w0_t, MD5C13, MD5S13);
  MD5_STEP_S (MD5_Go, a, b, c, d, w5_t, MD5C14, MD5S10);
  MD5_STEP_S (MD5_Go, d, a, b, c, wa_t, MD5C15, MD5S11);
  MD5_STEP_S (MD5_Go, c, d, a, b, wf_t, MD5C16, MD5S12);
  MD5_STEP_S (MD5_Go, b, c, d, a, w4_t, MD5C17, MD5S13);
  MD5_STEP_S (MD5_Go, a, b, c, d, w9_t, MD5C18, MD5S10);
  MD5_STEP_S (MD5_Go, d, a, b, c, we_t, MD5C19, MD5S11);
  MD5_STEP_S (MD5_Go, c, d, a, b, w3_t, MD5C1a, MD5S12);
  MD5_STEP_S (MD5_Go, b, c, d, a, w8_t, MD5C1b, MD5S13);
  MD5_STEP_S (MD5_Go, a, b, c, d, wd_t, MD5C1c, MD5S10);
  MD5_STEP_S (MD5_Go, d, a, b, c, w2_t, MD5C1d, MD5S11);
  MD5_STEP_S (MD5_Go, c, d, a, b, w7_t, MD5C1e, MD5S12);
  MD5_STEP_S (MD5_Go, b, c, d, a, wc_t, MD5C1f, MD5S13);

  uint t;

  MD5_STEP_S (MD5_H1, a, b, c, d, w5_t, MD5C20, MD5S20);
  MD5_STEP_S (MD5_H2, d, a, b, c, w8_t, MD5C21, MD5S21);
  MD5_STEP_S (MD5_H1, c, d, a, b, wb_t, MD5C22, MD5S22);
  MD5_STEP_S (MD5_H2, b, c, d, a, we_t, MD5C23, MD5S23);
  MD5_STEP_S (MD5_H1, a, b, c, d, w1_t, MD5C24, MD5S20);
  MD5_STEP_S (MD5_H2, d, a, b, c, w4_t, MD5C25, MD5S21);
  MD5_STEP_S (MD5_H1, c, d, a, b, w7_t, MD5C26, MD5S22);
  MD5_STEP_S (MD5_H2, b, c, d, a, wa_t, MD5C27, MD5S23);
  MD5_STEP_S (MD5_H1, a, b, c, d, wd_t, MD5C28, MD5S20);
  MD5_STEP_S (MD5_H2, d, a, b, c, w0_t, MD5C29, MD5S21);
  MD5_STEP_S (MD5_H1, c, d, a, b, w3_t, MD5C2a, MD5S22);
  MD5_STEP_S (MD5_H2, b, c, d, a, w6_t, MD5C2b, MD5S23);
  MD5_STEP_S (MD5_H1, a, b, c, d, w9_t, MD5C2c, MD5S20);
  MD5_STEP_S (MD5_H2, d, a, b, c, wc_t, MD5C2d, MD5S21);
  MD5_STEP_S (MD5_H1, c, d, a, b, wf_t, MD5C2e, MD5S22);
  MD5_STEP_S (MD5_H2, b, c, d, a, w2_t, MD5C2f, MD5S23);

  MD5_STEP_S (MD5_I , a, b, c, d, w0_t, MD5C30, MD5S30);
  MD5_STEP_S (MD5_I , d, a, b, c, w7_t, MD5C31, MD5S31);
  MD5_STEP_S (MD5_I , c, d, a, b, we_t, MD5C32, MD5S32);
  MD5_STEP_S (MD5_I , b, c, d, a, w5_t, MD5C33, MD5S33);
  MD5_STEP_S (MD5_I , a, b, c, d, wc_t, MD5C34, MD5S30);
  MD5_STEP_S (MD5_I , d, a, b, c, w3_t, MD5C35, MD5S31);
  MD5_STEP_S (MD5_I , c, d, a, b, wa_t, MD5C36, MD5S32);
  MD5_STEP_S (MD5_I , b, c, d, a, w1_t, MD5C37, MD5S33);
  MD5_STEP_S (MD5_I , a, b, c, d, w8_t, MD5C38, MD5S30);
  MD5_STEP_S (MD5_I , d, a, b, c, wf_t, MD5C39, MD5S31);
  MD5_STEP_S (MD5_I , c, d, a, b, w6_t, MD5C3a, MD5S32);
  MD5_STEP_S (MD5_I , b, c, d, a, wd_t, MD5C3b, MD5S33);
  MD5_STEP_S (MD5_I , a, b, c, d, w4_t, MD5C3c, MD5S30);
  MD5_STEP_S (MD5_I , d, a, b, c, wb_t, MD5C3d, MD5S31);
  MD5_STEP_S (MD5_I , c, d, a, b, w2_t, MD5C3e, MD5S32);
  MD5_STEP_S (MD5_I , b, c, d, a, w9_t, MD5C3f, MD5S33);

  digest[0] += a;
  digest[1] += b;
  digest[2] += c;
  digest[3] += d;
}

void md5_update_64 (md5_ctx_t *ctx, uint *w0, uint *w1, uint *w2, uint *w3, const int len)
{
  if (len == 0) return;

  const int pos = ctx->len & 63;

  ctx->len += len;

  if (pos == 0)
  {
    ctx->w0[0] = w0[0];
    ctx->w0[1] = w0[1];
    ctx->w0[2] = w0[2];
    ctx->w0[3] = w0[3];
    ctx->w1[0] = w1[0];
    ctx->w1[1] = w1[1];
    ctx->w1[2] = w1[2];
    ctx->w1[3] = w1[3];
    ctx->w2[0] = w2[0];
    ctx->w2[1] = w2[1];
    ctx->w2[2] = w2[2];
    ctx->w2[3] = w2[3];
    ctx->w3[0] = w3[0];
    ctx->w3[1] = w3[1];
    ctx->w3[2] = w3[2];
    ctx->w3[3] = w3[3];

    if (len == 64)
    {
      md5_transform (ctx->w0, ctx->w1, ctx->w2, ctx->w3, ctx->h);

      ctx->w0[0] = 0;
      ctx->w0[1] = 0;
      ctx->w0[2] = 0;
      ctx->w0[3] = 0;
      ctx->w1[0] = 0;
      ctx->w1[1] = 0;
      ctx->w1[2] = 0;
      ctx->w1[3] = 0;
      ctx->w2[0] = 0;
      ctx->w2[1] = 0;
      ctx->w2[2] = 0;
      ctx->w2[3] = 0;
      ctx->w3[0] = 0;
      ctx->w3[1] = 0;
      ctx->w3[2] = 0;
      ctx->w3[3] = 0;
    }
  }
  else
  {
    if ((pos + len) < 64)
    {
      switch_buffer_by_offset_le_S (w0, w1, w2, w3, pos);

      ctx->w0[0] |= w0[0];
      ctx->w0[1] |= w0[1];
      ctx->w0[2] |= w0[2];
      ctx->w0[3] |= w0[3];
      ctx->w1[0] |= w1[0];
      ctx->w1[1] |= w1[1];
      ctx->w1[2] |= w1[2];
      ctx->w1[3] |= w1[3];
      ctx->w2[0] |= w2[0];
      ctx->w2[1] |= w2[1];
      ctx->w2[2] |= w2[2];
      ctx->w2[3] |= w2[3];
      ctx->w3[0] |= w3[0];
      ctx->w3[1] |= w3[1];
      ctx->w3[2] |= w3[2];
      ctx->w3[3] |= w3[3];
    }
    else
    {
      uint c0[4] = { 0 };
      uint c1[4] = { 0 };
      uint c2[4] = { 0 };
      uint c3[4] = { 0 };

      switch_buffer_by_offset_carry_le_S (w0, w1, w2, w3, c0, c1, c2, c3, pos);

      ctx->w0[0] |= w0[0];
      ctx->w0[1] |= w0[1];
      ctx->w0[2] |= w0[2];
      ctx->w0[3] |= w0[3];
      ctx->w1[0] |= w1[0];
      ctx->w1[1] |= w1[1];
      ctx->w1[2] |= w1[2];
      ctx->w1[3] |= w1[3];
      ctx->w2[0] |= w2[0];
      ctx->w2[1] |= w2[1];
      ctx->w2[2] |= w2[2];
      ctx->w2[3] |= w2[3];
      ctx->w3[0] |= w3[0];
      ctx->w3[1] |= w3[1];
      ctx->w3[2] |= w3[2];
      ctx->w3[3] |= w3[3];

      md5_transform (ctx->w0, ctx->w1, ctx->w2, ctx->w3, ctx->h);

      ctx->w0[0] = c0[0];
      ctx->w0[1] = c0[1];
      ctx->w0[2] = c0[2];
      ctx->w0[3] = c0[3];
      ctx->w1[0] = c1[0];
      ctx->w1[1] = c1[1];
      ctx->w1[2] = c1[2];
      ctx->w1[3] = c1[3];
      ctx->w2[0] = c2[0];
      ctx->w2[1] = c2[1];
      ctx->w2[2] = c2[2];
      ctx->w2[3] = c2[3];
      ctx->w3[0] = c3[0];
      ctx->w3[1] = c3[1];
      ctx->w3[2] = c3[2];
      ctx->w3[3] = c3[3];
    }
  }
}

void md5_update (md5_ctx_t *ctx, const uint *w, const int len)
{
  uint w0[4];
  uint w1[4];
  uint w2[4];
  uint w3[4];

  int pos1;
  int pos4;

  for (pos1 = 0, pos4 = 0; pos1 < len - 64; pos1 += 64, pos4 += 16)
  {
    w0[0] = w[pos4 +  0];
    w0[1] = w[pos4 +  1];
    w0[2] = w[pos4 +  2];
    w0[3] = w[pos4 +  3];
    w1[0] = w[pos4 +  4];
    w1[1] = w[pos4 +  5];
    w1[2] = w[pos4 +  6];
    w1[3] = w[pos4 +  7];
    w2[0] = w[pos4 +  8];
    w2[1] = w[pos4 +  9];
    w2[2] = w[pos4 + 10];
    w2[3] = w[pos4 + 11];
    w3[0] = w[pos4 + 12];
    w3[1] = w[pos4 + 13];
    w3[2] = w[pos4 + 14];
    w3[3] = w[pos4 + 15];

    md5_update_64 (ctx, w0, w1, w2, w3, 64);
  }

  w0[0] = w[pos4 +  0];
  w0[1] = w[pos4 +  1];
  w0[2] = w[pos4 +  2];
  w0[3] = w[pos4 +  3];
  w1[0] = w[pos4 +  4];
  w1[1] = w[pos4 +  5];
  w1[2] = w[pos4 +  6];
  w1[3] = w[pos4 +  7];
  w2[0] = w[pos4 +  8];
  w2[1] = w[pos4 +  9];
  w2[2] = w[pos4 + 10];
  w2[3] = w[pos4 + 11];
  w3[0] = w[pos4 + 12];
  w3[1] = w[pos4 + 13];
  w3[2] = w[pos4 + 14];
  w3[3] = w[pos4 + 15];

  md5_update_64 (ctx, w0, w1, w2, w3, len - pos1);
}

void md5_final (md5_ctx_t *ctx)
{
  const int pos = ctx->len & 63;

  append_0x80_4x4_S (ctx->w0, ctx->w1, ctx->w2, ctx->w3, pos);

  if (pos >= 56)
  {
    md5_transform (ctx->w0, ctx->w1, ctx->w2, ctx->w3, ctx->h);

    ctx->w0[0] = 0;
    ctx->w0[1] = 0;
    ctx->w0[2] = 0;
    ctx->w0[3] = 0;
    ctx->w1[0] = 0;
    ctx->w1[1] = 0;
    ctx->w1[2] = 0;
    ctx->w1[3] = 0;
    ctx->w2[0] = 0;
    ctx->w2[1] = 0;
    ctx->w2[2] = 0;
    ctx->w2[3] = 0;
    ctx->w3[0] = 0;
    ctx->w3[1] = 0;
    ctx->w3[2] = 0;
    ctx->w3[3] = 0;
  }

  ctx->w3[2] = ctx->len * 8;
  ctx->w3[3] = 0;

  md5_transform (ctx->w0, ctx->w1, ctx->w2, ctx->w3, ctx->h);
}

__kernel void md5d(__global const char* concat,
                                __global char* results,
                                __global volatile int* count) {
    int lx = get_local_id(0);
    //int ly = get_local_id(1);

    char m_base[19];
    char m_hash[33];
    char m_diff[33];
    char m_stat[258];

    // init (nonce?)
    for (int i = 0; i < 64; i++) {
        m_stat[i] = concat[i];
    }

    for (int i = 18 + 30; i < 128 - (13 + 30); i++) {
        m_stat[i] = filler_chars[i % 85];
        //barrier(CLK_LOCAL_MEM_FENCE);
    }

    m_base[18] = '\0';
    m_hash[32] = '\0';
    m_diff[32] = '\0';

    md5_ctx_t m_md5_ctx;

    char *stat_1 = ((char*)m_stat) + 129;

    for(int i = 0; i < 1024; i++) {
        // stat

        
        for (int i = 0; i < 128; i++) {
            stat_1[i] = m_stat[i];
        }

        stat_1[128] = stat_1[0];

        for( int row = 0; row < 128; ++row ) {
            for( int col = 0; col < 128; ++col ) {
                stat_1[col] = nosohash_chars_table[stat_1[col + 0] + stat_1[col + 1]];
            }

            stat_1[128]=stat_1[0];
        }

        // pack
        m_hash[ 0] = hex_dec2char_table[nosohash_chars_table[m_stat[129+  0]+m_stat[129+  1]+m_stat[129+  2]+m_stat[129+  3]]%16];
        m_hash[ 1] = hex_dec2char_table[nosohash_chars_table[m_stat[129+  4]+m_stat[129+  5]+m_stat[129+  6]+m_stat[129+  7]]%16];
        m_hash[ 2] = hex_dec2char_table[nosohash_chars_table[m_stat[129+  8]+m_stat[129+  9]+m_stat[129+ 10]+m_stat[129+ 11]]%16];
        m_hash[ 3] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 12]+m_stat[129+ 13]+m_stat[129+ 14]+m_stat[129+ 15]]%16];
        m_hash[ 4] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 16]+m_stat[129+ 17]+m_stat[129+ 18]+m_stat[129+ 19]]%16];
        m_hash[ 5] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 20]+m_stat[129+ 21]+m_stat[129+ 22]+m_stat[129+ 23]]%16];
        m_hash[ 6] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 24]+m_stat[129+ 25]+m_stat[129+ 26]+m_stat[129+ 27]]%16];
        m_hash[ 7] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 28]+m_stat[129+ 29]+m_stat[129+ 30]+m_stat[129+ 31]]%16];
        m_hash[ 8] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 32]+m_stat[129+ 33]+m_stat[129+ 34]+m_stat[129+ 35]]%16];
        m_hash[ 9] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 36]+m_stat[129+ 37]+m_stat[129+ 38]+m_stat[129+ 39]]%16];
        m_hash[10] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 40]+m_stat[129+ 41]+m_stat[129+ 42]+m_stat[129+ 43]]%16];
        m_hash[11] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 44]+m_stat[129+ 45]+m_stat[129+ 46]+m_stat[129+ 47]]%16];
        m_hash[12] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 48]+m_stat[129+ 49]+m_stat[129+ 50]+m_stat[129+ 51]]%16];
        m_hash[13] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 52]+m_stat[129+ 53]+m_stat[129+ 54]+m_stat[129+ 55]]%16];
        m_hash[14] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 56]+m_stat[129+ 57]+m_stat[129+ 58]+m_stat[129+ 59]]%16];
        m_hash[15] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 60]+m_stat[129+ 61]+m_stat[129+ 62]+m_stat[129+ 63]]%16];
        m_hash[16] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 64]+m_stat[129+ 65]+m_stat[129+ 66]+m_stat[129+ 67]]%16];
        m_hash[17] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 68]+m_stat[129+ 69]+m_stat[129+ 70]+m_stat[129+ 71]]%16];
        m_hash[18] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 72]+m_stat[129+ 73]+m_stat[129+ 74]+m_stat[129+ 75]]%16];
        m_hash[19] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 76]+m_stat[129+ 77]+m_stat[129+ 78]+m_stat[129+ 79]]%16];
        m_hash[20] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 80]+m_stat[129+ 81]+m_stat[129+ 82]+m_stat[129+ 83]]%16];
        m_hash[21] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 84]+m_stat[129+ 85]+m_stat[129+ 86]+m_stat[129+ 87]]%16];
        m_hash[22] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 88]+m_stat[129+ 89]+m_stat[129+ 90]+m_stat[129+ 91]]%16];
        m_hash[23] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 92]+m_stat[129+ 93]+m_stat[129+ 94]+m_stat[129+ 95]]%16];
        m_hash[24] = hex_dec2char_table[nosohash_chars_table[m_stat[129+ 96]+m_stat[129+ 97]+m_stat[129+ 98]+m_stat[129+ 99]]%16];
        m_hash[25] = hex_dec2char_table[nosohash_chars_table[m_stat[129+100]+m_stat[129+101]+m_stat[129+102]+m_stat[129+103]]%16];
        m_hash[26] = hex_dec2char_table[nosohash_chars_table[m_stat[129+104]+m_stat[129+105]+m_stat[129+106]+m_stat[129+107]]%16];
        m_hash[27] = hex_dec2char_table[nosohash_chars_table[m_stat[129+108]+m_stat[129+109]+m_stat[129+110]+m_stat[129+111]]%16];
        m_hash[28] = hex_dec2char_table[nosohash_chars_table[m_stat[129+112]+m_stat[129+113]+m_stat[129+114]+m_stat[129+115]]%16];
        m_hash[29] = hex_dec2char_table[nosohash_chars_table[m_stat[129+116]+m_stat[129+117]+m_stat[129+118]+m_stat[129+119]]%16];
        m_hash[30] = hex_dec2char_table[nosohash_chars_table[m_stat[129+120]+m_stat[129+121]+m_stat[129+122]+m_stat[129+123]]%16];
        m_hash[31] = hex_dec2char_table[nosohash_chars_table[m_stat[129+124]+m_stat[129+125]+m_stat[129+126]+m_stat[129+127]]%16];

        // md5
        md5_init( &m_md5_ctx );
        md5_update( &m_md5_ctx, (uint*)m_hash, 32 );
        md5_final( &m_md5_ctx );

        if (m_md5_ctx.h[0] == 9) {
            int f = atomic_inc(count);
            results[f] = m_hash[0];
        }

        m_stat[4]++;

        /*        i = i % 1000000000;
        m_stat[ 9] = i /   100000000 + '0';
                i = i %   100000000;
        m_stat[10] = i /    10000000 + '0';
                i = i %    10000000;
        m_stat[11] = i /     1000000 + '0';
                i = i %     1000000;
        m_stat[12] = i /      100000 + '0';
                i = i %      100000;
        m_stat[13] = i /        10000 + '0';
                i = i %        10000;
        m_stat[14] = i /         1000 + '0';
                i = i %         1000;
        m_stat[15] = i /           100 + '0';
                i = i %           100;
        m_stat[16] = i /            10 + '0';
                i = i %            10;
        m_stat[17] = i                 + '0';*/
    }
}