/*
 * gpu-helper - single root entry point for g-helper's privileged GPU operations.
 *
 * Installed at /opt/ghelper/gpu-helper (root:root 755) and invoked through ONE
 * bare-path sudoers NOPASSWD entry. Every subcommand validates its arguments
 * against a hardcoded whitelist, it just funnels them through one
 * validated dispatcher. No shell is ever invoked; passthrough subcommands
 * execv the real tool directly so exit codes and stderr propagate verbatim.
 *
 * Subcommands:
 *
 *   list [skip_pid]
 *       Enumerate dGPU holders. Output (one TSV line per holder):
 *         <pid>\t<fdCount>\t<uid>\t<comm>\t<libsMapped>\t<serviceUnit>
 *       serviceUnit is the systemd unit owning the process (from
 *       /proc/<pid>/cgroup) or "-" when not part of a service.
 *
 *   kill <signal> <pid> [pid...]
 *       Service-aware kill. signal is -15 or -9. For each holder pid that
 *       belongs to a systemd unit, stop that unit first (system service via
 *       systemctl stop, user service via runuser ... systemctl --user stop) so
 *       it cannot respawn, then send the signal to the pid regardless.
 *
 *   daemon <stop|start|reset-failed> <nvidia-powerd|nvidia-persistenced>
 *       systemctl <verb> <unit> for the two NVIDIA daemons only.
 *
 *   rmmod <module>
 *       rmmod a whitelisted NVIDIA module. stderr/exit pass through so the
 *       caller can parse "not currently loaded" / "Permission denied" etc.
 *
 *   pci-unbind <driver> <bdf> | pci-bind <driver> <bdf>
 *       Write <bdf> to /sys/bus/pci/drivers/<driver>/{unbind,bind}.
 *       driver is restricted to nvidia | snd_hda_intel | amdgpu; bdf is validated.
 *
 *   pci-remove <bdf>
 *       Write 1 to /sys/bus/pci/devices/<bdf>/remove to drop the device node
 *       from the bus (AMD dGPU Eco release: unbind then remove each function).
 *       bdf is validated.
 *
 *   slot-power <slot> <0|1>
 *       Write <0|1> to /sys/bus/pci/slots/<slot>/power to power a PCIe hotplug
 *       slot off/on. Used to re-power the dGPU's slot on Eco->Standard (a bus
 *       rescan alone cannot wake a slot left at power=0). slot is validated as a
 *       bare slot name; value must be 0 or 1.
 *
 *   pci-power <bdf> <auto|on>
 *       Write <auto|on> to /sys/bus/pci/devices/<bdf>/power/control to set the
 *       device's runtime power management (auto = allow autosuspend). Applied to
 *       the dGPU after Standard re-enable. bdf validated; value auto|on only.
 *
 *   vulkan-icd <hide|show>
 *       Rename the NVIDIA Vulkan ICD manifest aside (hide, on Eco) or back
 *       (show, on Standard) so Vulkan falls back cleanly to the iGPU while the
 *       dGPU is disabled. Hardcoded path; no-ops if the file is already in the
 *       target state.
 *
 *   smi <flag> [value]
 *       nvidia-smi with a whitelisted write flag (-pl | -lgc | -rgc | -lmc | -rmc).
 *
 *   modprobe uvcvideo | modprobe -r uvcvideo | modprobe nvidia-wmi-ec-backlight
 *       Load/unload the whitelisted modules only.
 *
 *   nvml-clocks <core_mhz> <mem_mhz>
 *       Apply NVIDIA graphics + memory clock offsets at P0 via NVML (root-only).
 *       Prefers the modern per-pstate nvmlDeviceSetClockOffsets API (driver
 *       555+); falls back to the legacy global VF-curve setters on older
 *       drivers. libnvidia-ml.so.1 is dlopen'd so no -dev headers are needed.
 *
 *   nvml-info
 *       Read NVML driver version + current/min/max GPC & Mem clock offsets and
 *       print them as key=value lines (driver=, core-offset=, mem-offset=,
 *       core-range=min,max, mem-range=min,max, lock-gpu=, lock-mem=,
 *       power-mgmt= capability flags). Lets ghelper read the values
 *       without ever opening /dev/nvidia* in its own process (a persistent NVML
 *       handle there would block the live Eco PCI unbind).
 *
 *   wmi-dsts <devid> | wmi-devs <devid> <ctrl_param> | wmi-probe
 *       Raw asus-nb-wmi debugfs DSTS/DEVS access (root-only debugfs). devid is
 *       restricted to the known GPU / eGPU ACPI device IDs; ctrl_param is the
 *       data value. wmi-probe DSTS-reads all whitelisted IDs, one line each.
 *
 *   msr-uv <mv>
 *       Intel CPU undervolt via the OC mailbox (MSR 0x150), applied to the core
 *       (plane 0) and cache (plane 2) voltage planes. mv is restricted to
 *       [-150, 0] (undervolt only). Prints the decoded readback ("core=<mv>
 *       cache=<mv>") so the caller can verify the offset took effect (a
 *       firmware-locked mailbox reads back 0). Non-persistent; resets on reboot.
 *
 * Sudoers (installed by install.sh, bare path = any subcommand/args):
 *   (root) NOPASSWD: /opt/ghelper/gpu-helper
 *
 * Exit codes: 0 success; 1 usage / not-permitted; 2 invalid signal;
 *             3 sysfs write error; 127 exec failure.
 */

