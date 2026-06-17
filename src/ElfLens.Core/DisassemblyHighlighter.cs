using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ElfLens.Core;

public record ColoredSegment(string Text, string Color);

public static class DisassemblyHighlighter
{
    private static readonly HashSet<string> BranchMnemonics = new()
        { "jmp", "je", "jne", "jz", "jnz", "jg", "jge", "jl", "jle", "ja", "jae", "jb", "jbe",
          "jo", "jno", "js", "jns", "jp", "jnp", "jcxz", "jecxz", "loop", "loope", "loopne" };

    private static readonly HashSet<string> CallMnemonics = new() { "call", "int", "syscall", "sysenter" };

    private static readonly HashSet<string> RetMnemonics = new() { "ret", "retn", "retf", "iret", "iretq", "sysret" };

    private static readonly HashSet<string> ArithMnemonics = new()
        { "add", "sub", "mul", "imul", "div", "idiv", "inc", "dec",
          "and", "or", "xor", "not", "neg", "shl", "shr", "sal", "sar", "rol", "ror" };

    private static readonly Regex FuncHeaderRegex = new(@"^([0-9a-f]+)\s+<([^>]+)>:$", RegexOptions.Compiled);

    // Matches instruction: optional whitespace, optional address: optional bytes, then mnemonic + operands
    private static readonly Regex InstRegex = new(
        @"^(\s*(?:[0-9a-f]+:\s+)?(?:[0-9a-f]{2}\s+)+)?\s*([a-z]+)(\s+.*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RegRegex = new(@"%[a-z][a-z0-9]*", RegexOptions.Compiled);
    private static readonly Regex HexRegex = new(@"(?:0x)?[0-9a-f]{4,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FuncRefRegex = new(@"<([^>]+)>", RegexOptions.Compiled);

    public static List<ColoredSegment> Highlight(string line)
    {
        var result = new List<ColoredSegment>();

        // Function header?
        var fh = FuncHeaderRegex.Match(line);
        if (fh.Success)
        {
            result.Add(new ColoredSegment(fh.Groups[1].Value, "#90A4AE"));  // address
            result.Add(new ColoredSegment(" <", "#666666"));
            result.Add(new ColoredSegment(fh.Groups[2].Value, "#4FC3F7"));  // name
            result.Add(new ColoredSegment(">:", "#666666"));
            return result;
        }

        // Try instruction match
        var im = InstRegex.Match(line);
        if (im.Success)
        {
            // Prefix (address+bytes)
            if (im.Groups[1].Success)
                result.Add(new ColoredSegment(im.Groups[1].Value, "#546E7A"));

            // Mnemonic
            var mnem = im.Groups[2].Value.ToLower();
            var color = mnem switch
            {
                _ when BranchMnemonics.Contains(mnem) => "#FFB74D",
                _ when CallMnemonics.Contains(mnem) => "#81C784",
                _ when RetMnemonics.Contains(mnem) => "#EF5350",
                _ when ArithMnemonics.Contains(mnem) => "#64B5F6",
                _ => "#E0E0E0"
            };
            result.Add(new ColoredSegment(mnem, color));

            // Operands
            if (im.Groups[3].Success)
            {
                var ops = im.Groups[3].Value;
                int last = 0;
                foreach (Match rm in RegRegex.Matches(ops))
                {
                    if (rm.Index > last)
                        HighlightOperand(ops[last..rm.Index], result);
                    result.Add(new ColoredSegment(rm.Value, "#CE93D8"));  // register
                    last = rm.Index + rm.Length;
                }
                if (last < ops.Length)
                    HighlightOperand(ops[last..], result);
            }
        }
        else
        {
            // Plain line
            result.Add(new ColoredSegment(line, "#757575"));
        }

        return result;
    }

    private static void HighlightOperand(string text, List<ColoredSegment> result)
    {
        int last = 0;
        foreach (Match hm in HexRegex.Matches(text))
        {
            if (hm.Index > last)
                result.Add(new ColoredSegment(text[last..hm.Index], "#B0BEC5"));
            result.Add(new ColoredSegment(hm.Value, "#FFE082"));  // hex values
            last = hm.Index + hm.Length;
        }
        if (last < text.Length)
            result.Add(new ColoredSegment(text[last..], "#B0BEC5"));
        else if (last == 0 && text.Length > 0)
            result.Add(new ColoredSegment(text, "#B0BEC5"));
    }
}
