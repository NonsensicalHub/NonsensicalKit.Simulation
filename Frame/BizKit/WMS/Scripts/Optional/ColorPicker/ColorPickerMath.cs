using UnityEngine;

namespace Frame.BizKit.WMS
{
/// <summary>HSV / Hex 换算与取色贴图（可复用 Texture / 像素缓冲，避免拖动时 GC）。</summary>
public static class ColorPickerMath
{
    private static Color32[] s_svPixels;
    private static Color32[] s_alphaPixels;

    public static void ColorToHsv(Color color, out float h, out float s, out float v) =>
        Color.RGBToHSV(color, out h, out s, out v);

    public static Color HsvToColor(float h, float s, float v, float a = 1f)
    {
        var c = Color.HSVToRGB(Mathf.Repeat(h, 1f), Mathf.Clamp01(s), Mathf.Clamp01(v));
        c.a = Mathf.Clamp01(a);
        return c;
    }

    public static string ToHex(Color color, bool includeAlpha = true) =>
        includeAlpha ? ColorUtility.ToHtmlStringRGBA(color) : ColorUtility.ToHtmlStringRGB(color);

    public static bool TryParseHex(string hex, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.Trim();
        if (hex[0] != '#') hex = "#" + hex;
        return ColorUtility.TryParseHtmlString(hex, out color);
    }

    public static Texture2D CreateSvTexture(float hue, int size = 128)
    {
        size = Mathf.Clamp(size, 32, 256);
        var tex = NewTex(size, size);
        FillSvTexture(tex, hue);
        return tex;
    }

    public static void FillSvTexture(Texture2D tex, float hue)
    {
        int w = tex.width;
        int h = tex.height;
        int len = w * h;
        var pixels = EnsureBuffer(ref s_svPixels, len);
        float denomX = Mathf.Max(w - 1, 1);
        float denomY = Mathf.Max(h - 1, 1);
        hue = Mathf.Repeat(hue, 1f);

        for (int y = 0; y < h; y++)
        {
            float v = y / denomY;
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                // 已知 s/v 已在 [0,1]，跳过额外 Clamp
                Color c = Color.HSVToRGB(hue, x / denomX, v);
                pixels[row + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
    }

    public static Texture2D CreateHueTexture(int width, int height)
    {
        width = Mathf.Max(4, width);
        height = Mathf.Clamp(height, 32, 512);
        var tex = NewTex(width, height);
        var pixels = new Color32[width * height];
        float denom = Mathf.Max(height - 1, 1);
        for (int y = 0; y < height; y++)
        {
            Color32 c = Color.HSVToRGB(1f - y / denom, 1f, 1f);
            int row = y * width;
            for (int x = 0; x < width; x++)
                pixels[row + x] = c;
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }

    public static Texture2D CreateAlphaGradientTexture(Color rgb, int width = 128, int height = 1)
    {
        width = Mathf.Clamp(width, 32, 256);
        height = Mathf.Max(1, height);
        var tex = NewTex(width, height);
        FillAlphaGradientTexture(tex, rgb);
        return tex;
    }

    public static void FillAlphaGradientTexture(Texture2D tex, Color rgb)
    {
        int w = tex.width;
        int h = tex.height;
        int len = w * h;
        var pixels = EnsureBuffer(ref s_alphaPixels, len);
        float denom = Mathf.Max(w - 1, 1);
        byte r = (byte)Mathf.RoundToInt(rgb.r * 255f);
        byte g = (byte)Mathf.RoundToInt(rgb.g * 255f);
        byte b = (byte)Mathf.RoundToInt(rgb.b * 255f);

        for (int x = 0; x < w; x++)
        {
            var c = new Color32(r, g, b, (byte)Mathf.RoundToInt(x / denom * 255f));
            if (h == 1)
            {
                pixels[x] = c;
                continue;
            }

            for (int y = 0; y < h; y++)
                pixels[y * w + x] = c;
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
    }

    public static Texture2D CreateCheckerTexture(int size = 16, int cell = 4)
    {
        var tex = NewTex(size, size);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Point;
        Color32 a = new Color32(52, 58, 70, 255);
        Color32 b = new Color32(36, 40, 50, 255);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            pixels[y * size + x] = ((x / cell) + (y / cell)) % 2 == 0 ? a : b;
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private static Color32[] EnsureBuffer(ref Color32[] buffer, int length)
    {
        if (buffer == null || buffer.Length < length)
            buffer = new Color32[length];
        return buffer;
    }

    private static Texture2D NewTex(int w, int h) =>
        new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
}
}
