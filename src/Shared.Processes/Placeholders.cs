using System.Text;

namespace Shared.Processes;

/// <summary>An input the user must supply: its name, and whether an empty value is allowed.</summary>
/// <param name="Name">The name, case-sensitive.</param>
/// <param name="AllowEmpty">True when every occurrence of the name is written with <c>?</c>.</param>
public sealed record InputRequest(string Name, bool AllowEmpty);

/// <summary>
/// The placeholders of config text, one syntax for all of them. <c>${env:NAME}</c> is an environment variable (in an env value,
/// the value from the level above; undefined means empty). <c>${input:NAME}</c> is a value the user is asked for when a command
/// starts, and <c>${input:NAME?}</c> allows it to be empty. <c>${cwd}</c>, <c>${configDir}</c> and <c>${pathsep}</c> are built-ins.
/// <c>$${</c> is an escape for the literal text <c>${</c>. Any other <c>$</c> is literal.
/// </summary>
/// <remarks>
/// Text of the form <c>${...}</c> that is none of these is malformed: <see cref="Errors"/> reports it, and every expansion copies it
/// literally. A bare <c>${NAME}</c> gets its own message that suggests <c>${env:NAME}</c>. An unterminated <c>${</c> is literal
/// too, and is reported.
/// </remarks>
public static class Placeholders
{
    private const string Escape = "$${";
    private const string Opening = "${";
    private const string EnvPrefix = "env:";
    private const string InputPrefix = "input:";

    /// <summary>
    /// Describes every malformed placeholder in <paramref name="text"/>, one message each, e.g. <c>malformed placeholder '${input:a b}'</c>
    /// or <c>unknown placeholder '${PATH}' (write ${env:PATH} for a variable, or $${ for a literal ${)</c>. Empty when all placeholders
    /// are well formed. A message contains only the source text (and, for an unknown placeholder, the suggestion).
    /// </summary>
    public static IReadOnlyList<string> Errors(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var errors = new List<string>();
        foreach (Token token in Scan(text))
        {
            switch (token.Kind)
            {
                case TokenKind.Malformed:
                    errors.Add("malformed placeholder '" + token.Source + "'");
                    break;
                case TokenKind.Unterminated:
                    errors.Add("unterminated placeholder '" + token.Source + "'");
                    break;
                case TokenKind.Unknown:
                    errors.Add("unknown placeholder '" + token.Source + "' (write ${env:" + token.Name + "} for a variable, or $${ for a literal ${)");
                    break;
            }
        }

        return errors;
    }

    /// <summary>
    /// The inputs the texts need: distinct names in first-seen order. A name is required (<see cref="InputRequest.AllowEmpty"/>
    /// false) if any occurrence lacks <c>?</c>. Malformed placeholders and escapes are ignored.
    /// </summary>
    public static IReadOnlyList<InputRequest> Requests(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var order = new List<string>();
        var allowEmpty = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (string text in texts)
        {
            foreach (Token token in Scan(text))
            {
                if (token.Kind != TokenKind.Input)
                {
                    continue;
                }

                if (allowEmpty.TryGetValue(token.Name, out bool current))
                {
                    allowEmpty[token.Name] = current && token.AllowEmpty;
                }
                else
                {
                    order.Add(token.Name);
                    allowEmpty[token.Name] = token.AllowEmpty;
                }
            }
        }

        return order.Select(name => new InputRequest(name, allowEmpty[name])).ToList();
    }

    /// <summary>
    /// The names of the well-formed <c>${env:NAME}</c> placeholders of <paramref name="text"/>, in text order, one per occurrence (a
    /// name is repeated when it occurs again). Escapes, inputs, built-ins and malformed placeholders are not included. Empty when none.
    /// </summary>
    public static IReadOnlyList<string> EnvNames(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Scan(text).Where(token => token.Kind == TokenKind.Env).Select(token => token.Name).ToList();
    }

    /// <summary>
    /// The names of the built-in placeholders of <paramref name="text"/> (<c>cwd</c>, <c>configDir</c> and <c>pathsep</c>), in text order,
    /// one per occurrence. Escapes, inputs, variables and malformed placeholders are not included. Empty when none.
    /// </summary>
    public static IReadOnlyList<string> BuiltInNames(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Scan(text).Where(token => token.Kind == TokenKind.BuiltIn).Select(token => token.Name).ToList();
    }

    /// <summary>
    /// Expands every placeholder of <paramref name="text"/> in one pass. <c>${env:NAME}</c> becomes <paramref name="env"/>(NAME), or
    /// empty when that is null. The built-ins become <paramref name="cwd"/>, <paramref name="configDir"/> and
    /// <paramref name="pathSeparator"/>. <c>$${</c> becomes <c>${</c>. Malformed placeholders are copied literally. Input placeholders
    /// are copied as written when <paramref name="inputs"/> is null (for display), and otherwise replaced by their values. A value
    /// is never scanned again.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// An input placeholder has no value, or its value is empty and the placeholder does not allow an empty value. The message names
    /// the input and never contains a value.
    /// </exception>
    public static string Expand(
        string text,
        Func<string, string?> env,
        IReadOnlyDictionary<string, string>? inputs,
        string cwd,
        string configDir,
        char pathSeparator)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(cwd);
        ArgumentNullException.ThrowIfNull(configDir);

