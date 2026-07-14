#define _POSIX_C_SOURCE 200809L

#include <errno.h>
#include <poll.h>
#include <signal.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>

/*
 * Deliberately declare only the stable public libICE/libSM ABI used by this
 * smoke. This keeps the test runnable on release images that ship the runtime
 * libraries but not the X.Org development headers.
 */
typedef void *IceListenObj;
typedef void *IceConn;
typedef void *SmsConn;
typedef void *SmPointer;
typedef int Status;
typedef int Bool;

typedef struct {
    char *protocol_name;
    char *network_id;
    char *auth_name;
    unsigned short auth_data_length;
    char *auth_data;
} IceAuthDataEntry;

typedef struct {
    char *protocol_name;
    unsigned short protocol_data_length;
    char *protocol_data;
    char *network_id;
    char *auth_name;
    unsigned short auth_data_length;
    char *auth_data;
} IceAuthFileEntry;

typedef struct {
    void *callback;
    SmPointer manager_data;
} SmsCallbackEntry;

typedef struct {
    SmsCallbackEntry register_client;
    SmsCallbackEntry interact_request;
    SmsCallbackEntry interact_done;
    SmsCallbackEntry save_yourself_request;
    SmsCallbackEntry save_yourself_phase2_request;
    SmsCallbackEntry save_yourself_done;
    SmsCallbackEntry close_connection;
    SmsCallbackEntry set_properties;
    SmsCallbackEntry delete_properties;
    SmsCallbackEntry get_properties;
} SmsCallbacks;

typedef Status (*SmsNewClientProc)(
    SmsConn, SmPointer, unsigned long *, SmsCallbacks *, char **);
typedef Bool (*IceHostBasedAuthProc)(char *);

extern Status SmsInitialize(
    char *, char *, SmsNewClientProc, SmPointer, IceHostBasedAuthProc,
    int, char *);
extern Status SmsRegisterClientReply(SmsConn, char *);
extern void SmsSaveYourself(SmsConn, int, Bool, int, Bool);
extern void SmsInteract(SmsConn);
extern void SmsShutdownCancelled(SmsConn);
extern void SmsReturnProperties(SmsConn, int, void **);
extern void SmFreeProperty(void *);

extern Status IceListenForConnections(int *, IceListenObj **, int, char *);
extern char *IceComposeNetworkIdList(int, IceListenObj *);
extern int IceGetListenConnectionNumber(IceListenObj);
extern IceConn IceAcceptConnection(IceListenObj, int *);
extern int IceConnectionNumber(IceConn);
extern int IceProcessMessages(IceConn, void *, void *);
extern void IceFreeListenObjs(int, IceListenObj *);
extern int IceCloseConnection(IceConn);
extern void *IceSetIOErrorHandler(void (*)(IceConn));
extern char *IceGenerateMagicCookie(int);
extern void IceSetPaAuthData(int, IceAuthDataEntry *);
extern Status IceWriteAuthFileEntry(FILE *, IceAuthFileEntry *);

static SmsConn g_sms;
static int g_registered;
static int g_save_sent;
static int g_interact_requested;
static int g_cancelled;
static int g_save_done;
static int g_io_error;

static void ice_io_error(IceConn connection)
{
    (void)connection;
    g_io_error = 1;
}

static Status register_client(SmsConn connection, SmPointer data, char *previous_id)
{
    (void)data;
    (void)previous_id;
    g_sms = connection;
    if (!SmsRegisterClientReply(connection, "jalium-xsmp-smoke-client"))
        return 0;
    g_registered = 1;
    return 1;
}

static void interact_request(SmsConn connection, SmPointer data, int dialog_type)
{
    (void)data;
    (void)dialog_type;
    g_interact_requested = 1;
    SmsInteract(connection);
}

static void interact_done(SmsConn connection, SmPointer data, Bool cancel_shutdown)
{
    (void)data;
    if (cancel_shutdown) {
        g_cancelled = 1;
        SmsShutdownCancelled(connection);
    }
}

static void save_yourself_request(
    SmsConn connection, SmPointer data, int save_type, Bool shutdown,
    int interact_style, Bool fast, Bool global)
{
    (void)connection;
    (void)data;
    (void)save_type;
    (void)shutdown;
    (void)interact_style;
    (void)fast;
    (void)global;
}

static void save_yourself_phase2_request(SmsConn connection, SmPointer data)
{
    (void)connection;
    (void)data;
}

static void save_yourself_done(SmsConn connection, SmPointer data, Bool success)
{
    (void)connection;
    (void)data;
    (void)success;
    g_save_done = 1;
}

static void close_connection(
    SmsConn connection, SmPointer data, int count, char **reasons)
{
    (void)connection;
    (void)data;
    (void)count;
    (void)reasons;
}

