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
        // 双列海报式网格：列表填满左列剩余高度，由自身滚动，不再设 MaxHeight。
        Assert.Null((string?)characterCards.Attribute("MaxHeight"));
        Assert.Equal(
            "False",
            (string?)characterCards.Attribute(
                "ScrollViewer.IsDeferredScrollingEnabled"));
        // 非虚拟化面板用像素滚动，滚动平滑。
        Assert.Equal(
            "False",
            (string?)characterCards.Attribute("ScrollViewer.CanContentScroll"));
        XElement itemsPanel = Assert.Single(
            characterCards.Descendants(presentation + "UniformGrid"));
        Assert.Equal("2", (string?)itemsPanel.Attribute("Columns"));
    }

    [Fact]
    public void CharacterLibraryExposesGlobalFolderAndDirectSkinActions()
    {
        XDocument document = LoadMainWindowXaml();
        XDocument styles = LoadConfigPanelStyles();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        // 样式抽离到独立字典，由主窗口合并。
        Assert.Contains(
            document.Descendants(presentation + "ResourceDictionary"),
            element =>
                ((string?)element.Attribute("Source"))?.Contains(
                    "ConfigPanelStyles.xaml",
                    StringComparison.Ordinal) == true);

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
            styles.Descendants(presentation + "ContextMenu"),
            element =>
                (string?)element.Attribute("ItemsSource") ==
                "{Binding AvailableSkins}");
        Assert.Equal(
            "{Binding AvailableSkins}",
            (string?)skinMenu.Attribute("ItemsSource"));
        // 命令经 ContextMenu 的 PlacementTarget（卡片项）路由并冒泡到窗口。
        Assert.Contains(
            skinMenu.Descendants(presentation + "Setter"),
            setter =>
                (string?)setter.Attribute("Property") == "CommandTarget" &&
                ((string?)setter.Attribute("Value"))?.Contains(
                    "PlacementTarget",
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

    private static XDocument LoadConfigPanelStyles()
    {
        string xamlPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SpinePet",
            "Themes",
            "ConfigPanelStyles.xaml");
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
