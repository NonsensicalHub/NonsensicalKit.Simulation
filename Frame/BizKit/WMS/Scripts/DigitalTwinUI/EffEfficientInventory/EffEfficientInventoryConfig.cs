using UnityEngine;

namespace Frame.BizKit.WMS
{
public class EffEfficientInventoryConfig
{
    public string ID;
    public string Index;
    public string NearDate;
    public string ThresholdDate;
    public string ColorString;

    public Color ToColor()
    {
        return ToColor(ColorString);
    }

    public Color ToColor(string color)
    {
        if (string.IsNullOrEmpty(color))
            return Color.cyan;

        if (color[0] != '#')
            color = "#" + color;

        return ColorUtility.TryParseHtmlString(color, out var c) ? c : Color.cyan;
    }

    public void SaveColor(Color color)
    {
        ColorString = ColorUtility.ToHtmlStringRGBA(color);
    }
}
}
