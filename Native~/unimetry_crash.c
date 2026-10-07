#if defined(__APPLE__)

#include <errno.h>
#include <stdio.h>
#include <fcntl.h>
#include <pthread.h>
#include <signal.h>
#include <stdint.h>
#include <string.h>
#include <time.h>
#include <unistd.h>
#include <sys/ucontext.h>

#define CRASH_PATH_MAX 512
#define CRASH_JSON_MAX 8192
#define CRASH_TEXT_MAX 256
#define CRASH_BREADCRUMB_MAX 2048

static char crash_dir[CRASH_PATH_MAX];
static char breadcrumb_path[CRASH_PATH_MAX];
static char device_model[CRASH_TEXT_MAX];
static char os_type[64];
static char build_id[CRASH_TEXT_MAX];
static volatile sig_atomic_t installed;
static volatile sig_atomic_t handling;
static struct sigaction previous_segv;
static struct sigaction previous_bus;
static struct sigaction previous_abrt;
static struct sigaction previous_ill;
static struct sigaction previous_fpe;

static void copy_text(char* destination, size_t capacity, const char* source)
{
    size_t index;

    if (destination == NULL || capacity == 0)
    {
        return;
    }

    destination[0] = '\0';
    if (source == NULL)
    {
        return;
    }

    for (index = 0; index + 1 < capacity && source[index] != '\0'; index++)
    {
        destination[index] = source[index];
    }

    destination[index] = '\0';
}

static size_t text_length(const char* value)
{
    size_t length = 0;
    if (value == NULL)
    {
        return 0;
    }

    while (value[length] != '\0')
    {
        length++;
    }

    return length;
}

static void append_text(char* destination, size_t capacity, size_t* used, const char* value)
{
    size_t index;
    if (destination == NULL || used == NULL || value == NULL || *used >= capacity)
    {
        return;
    }

    for (index = 0; value[index] != '\0' && *used + 1 < capacity; index++)
    {
        destination[*used] = value[index];
        (*used)++;
    }

    destination[*used] = '\0';
}

static void append_json_escaped(char* destination, size_t capacity, size_t* used, const char* value)
{
    size_t index;
    if (value == NULL)
    {
        return;
    }

    for (index = 0; value[index] != '\0'; index++)
    {
        char extra[8];
        const char* piece = extra;
        unsigned char character = (unsigned char)value[index];
        extra[0] = (char)character;
        extra[1] = '\0';
        if (character == '\\' || character == '"')
        {
            extra[0] = '\\';
            extra[1] = (char)character;
            extra[2] = '\0';
        }
        else if (character == '\n')
        {
            extra[0] = '\\';
            extra[1] = 'n';
            extra[2] = '\0';
        }
        else if (character == '\t')
        {
            extra[0] = '\\';
            extra[1] = 't';
            extra[2] = '\0';
        }
        else if (character == '\r')
        {
            extra[0] = '\\';
            extra[1] = 'r';
            extra[2] = '\0';
        }
        else if (character < 32)
        {
            piece = "";
        }

        append_text(destination, capacity, used, piece);
    }
}

static void append_hex(char* destination, size_t capacity, size_t* used, uint64_t value, int digits)
{
    char buffer[17];
    int index;
    for (index = digits - 1; index >= 0; index--)
    {
        int nibble = (int)(value & 0xF);
        buffer[index] = (char)(nibble < 10 ? '0' + nibble : 'a' + (nibble - 10));
        value >>= 4;
    }

    buffer[digits] = '\0';
    append_text(destination, capacity, used, buffer);
}

static void append_u64(char* destination, size_t capacity, size_t* used, uint64_t value)
{
    char buffer[32];
    int index = (int)sizeof(buffer) - 1;
    buffer[index] = '\0';
    if (value == 0)
    {
        append_text(destination, capacity, used, "0");
        return;
    }

    while (value > 0 && index > 0)
    {
        index--;
        buffer[index] = (char)('0' + (value % 10));
        value /= 10;
    }

    append_text(destination, capacity, used, buffer + index);
}

static const char* signal_name(int signal_number)
{
    switch (signal_number)
    {
        case SIGSEGV:
            return "SIGSEGV";
        case SIGBUS:
            return "SIGBUS";
        case SIGABRT:
            return "SIGABRT";
        case SIGILL:
            return "SIGILL";
        case SIGFPE:
            return "SIGFPE";
        default:
            return "SIGUNKNOWN";
    }
}

