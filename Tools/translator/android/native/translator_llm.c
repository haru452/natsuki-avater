// Minimal C API over llama.cpp for the translator app (P/Invoke from C#).
// Built for arm64 with dotprod + i8mm (see build_android.sh); one model + one context per handle.
// Greedy decoding; the whole prompt is evaluated each call (prompts are short).
#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>

#define LLAMA_DEPRECATED_SILENT
#include "llama.h"

typedef struct {
    struct llama_model *model;
    struct llama_context *ctx;
    const struct llama_vocab *vocab;
    atomic_int cancel;
} tl_handle;

static bool tl_abort(void *data) {
    return atomic_load(&((tl_handle *)data)->cancel) != 0;
}

void *tl_load(const char *path, int n_threads, int n_ctx) {
    static int backend_ready = 0;
    if (!backend_ready) {
        llama_backend_init();
        backend_ready = 1;
    }
    struct llama_model_params mp = llama_model_default_params();
    mp.n_gpu_layers = 0;
    mp.use_mmap = false;  // models live on /sdcard (FUSE): page faults there are very slow
    struct llama_model *model = llama_model_load_from_file(path, mp);
    if (!model) return NULL;
    struct llama_context_params cp = llama_context_default_params();
    cp.n_ctx = n_ctx;
    cp.n_batch = n_ctx;
    cp.n_threads = n_threads;
    cp.n_threads_batch = n_threads;
    struct llama_context *ctx = llama_init_from_model(model, cp);
    if (!ctx) {
        llama_model_free(model);
        return NULL;
    }
    tl_handle *h = calloc(1, sizeof(tl_handle));
    h->model = model;
    h->ctx = ctx;
    h->vocab = llama_model_get_vocab(model);
    llama_set_abort_callback(ctx, tl_abort, h);
    return h;
}

/// Writes the completion (UTF-8, NUL-terminated) into out. Returns its length, or < 0 on error
/// (-1 tokenize, -2 decode, -3 cancelled).
int tl_complete(void *handle, const char *prompt, int max_tokens, char *out, int out_len) {
    tl_handle *h = handle;
    atomic_store(&h->cancel, 0);
    llama_memory_clear(llama_get_memory(h->ctx), true);
    out[0] = 0;

    int n_prompt = -llama_tokenize(h->vocab, prompt, (int)strlen(prompt), NULL, 0, true, true);
    if (n_prompt <= 0) return -1;
    llama_token *tokens = malloc(sizeof(llama_token) * n_prompt);
    if (llama_tokenize(h->vocab, prompt, (int)strlen(prompt), tokens, n_prompt, true, true) < 0) {
        free(tokens);
        return -1;
    }
    int rc = llama_decode(h->ctx, llama_batch_get_one(tokens, n_prompt));
    free(tokens);
    if (rc == 2) return -3;
    if (rc != 0) return -2;

    struct llama_sampler *smpl = llama_sampler_init_greedy();
    int used = 0;
    for (int i = 0; i < max_tokens; i++) {
        llama_token tok = llama_sampler_sample(smpl, h->ctx, -1);
        if (llama_vocab_is_eog(h->vocab, tok)) break;
        char piece[256];
        int n = llama_token_to_piece(h->vocab, tok, piece, sizeof(piece), 0, false);
        if (n > 0 && used + n < out_len) {
            memcpy(out + used, piece, n);
            used += n;
            out[used] = 0;
        }
        rc = llama_decode(h->ctx, llama_batch_get_one(&tok, 1));
        if (rc == 2) { llama_sampler_free(smpl); return -3; }
        if (rc != 0) break;
    }
    llama_sampler_free(smpl);
    return used;
}

void tl_cancel(void *handle) {
    atomic_store(&((tl_handle *)handle)->cancel, 1);
}

void tl_free(void *handle) {
    tl_handle *h = handle;
    if (!h) return;
    llama_free(h->ctx);
    llama_model_free(h->model);
    free(h);
}