#include <ctype.h>
#include <dirent.h>
#include <dlfcn.h>
#include <errno.h>
#include <fcntl.h>
#include <pwd.h>
#include <signal.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <syslog.h>
#include <unistd.h>

/* Log to both syslog (journal, tag "gpu-helper") and stderr (captured by the
 * caller). Keeps a single audit trail of every privileged action the helper
 * performs, visible via `journalctl -t gpu-helper`. */
static void glog(int prio, const char *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    vsyslog(prio, fmt, ap);
    va_end(ap);
}

#define NVIDIA_PREFIX "/dev/nvidia"
#define NVIDIA_PREFIX_LEN 11
#define COMM_BUF_SIZE 64
#define PATH_BUF_SIZE 512
#define MAPS_LINE_SIZE 1024
#define CGROUP_BUF_SIZE 1024
#define UNIT_BUF_SIZE 256
#define MAX_STOPPED_UNITS 64

/* ---------- shared /proc detection ---------- */

static int count_nvidia_fds(int pid)
{
    char fdDir[64];
    snprintf(fdDir, sizeof(fdDir), "/proc/%d/fd", pid);
    DIR *dir = opendir(fdDir);
    if (!dir)
        return 0;
    int count = 0;
    struct dirent *e;
    char fdPath[PATH_BUF_SIZE];
    char target[PATH_BUF_SIZE];
    while ((e = readdir(dir)) != NULL)
    {
        if (e->d_name[0] == '.')
            continue;
        snprintf(fdPath, sizeof(fdPath), "%s/%s", fdDir, e->d_name);
        ssize_t n = readlink(fdPath, target, sizeof(target) - 1);
        if (n > 0)
        {
            target[n] = '\0';
            if (strncmp(target, NVIDIA_PREFIX, NVIDIA_PREFIX_LEN) == 0)
                count++;
        }
    }
    closedir(dir);
    return count;
}

static int has_libnvidia_mapped(int pid)
{
    char path[64];
    snprintf(path, sizeof(path), "/proc/%d/maps", pid);
    FILE *f = fopen(path, "r");
    if (!f)
        return 0;
    char line[MAPS_LINE_SIZE];
    int found = 0;
    while (fgets(line, sizeof(line), f) != NULL)
    {
        if (strstr(line, "/libnvidia-") != NULL || strstr(line, "/libcuda.so") != NULL || strstr(line, "/libnvcuvid.so") != NULL)
        {
            found = 1;
            break;
        }
    }
    fclose(f);
    return found;
}

static int is_holder(int pid)
{
    return count_nvidia_fds(pid) > 0 || has_libnvidia_mapped(pid);
}

static unsigned int read_uid(int pid)
{
    char path[64];
    snprintf(path, sizeof(path), "/proc/%d/status", pid);
    FILE *f = fopen(path, "r");
    if (!f)
        return (unsigned int)-1;
    char line[256];
    unsigned int uid = (unsigned int)-1;
    while (fgets(line, sizeof(line), f) != NULL)
    {
        if (strncmp(line, "Uid:", 4) == 0)
        {
            sscanf(line + 4, " %u", &uid);
            break;
        }
    }
    fclose(f);
    return uid;
}

static void read_comm(int pid, char *out, size_t outsz)
{
    char path[64];
    snprintf(path, sizeof(path), "/proc/%d/comm", pid);
    FILE *f = fopen(path, "r");
    if (!f)
    {
        strncpy(out, "?", outsz - 1);
        out[outsz - 1] = '\0';
        return;
    }
    if (fgets(out, outsz, f) != NULL)
    {
        size_t n = strlen(out);
        if (n > 0 && out[n - 1] == '\n')
            out[n - 1] = '\0';
        for (size_t i = 0; i < n; i++)
            if (out[i] == '\t' || out[i] == '\n')
                out[i] = ' ';
    }
    else
    {
        strncpy(out, "?", outsz - 1);
        out[outsz - 1] = '\0';
    }
    fclose(f);
}

/* Read /proc/<pid>/cgroup (cgroup v2: single "0::<path>" line) into out. */
static int read_cgroup(int pid, char *out, size_t outsz)
{
    char path[64];
    snprintf(path, sizeof(path), "/proc/%d/cgroup", pid);
    FILE *f = fopen(path, "r");
    if (!f)
        return 0;
    out[0] = '\0';
    char line[CGROUP_BUF_SIZE];
    while (fgets(line, sizeof(line), f) != NULL)
    {
        if (strncmp(line, "0::", 3) == 0)
        {
            strncpy(out, line + 3, outsz - 1);
            out[outsz - 1] = '\0';
            break;
        }
        if (out[0] == '\0')
        {
            const char *p = strrchr(line, ':');
            if (p)
            {
                strncpy(out, p + 1, outsz - 1);
                out[outsz - 1] = '\0';
            }
        }
    }
    fclose(f);
    size_t n = strlen(out);
    if (n > 0 && out[n - 1] == '\n')
        out[n - 1] = '\0';
    return out[0] != '\0';
}

