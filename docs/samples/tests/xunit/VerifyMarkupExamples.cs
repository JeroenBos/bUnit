using Xunit;
using Bunit;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

using static Bunit.ComponentParameterFactory;

namespace Bunit.Docs.Samples
{
  public class VerifyMarkupExamples
  {
    [Fact]
    public void RawMarkupVerify()
    {
      using var ctx = new TestContext();

      var cut = ctx.RenderComponent<HelloWorld>();

      var renderedMarkup = cut.Markup;
      Assert.Equal("<h1>Hello world from Blazor</h1>", renderedMarkup);
    }

    [Fact]
    public void MarkupMatchesOnRenderedFragment()
    {
      using var ctx = new TestContext();

      var cut = ctx.RenderComponent<Heading>();

      cut.MarkupMatches(@"<h3 id=""heading-1337"" required>
                            Heading text
                            <small class=""mark text-muted"">Secondary text</small>
                          </h3>");
    }

    [Fact]
    public void MarkupMatchesOnNode()
    {
      using var ctx = new TestContext();

      var cut = ctx.RenderComponent<Heading>();

      var smallElm = cut.Find("small");
      smallElm.MarkupMatches(@"<small class=""mark text-muted"">Secondary text</small>");
    }

    [Fact]
    public void MarkupMatchesOnTextNode()
    {
      using var ctx = new TestContext();

      var cut = ctx.RenderComponent<Heading>();

      var smallElmText = cut.Find("small").TextContent;
      smallElmText.MarkupMatches("Secondary text");
    }

    [Fact]
    public void FindAndFindAll()
    {
      using var ctx = new TestContext();

      var cut = ctx.RenderComponent<FancyTable>();

      var tableCaption = cut.Find("caption");
      var tableCells = cut.FindAll("td:first-child");

      Assert.Empty(tableCaption.Attributes);
      Assert.Equal(2, tableCells.Count);
      Assert.All(tableCells, td => td.HasAttribute("style"));
    }
  }
}