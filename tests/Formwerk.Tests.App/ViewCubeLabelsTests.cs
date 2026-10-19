using Formwerk.App.Resources;
using Formwerk.App.ViewModels;
using Formwerk.Kernel;

namespace Formwerk.Tests.App;

/// <summary>The names on the ViewCube come from the central resource file (English only).</summary>
public class ViewCubeLabelsTests
{
    [UiFact]
    public void The_six_faces_are_named_from_the_resources()
    {
        var vm = new MainViewModel(new ManifoldKernel(), new FakeDialogs());

        var labels = vm.ViewCubeLabels;

        Assert.Equal(Texts.Get("ViewCube.Front"), labels.Front);
        Assert.Equal(Texts.Get("ViewCube.Back"), labels.Back);
        Assert.Equal(Texts.Get("ViewCube.Left"), labels.Left);
        Assert.Equal(Texts.Get("ViewCube.Right"), labels.Right);
        Assert.Equal(Texts.Get("ViewCube.Top"), labels.Top);
        Assert.Equal(Texts.Get("ViewCube.Bottom"), labels.Bottom);
        Assert.Equal(["Front", "Back", "Left", "Right", "Top", "Bottom"],
            new[] { labels.Front, labels.Back, labels.Left, labels.Right, labels.Top, labels.Bottom });
    }
}