/* 0 none, 1 system service (unit), 2 user service (unit + *uid) */
static int classify_service(const char *cgroup, char *unit, size_t unitsz, unsigned int *uid)
{
    const char *leaf = strrchr(cgroup, '/');
    leaf = leaf ? leaf + 1 : cgroup;
    size_t len = strlen(leaf);
    const char *svc = ".service";
    size_t svclen = strlen(svc);
    if (len <= svclen || strcmp(leaf + len - svclen, svc) != 0)
        return 0;
    strncpy(unit, leaf, unitsz - 1);
    unit[unitsz - 1] = '\0';
    const char *u = strstr(cgroup, "user@");
    if (u != NULL)
    {
        unsigned int parsed = (unsigned int)-1;
        if (sscanf(u + 5, "%u", &parsed) == 1)
        {
            *uid = parsed;
            return 2;
        }
    }
    return 1;
}

/* ---------- exec helpers ---------- */

/* Validate done by caller; replace this process with the tool. */
static int exec_tool(const char *tool, char *const argv[])
{
    execvp(tool, argv);
    fprintf(stderr, "gpu-helper: exec %s failed: %s\n", tool, strerror(errno));
    return 127;
}

/* fork/exec argv, wait, ignore result (used for service stops in kill). */
static void run_cmd(char *const argv[])
{
    pid_t child = fork();
    if (child < 0)
        return;
    if (child == 0)
    {
        int devnull = open("/dev/null", O_WRONLY);
        if (devnull >= 0)
        {
            dup2(devnull, 1);
            dup2(devnull, 2);
        }
        execvp(argv[0], argv);
        _exit(127);
    }
    int status;
    waitpid(child, &status, 0);
}

/* ---------- kill (service-aware) ---------- */

static char g_stopped[MAX_STOPPED_UNITS][UNIT_BUF_SIZE];
static int g_stopped_count = 0;

static int already_stopped(const char *unit)
{
    for (int i = 0; i < g_stopped_count; i++)
        if (strcmp(g_stopped[i], unit) == 0)
            return 1;
    return 0;
}
static void remember_stopped(const char *unit)
{
    if (g_stopped_count < MAX_STOPPED_UNITS)
    {
        snprintf(g_stopped[g_stopped_count], UNIT_BUF_SIZE, "%s", unit);
        g_stopped_count++;
    }
}

static void stop_service(int pid)
{
    if (!is_holder(pid))
        return;
    char cgroup[CGROUP_BUF_SIZE];
    if (!read_cgroup(pid, cgroup, sizeof(cgroup)))
        return;
    char unit[UNIT_BUF_SIZE];
    unsigned int uid = (unsigned int)-1;
    int kind = classify_service(cgroup, unit, sizeof(unit), &uid);
    if (kind == 0)
        return;
    if (already_stopped(unit))
        return;
    remember_stopped(unit);
    if (kind == 1)
    {
        printf("stopped %s (system service, pid %d)\n", unit, pid);
        glog(LOG_INFO, "kill: stop system service %s (pid %d)", unit, pid);
        char *const argv[] = {"timeout", "10", "systemctl", "stop", unit, NULL};
        run_cmd(argv);
    }
    else
    {
        struct passwd *pw = getpwuid((uid_t)uid);
        if (pw == NULL || pw->pw_name == NULL)
            return;
        printf("stopped %s (user service uid %u, pid %d)\n", unit, uid, pid);
        glog(LOG_INFO, "kill: stop user service %s (uid %u, pid %d)", unit, uid, pid);
        char xdg[64];
        snprintf(xdg, sizeof(xdg), "XDG_RUNTIME_DIR=/run/user/%u", uid);
        char *const argv[] = {
            "timeout", "10", "runuser", "-u", pw->pw_name, "--",
            "env", xdg, "systemctl", "--user", "stop", unit, NULL};
        run_cmd(argv);
    }
}

static int parse_signal(const char *s)
{
    if (strcmp(s, "-15") == 0 || strcmp(s, "15") == 0 || strcmp(s, "TERM") == 0 || strcmp(s, "SIGTERM") == 0)
        return SIGTERM;
    if (strcmp(s, "-9") == 0 || strcmp(s, "9") == 0 || strcmp(s, "KILL") == 0 || strcmp(s, "SIGKILL") == 0)
        return SIGKILL;
    return -1;
}

static int do_kill(int argc, char **argv)
{
    if (argc < 4)
    {
        fprintf(stderr, "usage: kill <signal> <pid> [pid...]\n");
        return 1;
    }
    int sig = parse_signal(argv[2]);
    if (sig < 0)
    {
        fprintf(stderr, "kill: only -15 and -9 are allowed\n");
        return 2;
    }
    int signalled = 0;
    for (int i = 3; i < argc; i++)
    {
        int pid = atoi(argv[i]);
        if (pid <= 1)
            continue;
        if (pid == (int)getpid())
            continue;
        stop_service(pid);
        if (kill(pid, sig) == 0)
            signalled++;
    }
    glog(LOG_INFO, "kill: signal %d sent to %d/%d pid(s), %d service(s) stopped",
         sig, signalled, argc - 3, g_stopped_count);
    return 0;
}