        var result = new StringBuilder(text.Length);
        foreach (Token token in Scan(text))
        {
            switch (token.Kind)
            {
                case TokenKind.Env:
                    result.Append(env(token.Name) ?? "");
                    break;
                case TokenKind.BuiltIn:
                    result.Append(token.Name switch
                    {
                        "cwd" => cwd,
                        "configDir" => configDir,
                        _ => pathSeparator.ToString(),
                    });
                    break;
                case TokenKind.Input:
                    result.Append(inputs is null ? token.Source : ValueOf(token, inputs));
                    break;
                case TokenKind.Escape:
                    result.Append(Opening);
                    break;
                default:
                    result.Append(token.Source);
                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Checks that every input the texts use has a value that <see cref="Expand"/> would accept, without expanding anything. Throws
    /// for the first one that does not, in text order.
    /// </summary>
    /// <exception cref="InvalidOperationException">An input has no value, or an empty value where it is not allowed.</exception>
    public static void CheckInputs(IEnumerable<string> texts, IReadOnlyDictionary<string, string> inputs)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(inputs);

        foreach (string text in texts)
        {
            foreach (Token token in Scan(text))
            {
                if (token.Kind == TokenKind.Input)
                {
                    _ = ValueOf(token, inputs);
                }
            }
        }
    }

    private static string ValueOf(Token token, IReadOnlyDictionary<string, string> inputs)
    {
        if (!inputs.TryGetValue(token.Name, out string? value))
        {
            throw new InvalidOperationException($"no value for input '{token.Name}'");
        }

        if (value.Length == 0 && !token.AllowEmpty)
        {
            throw new InvalidOperationException($"input '{token.Name}' must not be empty");
        }

        return value;
    }

    private enum TokenKind
    {
        /// <summary>Literal text; <see cref="Token.Source"/> is copied as written.</summary>
        Text,

        /// <summary><c>$${</c>; means the literal <c>${</c>.</summary>
        Escape,

        /// <summary><c>${input:NAME}</c>; <see cref="Token.Name"/> and <see cref="Token.AllowEmpty"/> are set.</summary>
        Input,

        /// <summary><c>${env:NAME}</c>; <see cref="Token.Name"/> is set.</summary>
        Env,

        /// <summary><c>${cwd}</c>, <c>${configDir}</c> or <c>${pathsep}</c>; <see cref="Token.Name"/> is set.</summary>
        BuiltIn,

        /// <summary>A bare <c>${NAME}</c>, reported with a suggestion; copied literally.</summary>
        Unknown,

        /// <summary>A <c>${...}</c> that is none of the above; copied literally.</summary>
        Malformed,

        /// <summary>A <c>${</c> with no closing <c>}</c>, through the end of the text; copied literally.</summary>
        Unterminated,
    }

    /// <summary>One piece of a text. <see cref="Source"/> is what the text says; <see cref="Name"/> is the name for placeholders.</summary>
    private readonly record struct Token(TokenKind Kind, string Source, string Name = "", bool AllowEmpty = false);

    /// <summary>Splits a text into literal runs and placeholders, in order.</summary>
    private static List<Token> Scan(string text)
    {
        var tokens = new List<Token>();
        var literal = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            if (StartsAt(text, i, Escape))
            {
                Flush(tokens, literal);
                tokens.Add(new Token(TokenKind.Escape, Escape));
                i += Escape.Length;
                continue;
            }

            if (!StartsAt(text, i, Opening))
            {
                literal.Append(text[i]);
                i++;
                continue;
            }

            Flush(tokens, literal);
            int close = text.IndexOf('}', i + Opening.Length);
            if (close < 0)
            {
                tokens.Add(new Token(TokenKind.Unterminated, text[i..]));
                break;
            }

            tokens.Add(Classify(text[i..(close + 1)], text[(i + Opening.Length)..close]));
            i = close + 1;
        }

        Flush(tokens, literal);
        return tokens;
    }

    /// <summary>Classifies the body of a closed <c>${...}</c> (the text between <c>${</c> and <c>}</c>).</summary>
    private static Token Classify(string source, string body)
    {
        if (body is "cwd" or "configDir" or "pathsep")
        {
            return new Token(TokenKind.BuiltIn, source, body);
        }

        if (body.StartsWith(EnvPrefix, StringComparison.Ordinal))
        {
            string name = body[EnvPrefix.Length..];
            return IsEnvName(name) ? new Token(TokenKind.Env, source, name) : new Token(TokenKind.Malformed, source);
        }

        if (body.StartsWith(InputPrefix, StringComparison.Ordinal))
        {
            string rest = body[InputPrefix.Length..];
            bool allowEmpty = rest.EndsWith('?');
            string name = allowEmpty ? rest[..^1] : rest;
            return name.Length > 0 && name.All(IsInputNameChar)
                ? new Token(TokenKind.Input, source, name, allowEmpty)
                : new Token(TokenKind.Malformed, source);
        }

        // A bare name without a colon is what a variable reference would look like; it is reported with the env form to use.
        if (body.Length > 0 && body.IndexOf(':') < 0 && IsEnvName(body))
        {
            return new Token(TokenKind.Unknown, source, body);
        }

        return new Token(TokenKind.Malformed, source);
    }

    private static void Flush(List<Token> tokens, StringBuilder literal)
    {
        if (literal.Length > 0)
        {
            tokens.Add(new Token(TokenKind.Text, literal.ToString()));
            literal.Clear();
        }
    }

    private static bool StartsAt(string text, int index, string prefix) =>
        index + prefix.Length <= text.Length && string.CompareOrdinal(text, index, prefix, 0, prefix.Length) == 0;

    /// <summary>A variable name: non-empty, and without <c>=</c> or NUL. Anything else, such as spaces or <c>(</c>, is allowed (Windows has <c>ProgramFiles(x86)</c>).</summary>
    public static bool IsEnvName(string name) =>
        name.Length > 0 && name.IndexOf('=') < 0 && name.IndexOf('\0') < 0;

    private static bool IsInputNameChar(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '-';
}
