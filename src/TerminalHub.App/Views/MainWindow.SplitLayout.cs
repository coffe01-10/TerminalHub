using Avalonia.Controls;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.Views;

public partial class MainWindow
{
    private void UpdateSplitLayout()
    {
        if (!Vm.IsSplit) return;
        var quad = Vm.SplitLayout == SplitLayout.Quad;
        var vertical = Vm.SplitLayout == SplitLayout.Vertical;
        var maximized = Vm.PaneMaximized;
        var columns = quad || !vertical;
        SplitGrid.ColumnDefinitions[0].Width = maximized || !columns ? new GridLength(1, GridUnitType.Star) : new GridLength(Vm.ColumnRatio, GridUnitType.Star);
        SplitGrid.ColumnDefinitions[1].Width = new GridLength(!maximized && columns ? 5 : 0);
        SplitGrid.ColumnDefinitions[2].Width = !maximized && columns ? new GridLength(1 - Vm.ColumnRatio, GridUnitType.Star) : new GridLength(0);
        SplitGrid.RowDefinitions[0].Height = maximized || !(quad || vertical) ? new GridLength(1, GridUnitType.Star) : new GridLength(Vm.RowRatio, GridUnitType.Star);
        SplitGrid.RowDefinitions[1].Height = new GridLength(!maximized && (quad || vertical) ? 5 : 0);
        SplitGrid.RowDefinitions[2].Height = !maximized && (quad || vertical) ? new GridLength(1 - Vm.RowRatio, GridUnitType.Star) : new GridLength(0);
        ColumnDivider.IsVisible = !maximized && columns;
        RowDivider.IsVisible = !maximized && (quad || vertical);
        var boxes = new[] { LeftPaneBox, RightPaneBox, BottomLeftPaneBox, BottomRightPaneBox };
        for (var i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];
            box.IsVisible = i < Vm.PaneCount && (!maximized || i == Vm.FocusedPane);
            Grid.SetColumn(box, maximized || vertical ? 0 : (i % 2) * 2);
            Grid.SetRow(box, maximized ? 0 : vertical ? i * 2 : (i / 2) * 2);
        }
    }

    private void SavePaneRatios()
    {
        if (!Vm.IsSplit || Vm.PaneMaximized) return;
        var cols = SplitGrid.ColumnDefinitions;
        var rows = SplitGrid.RowDefinitions;
        if (ColumnDivider.IsVisible && cols[0].ActualWidth + cols[2].ActualWidth > 0)
            Vm.ColumnRatio = Math.Clamp(cols[0].ActualWidth / (cols[0].ActualWidth + cols[2].ActualWidth), .15, .85);
        if (RowDivider.IsVisible && rows[0].ActualHeight + rows[2].ActualHeight > 0)
            Vm.RowRatio = Math.Clamp(rows[0].ActualHeight / (rows[0].ActualHeight + rows[2].ActualHeight), .15, .85);
    }
}
