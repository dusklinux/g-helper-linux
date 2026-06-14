/* Lenovo Flip to Start (FBSWIF UEFI variable) for gpu-helper. */
#include "gpu-helper.h"
#include <sys/ioctl.h>

#ifndef FS_IOC_GETFLAGS
#define FS_IOC_GETFLAGS _IOR('f', 1, long)
#define FS_IOC_SETFLAGS _IOW('f', 2, long)
#endif
#ifndef FS_IMMUTABLE_FL
#define FS_IMMUTABLE_FL 0x00000010
#endif

#define FBSWIF_EFIVAR "/sys/firmware/efi/efivars/FBSWIF-d743491e-f484-4952-a87d-8d5dd189b70c"

/* Write the Lenovo "Flip to Start" (power on when the lid opens) UEFI
 * variable. efivarfs layout: 4-byte LE attributes word (0x7 = NV|BS|RT)
 * followed by the 4-byte payload (byte0 = enabled). efivarfs marks existing
 * variables immutable; the flag must be cleared before the write. Only this
 * one hardcoded variable can be touched. */
int do_lenovo_flip_to_start(int argc, char **argv)
{
    if (argc != 3 || (strcmp(argv[2], "0") != 0 && strcmp(argv[2], "1") != 0))
    {
        fprintf(stderr, "usage: lenovo-flip-to-start <0|1>\n");
        return 1;
    }
    unsigned char enabled = (unsigned char)(argv[2][0] - '0');

    if (access(FBSWIF_EFIVAR, F_OK) != 0)
    {
        glog(LOG_WARNING, "lenovo-flip-to-start: %s absent (not a Lenovo with FBSWIF?)", FBSWIF_EFIVAR);
        fprintf(stderr, "lenovo-flip-to-start: variable not present\n");
        return 1;
    }

    /* Clear the immutable flag efivarfs sets on existing variables. */
    int fd = open(FBSWIF_EFIVAR, O_RDONLY);
    if (fd >= 0)
    {
        long flags = 0;
        if (ioctl(fd, FS_IOC_GETFLAGS, &flags) == 0 && (flags & FS_IMMUTABLE_FL))
        {
            flags &= ~FS_IMMUTABLE_FL;
            if (ioctl(fd, FS_IOC_SETFLAGS, &flags) != 0)
                glog(LOG_WARNING, "lenovo-flip-to-start: clearing immutable failed: %s", strerror(errno));
        }
        close(fd);
    }

    unsigned char buf[8] = {0x07, 0x00, 0x00, 0x00, enabled, 0x00, 0x00, 0x00};
    fd = open(FBSWIF_EFIVAR, O_WRONLY);
    if (fd < 0)
    {
        glog(LOG_ERR, "lenovo-flip-to-start: open failed: %s", strerror(errno));
        fprintf(stderr, "lenovo-flip-to-start: open: %s\n", strerror(errno));
        return 3;
    }
    ssize_t n = write(fd, buf, sizeof(buf));
    int werr = errno;
    close(fd);
    if (n != (ssize_t)sizeof(buf))
    {
        glog(LOG_ERR, "lenovo-flip-to-start: write failed: %s", strerror(werr));
        fprintf(stderr, "lenovo-flip-to-start: write: %s\n", strerror(werr));
        return 3;
    }
    glog(LOG_INFO, "lenovo-flip-to-start: set to %u", enabled);
    return 0;
}
