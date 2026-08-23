using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace SpinePet.Views;

internal sealed class VirtualizingUniformGrid :
    VirtualizingPanel,
    IScrollInfo
{
    private const double DefaultItemHeight = 128;
    private const int BufferRows = 1;
    private Size _extent;
    private Size _viewport;
    private Point _offset;

    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(
            nameof(Columns),
            typeof(int),
            typeof(VirtualizingUniformGrid),
            new FrameworkPropertyMetadata(
                2,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                null,
                CoercePositiveInt));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(
            nameof(ItemHeight),
            typeof(double),
            typeof(VirtualizingUniformGrid),
            new FrameworkPropertyMetadata(
                DefaultItemHeight,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                null,
                CoercePositiveDouble));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; } = true;

    public double ExtentWidth => _extent.Width;

    public double ExtentHeight => _extent.Height;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => _offset.X;

    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        ItemsControl owner = ItemsControl.GetItemsOwner(this);
        int itemCount = owner?.Items.Count ?? 0;
        double viewportWidth = double.IsInfinity(availableSize.Width)
            ? 0
            : Math.Max(0, availableSize.Width);
        double viewportHeight = double.IsInfinity(availableSize.Height)
            ? ItemHeight
            : Math.Max(0, availableSize.Height);
        int rowCount = (int)Math.Ceiling(itemCount / (double)Columns);
        UpdateScrollInfo(
            new Size(viewportWidth, rowCount * ItemHeight),
            new Size(viewportWidth, viewportHeight));

        if (itemCount == 0 || viewportWidth <= 0)
        {
            CleanupItems(0, -1);
            return availableSize;
        }

        VirtualizedItemRange range =
            VirtualizingUniformGridLayout.CalculateRange(
                itemCount,
                Columns,
                ItemHeight,
                VerticalOffset,
                ViewportHeight,
                BufferRows);

        CleanupItems(range.FirstIndex, range.LastIndex);
        RealizeItems(
            range.FirstIndex,
            range.LastIndex,
            new Size(viewportWidth / Columns, ItemHeight));
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double itemWidth = Columns == 0
            ? finalSize.Width
            : finalSize.Width / Columns;
        ItemContainerGenerator generator =
            ItemsControl.GetItemsOwner(this).ItemContainerGenerator;
        foreach (UIElement child in InternalChildren)
        {
            int index = generator.IndexFromContainer(child);
            if (index < 0)
            {
                continue;
            }

            int row = index / Columns;
            int column = index % Columns;
            child.Arrange(new Rect(
                column * itemWidth,
                (row * ItemHeight) - VerticalOffset,
                itemWidth,
                ItemHeight));
        }

        return finalSize;
    }

    protected override void BringIndexIntoView(int index)
    {
        if (index < 0)
        {
            return;
        }

        double itemTop = (index / Columns) * ItemHeight;
        double itemBottom = itemTop + ItemHeight;
        if (itemTop < VerticalOffset)
        {
            SetVerticalOffset(itemTop);
        }
        else if (itemBottom > VerticalOffset + ViewportHeight)
        {
            SetVerticalOffset(itemBottom - ViewportHeight);
        }
    }

    public void LineUp() => SetVerticalOffset(VerticalOffset - ItemHeight / 3);

    public void LineDown() =>
        SetVerticalOffset(VerticalOffset + ItemHeight / 3);

    public void LineLeft()
    {
    }

    public void LineRight()
    {
    }

    public void MouseWheelUp() =>
        SetVerticalOffset(VerticalOffset - ItemHeight);

    public void MouseWheelDown() =>
        SetVerticalOffset(VerticalOffset + ItemHeight);

    public void MouseWheelLeft()
    {
    }

    public void MouseWheelRight()
    {
    }

    public void PageUp() =>
        SetVerticalOffset(VerticalOffset - ViewportHeight);

    public void PageDown() =>
        SetVerticalOffset(VerticalOffset + ViewportHeight);

    public void PageLeft()
    {
    }

    public void PageRight()
    {
    }

    public void SetHorizontalOffset(double offset)
    {
    }

    public void SetVerticalOffset(double offset)
    {
        double normalized = Math.Clamp(
            offset,
            0,
            Math.Max(0, ExtentHeight - ViewportHeight));
        if (Math.Abs(normalized - _offset.Y) < 0.01)
        {
            return;
        }

        _offset.Y = normalized;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        UIElement? child = visual as UIElement;
        while (child != null &&
               !ReferenceEquals(VisualTreeHelper.GetParent(child), this))
        {
            child = VisualTreeHelper.GetParent(child) as UIElement;
        }

        int index = child == null
            ? -1
            : ItemsControl.GetItemsOwner(this)
                .ItemContainerGenerator
                .IndexFromContainer(child);
        if (index >= 0)
        {
            BringIndexIntoView(index);
        }

        return rectangle;
    }

    private void RealizeItems(
        int firstIndex,
        int lastIndex,
        Size childSize)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        GeneratorPosition start =
            generator.GeneratorPositionFromIndex(firstIndex);
        int childIndex = start.Offset == 0
            ? start.Index
            : start.Index + 1;

        using IDisposable generation = generator.StartAt(
            start,
            GeneratorDirection.Forward,
            allowStartAtRealizedItem: true);
        for (int itemIndex = firstIndex;
             itemIndex <= lastIndex;
             itemIndex++, childIndex++)
        {
            UIElement child =
                (UIElement)generator.GenerateNext(out bool newlyRealized);
            if (newlyRealized)
            {
                if (childIndex >= InternalChildren.Count)
                {
                    AddInternalChild(child);
                }
                else
                {
                    InsertInternalChild(childIndex, child);
                }

                generator.PrepareItemContainer(child);
            }

            child.Measure(childSize);
        }
    }

    private void CleanupItems(int firstIndex, int lastIndex)
    {
        IItemContainerGenerator generator = ItemContainerGenerator;
        IRecyclingItemContainerGenerator? recycling =
            generator as IRecyclingItemContainerGenerator;
        for (int childIndex = InternalChildren.Count - 1;
             childIndex >= 0;
             childIndex--)
        {
            GeneratorPosition position =
                new(childIndex, 0);
            int itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex >= firstIndex && itemIndex <= lastIndex)
            {
                continue;
            }

            if (recycling != null)
            {
                recycling.Recycle(position, 1);
            }
            else
            {
                generator.Remove(position, 1);
            }

            RemoveInternalChildRange(childIndex, 1);
        }
    }

    private void UpdateScrollInfo(Size extent, Size viewport)
    {
        bool changed = extent != _extent || viewport != _viewport;
        _extent = extent;
        _viewport = viewport;
        double maximumOffset = Math.Max(0, ExtentHeight - ViewportHeight);
        if (_offset.Y > maximumOffset)
        {
            _offset.Y = maximumOffset;
        }

        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "WPF CoerceValueCallback requires an object return value.")]
    private static object CoercePositiveInt(
        DependencyObject dependencyObject,
        object value) =>
        Math.Max(1, (int)value);

    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "WPF CoerceValueCallback requires an object return value.")]
    private static object CoercePositiveDouble(
        DependencyObject dependencyObject,
        object value)
    {
        double result = (double)value;
        return double.IsFinite(result) && result > 0
            ? result
            : DefaultItemHeight;
    }
}

internal readonly record struct VirtualizedItemRange(
    int FirstIndex,
    int LastIndex);

internal static class VirtualizingUniformGridLayout
{
    public static VirtualizedItemRange CalculateRange(
        int itemCount,
        int columns,
        double itemHeight,
        double verticalOffset,
        double viewportHeight,
        int bufferRows)
    {
        if (itemCount <= 0)
            return new VirtualizedItemRange(0, -1);

        int normalizedColumns = Math.Max(1, columns);
        double normalizedHeight = itemHeight > 0 ? itemHeight : 1;
        int rowCount = (int)Math.Ceiling(
            itemCount / (double)normalizedColumns);
        int firstRow = Math.Max(
            0,
            (int)Math.Floor(verticalOffset / normalizedHeight) -
            Math.Max(0, bufferRows));
        int lastRow = Math.Min(
            rowCount - 1,
            (int)Math.Ceiling(
                (verticalOffset + viewportHeight) / normalizedHeight) +
            Math.Max(0, bufferRows));
        return new VirtualizedItemRange(
            firstRow * normalizedColumns,
            Math.Min(
                itemCount - 1,
                ((lastRow + 1) * normalizedColumns) - 1));
    }
}
