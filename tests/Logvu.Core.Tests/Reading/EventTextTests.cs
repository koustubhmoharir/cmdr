using System.Text;
using Logvu.Core.Reading;

namespace Logvu.Core.Tests.Reading;

public sealed class EventTextTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static (string Text, bool Truncated) Read(byte[] data, long start, long end, int maxBytes = EventText.DefaultCap)
    {
        using var stream = new MemoryStream(data, writable: false);
        return EventText.Read(stream, new EventExtent(start, end, end, Provisional: false, Orphan: false), maxBytes);
    }

    [Fact]
    public void Crlf_becomes_lf_and_the_final_terminator_is_dropped()
    {
        byte[] data = Bytes("a\r\nb\r\n");
        Assert.Equal(("a\nb", false), Read(data, 0, data.Length));
    }

    [Fact]
    public void Only_the_final_terminator_is_dropped()
    {
        byte[] data = Bytes("x\n\n");
        Assert.Equal(("x\n", false), Read(data, 0, data.Length));
    }

    [Fact]
    public void A_blank_event_has_empty_text()
    {
        byte[] data = Bytes("\r\n");
        Assert.Equal(("", false), Read(data, 0, data.Length));
    }

    [Fact]
    public void Unterminated_last_line_is_kept_whole()
    {
        byte[] data = Bytes("a\nb");
        Assert.Equal(("b", false), Read(data, 2, 3));
    }

    [Fact]
    public void Extent_in_the_middle_reads_only_its_bytes()
    {
        byte[] data = Bytes("aa\nbb\ncc\n");
        Assert.Equal(("bb", false), Read(data, 3, 6));
    }

    [Fact]
    public void Byte_order_mark_at_offset_zero_is_skipped()
    {
        byte[] data = Bytes("﻿abc\n");
        Assert.Equal(("abc", false), Read(data, 0, data.Length));
    }

    [Fact]
    public void Carriage_return_before_a_chunk_boundary_joins_its_line_feed()
    {
        // The CR is the last byte of the first 64 KiB chunk and the LF the first byte of the next.
        string head = new('x', 64 * 1024 - 1);
        byte[] data = Bytes(head + "\r\ny\n");
        Assert.Equal((head + "\ny", false), Read(data, 0, data.Length));
    }

    [Fact]
    public void Invalid_utf8_decodes_to_replacement_characters()
    {
        byte[] data = [(byte)'a', 0xFF, (byte)'b', (byte)'\n'];
        Assert.Equal(("a�b", false), Read(data, 0, data.Length));
    }

    [Fact]
    public void Multibyte_character_at_the_cap_boundary_is_not_split()
    {
        // "ab" then the 3-byte "€" at bytes 2..4, "cd" and a final LF: 8 bytes in all.
        byte[] data = Bytes("ab€cd\n");
        Assert.Equal(8, data.Length);

        Assert.Equal(("ab", true), Read(data, 0, data.Length, maxBytes: 3));
        Assert.Equal(("ab", true), Read(data, 0, data.Length, maxBytes: 4));
        Assert.Equal(("ab€", true), Read(data, 0, data.Length, maxBytes: 5));
        Assert.Equal(("ab€cd", true), Read(data, 0, data.Length, maxBytes: 7));
        Assert.Equal(("ab€cd", false), Read(data, 0, data.Length, maxBytes: 8));
    }

    [Fact]
    public void Four_byte_character_at_the_cap_boundary_is_not_split()
    {
        // "😀" is 4 bytes; a cut after its first three must leave out all of it.
        byte[] data = Bytes("a😀b\n");
        (string text, bool truncated) = Read(data, 0, data.Length, maxBytes: 4);
        Assert.Equal("a", text);
        Assert.True(truncated);
    }

    [Fact]
    public void Large_event_is_cut_to_the_cap_with_bounded_text()
    {
        string line = new string('z', 300_000);
        byte[] data = Bytes(line + "\n");

        (string text, bool truncated) = Read(data, 0, data.Length, maxBytes: 1000);

        Assert.True(truncated);
        Assert.Equal(1000, text.Length);
        Assert.True(Encoding.UTF8.GetByteCount(text) <= 1000);
    }

    [Fact]
    public void Large_event_within_the_default_cap_is_read_whole()
    {
        string line = new string('z', 300_000);
        byte[] data = Bytes(line + "\n");
        Assert.Equal((line, false), Read(data, 0, data.Length));
    }

    [Fact]
    public void Empty_extent_has_empty_text()
    {
        byte[] data = Bytes("abc\n");
        Assert.Equal(("", false), Read(data, 2, 2));
    }

    [Fact]
    public void Zero_cap_truncates_a_nonempty_event()
    {
        byte[] data = Bytes("abc\n");
        Assert.Equal(("", true), Read(data, 0, data.Length, maxBytes: 0));
    }

    [Fact]
    public void Negative_cap_is_rejected()
    {
        using var stream = new MemoryStream(Bytes("abc\n"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EventText.Read(stream, new EventExtent(0, 4, 4, false, false), -1));
    }
}
