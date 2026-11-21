using System.Text;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Tests.Text;

public sealed class SourceTextTests
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

    /// <summary>U+2028, written as a char constant so that no source file holds the raw character.</summary>
    private static readonly string LineSeparator = ((char)0x2028).ToString();

    private static byte[] Concat(params byte[][] parts)
    {
        return parts.SelectMany(p => p).ToArray();
    }

    // ----- Round trips -----

    [Fact]
    public void Utf8_without_bom_round_trips_byte_for_byte()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("héllo 日\n");

        SourceText text = SourceText.Decode(bytes);

        Assert.Equal("héllo 日\n", text.Text);
        Assert.False(text.HasBom);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Utf8_with_bom_round_trips_and_drops_the_bom_from_the_text()
    {
        byte[] bytes = Concat(Utf8Bom, Encoding.UTF8.GetBytes("a\r\nb"));

        SourceText text = SourceText.Decode(bytes);

        Assert.Equal("a\r\nb", text.Text);
        Assert.True(text.HasBom);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Utf16_le_with_bom_round_trips()
    {
        byte[] bytes = Concat(Utf16LeBom, [0x61, 0x00, 0x0A, 0x00, 0x62, 0x00]);

        SourceText text = SourceText.Decode(bytes);

        Assert.Equal("a\nb", text.Text);
        Assert.True(text.HasBom);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Utf16_be_with_bom_round_trips()
    {
        byte[] bytes = Concat(Utf16BeBom, [0x00, 0x61, 0x00, 0x0A, 0x00, 0x62]);

        SourceText text = SourceText.Decode(bytes);

        Assert.Equal("a\nb", text.Text);
        Assert.True(text.HasBom);
        Assert.Equal(bytes, text.Encode());
    }

    [Fact]
    public void Utf16_with_a_supplementary_character_round_trips()
    {
        byte[] bytes = Concat(Utf16BeBom, [0xD8, 0x3D, 0xDE, 0x00]);

        SourceText text = SourceText.Decode(bytes);

        Assert.Equal("\U0001F600", text.Text);
        Assert.Equal(bytes, text.Encode());
    }

    [Theory]
    [InlineData("crlf", new byte[] { 0x61, 0x0D, 0x0A, 0x62, 0x0D, 0x0A })]
    [InlineData("lf", new byte[] { 0x61, 0x0A, 0x62, 0x0A })]
    [InlineData("lone CR", new byte[] { 0x61, 0x0D, 0x62, 0x0D })]
    [InlineData("mixed endings", new byte[] { 0x61, 0x0D, 0x0A, 0x62, 0x0A, 0x63, 0x0D })]
    [InlineData("no final newline", new byte[] { 0x7B, 0x7D })]
    [InlineData("empty", new byte[] { })]
    [InlineData("only a newline", new byte[] { 0x0A })]
    public void Utf8_line_ending_variants_round_trip(string name, byte[] bytes)
    {
        SourceText text = SourceText.Decode(bytes);

        Assert.True(text.Encode().AsSpan().SequenceEqual(bytes), name);
    }

    [Fact]
    public void Empty_input_decodes_to_empty_text_without_bom()
    {
        SourceText text = SourceText.Decode([]);

        Assert.Equal("", text.Text);
        Assert.False(text.HasBom);
        Assert.Empty(text.Encode());
    }

    [Fact]
    public void Input_that_is_only_a_utf8_bom_keeps_the_bom()
    {
        SourceText text = SourceText.Decode(Utf8Bom);

        Assert.Equal("", text.Text);
        Assert.True(text.HasBom);
        Assert.Equal(Utf8Bom, text.Encode());
    }

    [Fact]
    public void Input_that_is_only_a_utf16_le_bom_keeps_the_bom()
    {
        SourceText text = SourceText.Decode(Utf16LeBom);

        Assert.Equal("", text.Text);
        Assert.True(text.HasBom);
        Assert.Equal(Utf16LeBom, text.Encode());
    }

    [Fact]
    public void Input_that_is_only_a_utf16_be_bom_keeps_the_bom()
    {
        SourceText text = SourceText.Decode(Utf16BeBom);

        Assert.Equal("", text.Text);
        Assert.True(text.HasBom);
        Assert.Equal(Utf16BeBom, text.Encode());
    }

    [Fact]
    public void Create_with_a_bom_writes_the_bom_before_the_text()
    {
        SourceText text = SourceText.Create("x", Encoding.Unicode, hasBom: true);

        Assert.Equal([0xFF, 0xFE, 0x78, 0x00], text.Encode());
    }

    [Fact]
    public void Create_without_a_bom_writes_utf8_text_only()
    {
        SourceText text = SourceText.Create("é", Encoding.UTF8, hasBom: false);

        Assert.Equal([0xC3, 0xA9], text.Encode());
    }

    [Fact]
    public void Create_maps_big_endian_unicode_to_its_strict_form()
    {
        SourceText text = SourceText.Create("x", Encoding.BigEndianUnicode, hasBom: false);

        Assert.Equal([0x00, 0x78], text.Encode());
    }

    [Fact]
    public void Create_rejects_an_encoding_other_than_utf8_or_utf16()
    {
        Assert.Throws<ArgumentException>(() => SourceText.Create("x", Encoding.Latin1, hasBom: false));
    }

    [Fact]
    public void WithText_keeps_the_encoding_and_bom_and_recomputes_newlines()
    {
        SourceText original = SourceText.Create("a\r\nb", Encoding.UTF8, hasBom: true);

        SourceText changed = original.WithText("a\nb\nc\n");

        Assert.True(changed.HasBom);
        Assert.Same(original.Encoding, changed.Encoding);
        Assert.Equal("\n", changed.Newline);
        Assert.True(changed.EndsWithNewline);
        Assert.Equal(Concat(Utf8Bom, "a\nb\nc\n"u8.ToArray()), changed.Encode());
    }

    [Fact]
    public void Text_with_a_lone_surrogate_cannot_be_encoded_and_the_error_has_a_position()
    {
        SourceText text = SourceText.Create("ab\uD800", Encoding.UTF8, hasBom: false);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => text.Encode());

        Assert.Equal(1, ex.Line);
        Assert.Equal(3, ex.Column);
    }

    // ----- Decoding errors -----

    [Fact]
    public void Lone_continuation_byte_is_invalid_at_its_byte_offset()
    {
        byte[] bytes = [0x80];

        CfgeditException ex = Assert.Throws<CfgeditException>(() => SourceText.Decode(bytes));

        Assert.Equal("not valid UTF-8 at byte 0", ex.Message);
        Assert.Null(ex.Line);
        Assert.Null(ex.Column);
    }

    [Fact]
    public void Invalid_utf8_reports_the_last_good_character_and_the_byte_offset()
    {
        byte[] bytes = [0x61, 0x80];

        CfgeditException ex = Assert.Throws<CfgeditException>(() => SourceText.Decode(bytes, "app.json"));

        Assert.Equal("not valid UTF-8 at byte 1", ex.Message);
        Assert.Equal("app.json", ex.Path);
        Assert.Equal(1, ex.Line);
        Assert.Equal(1, ex.Column);
    }

    [Fact]
    public void Overlong_encoding_is_invalid()
    {
        byte[] bytes = [0x61, 0xC0, 0x80];

        CfgeditException ex = Assert.Throws<CfgeditException>(() => SourceText.Decode(bytes));

        Assert.Equal("not valid UTF-8 at byte 1", ex.Message);
    }

    [Fact]
    public void Encoded_surrogate_is_invalid_and_the_offset_counts_the_bom()
    {
        byte[] bytes = Concat(Utf8Bom, [0x61, 0x62, 0xED, 0xA0, 0x80]);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => SourceText.Decode(bytes));

        Assert.Equal("not valid UTF-8 at byte 5", ex.Message);
        Assert.Equal(1, ex.Line);
        Assert.Equal(2, ex.Column);
    }

    [Fact]
    public void Odd_length_utf16_is_invalid_at_the_dangling_byte()
    {
        byte[] bytes = Concat(Utf16LeBom, [0x61]);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => SourceText.Decode(bytes));

        Assert.Equal("not valid UTF-16 LE at byte 2: odd number of bytes", ex.Message);
    }

    [Fact]
    public void Unpaired_high_surrogate_in_utf16_le_is_invalid()
    {
        byte[] bytes = Concat(Utf16LeBom, [0x00, 0xD8]);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => SourceText.Decode(bytes));

        Assert.Equal("not valid UTF-16 LE at byte 2: unpaired surrogate", ex.Message);
    }

    [Fact]
    public void Unpaired_low_surrogate_in_utf16_be_reports_the_last_good_character()
    {
        byte[] bytes = Concat(Utf16BeBom, [0x00, 0x61, 0xDC, 0x00]);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => SourceText.Decode(bytes));

        Assert.Equal("not valid UTF-16 BE at byte 4: unpaired surrogate", ex.Message);
        Assert.Equal(1, ex.Line);
        Assert.Equal(1, ex.Column);
    }

    // ----- Newlines -----

    [Fact]
    public void Dominant_newline_is_the_most_frequent_break()
    {
        Assert.Equal("\r\n", SourceText.Create("a\r\nb\nc\r\n", Encoding.UTF8, false).Newline);
    }

    [Fact]
    public void A_tie_goes_to_the_break_that_occurs_first()
    {
        Assert.Equal("\n", SourceText.Create("a\nb\r\n", Encoding.UTF8, false).Newline);
        Assert.Equal("\r\n", SourceText.Create("a\r\nb\n", Encoding.UTF8, false).Newline);
    }

    [Fact]
    public void A_lone_cr_is_a_newline()
    {
        Assert.Equal("\r", SourceText.Create("a\rb", Encoding.UTF8, false).Newline);
    }

    [Fact]
    public void Text_without_a_break_uses_the_environment_newline()
    {
        Assert.Equal(Environment.NewLine, SourceText.Create("abc", Encoding.UTF8, false).Newline);
        Assert.Equal(Environment.NewLine, SourceText.Create("", Encoding.UTF8, false).Newline);
    }

    [Theory]
    [InlineData("a\n", true)]
    [InlineData("a\r", true)]
    [InlineData("a\r\n", true)]
    [InlineData("a", false)]
    [InlineData("a\nb", false)]
    [InlineData("", false)]
    public void EndsWithNewline_reflects_the_last_character(string text, bool expected)
    {
        Assert.Equal(expected, SourceText.Create(text, Encoding.UTF8, false).EndsWithNewline);
    }

    // ----- Positions -----

    [Fact]
    public void Positions_on_crlf_and_lone_cr_and_lf()
    {
        // a b CR LF c d CR e LF f
        // 0 1 2  3  4 5 6  7 8  9
        SourceText text = SourceText.Create("ab\r\ncd\re\nf", Encoding.UTF8, false);

        Assert.Equal(new LinePosition(1, 1), text.GetPosition(0));
        Assert.Equal(new LinePosition(1, 3), text.GetPosition(2));
        Assert.Equal(new LinePosition(2, 1), text.GetPosition(4));
        Assert.Equal(new LinePosition(2, 3), text.GetPosition(6));
        Assert.Equal(new LinePosition(3, 1), text.GetPosition(7));
        Assert.Equal(new LinePosition(3, 2), text.GetPosition(8));
        Assert.Equal(new LinePosition(4, 1), text.GetPosition(9));
    }

    [Fact]
    public void Tab_counts_as_one_column()
    {
        SourceText text = SourceText.Create("\tx", Encoding.UTF8, false);

        Assert.Equal(new LinePosition(1, 2), text.GetPosition(1));
    }

    [Fact]
    public void Astral_character_counts_as_two_columns()
    {
        SourceText text = SourceText.Create("\U0001F600x", Encoding.UTF8, false);

        Assert.Equal(new LinePosition(1, 3), text.GetPosition(2));
    }

    [Fact]
    public void U_2028_is_not_a_line_break_for_positions()
    {
        SourceText text = SourceText.Create("a" + LineSeparator + "b", Encoding.UTF8, false);

        Assert.Equal(new LinePosition(1, 3), text.GetPosition(2));
    }

    [Fact]
    public void Offset_past_the_end_is_the_position_after_the_last_character()
    {
        SourceText text = SourceText.Create("ab", Encoding.UTF8, false);

        Assert.Equal(new LinePosition(1, 3), text.GetPosition(2));
        Assert.Equal(new LinePosition(1, 3), text.GetPosition(100));
    }

    [Fact]
    public void Offset_at_the_end_after_a_trailing_newline_is_on_a_new_line()
    {
        SourceText text = SourceText.Create("ab\n", Encoding.UTF8, false);

        Assert.Equal(new LinePosition(2, 1), text.GetPosition(3));
    }

    [Fact]
    public void Known_line_and_column_give_known_offsets()
    {
        SourceText text = SourceText.Create("ab\r\ncd\re\nf", Encoding.UTF8, false);

        Assert.Equal(4, text.GetOffset(new LinePosition(2, 1)));
        Assert.Equal(8, text.GetOffset(new LinePosition(3, 2)));
        Assert.Equal(10, text.GetOffset(new LinePosition(4, 2)));
    }

    [Fact]
    public void Offset_and_position_round_trip_at_every_offset()
    {
        // Includes CRLF, a lone CR, U+2028, an astral character and a tab.
        SourceText text = SourceText.Create("ab\r\ncd\re\nf" + LineSeparator + "\U0001F600\t\r\n", Encoding.UTF8, false);

        for (int offset = 0; offset <= text.Text.Length; offset++)
        {
            LinePosition position = text.GetPosition(offset);

            Assert.Equal(offset, text.GetOffset(position));
        }
    }

    [Fact]
    public void Negative_offset_is_rejected()
    {
        SourceText text = SourceText.Create("ab", Encoding.UTF8, false);

        Assert.Throws<ArgumentOutOfRangeException>(() => text.GetPosition(-1));
    }

    [Fact]
    public void Line_outside_the_text_is_rejected()
    {
        SourceText text = SourceText.Create("ab", Encoding.UTF8, false);

        Assert.Throws<ArgumentOutOfRangeException>(() => text.GetOffset(new LinePosition(2, 1)));
    }

    [Fact]
    public void Column_outside_the_line_is_rejected()
    {
        SourceText text = SourceText.Create("ab\ncd", Encoding.UTF8, false);

        Assert.Throws<ArgumentOutOfRangeException>(() => text.GetOffset(new LinePosition(1, 5)));
    }

    [Fact]
    public void Encode_reports_a_character_the_code_page_cannot_represent_with_its_line_and_column()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding windows1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        SourceText text = SourceText.CreateStrict("ab\nc" + (char)0x4E2D, windows1252, hasBom: false);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => text.Encode());

        Assert.Equal("text cannot be saved as Western European (Windows): U+4E2D cannot be encoded at line 2, column 2", ex.Message);
        Assert.Equal(2, ex.Line);
        Assert.Equal(2, ex.Column);
    }

    [Fact]
    public void Encode_reports_an_astral_character_the_code_page_cannot_represent_by_its_code_point()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding windows1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        SourceText text = SourceText.CreateStrict("a" + (char)0xD83D + (char)0xDE00, windows1252, hasBom: false);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => text.Encode());

        Assert.Equal("text cannot be saved as Western European (Windows): U+1F600 cannot be encoded at line 1, column 2", ex.Message);
        Assert.Equal(1, ex.Line);
        Assert.Equal(2, ex.Column);
    }

    [Fact]
    public void Lone_surrogate_message_names_the_surrogate_case()
    {
        SourceText text = SourceText.Create("ab" + (char)0xD800, Encoding.UTF8, false);

        CfgeditException ex = Assert.Throws<CfgeditException>(() => text.Encode());

        Assert.Equal("text cannot be saved as Unicode (UTF-8): a lone surrogate at line 1, column 3", ex.Message);
    }

    [Fact]
    public void CreateStrict_rejects_an_encoding_without_exception_fallbacks()
    {
        Assert.Throws<ArgumentException>(() => SourceText.CreateStrict("x", Encoding.Latin1, hasBom: false));
    }

    [Fact]
    public void CreateStrict_rejects_a_byte_order_mark_for_a_code_page()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding windows1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        Assert.Throws<ArgumentException>(() => SourceText.CreateStrict("x", windows1252, hasBom: true));
    }
}
