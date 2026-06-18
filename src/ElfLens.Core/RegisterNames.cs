using System.Collections.Generic;

namespace ElfLens.Core;

/// <summary>
/// Shared set of x86/x86_64 register names used by tokenizers.
/// </summary>
public static class RegisterNames
{
    public static readonly HashSet<string> All = new()
    {
        "rax","rbx","rcx","rdx","rsi","rdi","rbp","rsp","rip",
        "eax","ebx","ecx","edx","esi","edi","ebp","esp","eip",
        "ax","bx","cx","dx","si","di","bp","sp",
        "al","ah","bl","bh","cl","ch","dl","dh",
        "r8","r9","r10","r11","r12","r13","r14","r15",
        "r8d","r9d","r10d","r11d","r12d","r13d","r14d","r15d",
        "xmm0","xmm1","xmm2","xmm3","xmm4","xmm5","xmm6","xmm7",
        "ymm0","ymm1","ymm2","ymm3",
        "cs","ds","es","fs","gs","ss",
        "st0","st1","st2","st3","st4","st5","st6","st7",
        "cr0","cr2","cr3","cr4","dr0","dr1","dr2","dr3","dr6","dr7",
        "mm0","mm1","mm2","mm3","mm4","mm5","mm6","mm7",
    };
}
