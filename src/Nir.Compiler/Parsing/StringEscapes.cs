namespace Nir.Compiler.Parsing;

/// <summary>Turns the raw text between the quotes of a string into the characters it stands for.</summary>
internal static class StringEscapes
{
    /// <summary>
    /// Decodes <c>\n \t \r \\ \" \{</c>, and in a character literal (<paramref name="forCharacter"/>) also <c>\' \0</c>. In an
    /// interpolated string, <c>{{</c> and <c>}}</c> are also one brace each.
    /// <paramref name="onUnknown"/> is called with the offset in <paramref name="raw"/>, the length (2) and the text of every
    /// other backslash sequence, which is then dropped from the result.
    /// </summary>
    public static string Decode(string raw, bool interpolated, bool forCharacter, Action<int, int, string> onUnknown)
    {
        if (!raw.Contains('\\', StringComparison.Ordinal) && !(interpolated && (raw.Contains("{{", StringComparison.Ordinal) || raw.Contains("}}", StringComparison.Ordinal))))
        {
            return raw;
        }

        var decoded = new System.Text.StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '\\' && i + 1 < raw.Length)
            {
                var next = raw[i + 1];
                switch (next)
                {
                    case 'n': decoded.Append('\n'); break;
                    case 't': decoded.Append('\t'); break;
                    case 'r': decoded.Append('\r'); break;
                    case '\\': decoded.Append('\\'); break;
                    case '"': decoded.Append('"'); break;
                    case '{': decoded.Append('{'); break;
                    case '\'' when forCharacter: decoded.Append('\''); break;
                    case '0' when forCharacter: decoded.Append('\0'); break;
                    default: onUnknown(i, 2, raw.Substring(i, 2)); break;
                }

                i++;
            }
            else if (interpolated && (c == '{' || c == '}') && i + 1 < raw.Length && raw[i + 1] == c)
            {
                decoded.Append(c);
                i++;
            }
            else
            {
                decoded.Append(c);
            }
        }

        return decoded.ToString();
    }
}
