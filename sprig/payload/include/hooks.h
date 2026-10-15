#pragma once

#include <stdint.h>
#include <stdbool.h>

#define HOOK_TRAMPOLINE_SIZE 28

typedef struct {
    uint32_t site;
    uint32_t trampoline;
    void *handler;
    const char *name;
} hook_t;

#define HOOK(_site, _trampoline, _handler, _name) { \
    .site = (_site),                                \
    .trampoline = (_trampoline),                    \
    .handler = (void *)(_handler),                  \
    .name = (_name)                                 \
}

bool hook_install(const hook_t *hook);
void hook_install_all(void);

/* bl has ±128MB range limit */
static inline uint32_t encode_bl(uint32_t from, uint32_t to) {
    int64_t offset = (int64_t)to - (int64_t)from;

    if (offset > 0x7FFFFFC || offset < -0x8000000)
        return 0;

    uint32_t imm26 = ((uint32_t)(offset >> 2)) & 0x03FFFFFF;
    return 0x94000000 | imm26;
}

static inline uint32_t encode_b(uint32_t from, uint32_t to) {
    int64_t offset = (int64_t)to - (int64_t)from;

    if (offset > 0x7FFFFFC || offset < -0x8000000)
        return 0;

    uint32_t imm26 = ((uint32_t)(offset >> 2)) & 0x03FFFFFF;
    return 0x14000000 | imm26;
}
