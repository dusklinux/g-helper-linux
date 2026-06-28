/* Raw asus-nb-wmi debugfs DSTS/DEVS access for gpu-helper. */
#include "gpu-helper.h"

#define WMI_DEBUGFS_DIR "/sys/kernel/debug/asus-nb-wmi"

/* GPU / eGPU ACPI device IDs. Order matches the C# ProbeIds array so wmi-probe
 * output lines line up with the caller's parsing. */
static const unsigned long WMI_GPU_IDS[] = {
    0x00090020UL, /* dGPU disable (ROG/TUF) */
    0x00090120UL, /* dGPU disable (Vivobook/Zenbook) */
    0x00090016UL, /* GPU MUX (ROG/TUF) */
    0x00090026UL, /* GPU MUX (Vivobook) */
    0x00090018UL, /* eGPU connected */
    0x00090019UL, /* eGPU enable */
    0x00120099UL, /* dGPU base TGP */
};
#define WMI_GPU_IDS_N (sizeof(WMI_GPU_IDS) / sizeof(WMI_GPU_IDS[0]))

static int wmi_devid_allowed(unsigned long id)
{
    for (size_t i = 0; i < WMI_GPU_IDS_N; i++)
        if (id == WMI_GPU_IDS[i])
            return 1;
    return 0;
}

static int wmi_write_attr(const char *name, const char *val)
{
    char path[PATH_BUF_SIZE];
    snprintf(path, sizeof(path), "%s/%s", WMI_DEBUGFS_DIR, name);
    int fd = open(path, O_WRONLY);
    if (fd < 0)
    {
        glog(LOG_ERR, "wmi: open %s: %s", path, strerror(errno));
        fprintf(stderr, "wmi: open %s: %s\n", path, strerror(errno));
        return -1;
    }
    ssize_t n = write(fd, val, strlen(val));
    int e = errno;
    close(fd);
    if (n < 0)
    {
        glog(LOG_ERR, "wmi: write %s: %s", path, strerror(e));
        fprintf(stderr, "wmi: write %s: %s\n", path, strerror(e));
        return -1;
    }
    return 0;
}

/* Read a debugfs attr into out (NUL-terminated, trailing newline stripped). */
static int wmi_read_attr(const char *name, char *out, size_t outsz)
{
    char path[PATH_BUF_SIZE];
    snprintf(path, sizeof(path), "%s/%s", WMI_DEBUGFS_DIR, name);
    int fd = open(path, O_RDONLY);
    if (fd < 0)
    {
        glog(LOG_ERR, "wmi: open %s: %s", path, strerror(errno));
        return -1;
    }
    ssize_t n = read(fd, out, outsz - 1);
    int e = errno;
    close(fd);
    if (n < 0)
    {
        glog(LOG_ERR, "wmi: read %s: %s", path, strerror(e));
        return -1;
    }
    out[n] = '\0';
    while (n > 0 && (out[n - 1] == '\n' || out[n - 1] == '\r'))
        out[--n] = '\0';
    return 0;
}

static int wmi_select_devid(unsigned long id)
{
    char idbuf[16];
    snprintf(idbuf, sizeof(idbuf), "0x%08lx", id);
    return wmi_write_attr("dev_id", idbuf);
}

int do_wmi_dsts(int argc, char **argv)
{
    if (argc != 3)
    {
        fprintf(stderr, "usage: wmi-dsts <devid>\n");
        return 1;
    }
    unsigned long id = strtoul(argv[2], NULL, 0);
    if (!wmi_devid_allowed(id))
    {
        glog(LOG_WARNING, "wmi-dsts: devid not permitted: 0x%08lx", id);
        fprintf(stderr, "wmi-dsts: devid not permitted\n");
        return 1;
    }
    if (wmi_select_devid(id) != 0)
        return 3;
    char buf[512];
    if (wmi_read_attr("dsts", buf, sizeof(buf)) != 0)
        return 3;
    glog(LOG_INFO, "wmi-dsts 0x%08lx", id);
    printf("%s\n", buf);
    return 0;
}

int do_wmi_devs(int argc, char **argv)
{
    if (argc != 4)
    {
        fprintf(stderr, "usage: wmi-devs <devid> <ctrl_param>\n");
        return 1;
    }
    unsigned long id = strtoul(argv[2], NULL, 0);
    if (!wmi_devid_allowed(id))
    {
        glog(LOG_WARNING, "wmi-devs: devid not permitted: 0x%08lx", id);
        fprintf(stderr, "wmi-devs: devid not permitted\n");
        return 1;
    }
    char *endp;
    unsigned long ctrl = strtoul(argv[3], &endp, 0);
    if (*endp != '\0')
    {
        fprintf(stderr, "wmi-devs: ctrl_param not an integer\n");
        return 1;
    }
    if (wmi_select_devid(id) != 0)
        return 3;
    char ctrlbuf[24];
    snprintf(ctrlbuf, sizeof(ctrlbuf), "%lu", ctrl);
    if (wmi_write_attr("ctrl_param", ctrlbuf) != 0)
        return 3;
    char buf[512];
    if (wmi_read_attr("devs", buf, sizeof(buf)) != 0)
        return 3;
    glog(LOG_INFO, "wmi-devs 0x%08lx %lu", id, ctrl);
    printf("%s\n", buf);
    return 0;
}

int do_wmi_probe(void)
{
    char buf[512];
    for (size_t i = 0; i < WMI_GPU_IDS_N; i++)
    {
        if (wmi_select_devid(WMI_GPU_IDS[i]) == 0 && wmi_read_attr("dsts", buf, sizeof(buf)) == 0)
            printf("%s\n", buf);
        else
            printf("\n");
    }
    glog(LOG_INFO, "wmi-probe: %zu ids", WMI_GPU_IDS_N);
    return 0;
}
