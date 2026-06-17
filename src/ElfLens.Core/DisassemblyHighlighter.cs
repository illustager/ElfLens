using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ElfLens.Core;

public record Token(string Text, string Color);

public static class DisassemblyHighlighter
{
    private const string CAddr    = "#546E7A";
    private const string CBytes   = "#546E7A";
    private const string CInst    = "#B0BEC5";
    private const string CBranch  = "#FFB74D";
    private const string CCall    = "#81C784";
    private const string CRet     = "#EF5350";
    private const string CFunc    = "#4FC3F7";
    private const string CReg     = "#CE93D8";
    private const string CHex     = "#FFE082";
    private const string CComment = "#6A9955";
    private const string CDef     = "#B0BEC5";

    private static readonly HashSet<string> BranchSet = new()
        { "jmp","je","jne","jz","jnz","jg","jge","jl","jle","ja","jae","jb","jbe",
          "jo","jno","js","jns","jp","jnp","jcxz","jecxz","loop","loope","loopne" };
    private static readonly HashSet<string> CallSet = new() { "call","int","syscall","sysenter" };
    private static readonly HashSet<string> RetSet = new() { "ret","retn","retf","iret","iretq","sysret" };

    private static readonly Regex FuncRx =
        new(@"^([0-9a-f]+)\s+<([^>]+)>:$", RegexOptions.Compiled);
    // [addr] [bytes] [spc] [prefix?] [spc?] [mnemonic] [spc] [rest] [;comment?]
    private static readonly Regex InstRx = new(
        @"^(\s*[0-9a-f]+:\s+)?((?:[0-9a-f]{2}\s)+)(\s+)(?:([a-z]\w*)(\s+))?([a-z]\w*)(\s*)(.*?)?(\s*;.*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RegRx = new(
        @"%(?:[re]?[abcd]x|[re]?[sd]i|[re]?[sb]p|[re]?ip|[re]?[a-d][lh]|[re]?s[ip]l|[re]?bpl|[re]?dil|[cr]r[0-9]|dr[0-7]|st\(?[0-7]\)?|[xyz]?mm[0-9]|xmm1[0-5]|[xy]mm1[0-5])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HexRx =
        new(@"\b(?:0x[0-9a-f]+|[0-9a-f]{4,}h?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NumRx = new(@"\b[0-9]+\b", RegexOptions.Compiled);
    private static readonly Regex RefRx = new(@"<([^>]+)>", RegexOptions.Compiled);

    public static List<Token> Tokenize(string line)
    {
        var t = new List<Token>();

        // Entire-line comment
        if (line.TrimStart().StartsWith(';') || line.TrimStart().StartsWith('#'))
        {
            t.Add(new Token(line, CComment));
            return t;
        }

        var fh = FuncRx.Match(line);
        if (fh.Success)
        {
            t.Add(new Token(fh.Groups[1].Value, CAddr));
            t.Add(new Token(" <", CDef));
            t.Add(new Token(fh.Groups[2].Value, CFunc));
            t.Add(new Token(">:", CDef));
            return t;
        }

        var im = InstRx.Match(line);
        if (im.Success)
        {
            // 1=addr,2=bytes,3=spc,4=prefix?,5=spc?,6=mnemonic,7=spc,8=rest,9=comment?
            if (im.Groups[1].Success) t.Add(new Token(im.Groups[1].Value, CAddr));
            if (im.Groups[2].Success) t.Add(new Token(im.Groups[2].Value, CBytes));
            if (im.Groups[3].Success) t.Add(new Token(im.Groups[3].Value, CDef));
            if (im.Groups[4].Success) t.Add(new Token(im.Groups[4].Value, CDef));
            if (im.Groups[5].Success) t.Add(new Token(im.Groups[5].Value, CDef));
            var mnem = im.Groups[6].Value.ToLower();
            t.Add(new Token(im.Groups[6].Value, MnemColor(mnem)));
            if (im.Groups[7].Success) t.Add(new Token(im.Groups[7].Value, CDef));
            if (im.Groups[8].Success) TokenizeOps(im.Groups[8].Value, t);
            if (im.Groups[9].Success) t.Add(new Token(im.Groups[9].Value, CComment));
        }
        else
        {
            t.Add(new Token(line, CDef));
        }

        return t;
    }

    private static string MnemColor(string m)
    {
        if (CallSet.Contains(m)) return CCall;
        if (RetSet.Contains(m)) return CRet;
        if (BranchSet.Contains(m)) return CBranch;
        return CInst;
    }

    private static void TokenizeOps(string text, List<Token> tokens)
    {
        int p = 0;
        while (p < text.Length)
        {
            var refM = RefRx.Match(text, p);
            if (refM.Success && refM.Index == p) { tokens.Add(new Token(refM.Value, CFunc)); p += refM.Length; continue; }
            var regM = RegRx.Match(text, p);
            if (regM.Success && regM.Index == p) { tokens.Add(new Token(regM.Value, CReg)); p += regM.Length; continue; }
            var hexM = HexRx.Match(text, p);
            if (hexM.Success && hexM.Index == p) { tokens.Add(new Token(hexM.Value, CHex)); p += hexM.Length; continue; }
            var numM = NumRx.Match(text, p);
            if (numM.Success && numM.Index == p) { tokens.Add(new Token(numM.Value, CHex)); p += numM.Length; continue; }

            int next = text.Length;
            foreach (var rx in new[] { RefRx, RegRx, HexRx, NumRx })
            { var m = rx.Match(text, p); if (m.Success && m.Index < next) next = m.Index; }
            if (next > p) { tokens.Add(new Token(text[p..next], CDef)); p = next; }
            else p++;
        }
    }
}
