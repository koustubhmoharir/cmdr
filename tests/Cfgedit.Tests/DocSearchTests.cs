using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Shared.Core;

namespace Cfgedit.Tests;

/// <summary>
/// The matches of a pattern over a document: which parts are searched, the order of the matches, the case and whole-word options, and the
/// marks that the highlights are drawn from. A match is named by its node's first part, its part's kind, and its range.
/// </summary>
public sealed class DocSearchTests
{
    // {"name": "web", "list": [10, "x"], "obj": {"a": 1}}: the list has a summary "2" and items with indexes "0" and "1";
    // the object has a summary "1".
    private const string Json = "{\"name\": \"web\", \"list\": [10, \"x\"], \"obj\": {\"a\": 1}}";

    private static DocSearch Search(string text, string format, string query, bool caseSensitive = false, bool wholeWord = false) =>
        DocSearch.Build(TreeFixtures.Parse(text, format), Pattern(query, caseSensitive, wholeWord));

    private static SearchPattern Pattern(string query, bool caseSensitive = false, bool wholeWord = false)
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery(query, caseSensitive, wholeWord, false), out string? error);
        Assert.Null(error);
        return pattern!;
    }

    private static string Describe(DocMatch match) =>
        $"{match.Node.Parts[0].Value}/{match.Node.Parts[match.Part].Kind}@{match.Start}+{match.Length}";

    private static void AssertMatches(DocSearch search, params string[] expected) =>
        Assert.Equal(expected, search.Matches.Select(Describe).ToArray());

    [Fact]
    public void A_key_and_a_value_are_both_searched_in_document_order()
    {
        AssertMatches(Search(Json, "json", "e"), "name/Key@3+1", "name/Value@1+1");
    }

    [Fact]
    public void Summaries_and_indexes_are_not_searched_but_the_values_they_sit_beside_are()
    {
        // "1" is the index of the second item and the summary of the object, and it is also in the value 10 and in the value of "a".
        AssertMatches(Search(Json, "json", "1"), "0/Value@0+1", "a/Value@0+1");
    }

    [Fact]
    public void A_summary_alone_matches_nothing()
    {
        AssertMatches(Search(Json, "json", "2"));
    }

    [Fact]
    public void An_index_alone_matches_nothing()
    {
        // The item indexes are "0" and "1"; the only "0" in the document is inside the value 10.
        AssertMatches(Search(Json, "json", "0"), "0/Value@1+1");
    }

    [Fact]
    public void The_root_marker_is_not_searched_so_a_dollar_sign_matches_only_the_value_that_holds_it()
    {
        // The root marker is "$", as is the value below; only the value is a match.
        AssertMatches(Search("{\"a\": \"$\"}", "json", "$"), "a/Value@0+1");
    }

    [Fact]
    public void Matches_ignore_case_unless_asked_and_whole_words_need_word_edges()
    {
        const string text = "{\"a\": \"web\", \"b\": \"WEB\", \"c\": \"webs\"}";

        AssertMatches(Search(text, "json", "web"), "a/Value@0+3", "b/Value@0+3", "c/Value@0+3");
        AssertMatches(Search(text, "json", "WEB", caseSensitive: true), "b/Value@0+3");
        AssertMatches(Search(text, "json", "web", wholeWord: true), "a/Value@0+3", "b/Value@0+3");
    }

    [Fact]
    public void A_regular_expression_matches_the_logical_values()
    {
        SearchPattern pattern = SearchPattern.Create(new SearchQuery("w.b|x", false, false, true), out string? error)!;
        Assert.Null(error);

        DocSearch search = DocSearch.Build(TreeFixtures.Parse("{\"a\": \"web\", \"b\": \"xyz\"}", "json"), pattern);

        AssertMatches(search, "a/Value@0+3", "b/Value@0+1");
    }

    [Fact]
    public void The_matches_of_one_part_are_listed_left_to_right()
    {
        AssertMatches(Search("{\"s\": \"aXaXa\"}", "json", "a"), "s/Value@0+1", "s/Value@2+1", "s/Value@4+1");
    }

    [Fact]
    public void In_xml_the_element_name_is_searched_but_its_end_tag_is_not()
    {
        // <root> has children, so it has an end row, whose name "root" must not match a second time.
        AssertMatches(Search("<root><item>text</item></root>", "xml", "root"), "root/ElementName@0+4");
    }

    [Fact]
    public void In_xml_attributes_text_comments_and_cdata_are_searched()
    {
        const string text = "<r id=\"r1\"><!--note--><![CDATA[note]]><t>text</t></r>";

        // A CDATA section's text is a Text part, as a text node's is.
        AssertMatches(Search(text, "xml", "note"), "note/Comment@0+4", "note/Text@0+4");
        AssertMatches(Search(text, "xml", "r1"), "r/AttributeValue@0+2");
        AssertMatches(Search(text, "xml", "text"), "t/Text@0+4");
    }

    [Fact]
    public void The_declaration_is_read_only_but_it_is_searched()
    {
        DocSearch search = Search("<?xml version=\"1.0\"?><r/>", "xml", "version");

        DocMatch match = Assert.Single(search.Matches);
        Assert.Equal(PartKind.ReadOnly, match.Node.Parts[match.Part].Kind);
    }

    [Fact]
    public void The_order_of_a_match_follows_the_document()
    {
        DocSearch search = Search("<a><b>x</b><c>x</c></a>", "xml", "x");

        AssertMatches(search, "b/Text@0+1", "c/Text@0+1");
        Assert.True(search.Matches[0].Order < search.Matches[1].Order);
    }

    [Fact]
    public void The_end_row_of_an_element_comes_after_the_matches_of_its_content()
    {
        DocSearch search = Search("<a><b>x</b></a>", "xml", "x");
        DocNode element = TreeFixtures.Parse("<a><b>x</b></a>", "xml").Root.Children[0];

        DocMatch match = Assert.Single(search.Matches);
        Assert.True(search.OrderOf(element.Path, isEnd: false, 0) < match.Order);
        Assert.True(search.OrderOf(element.Path, isEnd: true, 0) > match.Order);
    }

    [Fact]
    public void The_marks_of_a_part_name_the_active_match_only_in_its_own_part()
    {
        DocSearch search = Search("{\"s\": \"aXaXa\", \"t\": \"a\"}", "json", "a");
        DocMatch third = search.Matches[2];
        DocMatch last = search.Matches[^1];

        PartMarks? marks = search.MarksOf(third.Node.Path, third.Part, third);
        Assert.NotNull(marks);
        Assert.Equal(3, marks.Matches.Count);
        Assert.Equal(2, marks.Active);

        PartMarks? other = search.MarksOf(last.Node.Path, last.Part, third);
        Assert.NotNull(other);
        Assert.Equal(-1, other.Active);
        Assert.Null(search.MarksOf(last.Node.Path, 0, null));
    }

    [Fact]
    public void A_document_without_matches_has_no_matches_and_no_marks()
    {
        DocSearch search = Search(Json, "json", "zzz");

        Assert.Empty(search.Matches);
        Assert.False(search.TimedOut);
        Assert.Null(search.MarksOf(TreeFixtures.Parse(Json, "json").Root.Children[0].Path, 1, null));
    }
}
