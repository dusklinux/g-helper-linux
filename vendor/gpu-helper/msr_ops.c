/* Intel CPU undervolt via MSR 0x150 OC mailbox for gpu-helper. */
#include "gpu-helper.h"

/*
 * Intel voltage-offset undervolting via the OC mailbox MSR 0x150.
 *   plane 0 = CPU core, plane 2 = CPU cache/ring.
 *   write cmd hi32: 0x80000011 (core) / 0x80000211 (cache)
 *   read  cmd hi32: 0x80000010 (core) / 0x80000210 (cache)
 *   payload: round(mv * 1.024) as 11-bit twos complement, shifted to bits[31:21]
 * Offset is restricted to [-150, 0] mV: undervolt only, bounded magnitude.
 * Non-persistent (resets on reboot). Package-scoped, so cpu0 is sufficient.
 */

#define MSR_OC_MAILBOX 0x150
#define UV_MIN_MV (-150)
#define UV_MAX_MV 0

static uint32_t uv_encode(int mv)
{
    double d = mv * 1.024;
    int v = (int)(d < 0 ? d - 0.5 : d + 0.5);
    return ((uint32_t)(v & 0x7FF)) << 21;
}

static int uv_decode(uint32_t low)
{
    int o = (int)((low >> 21) & 0x7FF);
    if (o & 0x400)
        o -= 0x800;
    double mv = o / 1.024;
    return (int)(mv < 0 ? mv - 0.5 : mv + 0.5);
}

static void uv_allow_writes(void)
{
    int fd = open("/sys/module/msr/parameters/allow_writes", O_WRONLY);
    if (fd >= 0)
    {
        ssize_t w = write(fd, "on\n", 3);
        (void)w;
        close(fd);
    }
}

static void uv_ensure_msr(void)
{
    if (access("/dev/cpu/0/msr", F_OK) == 0)
        return;
    pid_t pid = fork();
    if (pid == 0)
    {
        execlp("modprobe", "modprobe", "msr", (char *)NULL);
        _exit(127);
    }
    else if (pid > 0)
    {
        int st;
        waitpid(pid, &st, 0);
    }
}

static int uv_msr_write(int fd, uint64_t v)
{
    return (pwrite(fd, &v, 8, MSR_OC_MAILBOX) == 8) ? 0 : -1;
}

static int uv_read_plane(int fd, int plane)
{
    uint64_t cmd = 0x8000001000000000ULL | ((uint64_t)plane << 40);
    if (uv_msr_write(fd, cmd) != 0)
        return -1000000;
    uint64_t val = 0;
    if (pread(fd, &val, 8, MSR_OC_MAILBOX) != 8)
        return -1000000;
    return uv_decode((uint32_t)(val & 0xFFFFFFFFULL));
}

int do_msr_uv(int argc, char **argv)
{
    if (argc != 3)
    {
        fprintf(stderr, "usage: msr-uv <mv>  (integer in [%d,%d])\n", UV_MIN_MV, UV_MAX_MV);
        return 1;
    }
    char *end = NULL;
    long mv = strtol(argv[2], &end, 10);
    if (end == argv[2] || *end != '\0' || mv > UV_MAX_MV || mv < UV_MIN_MV)
    {
        glog(LOG_WARNING, "msr-uv: rejected offset '%s' (allowed [%d,%d])", argv[2], UV_MIN_MV, UV_MAX_MV);
        fprintf(stderr, "msr-uv: offset must be an integer in [%d,%d]\n", UV_MIN_MV, UV_MAX_MV);
        return 1;
    }

    uv_ensure_msr();
    uv_allow_writes();

    int fd = open("/dev/cpu/0/msr", O_RDWR);
    if (fd < 0)
    {
        glog(LOG_ERR, "msr-uv: open /dev/cpu/0/msr: %s", strerror(errno));
        fprintf(stderr, "msr-uv: open /dev/cpu/0/msr: %s (msr module loaded?)\n", strerror(errno));
        return 3;
    }

    uint32_t enc = uv_encode((int)mv);
    uint64_t core_w = 0x8000001100000000ULL | enc;
    uint64_t cache_w = 0x8000021100000000ULL | enc;

    int rc = 0;
    if (uv_msr_write(fd, core_w) != 0)
    {
        glog(LOG_ERR, "msr-uv: core (plane 0) write failed: %s", strerror(errno));
        fprintf(stderr, "msr-uv: core write failed: %s\n", strerror(errno));
        rc = 3;
    }
    else if (uv_msr_write(fd, cache_w) != 0)
    {
        glog(LOG_ERR, "msr-uv: cache (plane 2) write failed: %s", strerror(errno));
        fprintf(stderr, "msr-uv: cache write failed: %s\n", strerror(errno));
        rc = 3;
    }

    if (rc == 0)
    {
        int core_rb = uv_read_plane(fd, 0);
        int cache_rb = uv_read_plane(fd, 2);
        printf("core=%d cache=%d\n", core_rb, cache_rb);
        glog(LOG_INFO, "msr-uv: requested %ld mV -> readback core=%d cache=%d", mv, core_rb, cache_rb);
    }
    close(fd);
    return rc;
}
