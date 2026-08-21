using System.Xml.Linq;
using SpinePet.Models;

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
        Assert.DoesNotContain(
            characterCards.Attributes(),
            attribute => attribute.Name.LocalName == "ScrollViewer.ScrollChanged");
    }

    [Fact]
    public void ConfigurationPanelUsesExpandedDefaultWidth()
    {
        XDocument document = LoadMainWindowXaml();

        Assert.Equal(
            1020d,
            GlobalConfig.DefaultConfigPanelWidth);
        Assert.Equal(
            GlobalConfig.DefaultConfigPanelWidth.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            (string?)document.Root?.Attribute("Width"));
    }

    [Fact]
    public void CharacterCardsOwnThePositionResetAction()
    {
        XDocument document = LoadMainWindowXaml();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Contains(
            document.Descendants(presentation + "Button"),
            element =>
                (string?)element.Attribute(
                    "AutomationProperties.Name") ==
                "Reset character position");
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attributes().Any(attribute =>
                attribute.Value.Contains(
                    "OnResetSelectedPosition",
                    StringComparison.Ordinal)));

        XElement visibilityButton = Assert.Single(
            document.Descendants(presentation + "Button"),
            element =>
                (string?)element.Attribute(
                    XNamespace.Get(
                        "http://schemas.microsoft.com/winfx/2006/xaml") +
                    "Name") == "VisibilityButton");
        XElement resetButton = Assert.Single(
            document.Descendants(presentation + "Button"),
            element =>
                (string?)element.Attribute(
                    "AutomationProperties.Name") ==
                "Reset character position");
        Assert.Same(visibilityButton.Parent, resetButton.Parent);
    }

    [Fact]
    public void CharacterCardItemsStayTopAlignedAndHaveAMaximumHeight()
    {
        XDocument styles = LoadConfigPanelStyles();
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement cardStyle = Assert.Single(
            styles.Descendants(presentation + "Style"),
            element => (string?)element.Attribute(x + "Key") ==
                "CharacterCardItemStyle");

        Assert.Contains(
            cardStyle.Descendants(presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") ==
                    "VerticalAlignment" &&
                (string?)setter.Attribute("Value") == "Top");
        Assert.Contains(
            cardStyle.Descendants(presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") == "MaxHeight" &&
                (string?)setter.Attribute("Value") == "120");
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
                (string?)element.Attribute("AutomationProperties.Name") ==
                "Open resource folder");
        Assert.Contains(
            document.Descendants(presentation + "Button"),
            element =>
                (string?)element.Attribute("AutomationProperties.Name") ==
                "Delete selected skin");

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
    public void ViewUsesRuntimeEventWiringAndNoPartialWindowFilesRemain()
    {
        XDocument document = LoadMainWindowXaml();
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        Assert.Null(document.Root?.Attribute(x + "Class"));
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName is
                    "Click" or
                    "Loaded" or
                    "Checked" or
                    "Unchecked" or
                    "SelectionChanged" or
                    "PreviewKeyDown" or
                    "ValueChanged" or
                    "MouseLeftButtonDown"));

        string viewsPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SpinePet",
            "Views");
        Assert.False(File.Exists(
            Path.Combine(viewsPath, "MainWindow.CharacterLibrary.cs")));
        Assert.False(File.Exists(
            Path.Combine(viewsPath, "MainWindow.SelectionSettings.cs")));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(
                viewsPath,
                "*.cs",
                SearchOption.TopDirectoryOnly),
            path => File.ReadAllText(path).Contains(
                "partial class MainWindow",
                StringComparison.Ordinal));
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
