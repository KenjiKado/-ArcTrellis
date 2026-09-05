using System.Windows;
using System.Windows.Controls.Primitives;

namespace ArcTrellis.App;

public sealed class LeftAlignedMenuPopup : Popup
{
    public LeftAlignedMenuPopup()
    {
        Placement = PlacementMode.Custom;
        CustomPopupPlacementCallback = PlaceBelowLeft;
    }

    private static CustomPopupPlacement[] PlaceBelowLeft(Size popupSize, Size targetSize, Point offset) =>
    [
        new CustomPopupPlacement(new Point(0, targetSize.Height), PopupPrimaryAxis.Horizontal),
        new CustomPopupPlacement(new Point(0, -popupSize.Height), PopupPrimaryAxis.Horizontal)
    ];
}
