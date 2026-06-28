/* PCI bind/unbind/remove, slot power, runtime PM for gpu-helper. */
#include "gpu-helper.h"

/* ---------- pci unbind / bind ---------- */

/* Shared with nvidia_ops.c (drm-notify-remove). */
int valid_bdf(const char *s)
{
    /* DDDD:BB:DD.F lowercase hex */
    if (strlen(s) != 12)
        return 0;
    const char fmt[] = "hhhh:hh:hh.h"; /* h=hex, others literal */
    for (int i = 0; i < 12; i++)
    {
        char c = s[i];
        if (fmt[i] == 'h')
        {
            if (!isxdigit((unsigned char)c))
                return 0;
        }
        else if (c != fmt[i])
            return 0;
    }
    return 1;
}

int do_pci(const char *action, int argc, char **argv)
{
    if (argc != 4)
    {
        fprintf(stderr, "usage: pci-%s <driver> <bdf>\n", action);
        return 1;
    }
    const char *driver = argv[2];
    const char *bdf = argv[3];
    if (strcmp(driver, "nvidia") != 0 && strcmp(driver, "snd_hda_intel") != 0 && strcmp(driver, "amdgpu") != 0)
    {
        fprintf(stderr, "pci-%s: driver not permitted\n", action);
        return 1;
    }
    if (!valid_bdf(bdf))
    {
        fprintf(stderr, "pci-%s: invalid bdf\n", action);
        return 1;
    }
    char path[PATH_BUF_SIZE];
    snprintf(path, sizeof(path), "/sys/bus/pci/drivers/%s/%s", driver, action);
    int fd = open(path, O_WRONLY);
    if (fd < 0)
    {
        glog(LOG_ERR, "pci-%s %s %s: open failed: %s", action, driver, bdf, strerror(errno));
        fprintf(stderr, "pci-%s: open %s: %s\n", action, path, strerror(errno));
        return 3;
    }
    ssize_t n = write(fd, bdf, strlen(bdf));
    int werr = errno;
    close(fd);
    if (n < 0)
    {
        glog(LOG_ERR, "pci-%s %s %s: write failed: %s", action, driver, bdf, strerror(werr));
        fprintf(stderr, "pci-%s: write %s: %s\n", action, path, strerror(werr));
        return 3;
    }
    glog(LOG_INFO, "pci-%s %s %s", action, driver, bdf);
    return 0;
}

int do_pci_remove(int argc, char **argv)
{
    if (argc != 3)
    {
        fprintf(stderr, "usage: pci-remove <bdf>\n");
        return 1;
    }
    const char *bdf = argv[2];
    if (!valid_bdf(bdf))
    {
        fprintf(stderr, "pci-remove: invalid bdf\n");
        return 1;
    }
    char path[PATH_BUF_SIZE];
    snprintf(path, sizeof(path), "/sys/bus/pci/devices/%s/remove", bdf);
    int fd = open(path, O_WRONLY);
    if (fd < 0)
    {
        glog(LOG_ERR, "pci-remove %s: open failed: %s", bdf, strerror(errno));
        fprintf(stderr, "pci-remove: open %s: %s\n", path, strerror(errno));
        return 3;
    }
    ssize_t n = write(fd, "1", 1);
    int werr = errno;
    close(fd);
    if (n < 0)
    {
        glog(LOG_ERR, "pci-remove %s: write failed: %s", bdf, strerror(werr));
        fprintf(stderr, "pci-remove: write %s: %s\n", path, strerror(werr));
        return 3;
    }
    glog(LOG_INFO, "pci-remove %s", bdf);
    return 0;
}

/* ---------- pcie slot power (hotplug) ---------- */

/* Bare slot name: non-empty, only [0-9A-Za-z-], no path separators or dots. */
static int valid_slot(const char *s)
{
    if (s[0] == '\0' || strlen(s) > 32)
        return 0;
    for (const char *p = s; *p; p++)
    {
        char c = *p;
        if (!(isalnum((unsigned char)c) || c == '-'))
            return 0;
    }
    return 1;
}

int do_slot_power(int argc, char **argv)
{
    if (argc != 4)
    {
        fprintf(stderr, "usage: slot-power <slot> <0|1>\n");
        return 1;
    }
    const char *slot = argv[2];
    const char *val = argv[3];
    if (!valid_slot(slot))
    {
        glog(LOG_WARNING, "slot-power: invalid slot: %s", slot);
        fprintf(stderr, "slot-power: invalid slot\n");
        return 1;
    }
    if (strcmp(val, "0") != 0 && strcmp(val, "1") != 0)
    {
        fprintf(stderr, "slot-power: value must be 0 or 1\n");
        return 1;
    }
    char path[PATH_BUF_SIZE];
    snprintf(path, sizeof(path), "/sys/bus/pci/slots/%s/power", slot);
    int fd = open(path, O_WRONLY);
    if (fd < 0)
    {
        glog(LOG_ERR, "slot-power %s %s: open failed: %s", slot, val, strerror(errno));
        fprintf(stderr, "slot-power: open %s: %s\n", path, strerror(errno));
        return 3;
    }
    ssize_t n = write(fd, val, strlen(val));
    int werr = errno;
    close(fd);
    if (n < 0)
    {
        glog(LOG_ERR, "slot-power %s %s: write failed: %s", slot, val, strerror(werr));
        fprintf(stderr, "slot-power: write %s: %s\n", path, strerror(werr));
        return 3;
    }
    glog(LOG_INFO, "slot-power %s %s", slot, val);
    return 0;
}

/* ---------- pci runtime power management (power/control) ---------- */

int do_pci_power(int argc, char **argv)
{
    if (argc != 4)
    {
        fprintf(stderr, "usage: pci-power <bdf> <auto|on>\n");
        return 1;
    }
    const char *bdf = argv[2];
    const char *val = argv[3];
    if (!valid_bdf(bdf))
    {
        fprintf(stderr, "pci-power: invalid bdf\n");
        return 1;
    }
    if (strcmp(val, "auto") != 0 && strcmp(val, "on") != 0)
    {
        fprintf(stderr, "pci-power: value must be auto or on\n");
        return 1;
    }
    char path[PATH_BUF_SIZE];
    snprintf(path, sizeof(path), "/sys/bus/pci/devices/%s/power/control", bdf);
    int fd = open(path, O_WRONLY);
    if (fd < 0)
    {
        glog(LOG_ERR, "pci-power %s %s: open failed: %s", bdf, val, strerror(errno));
        fprintf(stderr, "pci-power: open %s: %s\n", path, strerror(errno));
        return 3;
    }
    ssize_t n = write(fd, val, strlen(val));
    int werr = errno;
    close(fd);
    if (n < 0)
    {
        glog(LOG_ERR, "pci-power %s %s: write failed: %s", bdf, val, strerror(werr));
        fprintf(stderr, "pci-power: write %s: %s\n", path, strerror(werr));
        return 3;
    }
    glog(LOG_INFO, "pci-power %s %s", bdf, val);
    return 0;
}
