// Independent FFI layout assertions; compile against the Apple SDK, never vend a private XNU header.
#include <libproc.h>
#include <stddef.h>
#include <sys/proc.h>
#include <sys/proc_info.h>
#define OFFSET(field, value) _Static_assert(offsetof(struct proc_bsdinfo, field) == value, #field)
_Static_assert(PROC_PIDTBSDINFO==3 && SZOMB==5 && sizeof(struct proc_bsdinfo)==136, "proc_bsdinfo ABI");
OFFSET(pbi_status,4); OFFSET(pbi_pid,12); OFFSET(pbi_ppid,16); OFFSET(pbi_comm,48); OFFSET(pbi_name,64);
OFFSET(pbi_pgid,100); OFFSET(e_tdev,108); OFFSET(e_tpgid,112); OFFSET(pbi_start_tvsec,120); OFFSET(pbi_start_tvusec,128);
_Static_assert(sizeof(((struct proc_bsdinfo*)0)->pbi_comm)==16 && sizeof(((struct proc_bsdinfo*)0)->pbi_name)==32,"process names");