static void set_properties(
    SmsConn connection, SmPointer data, int property_count, void **properties)
{
    (void)connection;
    (void)data;
    for (int index = 0; index < property_count; ++index)
        SmFreeProperty(properties[index]);
    free(properties);
}

static void delete_properties(
    SmsConn connection, SmPointer data, int property_count, char **names)
{
    (void)connection;
    (void)data;
    (void)property_count;
    (void)names;
}

static void get_properties(SmsConn connection, SmPointer data)
{
    (void)data;
    SmsReturnProperties(connection, 0, NULL);
}

static Status new_client(
    SmsConn connection,
    SmPointer manager_data,
    unsigned long *mask,
    SmsCallbacks *callbacks,
    char **failure_reason)
{
    (void)connection;
    (void)manager_data;
    *failure_reason = NULL;
    *mask = 0x3ffUL;
    memset(callbacks, 0, sizeof(*callbacks));
    callbacks->register_client.callback = (void *)register_client;
    callbacks->interact_request.callback = (void *)interact_request;
    callbacks->interact_done.callback = (void *)interact_done;
    callbacks->save_yourself_request.callback = (void *)save_yourself_request;
    callbacks->save_yourself_phase2_request.callback = (void *)save_yourself_phase2_request;
    callbacks->save_yourself_done.callback = (void *)save_yourself_done;
    callbacks->close_connection.callback = (void *)close_connection;
    callbacks->set_properties.callback = (void *)set_properties;
    callbacks->delete_properties.callback = (void *)delete_properties;
    callbacks->get_properties.callback = (void *)get_properties;
    return 1;
}

static Bool allow_host(char *host_name)
{
    (void)host_name;
    return 1;
}

static int64_t monotonic_milliseconds(void)
{
    struct timespec now;
    clock_gettime(CLOCK_MONOTONIC, &now);
    return (int64_t)now.tv_sec * 1000 + now.tv_nsec / 1000000;
}

static int setup_authentication(
    const char *network_ids,
    char *authority_path,
    size_t authority_path_size)
{
    size_t network_count = 1;
    for (const char *cursor = network_ids; *cursor; ++cursor) {
        if (*cursor == ',')
            ++network_count;
    }

    size_t entry_count = network_count * 2;
    IceAuthDataEntry *memory_entries =
        (IceAuthDataEntry *)calloc(entry_count, sizeof(*memory_entries));
    IceAuthFileEntry *file_entries =
        (IceAuthFileEntry *)calloc(entry_count, sizeof(*file_entries));
    char *network_copy = strdup(network_ids);
    char *cookie = IceGenerateMagicCookie(16);
    if (!memory_entries || !file_entries || !network_copy || !cookie)
        return 0;

    char path_template[] = "/tmp/jalium-xsmp-auth-XXXXXX";
    int authority_fd = mkstemp(path_template);
    if (authority_fd < 0)
        return 0;
    FILE *authority = fdopen(authority_fd, "wb");
    if (!authority) {
        close(authority_fd);
        unlink(path_template);
        return 0;
    }

    size_t entry_index = 0;
    char *save_pointer = NULL;
    for (char *network = strtok_r(network_copy, ",", &save_pointer);
         network;
         network = strtok_r(NULL, ",", &save_pointer)) {
        const char *protocols[] = {"ICE", "XSMP"};
        for (size_t protocol_index = 0; protocol_index < 2; ++protocol_index) {
            IceAuthDataEntry *memory = &memory_entries[entry_index];
            memory->protocol_name = (char *)protocols[protocol_index];
            memory->network_id = strdup(network);
            memory->auth_name = "MIT-MAGIC-COOKIE-1";
            memory->auth_data_length = 16;
            memory->auth_data = cookie;
            if (!memory->network_id) {
                fclose(authority);
                unlink(path_template);
                return 0;
            }

            IceAuthFileEntry *file = &file_entries[entry_index];
            file->protocol_name = memory->protocol_name;
            file->protocol_data_length = 0;
            file->protocol_data = NULL;
            file->network_id = memory->network_id;
            file->auth_name = memory->auth_name;
            file->auth_data_length = memory->auth_data_length;
            file->auth_data = memory->auth_data;
            if (!IceWriteAuthFileEntry(authority, file)) {
                fclose(authority);
                unlink(path_template);
                return 0;
            }
            ++entry_index;
        }
    }

    if (fclose(authority) != 0)
        return 0;
    IceSetPaAuthData((int)entry_count, memory_entries);
    if (snprintf(authority_path, authority_path_size, "%s", path_template) < 0 ||
        setenv("ICEAUTHORITY", path_template, 1) != 0) {
        unlink(path_template);
        return 0;
    }
    return 1;
}

