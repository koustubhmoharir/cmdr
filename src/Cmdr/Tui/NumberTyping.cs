using System.Globalization;
using System.Text;

namespace Cmdr.Tui;

/// <summary>
/// Typed digits that select a row by its 1-based number. Digits typed within <see cref="Timeout"/> of each other form one
/// number; a number that is out of range starts again with the last digit. Pure: the time is passed in.
/// </summary>
internal sealed class NumberTyping
{
    /// <summary>How long after a digit the next one still continues the number.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private readonly StringBuilder _buffer = new();
    private DateTimeOffset _last;

    /// <summary>
    /// Takes a typed digit. Returns the 0-based index to select, or null when no row is selected. Only digits are accepted.
    /// </summary>
    /// <param name="digit">A character from '0' to '9'.</param>
    /// <param name="now">The time of the key.</param>
    /// <param name="count">The number of rows; numbers from 1 to this are valid.</param>
    public int? Type(char digit, DateTimeOffset now, int count)
    {
        if (digit is < '0' or > '9')
        {
            throw new ArgumentOutOfRangeException(nameof(digit), "Only the digits 0 to 9 are accepted.");
        }

        if (_buffer.Length > 0 && now - _last > Timeout)
        {
            _buffer.Clear();
        }

        _last = now;
        _buffer.Append(digit);
        if (TryIndex(count, out int index))
        {
            return index;
        }

        // Out of range: the digit starts a new number.
        _buffer.Clear();
        _buffer.Append(digit);
        if (TryIndex(count, out index))
        {
            return index;
        }

        _buffer.Clear();
        return null;
    }

    /// <summary>Forgets the digits typed so far. Call on any key that is not a digit.</summary>
    public void Reset() => _buffer.Clear();

    private bool TryIndex(int count, out int index)
    {
        if (int.TryParse(_buffer.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            && value >= 1 && value <= count)
        {
            index = value - 1;
            return true;
        }

        index = -1;
        return false;
    }
}
