using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BusinessIntelligence.Host.Components;

/// <summary>Renders <c>&lt;md-icon name="search" /&gt;</c> as a reference into the layout's SVG sprite.</summary>
[HtmlTargetElement("md-icon", TagStructure = TagStructure.WithoutEndTag)]
public sealed class IconTagHelper : TagHelper
{
    public required string Name { get; set; }

    /// <summary>Accessible name; leave empty for decorative icons next to visible text.</summary>
    public string? Label { get; set; }

    public bool Small { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!Icons.Paths.ContainsKey(Name))
        {
            throw new InvalidOperationException($"Unknown icon '{Name}'. Add it to Icons.Paths first.");
        }

        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", Small ? "md-icon md-icon--small" : "md-icon");
        output.Attributes.SetAttribute("viewBox", "0 0 24 24");
        if (string.IsNullOrEmpty(Label))
        {
            output.Attributes.SetAttribute("aria-hidden", "true");
            output.Attributes.SetAttribute("focusable", "false");
        }
        else
        {
            output.Attributes.SetAttribute("role", "img");
            output.Attributes.SetAttribute("aria-label", Label);
        }

        output.Content.SetHtmlContent($"<use href=\"#icon-{Name}\"></use>");
    }
}