/* ---------- list ---------- */

static int do_list(int skip_pid)
{
    DIR *proc = opendir("/proc");
    if (!proc)
    {
        fprintf(stderr, "gpu-helper: cannot open /proc\n");
        return 1;
    }
    struct dirent *e;
    char comm[COMM_BUF_SIZE];
    while ((e = readdir(proc)) != NULL)
    {
        if (!isdigit((unsigned char)e->d_name[0]))
            continue;
        int pid = atoi(e->d_name);
        if (pid <= 0 || pid == skip_pid)
            continue;
        int fds = count_nvidia_fds(pid);
        int libs = has_libnvidia_mapped(pid);
        if (fds > 0 || libs)
        {
            unsigned int uid = read_uid(pid);
            read_comm(pid, comm, sizeof(comm));
            const char *unit_out = "-";
            char cgroup[CGROUP_BUF_SIZE];
            char unit[UNIT_BUF_SIZE];
            unsigned int suid;
            if (read_cgroup(pid, cgroup, sizeof(cgroup)) && classify_service(cgroup, unit, sizeof(unit), &suid) != 0)
                unit_out = unit;
            printf("%d\t%d\t%u\t%s\t%d\t%s\n", pid, fds, uid, comm, libs, unit_out);
        }
    }
    closedir(proc);
    return 0;
}

/* ---------- daemon (systemctl) ---------- */

static int do_daemon(int argc, char **argv)
{
    if (argc != 4)
    {
        fprintf(stderr, "usage: daemon <stop|start|reset-failed> <unit>\n");
        return 1;
    }
    const char *verb = argv[2];
    const char *unit = argv[3];
    if (strcmp(verb, "stop") != 0 && strcmp(verb, "start") != 0 && strcmp(verb, "reset-failed") != 0)
    {
        fprintf(stderr, "daemon: verb not permitted\n");
        return 1;
    }
    if (strcmp(unit, "nvidia-powerd") != 0 && strcmp(unit, "nvidia-persistenced") != 0)
    {
        glog(LOG_WARNING, "daemon: unit not permitted: %s", unit);
        fprintf(stderr, "daemon: unit not permitted\n");
        return 1;
    }
    glog(LOG_INFO, "daemon: systemctl %s %s", verb, unit);
    char *const a[] = {"systemctl", (char *)verb, (char *)unit, NULL};
    return exec_tool("systemctl", a);
}

/* ---------- rmmod ---------- */

static int do_rmmod(int argc, char **argv)
{
    if (argc != 3)
    {
        fprintf(stderr, "usage: rmmod <module>\n");
        return 1;
    }
    const char *m = argv[2];
    static const char *allowed[] = {
        "nvidia_drm", "nvidia_modeset", "nvidia_uvm", "nvidia", "nvidia_wmi_ec_backlight"};
    int ok = 0;
    for (size_t i = 0; i < sizeof(allowed) / sizeof(allowed[0]); i++)
        if (strcmp(m, allowed[i]) == 0)
        {
            ok = 1;
            break;
        }
    if (!ok)
    {
        glog(LOG_WARNING, "rmmod: module not permitted: %s", m);
        fprintf(stderr, "rmmod: module not permitted\n");
        return 1;
    }
    glog(LOG_INFO, "rmmod %s", m);
    char *const a[] = {"rmmod", (char *)m, NULL};
    return exec_tool("rmmod", a);
}

/* ---------- pci unbind / bind ---------- */

static int valid_bdf(const char *s)
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

static int do_pci(const char *action, int argc, char **argv)
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

static int do_pci_remove(int argc, char **argv)
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

static int do_slot_power(int argc, char **argv)
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

static int do_pci_power(int argc, char **argv)
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

/* ---------- vulkan icd (nvidia) ---------- */

#define VK_NVIDIA_ICD "/usr/share/vulkan/icd.d/nvidia_icd.json"
#define VK_NVIDIA_ICD_INACTIVE "/usr/share/vulkan/icd.d/nvidia_icd.json_inactive"

/* Hide/show the NVIDIA Vulkan ICD manifest so Vulkan cleanly falls back to the
 * iGPU while the dGPU is disabled (Eco). Hardcoded path; hide<->show only. */
static int do_vulkan_icd(int argc, char **argv)
{
    if (argc != 3)
    {
        fprintf(stderr, "usage: vulkan-icd <hide|show>\n");
        return 1;
    }
    const char *src, *dst;
    if (strcmp(argv[2], "hide") == 0)
    {
        src = VK_NVIDIA_ICD;
        dst = VK_NVIDIA_ICD_INACTIVE;
    }
    else if (strcmp(argv[2], "show") == 0)
    {
        src = VK_NVIDIA_ICD_INACTIVE;
        dst = VK_NVIDIA_ICD;
    }
    else
    {
        fprintf(stderr, "vulkan-icd: arg must be hide or show\n");
        return 1;
    }

    if (access(src, F_OK) != 0)
    {
        /* Already in the desired state (or no NVIDIA ICD present): no-op. */
        glog(LOG_INFO, "vulkan-icd %s: %s absent, nothing to do", argv[2], src);
        return 0;
    }
    if (rename(src, dst) != 0)
    {
        glog(LOG_ERR, "vulkan-icd %s: rename %s -> %s failed: %s", argv[2], src, dst, strerror(errno));
        fprintf(stderr, "vulkan-icd: rename %s -> %s: %s\n", src, dst, strerror(errno));
        return 3;
    }
    glog(LOG_INFO, "vulkan-icd %s: %s -> %s", argv[2], src, dst);
    return 0;
}

