using Kavita.API.Services.Metadata;
using Kavita.Services.Metadata;
using Xunit;

namespace Kavita.Services.Tests.Metadata;

public class ExternalMetadataTextTests
{
    [Fact]
    public void Description_decodes_entities_and_turns_blocks_into_line_breaks()
    {
        var description = ExternalMetadataText.CleanDescription(
            "You&rsquo;re after the good stuff. Gems, diamonds, Faberg&eacute; eggs.<br /><br />" +
            "<p>Nothing short will do. You live fast, think fast and move fast.</p>");

        Assert.Equal("You’re after the good stuff. Gems, diamonds, Fabergé eggs.\n\n" +
                     "Nothing short will do. You live fast, think fast and move fast.", description);
    }

    [Fact]
    public void Description_handles_double_encoded_entities_and_escaped_tags()
    {
        var description = ExternalMetadataText.CleanDescription(
            "Faberg&amp;eacute; eggs&amp;lt;br /&amp;gt;The Queen&amp;rsquo;s crown &amp;amp; more");

        Assert.Equal("Fabergé eggs\nThe Queen’s crown & more", description);
    }

    [Fact]
    public void Text_keeps_comparisons_and_removes_only_tag_shaped_markup()
    {
        Assert.Equal("Roll 2 < 5 and 7 > 3", ExternalMetadataText.CleanText("Roll 2 < 5 and 7 > 3"));
        Assert.Equal("Bold and italic", ExternalMetadataText.CleanText("<b>Bold</b> and <i>italic</i>"));
        Assert.Equal("Line one Line two", ExternalMetadataText.CleanText("Line one<br />Line two"));
    }

    [Fact]
    public void Sanitize_cleans_rpg_geek_product_and_search_fields()
    {
        var product = new RpgGeekProduct(370894, "Frontier Scum &amp; Friends", 2022,
            "<p>An acid western RPG.</p>", ["Karl &amp; Co", "<i>Someone</i>"],
            ["Games &amp; Omnivorous"], null);

        var sanitized = ExternalMetadataText.Sanitize(product);

        Assert.Equal("Frontier Scum & Friends", sanitized.Title);
        Assert.Equal("An acid western RPG.", sanitized.Description);
        Assert.Equal(new[] {"Karl & Co", "Someone"}, sanitized.Designers);
        Assert.Equal(new[] {"Games & Omnivorous"}, sanitized.Publishers);
        Assert.Equal("Heart & Spire", ExternalMetadataText.Sanitize(new RpgGeekSearchResult(1, "Heart &amp; Spire", 2020)).Name);
    }

    [Fact]
    public void Sanitize_cleans_drivethru_fields_and_keeps_empty_values()
    {
        var product = new DriveThruRpgProduct(2468, "Dragonbane &mdash; Core Rulebook", ["Author &amp; Co"],
            "<p>Core rules for &eacute;lite play.</p>", "Publisher &amp; Sons", null, null, null);

        var sanitized = ExternalMetadataText.Sanitize(product);

        Assert.Equal("Dragonbane — Core Rulebook", sanitized.Title);
        Assert.Equal("Core rules for élite play.", sanitized.Description);
        Assert.Equal(new[] {"Author & Co"}, sanitized.Authors);
        Assert.Equal("Publisher & Sons", sanitized.Publisher);
        Assert.Null(ExternalMetadataText.Sanitize(product with {Publisher = "<p></p>"}).Publisher);
        Assert.Empty(ExternalMetadataText.CleanText(" <br /> "));
    }
}