static void read_breadcrumbs(char* destination, size_t capacity)
{
    int file;
    ssize_t count;
    if (destination == NULL || capacity == 0)
    {
        return;
    }

    destination[0] = '\0';
    if (breadcrumb_path[0] == '\0')
    {
        return;
    }

    file = open(breadcrumb_path, O_RDONLY);
    if (file < 0)
    {
        return;
    }

    count = read(file, destination, capacity - 1);
    if (count < 0)
    {
        count = 0;
    }

    destination[count] = '\0';
    close(file);
}

static void append_registers(char* destination, size_t capacity, size_t* used, ucontext_t* context)
{
    if (context == NULL || context->uc_mcontext == NULL)
    {
        return;
    }

#if defined(__aarch64__)
    append_text(destination, capacity, used, "pc=0x");
    append_hex(destination, capacity, used, context->uc_mcontext->__ss.__pc, 16);
    append_text(destination, capacity, used, " sp=0x");
    append_hex(destination, capacity, used, context->uc_mcontext->__ss.__sp, 16);
    append_text(destination, capacity, used, " lr=0x");
    append_hex(destination, capacity, used, context->uc_mcontext->__ss.__lr, 16);
    append_text(destination, capacity, used, " x0=0x");
    append_hex(destination, capacity, used, context->uc_mcontext->__ss.__x[0], 16);
#elif defined(__x86_64__)
    append_text(destination, capacity, used, "rip=0x");
    append_hex(destination, capacity, used, context->uc_mcontext->__ss.__rip, 16);
    append_text(destination, capacity, used, " rsp=0x");
    append_hex(destination, capacity, used, context->uc_mcontext->__ss.__rsp, 16);
    append_text(destination, capacity, used, " rax=0x");
    append_hex(destination, capacity, used, context->uc_mcontext->__ss.__rax, 16);
#else
    (void)used;
#endif
}

static void write_artifact(int signal_number, ucontext_t* context)
{
    char id[33];
    char path[CRASH_PATH_MAX];
    char temp_path[CRASH_PATH_MAX];
    char json[CRASH_JSON_MAX];
    char breadcrumbs[CRASH_BREADCRUMB_MAX];
    char registers[256];
    size_t used = 0;
    size_t path_used = 0;
    size_t id_used = 0;
    struct timespec time_spec;
    uint64_t unix_ms;
    uint64_t thread_id = (uint64_t)(uintptr_t)pthread_self();
    int file;

    if (crash_dir[0] == '\0')
    {
        return;
    }

    if (clock_gettime(CLOCK_REALTIME, &time_spec) != 0)
    {
        time_spec.tv_sec = 0;
        time_spec.tv_nsec = 0;
    }

    unix_ms = ((uint64_t)time_spec.tv_sec * 1000ULL) + ((uint64_t)time_spec.tv_nsec / 1000000ULL);
    append_hex(id, sizeof(id), &id_used, (uint64_t)time_spec.tv_sec, 16);
    append_hex(id, sizeof(id), &id_used, ((uint64_t)time_spec.tv_nsec << 16) ^ (uint64_t)getpid(), 16);

    append_text(path, sizeof(path), &path_used, crash_dir);
    append_text(path, sizeof(path), &path_used, "/");
    append_text(path, sizeof(path), &path_used, id);
    append_text(path, sizeof(path), &path_used, ".crash.json");
    path_used = 0;
    append_text(temp_path, sizeof(temp_path), &path_used, path);
    append_text(temp_path, sizeof(temp_path), &path_used, ".tmp");

    registers[0] = '\0';
    used = 0;
    append_registers(registers, sizeof(registers), &used, context);
    read_breadcrumbs(breadcrumbs, sizeof(breadcrumbs));

    used = 0;
    append_text(json, sizeof(json), &used, "{\"schema\":1,\"id\":\"");
    append_json_escaped(json, sizeof(json), &used, id);
    append_text(json, sizeof(json), &used, "\",\"capturedAtUnixMs\":");
    append_u64(json, sizeof(json), &used, unix_ms);
    append_text(json, sizeof(json), &used, ",\"signal\":\"0x");
    append_hex(json, sizeof(json), &used, (uint64_t)signal_number, 8);
    append_text(json, sizeof(json), &used, "\",\"exceptionType\":\"");
    append_json_escaped(json, sizeof(json), &used, signal_name(signal_number));
    append_text(json, sizeof(json), &used, "\",\"message\":\"Native crash ");
    append_json_escaped(json, sizeof(json), &used, signal_name(signal_number));
    append_text(json, sizeof(json), &used, "\",\"threadName\":\"");
    append_u64(json, sizeof(json), &used, thread_id);
    append_text(json, sizeof(json), &used, "\",\"registers\":\"");
    append_json_escaped(json, sizeof(json), &used, registers);
    append_text(json, sizeof(json), &used, "\",\"managedStack\":\"\",\"breadcrumbs\":\"");
    append_json_escaped(json, sizeof(json), &used, breadcrumbs);
    append_text(json, sizeof(json), &used, "\",\"deviceModel\":\"");
    append_json_escaped(json, sizeof(json), &used, device_model);
    append_text(json, sizeof(json), &used, "\",\"osType\":\"");
    append_json_escaped(json, sizeof(json), &used, os_type);
    append_text(json, sizeof(json), &used, "\",\"buildId\":\"");
    append_json_escaped(json, sizeof(json), &used, build_id);
    append_text(json, sizeof(json), &used, "\",\"minidumpFile\":\"\",\"minidumpBytes\":0}");

    file = open(temp_path, O_WRONLY | O_CREAT | O_TRUNC, 0600);
    if (file < 0)
    {
        return;
    }

    if (write(file, json, text_length(json)) >= 0)
    {
        close(file);
        rename(temp_path, path);
        return;
    }

    close(file);
}

