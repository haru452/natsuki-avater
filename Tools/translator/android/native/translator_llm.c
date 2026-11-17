// Minimal C API over llama.cpp for the translator app, with JNI entry points for app/NativeLlmJni.kt.
// Built for arm64 with dotprod + i8mm (see build_android.sh); one model + one context per handle.
// Greedy decoding; the whole prompt is evaluated each call (prompts are short).
#include <stdatomic.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#define LLAMA_DEPRECATED_SILENT
#include "llama.h"
#include <jni.h>

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

// --- JNI (com.haru452.vrctranslator.app.NativeLlmJni, @JvmStatic) -------------------------------------
// Text crosses as UTF-8 byte arrays: JNI's "modified UTF-8" strings mangle emoji and other 4-byte characters.

JNIEXPORT jlong JNICALL Java_com_haru452_vrctranslator_app_NativeLlmJni_load(
        JNIEnv *env, jclass cls, jstring path, jint threads, jint ctx) {
    const char *p = (*env)->GetStringUTFChars(env, path, NULL);
    void *h = tl_load(p, threads, ctx);
    (*env)->ReleaseStringUTFChars(env, path, p);
    return (jlong)(intptr_t)h;
}

JNIEXPORT jbyteArray JNICALL Java_com_haru452_vrctranslator_app_NativeLlmJni_complete(
        JNIEnv *env, jclass cls, jlong handle, jbyteArray prompt, jint max_tokens) {
    jsize n = (*env)->GetArrayLength(env, prompt);
    char *text = malloc(n + 1);
    (*env)->GetByteArrayRegion(env, prompt, 0, n, (jbyte *)text);
    text[n] = 0;
    const int cap = 8192;
    char *out = malloc(cap);
    int len = tl_complete((void *)(intptr_t)handle, text, max_tokens, out, cap);
    free(text);
    if (len < 0) {
        free(out);
        return NULL;
    }
    jbyteArray result = (*env)->NewByteArray(env, len);
    (*env)->SetByteArrayRegion(env, result, 0, len, (const jbyte *)out);
    free(out);
    return result;
}

JNIEXPORT void JNICALL Java_com_haru452_vrctranslator_app_NativeLlmJni_cancel(JNIEnv *env, jclass cls, jlong handle) {
    tl_cancel((void *)(intptr_t)handle);
}

JNIEXPORT void JNICALL Java_com_haru452_vrctranslator_app_NativeLlmJni_free(JNIEnv *env, jclass cls, jlong handle) {
    tl_free((void *)(intptr_t)handle);
}
