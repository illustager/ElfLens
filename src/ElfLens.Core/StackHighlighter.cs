using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ElfLens.Core;

/// <summary>
/// Tokenizer for stack memory dump lines (pwndbg stack / GDB x/gx output).
/// </summary>
public static class StackHighlighter
{
    private const string CAddr  = "#546E7A";
    private const string CHex   = "#FFE082";
    private const string CFunc  = "#4FC3F7";
    private const string CReg   = "#CE93D8";
    private const string CInst  = "#B0BEC5";
    private const string CDef   = "#B0BEC5";
    private const string CArrow = "#546E7A";

    // Matches leading "NN:XXXX│ " offset marker
    private static readonly Regex OffsetRx = new(@"^\d+:[0-9a-f]+│");

    public static List<Token> Tokenize(string line)
    {
        if (line.Contains('│') || line.Contains('◂') || line.Contains('▸'))
            return TokenizePwndbgStack(line);
        else
            return TokenizeXDump(line);
    }

    /// <summary>
    /// pwndbg stack format:
    ///   00:0000│ rbp rsp 0x7fffffffe040 ◂— 0x1
    ///   01:0008│         0x7fffffffe048 —▸ 0x7fff... (func+128) ◂— mov edi, eax
    /// </summary>
    private static List<Token> TokenizePwndbgStack(string line)
    {
        var tokens = new List<Token>();
        int p = 0;

        // Leading offset: "00:0000│"
        var offsetM = OffsetRx.Match(line);
        if (offsetM.Success && offsetM.Index == 0)
        {
            tokens.Add(new Token(offsetM.Value, CAddr));
            p = offsetM.Length;
        }

        while (p < line.Length)
        {
            // Preserve spaces as tokens to keep column alignment
            if (line[p] == ' ')
            {
                int s = p;
                while (p < line.Length && line[p] == ' ') p++;
                tokens.Add(new Token(line[s..p], CDef));
                continue;
            }

            // Arrow characters
            if (line[p] == '◂' || line[p] == '▸' || line[p] == '─')
            {
                int start = p;
                while (p < line.Length && (line[p] == '◂' || line[p] == '▸' || line[p] == '─')) p++;
                tokens.Add(new Token(line[start..p], CArrow));
                continue;
            }

            // Hex address/value: 0x...
            if (line[p] == '0' && p + 1 < line.Length && line[p + 1] == 'x')
            {
                var hexM = Regex.Match(line[p..], @"^0x[0-9a-fA-F]+");
                if (hexM.Success)
                {
                    // Check if followed by space — standalone hex value
                    // Check if this looks like a stack address (long, 0x7fff...)
                    var val = hexM.Value;
                    var color = val.Length >= 14 ? CAddr : CHex;
                    tokens.Add(new Token(val, color));
                    p += hexM.Length;
                    continue;
                }
            }

            // Function reference in parens: (name+offset) or (name)
            if (line[p] == '(')
            {
                var parenM = Regex.Match(line[p..], @"^\([^)]+\)");
                if (parenM.Success)
                {
                    tokens.Add(new Token(parenM.Value, CFunc));
                    p += parenM.Length;
                    continue;
                }
            }

            // Register name (followed by space or end)
            var wordM = Regex.Match(line[p..], @"^[a-z][a-z0-9]+");
            if (wordM.Success)
            {
                var word = wordM.Value;
                if (RegisterNames.All.Contains(word))
                {
                    tokens.Add(new Token(word, CReg));
                    p += wordM.Length;
                    continue;
                }
                // Disassembly mnemonic like "mov", "endbr64", "call", etc.
                // These are space-separated lowercase words after hex values/parens
                tokens.Add(new Token(word, CInst));
                p += wordM.Length;
                continue;
            }

            // Path-like symbols: std::wcerr, std::char_traits<...>
            var symM = Regex.Match(line[p..], @"^[a-zA-Z_]\w*(?:::[a-zA-Z_]\w*)*(?:<[^>]*>)?");
            if (symM.Success && symM.Length > 0)
            {
                tokens.Add(new Token(symM.Value, CFunc));
                p += symM.Length;
                continue;
            }

            // Fallback: consume one char
            tokens.Add(new Token(line[p..(p + 1)], CDef));
            p++;
        }

        return tokens;
    }

    /// <summary>
    /// GDB x/Ngx format:
    ///   0x7fffffffe050:    0x00007ffff7e28fa0    0x00005555555551b4
    /// </summary>
    private static List<Token> TokenizeXDump(string line)
    {
        var tokens = new List<Token>();
        int p = 0;

        // Leading whitespace
        while (p < line.Length && line[p] == ' ') { p++; }
        if (p > 0) tokens.Add(new Token(line[0..p], CDef));

        // Address: "0x7fffffffe050:"
        var addrM = Regex.Match(line[p..], @"^(0x[0-9a-fA-F]+):");
        if (addrM.Success)
        {
            tokens.Add(new Token(addrM.Groups[1].Value, CAddr));
            tokens.Add(new Token(":", CAddr));
            p += addrM.Length;

            // Rest: alternating spaces and hex values
            while (p < line.Length)
            {
                // Spaces
                int s = p;
                while (p < line.Length && line[p] == ' ') p++;
                if (p > s) tokens.Add(new Token(line[s..p], CDef));

                // Hex value
                var hexM = Regex.Match(line[p..], @"^0x[0-9a-fA-F]+");
                if (hexM.Success)
                {
                    tokens.Add(new Token(hexM.Value, CHex));
                    p += hexM.Length;
                }
                else if (p < line.Length)
                {
                    // Unexpected char
                    tokens.Add(new Token(line[p..(p + 1)], CDef));
                    p++;
                }
            }
        }
        else
        {
            tokens.Add(new Token(line[p..], CDef));
        }

        return tokens;
    }
}
