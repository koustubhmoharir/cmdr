namespace Logvu.Core.Reading;

/// <summary>
/// One line of a file, identified by its byte extent. <see cref="Text"/> is only a prefix of the content (see
/// <see cref="LineReader.TextCap"/>); <see cref="Truncated"/> says the prefix is cut short.
/// </summary>
/// <param name="Start">The offset of the first byte of the line.</param>
/// <param name="End">The offset after the line, including its <c>\n</c>; the end of data for an unterminated line.</param>
/// <param name="Terminated">Whether the line ends with <c>\n</c>.</param>
/// <param name="Text">The decoded content without <c>\r\n</c> or <c>\n</c>, cut to a character boundary at the text cap.</param>
/// <param name="Truncated">Whether the content was longer than the text cap and <paramref name="Text"/> is a prefix.</param>
public readonly record struct RawLine(long Start, long End, bool Terminated, string Text, bool Truncated);