int main(int argc, char **argv)
{
    if (argc < 2) {
        fprintf(stderr, "usage: %s command [args...]\n", argv[0]);
        return 2;
    }

    char error[256] = {0};
    (void)IceSetIOErrorHandler(ice_io_error);
    if (!SmsInitialize(
            "Jalium.UI", "1", new_client, NULL, allow_host,
            (int)sizeof(error), error)) {
        fprintf(stderr, "SmsInitialize failed: %s\n", error);
        return 3;
    }

    int listener_count = 0;
    IceListenObj *listeners = NULL;
    if (!IceListenForConnections(
            &listener_count, &listeners, (int)sizeof(error), error) ||
        listener_count <= 0) {
        fprintf(stderr, "IceListenForConnections failed: %s\n", error);
        return 4;
    }

    char *network_ids = IceComposeNetworkIdList(listener_count, listeners);
    char authority_path[128] = {0};
    if (!network_ids ||
        !setup_authentication(network_ids, authority_path, sizeof(authority_path)) ||
        setenv("SESSION_MANAGER", network_ids, 1) != 0) {
        fprintf(stderr, "Could not publish SESSION_MANAGER.\n");
        IceFreeListenObjs(listener_count, listeners);
        free(network_ids);
        return 5;
    }
    free(network_ids);
    pid_t child = fork();
    if (child == 0) {
        execvp(argv[1], &argv[1]);
        perror("execvp");
        _exit(127);
    }
    if (child < 0) {
        perror("fork");
        IceFreeListenObjs(listener_count, listeners);
        return 6;
    }

    IceConn connections[8] = {0};
    int connection_count = 0;
    int child_status = 0;
    int child_exited = 0;
    int64_t deadline = monotonic_milliseconds() + 15000;

    while (monotonic_milliseconds() < deadline) {
        struct pollfd descriptors[16];
        int descriptor_count = 0;
        for (int index = 0; index < listener_count; ++index) {
            descriptors[descriptor_count].fd =
                IceGetListenConnectionNumber(listeners[index]);
            descriptors[descriptor_count].events = POLLIN;
            descriptors[descriptor_count].revents = 0;
            ++descriptor_count;
        }
        for (int index = 0; index < connection_count; ++index) {
            descriptors[descriptor_count].fd = IceConnectionNumber(connections[index]);
            descriptors[descriptor_count].events = POLLIN;
            descriptors[descriptor_count].revents = 0;
            ++descriptor_count;
        }

        int ready = poll(descriptors, (nfds_t)descriptor_count, 100);
        if (ready < 0 && errno != EINTR) {
            perror("poll");
            break;
        }

        for (int index = 0; index < listener_count; ++index) {
            if ((descriptors[index].revents & POLLIN) == 0)
                continue;
            int accept_status = 0;
            IceConn accepted = IceAcceptConnection(listeners[index], &accept_status);
            if (accepted && connection_count < 8)
                connections[connection_count++] = accepted;
        }

        for (int index = 0; index < connection_count; ++index) {
            int descriptor_index = listener_count + index;
            if ((descriptors[descriptor_index].revents & POLLIN) != 0)
                (void)IceProcessMessages(connections[index], NULL, NULL);
        }

        if (g_registered && !g_save_sent) {
            g_save_sent = 1;
            /* SmSaveBoth, shutdown=True, SmInteractStyleAny, fast=False. */
            SmsSaveYourself(g_sms, 2, 1, 2, 0);
        }

        pid_t waited = waitpid(child, &child_status, WNOHANG);
        if (waited == child) {
            child_exited = 1;
            break;
        }
    }

    if (!child_exited) {
        kill(child, SIGTERM);
        (void)waitpid(child, &child_status, 0);
    }
    for (int index = 0; index < connection_count; ++index)
        (void)IceCloseConnection(connections[index]);
    IceFreeListenObjs(listener_count, listeners);
    if (authority_path[0])
        unlink(authority_path);

    if (!child_exited || !WIFEXITED(child_status) || WEXITSTATUS(child_status) != 0) {
        fprintf(stderr, "XSMP client failed or timed out (status=%d).\n", child_status);
        return 7;
    }
    if (!g_registered || !g_save_sent || !g_interact_requested ||
        !g_cancelled || !g_save_done) {
        fprintf(stderr,
            "Incomplete XSMP exchange: registered=%d save=%d interact=%d cancel=%d done=%d\n",
            g_registered, g_save_sent, g_interact_requested, g_cancelled, g_save_done);
        return 8;
    }

    puts("XSMP_MANAGER_PROTOCOL_OK");
    return 0;
}
