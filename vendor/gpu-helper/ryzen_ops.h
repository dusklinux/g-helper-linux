/* Ryzen SMU operations for gpu-helper. Wraps the vendored library. */
#ifndef RYZEN_OPS_H
#define RYZEN_OPS_H

/* Print PM table values as key=value lines. Returns 0 on success. */
int ryzen_do_info(void);

/* Set a single parameter by name. Value is raw (mW for power, °C for temp,
 * mA for current, MHz for clocks). Returns 0/success, 1/unsupported family,
 * 2/unsupported SMU, 3/rejected, -1/other error. */
int ryzen_do_set(const char *param, unsigned int value);

/* Probe which parameters are supported on this CPU. Prints one line:
 * supported=stapm-limit,fast-limit,... (comma-separated names).
 * Returns 0 on success, -1 if not a Ryzen APU. */
int ryzen_do_probe(void);

#endif