/* ---------- nvidia-smi ---------- */

static int do_smi(int argc, char **argv)
{
    if (argc < 3)
    {
        fprintf(stderr, "usage: smi <flag> [value]\n");
        return 1;
    }
    const char *flag = argv[2];
    if (strcmp(flag, "-pl") != 0 && strcmp(flag, "-lgc") != 0 && strcmp(flag, "-rgc") != 0 && strcmp(flag, "-lmc") != 0 && strcmp(flag, "-rmc") != 0)
    {
        glog(LOG_WARNING, "smi: flag not permitted: %s", flag);
        fprintf(stderr, "smi: flag not permitted\n");
        return 1;
    }
    /* nvidia-smi <flag> [value] - at most one value argument. */
    if (argc > 4)
    {
        fprintf(stderr, "smi: too many args\n");
        return 1;
    }
    glog(LOG_INFO, "smi %s%s%s", flag, argc == 4 ? " " : "", argc == 4 ? argv[3] : "");
    char *a[4];
    a[0] = "nvidia-smi";
    a[1] = (char *)flag;
    if (argc == 4)
    {
        a[2] = argv[3];
        a[3] = NULL;
    }
    else
    {
        a[2] = NULL;
    }
    return exec_tool("nvidia-smi", a);
}

/* ---------- modprobe ---------- */

static int do_modprobe(int argc, char **argv)
{
    /* Permitted exactly: uvcvideo | -r uvcvideo | nvidia-wmi-ec-backlight | amdgpu | nvidia */
    int ok = 0;
    if (argc == 3 && strcmp(argv[2], "uvcvideo") == 0)
        ok = 1;
    else if (argc == 3 && strcmp(argv[2], "nvidia-wmi-ec-backlight") == 0)
        ok = 1;
    else if (argc == 3 && strcmp(argv[2], "amdgpu") == 0)
        ok = 1;
    else if (argc == 3 && strcmp(argv[2], "nvidia") == 0)
        ok = 1;
    else if (argc == 4 && strcmp(argv[2], "-r") == 0 && strcmp(argv[3], "uvcvideo") == 0)
        ok = 1;
    if (!ok)
    {
        glog(LOG_WARNING, "modprobe: arguments not permitted");
        fprintf(stderr, "modprobe: arguments not permitted\n");
        return 1;
    }
    glog(LOG_INFO, "modprobe %s%s%s", argv[2], argc == 4 ? " " : "", argc == 4 ? argv[3] : "");
    char *a[4];
    int j = 0;
    a[j++] = "modprobe";
    a[j++] = argv[2];
    if (argc == 4)
        a[j++] = argv[3];
    a[j] = NULL;
    return exec_tool("modprobe", a);
}

/* ---------- nvml clock offsets ---------- */

/* Permissive sanity bounds; NVML rejects values outside the device range. */
#define CORE_MIN -3000
#define CORE_MAX 3000
#define MEM_MIN -10000
#define MEM_MAX 10000

/* Clock types (nvmlClockType_t) and pstate used by the modern offset API. */
#define NVML_CLOCK_GRAPHICS 0
#define NVML_CLOCK_MEM 2
#define NVML_PSTATE_0 0
#define NVML_ERROR_NOT_SUPPORTED 3

/*
 * Modern per-pstate clock offset struct (NVML / driver 555+). LACT uses this
 * API (nvmlDeviceGet/SetClockOffsets) instead of the legacy global VF-curve
 * setters: it reports realistic per-pstate min/max bounds, so the UI sliders
 * cannot reach the absurd values (e.g. +6000 mem) that the legacy
 * MemClkVfOffset range exposed and which hard-crash the GPU (Xid 62 ->
 * NVML_ERROR_RESET_REQUIRED).
 */
typedef struct
{
    unsigned int version;
    unsigned int type;   /* nvmlClockType_t */
    unsigned int pstate; /* nvmlPstates_t */
    int clockOffsetMHz;
    int minClockOffsetMHz;
    int maxClockOffsetMHz;
} nvmlClockOffset_v1_t;

#define NVML_CLOCKOFFSET_V1 ((unsigned int)(sizeof(nvmlClockOffset_v1_t) | (1u << 24)))

typedef int (*nvmlInit_fn)(void);
typedef int (*nvmlGetHandle_fn)(unsigned int index, void **dev);
typedef int (*nvmlSetOffset_fn)(void *dev, int offset);
typedef int (*nvmlShutdown_fn)(void);
typedef int (*nvmlClockOffsets_fn)(void *dev, nvmlClockOffset_v1_t *info);

/*
 * Apply one clock-type offset at P0. Prefer the modern per-pstate API; fall
 * back to the legacy global VF-curve setter only when the modern symbol is
 * absent or returns NVML_ERROR_NOT_SUPPORTED (driver < 555). Returns the NVML
 * return code (0 = success).
 */
