using System.Xml.Linq;

namespace SpinePet.Tests;

public sealed class CharacterLibraryLayoutTests
{
    [Fact]
    public void CharacterLibraryUsesExpandedPixelScrollingViewport()
    {
        string xamlPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SpinePet",
            "Views",
            "MainWindow.xaml");
        XDocument document = XDocument.Load(xamlPath);
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement characterCards = Assert.Single(
            document.Descendants(presentation + "ListBox"),
            element =>
                (string?)element.Attribute(x + "Name") ==
                "CharacterCards");

        Assert.Equal("216", (string?)characterCards.Attribute("MinHeight"));
        Assert.Equal("424", (string?)characterCards.Attribute("MaxHeight"));
        Assert.Equal(
            "False",
            (string?)characterCards.Attribute(
                "ScrollViewer.IsDeferredScrollingEnabled"));
        Assert.Equal(
            "Pixel",
            (string?)characterCards.Attribute("VirtualizingPanel.ScrollUnit"));
        Assert.Equal(
            "True",
            (string?)characterCards.Attribute(
                "VirtualizingPanel.IsVirtualizing"));
        Assert.Equal(
            "Recycling",
            (string?)characterCards.Attribute(
                "VirtualizingPanel.VirtualizationMode"));
        Assert.Equal(
                "Page",
                (string?)characterCards.Attribute(
                    "VirtualizingPanel.CacheLengthUnit"));
    }

    [Fact]
    public void CharacterLibraryExposesGlobalFolderAndDirectSkinActions()
    {
        XDocument document = LoadMainWindowXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Contains(
            document.Descendants(presentation + "Button"),
            element =>
                (string?)element.Attribute("Click") ==
                "OnOpenResourceFolder");
        Assert.Contains(
            document.Descendants(presentation + "Button"),
            element =>
                (string?)element.Attribute("Click") ==
                "OnDeleteSelectedSkin");

        XElement skinMenu = Assert.Single(
            document.Descendants(presentation + "ContextMenu"),
            element =>
                (string?)element.Attribute("ItemsSource") ==
                "{Binding AvailableSkins}");
        Assert.Equal(
            "{Binding AvailableSkins}",
            (string?)skinMenu.Attribute("ItemsSource"));
        Assert.Contains(
            skinMenu.Descendants(presentation + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == "CommandTarget" &&
                ((string?)setter.Attribute("Value"))?.Contains(
                    "RootWindow",
                    StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            document.Descendants(presentation + "MenuItem"),
            element => (string?)element.Attribute("Header") == "S_tate");
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attributes().Any(attribute =>
                attribute.Value.Contains(
                    "SwitchResourceCommand",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void CharacterSearchDescribesOnlySupportedNameAndSkinFields()
    {
        XDocument document = LoadMainWindowXaml();

        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attributes().Any(attribute =>
                attribute.Value.Contains(
                    "resource type",
                    StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(
            document.Descendants(),
            element => element.Attributes().Any(attribute =>
                attribute.Value.Contains(
                    "Name or skin",
                    StringComparison.OrdinalIgnoreCase)));
    }

    private static XDocument LoadMainWindowXaml()
    {
        string xamlPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SpinePet",
            "Views",
            "MainWindow.xaml");
        return XDocument.Load(xamlPath);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(
                    Path.Combine(directory.FullName, "SpinePet.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate SpinePet.sln.");
    }
}
