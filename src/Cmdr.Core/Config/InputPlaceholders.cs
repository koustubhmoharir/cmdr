using System.Text;

namespace Cmdr.Core.Config;

/// <summary>
/// Input placeholders in command arguments. <c>${input:NAME}</c> is replaced by a value the user is asked for before the
/// command starts and must be non-empty; <c>${input:NAME?}</c> may be empty. <c>$${input:</c> is an escape that produces the
/// literal text <c>${input:</c>. Text that looks like a placeholder but is malformed is reported by <see cref="Errors"/> and
/// is copied literally by <see cref="Substitute(string, IReadOnlyDictionary{string, string})"/>.
/// </summary>
public static class InputPlaceholders
{
    private const string Opening = "${input:";
    private const string Escaped = "$${input:";

    /// <summary>An input the user must supply: its name, and whether an empty value is allowed.</summary>
    /// <param name="Name">The name, case-sensitive.</param>
    /// <param name="AllowEmpty">True when every occurrence of the name is written with <c>?</c>.</param>
    public sealed record InputRequest(string Name, bool AllowEmpty);

    /// <summary>
    /// Describes every malformed placeholder in the arguments, e.g. <c>args[2]: malformed input placeholder '${input:a b}'</c>.
    /// Empty when all placeholders are well formed.
    /// </summary>
    public static IReadOnlyList<string> Errors(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var errors = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            foreach (Token token in Scan(args[i]))
            {
                if (token.Malformed is not null)
                {
                    errors.Add($"args[{i}]: malformed input placeholder '{token.Malformed}'");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// The inputs the argument lists need: distinct names in first-seen order. A name is required (<see cref="InputRequest.AllowEmpty"/>
    /// false) if any occurrence lacks <c>?</c>. Malformed placeholders are ignored.
    /// </summary>
    public static IReadOnlyList<InputRequest> Requests(IEnumerable<IReadOnlyList<string>> argLists)
    {
        ArgumentNullException.ThrowIfNull(argLists);

        var order = new List<string>();
        var allowEmpty = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (IReadOnlyList<string> args in argLists)
        {
            foreach (string arg in args)
            {
                foreach (Token token in Scan(arg))
                {
                    if (token.Name is not { } name)
                    {
                        continue;
                    }

                    if (allowEmpty.TryGetValue(name, out bool current))
                    {
                        allowEmpty[name] = current && token.AllowEmpty;
                    }
                    else
                    {
                        order.Add(name);
                        allowEmpty[name] = token.AllowEmpty;
                    }
                }
            }
        }

        return order.Select(name => new InputRequest(name, allowEmpty[name])).ToList();
    }

    /// <summary>
    /// Replaces the placeholders of one argument with their values.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A placeholder has no value, or its value is empty and the placeholder does not allow an empty value. The message names
    /// the input and never contains a value.
    /// </exception>
    public static string Substitute(string arg, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(arg);
        ArgumentNullException.ThrowIfNull(values);

        var result = new StringBuilder(arg.Length);
        foreach (Token token in Scan(arg))
        {
            if (token.Name is not { } name)
            {
                result.Append(token.Text);
                continue;
            }

            if (!values.TryGetValue(name, out string? value))
            {
                throw new InvalidOperationException($"no value for input '{name}'");
            }

            if (value.Length == 0 && !token.AllowEmpty)
            {
                throw new InvalidOperationException($"input '{name}' must not be empty");
            }

            result.Append(value);
        }

        return result.ToString();
    }

    /// <summary>Replaces the placeholders of every argument. See <see cref="Substitute(string, IReadOnlyDictionary{string, string})"/>.</summary>
    /// <exception cref="InvalidOperationException">A value is missing or empty where it is not allowed.</exception>
    public static IReadOnlyList<string> Substitute(IReadOnlyList<string> args, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Select(arg => Substitute(arg, values)).ToList();
    }

    /// <summary>
    /// One piece of an argument: literal text (<see cref="Name"/> null, <see cref="Text"/> is what to copy), a well-formed
    /// placeholder (<see cref="Name"/> set, <see cref="Text"/> is its source text), or a malformed placeholder
    /// (<see cref="Malformed"/> set, copied literally).
    /// </summary>
    private readonly record struct Token(string Text, string? Name = null, bool AllowEmpty = false, string? Malformed = null);

    /// <summary>Splits an argument into literal text, placeholders and malformed placeholders, in order.</summary>
    private static IEnumerable<Token> Scan(string arg)
    {
        var literal = new StringBuilder();
        int i = 0;
        while (i < arg.Length)
        {
            if (string.CompareOrdinal(arg, i, Escaped, 0, Escaped.Length) == 0)
            {
                // The escape yields the opening text literally; the rest of the text is scanned normally.
                literal.Append(Opening);
                i += Escaped.Length;
                continue;
            }

            if (string.CompareOrdinal(arg, i, Opening, 0, Opening.Length) != 0)
            {
                literal.Append(arg[i]);
                i++;
                continue;
            }

            if (literal.Length > 0)
            {
                yield return new Token(literal.ToString());
                literal.Clear();
            }

            int start = i;
            int bodyStart = i + Opening.Length;
            int close = arg.IndexOf('}', bodyStart);
            if (close < 0)
            {
                yield return new Token(arg[start..], Malformed: arg[start..]);
                yield break;
            }

            string source = arg[start..(close + 1)];
            string body = arg[bodyStart..close];
            bool allowEmpty = body.EndsWith('?');
            string name = allowEmpty ? body[..^1] : body;
            if (name.Length == 0 || !name.All(IsNameChar))
            {
                yield return new Token(source, Malformed: source);
            }
            else
            {
                yield return new Token(source, name, allowEmpty);
            }

            i = close + 1;
        }

        if (literal.Length > 0)
        {
            yield return new Token(literal.ToString());
        }
    }

    private static bool IsNameChar(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '-';
}
