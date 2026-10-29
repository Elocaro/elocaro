using Bunit;
using Elocaro.Web.Components.Common;

namespace Elocaro.Web.Tests.Components;

public class NoticeTests : BunitContext
{
    [Theory]
    [InlineData(NoticeLevel.Info, "message-info", "status")]
    [InlineData(NoticeLevel.Success, "message-success", "status")]
    [InlineData(NoticeLevel.Warning, "message-warning", "status")]
    [InlineData(NoticeLevel.Error, "message-error", "alert")]
    public void Level_decides_class_and_role(NoticeLevel level, string cssClass, string role)
    {
        var cut = Render<Notice>(p => p.Add(x => x.Level, level).AddChildContent("Text"));

        var element = cut.Find("div.message");
        Assert.Contains(cssClass, element.ClassList);
        Assert.Equal(role, element.GetAttribute("role"));
        Assert.Equal("Text", element.TextContent);
    }

    [Fact]
    public void Additional_attributes_are_passed_through()
    {
        var cut = Render<Notice>(p => p.AddUnmatched("id", "deadline").AddChildContent("Frist"));

        Assert.Equal("deadline", cut.Find("div.message").Id);
    }

    [Fact]
    public void Error_list_renders_a_single_error_as_text_and_several_as_list()
    {
        var one = Render<ErrorList>(p => p.Add(x => x.Errors, ["Eins"]));
        Assert.Empty(one.FindAll("li"));
        Assert.Equal("alert", one.Find("div.message-error").GetAttribute("role"));

        var two = Render<ErrorList>(p => p.Add(x => x.Errors, ["Eins", "Zwei"]));
        Assert.Equal(2, two.FindAll("li").Count);
    }
}