static int set_clock_offset(void *dev, unsigned int clockType, int value,
                            nvmlClockOffsets_fn setModern, nvmlSetOffset_fn setLegacy)
{
    if (setModern)
    {
        nvmlClockOffset_v1_t info;
        memset(&info, 0, sizeof(info));
        info.version = NVML_CLOCKOFFSET_V1;
        info.type = clockType;
        info.pstate = NVML_PSTATE_0;
        info.clockOffsetMHz = value;
        int rc = setModern(dev, &info);
        if (rc != NVML_ERROR_NOT_SUPPORTED)
            return rc;
    }
    if (setLegacy)
        return setLegacy(dev, value);
    return NVML_ERROR_NOT_SUPPORTED;
}

static int do_nvml(int argc, char **argv)
{
    if (argc != 4)
    {
        fprintf(stderr, "usage: nvml-clocks <core_mhz> <mem_mhz>\n");
        return 1;
    }

    char *endp;
    long core = strtol(argv[2], &endp, 10);
    if (*endp != '\0')
    {
        fprintf(stderr, "core: not an integer\n");
        return 1;
    }
    long mem = strtol(argv[3], &endp, 10);
    if (*endp != '\0')
    {
        fprintf(stderr, "mem: not an integer\n");
        return 1;
    }

    if (core < CORE_MIN || core > CORE_MAX)
    {
        fprintf(stderr, "core offset %ld out of range [%d, %d]\n", core, CORE_MIN, CORE_MAX);
        return 1;
    }
    if (mem < MEM_MIN || mem > MEM_MAX)
    {
        fprintf(stderr, "mem offset %ld out of range [%d, %d]\n", mem, MEM_MIN, MEM_MAX);
        return 1;
    }

    void *lib = dlopen("libnvidia-ml.so.1", RTLD_NOW);
    if (!lib)
    {
        glog(LOG_ERR, "nvml-clocks: dlopen libnvidia-ml.so.1 failed: %s", dlerror());
        fprintf(stderr, "dlopen libnvidia-ml.so.1 failed: %s\n", dlerror());
        return 2;
    }

    nvmlInit_fn nvmlInit = (nvmlInit_fn)dlsym(lib, "nvmlInit_v2");
    nvmlGetHandle_fn nvmlGetHandle = (nvmlGetHandle_fn)dlsym(lib, "nvmlDeviceGetHandleByIndex_v2");
    nvmlClockOffsets_fn nvmlSetModern = (nvmlClockOffsets_fn)dlsym(lib, "nvmlDeviceSetClockOffsets");
    nvmlSetOffset_fn nvmlSetGpc = (nvmlSetOffset_fn)dlsym(lib, "nvmlDeviceSetGpcClkVfOffset");
    nvmlSetOffset_fn nvmlSetMem = (nvmlSetOffset_fn)dlsym(lib, "nvmlDeviceSetMemClkVfOffset");
    nvmlShutdown_fn nvmlShutdown = (nvmlShutdown_fn)dlsym(lib, "nvmlShutdown");

    if (!nvmlInit || !nvmlGetHandle || !nvmlShutdown || (!nvmlSetModern && (!nvmlSetGpc || !nvmlSetMem)))
    {
        glog(LOG_ERR, "nvml-clocks: dlsym failed (driver too old? need NVML 11.x+)");
        fprintf(stderr, "dlsym failed (driver too old? need NVML 11.x+)\n");
        dlclose(lib);
        return 2;
    }

    int rc = nvmlInit();
    if (rc != 0)
    {
        glog(LOG_ERR, "nvml-clocks: nvmlInit_v2 failed: %d", rc);
        fprintf(stderr, "nvmlInit_v2 failed: %d\n", rc);
        dlclose(lib);
        return 2;
    }

    void *dev = NULL;
    rc = nvmlGetHandle(0, &dev);
    if (rc != 0 || dev == NULL)
    {
        glog(LOG_ERR, "nvml-clocks: GetHandleByIndex(0) failed: %d", rc);
        fprintf(stderr, "nvmlDeviceGetHandleByIndex_v2(0) failed: %d\n", rc);
        nvmlShutdown();
        dlclose(lib);
        return 3;
    }

    rc = set_clock_offset(dev, NVML_CLOCK_GRAPHICS, (int)core, nvmlSetModern, nvmlSetGpc);
    if (rc != 0)
    {
        glog(LOG_ERR, "nvml-clocks: set core offset %ld failed: %d", core, rc);
        fprintf(stderr, "set core offset %ld failed\nnvml-error=%d\n", core, rc);
        nvmlShutdown();
        dlclose(lib);
        return 4;
    }
    rc = set_clock_offset(dev, NVML_CLOCK_MEM, (int)mem, nvmlSetModern, nvmlSetMem);
    if (rc != 0)
    {
        glog(LOG_ERR, "nvml-clocks: set mem offset %ld failed: %d", mem, rc);
        fprintf(stderr, "set mem offset %ld failed\nnvml-error=%d\n", mem, rc);
        nvmlShutdown();
        dlclose(lib);
        return 4;
    }

    nvmlShutdown();
    dlclose(lib);
    glog(LOG_INFO, "nvml-clocks: applied core=%ld mem=%ld (%s)", core, mem,
         nvmlSetModern ? "modern" : "legacy");
    printf("applied core=%ld mem=%ld\n", core, mem);
    return 0;
}