static void handle_signal(int signal_number, siginfo_t* info, void* context)
{
    struct sigaction* previous = NULL;
    (void)info;
    if (handling)
    {
        return;
    }

    handling = 1;
    write_artifact(signal_number, (ucontext_t*)context);
    switch (signal_number)
    {
        case SIGSEGV:
            previous = &previous_segv;
            break;
        case SIGBUS:
            previous = &previous_bus;
            break;
        case SIGABRT:
            previous = &previous_abrt;
            break;
        case SIGILL:
            previous = &previous_ill;
            break;
        case SIGFPE:
            previous = &previous_fpe;
            break;
        default:
            break;
    }

    if (previous != NULL && previous->sa_sigaction != NULL && (previous->sa_flags & SA_SIGINFO))
    {
        previous->sa_sigaction(signal_number, info, context);
        return;
    }

    signal(signal_number, SIG_DFL);
    raise(signal_number);
}

static int install_one(int signal_number, struct sigaction* previous)
{
    struct sigaction action;
    memset(&action, 0, sizeof(action));
    action.sa_sigaction = handle_signal;
    action.sa_flags = SA_SIGINFO;
    sigemptyset(&action.sa_mask);
    return sigaction(signal_number, &action, previous);
}

__attribute__((visibility("default")))
void unimetry_install(
    const char* crash_directory,
    const char* breadcrumb_file,
    const char* device,
    const char* operating_system,
    const char* build)
{
    if (installed)
    {
        return;
    }

    copy_text(crash_dir, sizeof(crash_dir), crash_directory);
    copy_text(breadcrumb_path, sizeof(breadcrumb_path), breadcrumb_file);
    copy_text(device_model, sizeof(device_model), device);
    copy_text(os_type, sizeof(os_type), operating_system);
    copy_text(build_id, sizeof(build_id), build);
    install_one(SIGSEGV, &previous_segv);
    install_one(SIGBUS, &previous_bus);
    install_one(SIGABRT, &previous_abrt);
    install_one(SIGILL, &previous_ill);
    install_one(SIGFPE, &previous_fpe);
    installed = 1;
}

__attribute__((visibility("default")))
void unimetry_uninstall(void)
{
    if (!installed)
    {
        return;
    }

    sigaction(SIGSEGV, &previous_segv, NULL);
    sigaction(SIGBUS, &previous_bus, NULL);
    sigaction(SIGABRT, &previous_abrt, NULL);
    sigaction(SIGILL, &previous_ill, NULL);
    sigaction(SIGFPE, &previous_fpe, NULL);
    installed = 0;
}

#else

void unimetry_install(
    const char* crash_directory,
    const char* breadcrumb_file,
    const char* device,
    const char* operating_system,
    const char* build)
{
    (void)crash_directory;
    (void)breadcrumb_file;
    (void)device;
    (void)operating_system;
    (void)build;
}

void unimetry_uninstall(void)
{
}

#endif
