using System.Globalization;
using System.Text;

namespace Jalium.UI.Controls;

/// <summary>
/// HTML 实体解码。CommonMark 规定实体引用等价于它所代表的字符，因此解析行内文本时要先还原，
/// 否则 <c>AT&amp;amp;T</c> 会原样显示成源码。
/// </summary>
/// <remarks>
/// 只收录常用实体而不是 HTML5 的全表（2000+ 项）：数字实体（<c>&amp;#65;</c>、<c>&amp;#x41;</c>）
/// 走通用路径，命名实体覆盖排版、货币、数学、箭头与希腊字母这些在文档里真正会出现的部分。
/// 表里查不到的名字按 CommonMark 的规定原样保留，不做猜测。
/// </remarks>
internal static class MarkdownEntities
{
    private static readonly Dictionary<string, string> s_named = new(StringComparer.Ordinal)
    {
        // 必需的五个
        ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'",

        // 空白与排版
        ["nbsp"] = " ", ["ensp"] = " ", ["emsp"] = " ", ["thinsp"] = " ",
        ["zwnj"] = "‌", ["zwj"] = "‍", ["lrm"] = "‎", ["rlm"] = "‏",
        ["ndash"] = "–", ["mdash"] = "—", ["horbar"] = "―",
        ["lsquo"] = "‘", ["rsquo"] = "’", ["sbquo"] = "‚",
        ["ldquo"] = "“", ["rdquo"] = "”", ["bdquo"] = "„",
        ["dagger"] = "†", ["Dagger"] = "‡", ["bull"] = "•",
        ["hellip"] = "…", ["permil"] = "‰", ["prime"] = "′", ["Prime"] = "″",
        ["lsaquo"] = "‹", ["rsaquo"] = "›", ["oline"] = "‾", ["frasl"] = "⁄",
        ["laquo"] = "«", ["raquo"] = "»", ["shy"] = "­", ["macr"] = "¯",
        ["iexcl"] = "¡", ["iquest"] = "¿", ["sect"] = "§", ["para"] = "¶",
        ["middot"] = "·", ["cedil"] = "¸", ["uml"] = "¨", ["acute"] = "´",
        ["circ"] = "ˆ", ["tilde"] = "˜", ["brvbar"] = "¦",

        // 版权与货币
        ["copy"] = "©", ["reg"] = "®", ["trade"] = "™",
        ["cent"] = "¢", ["pound"] = "£", ["curren"] = "¤", ["yen"] = "¥",
        ["euro"] = "€",

        // 数学与逻辑
        ["deg"] = "°", ["plusmn"] = "±", ["times"] = "×", ["divide"] = "÷",
        ["frac14"] = "¼", ["frac12"] = "½", ["frac34"] = "¾",
        ["sup1"] = "¹", ["sup2"] = "²", ["sup3"] = "³",
        ["micro"] = "µ", ["not"] = "¬",
        ["minus"] = "−", ["lowast"] = "∗", ["radic"] = "√", ["infin"] = "∞",
        ["ne"] = "≠", ["equiv"] = "≡", ["le"] = "≤", ["ge"] = "≥",
        ["asymp"] = "≈", ["prop"] = "∝", ["ang"] = "∠",
        ["and"] = "∧", ["or"] = "∨", ["cap"] = "∩", ["cup"] = "∪",
        ["int"] = "∫", ["there4"] = "∴", ["sim"] = "∼",
        ["sub"] = "⊂", ["sup"] = "⊃", ["nsub"] = "⊄",
        ["sube"] = "⊆", ["supe"] = "⊇", ["isin"] = "∈", ["notin"] = "∉",
        ["ni"] = "∋", ["prod"] = "∏", ["sum"] = "∑", ["part"] = "∂",
        ["exist"] = "∃", ["forall"] = "∀", ["empty"] = "∅", ["nabla"] = "∇",
        ["oplus"] = "⊕", ["otimes"] = "⊗", ["perp"] = "⊥", ["sdot"] = "⋅",
        ["fnof"] = "ƒ", ["alefsym"] = "ℵ", ["weierp"] = "℘",
        ["image"] = "ℑ", ["real"] = "ℜ",

        // 箭头
        ["larr"] = "←", ["uarr"] = "↑", ["rarr"] = "→", ["darr"] = "↓",
        ["harr"] = "↔", ["crarr"] = "↵",
        ["lArr"] = "⇐", ["uArr"] = "⇑", ["rArr"] = "⇒", ["dArr"] = "⇓",
        ["hArr"] = "⇔",

        // 牌面与几何
        ["loz"] = "◊", ["spades"] = "♠", ["clubs"] = "♣",
        ["hearts"] = "♥", ["diams"] = "♦",

        // 希腊字母
        ["Alpha"] = "Α", ["Beta"] = "Β", ["Gamma"] = "Γ", ["Delta"] = "Δ",
        ["Epsilon"] = "Ε", ["Zeta"] = "Ζ", ["Eta"] = "Η", ["Theta"] = "Θ",
        ["Iota"] = "Ι", ["Kappa"] = "Κ", ["Lambda"] = "Λ", ["Mu"] = "Μ",
        ["Nu"] = "Ν", ["Xi"] = "Ξ", ["Omicron"] = "Ο", ["Pi"] = "Π",
        ["Rho"] = "Ρ", ["Sigma"] = "Σ", ["Tau"] = "Τ", ["Upsilon"] = "Υ",
        ["Phi"] = "Φ", ["Chi"] = "Χ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
        ["epsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η", ["theta"] = "θ",
        ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ",
        ["nu"] = "ν", ["xi"] = "ξ", ["omicron"] = "ο", ["pi"] = "π",
        ["rho"] = "ρ", ["sigmaf"] = "ς", ["sigma"] = "σ", ["tau"] = "τ",
        ["upsilon"] = "υ", ["phi"] = "φ", ["chi"] = "χ", ["psi"] = "ψ",
        ["omega"] = "ω", ["thetasym"] = "ϑ", ["upsih"] = "ϒ", ["piv"] = "ϖ",

        // 重音拉丁字母
        ["Agrave"] = "À", ["Aacute"] = "Á", ["Acirc"] = "Â", ["Atilde"] = "Ã",
        ["Auml"] = "Ä", ["Aring"] = "Å", ["AElig"] = "Æ", ["Ccedil"] = "Ç",
        ["Egrave"] = "È", ["Eacute"] = "É", ["Ecirc"] = "Ê", ["Euml"] = "Ë",
        ["Igrave"] = "Ì", ["Iacute"] = "Í", ["Icirc"] = "Î", ["Iuml"] = "Ï",
        ["ETH"] = "Ð", ["Ntilde"] = "Ñ", ["Ograve"] = "Ò", ["Oacute"] = "Ó",
        ["Ocirc"] = "Ô", ["Otilde"] = "Õ", ["Ouml"] = "Ö", ["Oslash"] = "Ø",
        ["Ugrave"] = "Ù", ["Uacute"] = "Ú", ["Ucirc"] = "Û", ["Uuml"] = "Ü",
        ["Yacute"] = "Ý", ["THORN"] = "Þ", ["szlig"] = "ß",
        ["agrave"] = "à", ["aacute"] = "á", ["acirc"] = "â", ["atilde"] = "ã",
        ["auml"] = "ä", ["aring"] = "å", ["aelig"] = "æ", ["ccedil"] = "ç",
        ["egrave"] = "è", ["eacute"] = "é", ["ecirc"] = "ê", ["euml"] = "ë",
        ["igrave"] = "ì", ["iacute"] = "í", ["icirc"] = "î", ["iuml"] = "ï",
        ["eth"] = "ð", ["ntilde"] = "ñ", ["ograve"] = "ò", ["oacute"] = "ó",
        ["ocirc"] = "ô", ["otilde"] = "õ", ["ouml"] = "ö", ["oslash"] = "ø",
        ["ugrave"] = "ù", ["uacute"] = "ú", ["ucirc"] = "û", ["uuml"] = "ü",
        ["yacute"] = "ý", ["thorn"] = "þ", ["yuml"] = "ÿ",
        ["OElig"] = "Œ", ["oelig"] = "œ", ["Scaron"] = "Š", ["scaron"] = "š",
        ["Yuml"] = "Ÿ",
    };

    /// <summary>
    /// 尝试把 <paramref name="text"/> 中 <paramref name="start"/> 处的 <c>&amp;</c> 解释成一个实体引用。
    /// 成功时 <paramref name="length"/> 是包含 <c>&amp;</c> 与 <c>;</c> 的总长度。
    /// </summary>
    public static bool TryDecode(string text, int start, out string value, out int length)
    {
        value = string.Empty;
        length = 0;

        if (start >= text.Length || text[start] != '&')
        {
            return false;
        }

        var semicolon = text.IndexOf(';', start + 1);
        if (semicolon < 0 || semicolon == start + 1 || semicolon - start > 34)
        {
            return false;
        }

        var body = text.AsSpan(start + 1, semicolon - start - 1);

        if (body[0] == '#')
        {
            return TryDecodeNumeric(body, out value)
                ? Accept(semicolon - start + 1, out length)
                : false;
        }

        foreach (var c in body)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        if (!s_named.TryGetValue(body.ToString(), out var named))
        {
            return false;
        }

        value = named;
        return Accept(semicolon - start + 1, out length);

        bool Accept(int consumed, out int outLength)
        {
            outLength = consumed;
            return true;
        }
    }

    private static bool TryDecodeNumeric(ReadOnlySpan<char> body, out string value)
    {
        value = string.Empty;
        int codePoint;

        if (body.Length > 1 && (body[1] == 'x' || body[1] == 'X'))
        {
            var digits = body[2..];
            if (digits.Length is 0 or > 6 ||
                !int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out codePoint))
            {
                return false;
            }
        }
        else
        {
            var digits = body[1..];
            if (digits.Length is 0 or > 7 ||
                !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out codePoint))
            {
                return false;
            }
        }

        // CommonMark：0 与非法码位都还原成 U+FFFD。代理区单独挡掉，否则 ConvertFromUtf32 会抛。
        if (codePoint is 0 or > 0x10FFFF || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
        {
            value = "�";
            return true;
        }

        value = char.ConvertFromUtf32(codePoint);
        return true;
    }

    /// <summary>把一段文本里的全部实体引用就地展开；没有 <c>&amp;</c> 时原样返回，不产生分配。</summary>
    public static string DecodeAll(string text)
    {
        if (text.IndexOf('&', StringComparison.Ordinal) < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            if (text[index] == '&' && TryDecode(text, index, out var value, out var length))
            {
                builder.Append(value);
                index += length;
                continue;
            }

            builder.Append(text[index]);
            index++;
        }

        return builder.ToString();
    }
}