/* ---------- nvml read-only info ---------- */

typedef int (*nvmlGetOffset_fn)(void *dev, int *offset);
typedef int (*nvmlGetMinMax_fn)(void *dev, int *min, int *max);
typedef int (*nvmlGetDriver_fn)(char *version, unsigned int length);
typedef int (*nvmlGetSuppMemClocks_fn)(void *dev, unsigned int *count, unsigned int *clocksMHz);
typedef int (*nvmlGetSuppGfxClocks_fn)(void *dev, unsigned int memClockMHz, unsigned int *count, unsigned int *clocksMHz);
typedef int (*nvmlGetPowerMgmtMode_fn)(void *dev, int *mode);

#define NVML_ERROR_INSUFFICIENT_SIZE 7

static int do_nvml_info(void)
{
    void *lib = dlopen("libnvidia-ml.so.1", RTLD_NOW);
    if (!lib)
    {
        glog(LOG_ERR, "nvml-info: dlopen libnvidia-ml.so.1 failed: %s", dlerror());
        fprintf(stderr, "dlopen libnvidia-ml.so.1 failed: %s\n", dlerror());
        return 2;
    }

    nvmlInit_fn nvmlInit = (nvmlInit_fn)dlsym(lib, "nvmlInit_v2");
    nvmlGetHandle_fn nvmlGetHandle = (nvmlGetHandle_fn)dlsym(lib, "nvmlDeviceGetHandleByIndex_v2");
    nvmlShutdown_fn nvmlShutdown = (nvmlShutdown_fn)dlsym(lib, "nvmlShutdown");
    nvmlGetDriver_fn getDriver = (nvmlGetDriver_fn)dlsym(lib, "nvmlSystemGetDriverVersion");
    nvmlClockOffsets_fn getModern = (nvmlClockOffsets_fn)dlsym(lib, "nvmlDeviceGetClockOffsets");
    nvmlGetOffset_fn getGpc = (nvmlGetOffset_fn)dlsym(lib, "nvmlDeviceGetGpcClkVfOffset");
    nvmlGetOffset_fn getMem = (nvmlGetOffset_fn)dlsym(lib, "nvmlDeviceGetMemClkVfOffset");
    nvmlGetMinMax_fn getGpcMM = (nvmlGetMinMax_fn)dlsym(lib, "nvmlDeviceGetGpcClkMinMaxVfOffset");
    nvmlGetMinMax_fn getMemMM = (nvmlGetMinMax_fn)dlsym(lib, "nvmlDeviceGetMemClkMinMaxVfOffset");
    nvmlGetSuppMemClocks_fn getSuppMem = (nvmlGetSuppMemClocks_fn)dlsym(lib, "nvmlDeviceGetSupportedMemoryClocks");
    nvmlGetSuppGfxClocks_fn getSuppGfx = (nvmlGetSuppGfxClocks_fn)dlsym(lib, "nvmlDeviceGetSupportedGraphicsClocks");
    nvmlGetPowerMgmtMode_fn getPwrMode = (nvmlGetPowerMgmtMode_fn)dlsym(lib, "nvmlDeviceGetPowerManagementMode");

    if (!nvmlInit || !nvmlGetHandle || !nvmlShutdown)
    {
        glog(LOG_ERR, "nvml-info: dlsym failed (driver too old?)");
        fprintf(stderr, "dlsym failed (driver too old?)\n");
        dlclose(lib);
        return 2;
    }

    int rc = nvmlInit();
    if (rc != 0)
    {
        glog(LOG_ERR, "nvml-info: nvmlInit_v2 failed: %d", rc);
        fprintf(stderr, "nvmlInit_v2 failed: %d\n", rc);
        dlclose(lib);
        return 2;
    }

    void *dev = NULL;
    rc = nvmlGetHandle(0, &dev);
    if (rc != 0 || dev == NULL)
    {
        glog(LOG_ERR, "nvml-info: GetHandleByIndex(0) failed: %d", rc);
        fprintf(stderr, "nvmlDeviceGetHandleByIndex_v2(0) failed: %d\n", rc);
        nvmlShutdown();
        dlclose(lib);
        return 3;
    }

    if (getDriver)
    {
        char ver[96];
        if (getDriver(ver, sizeof(ver)) == 0)
            printf("driver=%s\n", ver);
    }
    int v, mn, mx;
    int gotCore = 0, gotMem = 0;
    if (getModern)
    {
        nvmlClockOffset_v1_t info;
        memset(&info, 0, sizeof(info));
        info.version = NVML_CLOCKOFFSET_V1;
        info.type = NVML_CLOCK_GRAPHICS;
        info.pstate = NVML_PSTATE_0;
        if (getModern(dev, &info) == 0)
        {
            printf("core-offset=%d\n", info.clockOffsetMHz);
            printf("core-range=%d,%d\n", info.minClockOffsetMHz, info.maxClockOffsetMHz);
            gotCore = 1;
        }
        memset(&info, 0, sizeof(info));
        info.version = NVML_CLOCKOFFSET_V1;
        info.type = NVML_CLOCK_MEM;
        info.pstate = NVML_PSTATE_0;
        if (getModern(dev, &info) == 0)
        {
            printf("mem-offset=%d\n", info.clockOffsetMHz);
            printf("mem-range=%d,%d\n", info.minClockOffsetMHz, info.maxClockOffsetMHz);
            gotMem = 1;
        }
    }
    if (!gotCore)
    {
        if (getGpc && getGpc(dev, &v) == 0)
            printf("core-offset=%d\n", v);
        if (getGpcMM && getGpcMM(dev, &mn, &mx) == 0)
            printf("core-range=%d,%d\n", mn, mx);
    }
    if (!gotMem)
    {
        if (getMem && getMem(dev, &v) == 0)
            printf("mem-offset=%d\n", v);
        if (getMemMM && getMemMM(dev, &mn, &mx) == 0)
            printf("mem-range=%d,%d\n", mn, mx);
    }

    /* Capability flags. A clock type is lockable when NVML reports supported
     * clocks for it; power-mgmt indicates the power limit is settable. */
    int lockMem = 0, lockGpu = 0, powerMgmt = 0;
    if (getSuppMem)
    {
        unsigned int cnt = 256;
        unsigned int clk[256];
        int r = getSuppMem(dev, &cnt, clk);
        if ((r == 0 || r == NVML_ERROR_INSUFFICIENT_SIZE) && cnt > 0)
        {
            lockMem = 1;
            if (getSuppGfx)
            {
                unsigned int gcnt = 256;
                unsigned int gclk[256];
                int rg = getSuppGfx(dev, clk[0], &gcnt, gclk);
                if ((rg == 0 || rg == NVML_ERROR_INSUFFICIENT_SIZE) && gcnt > 0)
                    lockGpu = 1;
            }
        }
    }
    if (getPwrMode)
    {
        int mode = 0;
        if (getPwrMode(dev, &mode) == 0 && mode == 1)
            powerMgmt = 1;
    }
    printf("lock-mem=%d\n", lockMem);
    printf("lock-gpu=%d\n", lockGpu);
    printf("power-mgmt=%d\n", powerMgmt);

    nvmlShutdown();
    dlclose(lib);
    glog(LOG_INFO, "nvml-info: queried");
    return 0;
}

/* ---------- raw asus-nb-wmi debugfs (DSTS / DEVS) ---------- */

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

static int do_wmi_dsts(int argc, char **argv)
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

static int do_wmi_devs(int argc, char **argv)
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

static int do_wmi_probe(void)
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

/* ---------- intel cpu undervolt (MSR 0x150 OC mailbox) ---------- */

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

static int do_msr_uv(int argc, char **argv)
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

/* ---------- dispatch ---------- */

int main(int argc, char **argv)
{
    /* Stable PATH for execvp regardless of caller environment. */
    setenv("PATH", "/usr/sbin:/usr/bin:/sbin:/bin", 1);
    openlog("gpu-helper", LOG_PID, LOG_DAEMON);

    if (argc >= 2)
    {
        if (strcmp(argv[1], "list") == 0)
            return do_list(argc >= 3 ? atoi(argv[2]) : 0);
        if (strcmp(argv[1], "kill") == 0)
            return do_kill(argc, argv);
        if (strcmp(argv[1], "daemon") == 0)
            return do_daemon(argc, argv);
        if (strcmp(argv[1], "rmmod") == 0)
            return do_rmmod(argc, argv);
        if (strcmp(argv[1], "pci-unbind") == 0)
            return do_pci("unbind", argc, argv);
        if (strcmp(argv[1], "pci-bind") == 0)
            return do_pci("bind", argc, argv);
        if (strcmp(argv[1], "pci-remove") == 0)
            return do_pci_remove(argc, argv);
        if (strcmp(argv[1], "slot-power") == 0)
            return do_slot_power(argc, argv);
        if (strcmp(argv[1], "pci-power") == 0)
            return do_pci_power(argc, argv);
        if (strcmp(argv[1], "vulkan-icd") == 0)
            return do_vulkan_icd(argc, argv);
        if (strcmp(argv[1], "smi") == 0)
            return do_smi(argc, argv);
        if (strcmp(argv[1], "modprobe") == 0)
            return do_modprobe(argc, argv);
        if (strcmp(argv[1], "nvml-clocks") == 0)
            return do_nvml(argc, argv);
        if (strcmp(argv[1], "nvml-info") == 0)
            return do_nvml_info();
        if (strcmp(argv[1], "wmi-dsts") == 0)
            return do_wmi_dsts(argc, argv);
        if (strcmp(argv[1], "wmi-devs") == 0)
            return do_wmi_devs(argc, argv);
        if (strcmp(argv[1], "wmi-probe") == 0)
            return do_wmi_probe();
        if (strcmp(argv[1], "msr-uv") == 0)
            return do_msr_uv(argc, argv);
    }

    /* Backward-compatible default: bare numeric arg is the legacy skip_pid. */
    return do_list(argc >= 2 ? atoi(argv[1]) : 0);
}
