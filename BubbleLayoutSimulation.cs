using System;
using System.Drawing;
using System.Windows.Forms;

internal static class BubbleLayoutSimulation
{
    private const int BubbleTextWidth = 134;
    private const int GachaTitleWidth = 138;
    private const int GachaResultWidth = 140;

    [STAThread]
    private static int Main()
    {
        int failures = 0;
        string[] normal = { "Core calm", "Orbit merging", "Rhythm 999 key/sec", "Waiting for input", "Total 999.9B", "Planets: 8 / 8" };
        string[] special =
        {
            "CRIT+2 10%#999k", "CRIT+3 3%#999k", "CRIT+4 1.5%#999k", "CRIT+5 .5%#999k",
            "REWIND .01%#999M", "ECHO+1 .25%#999M", "ECHO+3 .1%#999M", "ECHO+10 .05%#999M", "ECHO+20 .02%#999M",
            "DECAY-2 .63%#999M", "DECAY-3 .25%#999M", "DECAY-5 .1%#999M"
        };
        string[] planets = { "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };
        using (Font normalFont = new Font("Segoe UI", 8F, FontStyle.Regular))
        using (Font specialFont = new Font("Segoe UI", 8.5F, FontStyle.Bold))
        using (Font gachaTitle = new Font("Segoe UI", 8.5F, FontStyle.Bold))
        using (Font gachaResult = new Font("Segoe UI", 9F, FontStyle.Bold))
        {
            foreach (string text in normal) failures += Check("normal", text, normalFont, BubbleTextWidth);
            foreach (string text in special) failures += Check("special", text, specialFont, BubbleTextWidth);
            failures += Check("gacha-title", "PLANET GACHA", gachaTitle, GachaTitleWidth);
            foreach (string planet in planets)
            {
                failures += Check("gacha-roll", planet, gachaResult, GachaResultWidth);
                failures += Check("gacha-result", "NEW: " + planet, gachaResult, GachaResultWidth);
            }
        }
        Console.WriteLine(failures == 0 ? "PASS: all simulated bubble strings fit." : "FAIL: " + failures + " bubble strings exceed their safe text width.");
        return failures == 0 ? 0 : 1;
    }

    private static int Check(string group, string text, Font font, int safeWidth)
    {
        int width = TextRenderer.MeasureText(text, font, new Size(Int32.MaxValue, Int32.MaxValue), TextFormatFlags.NoPadding).Width;
        Console.WriteLine(group + " | " + width + "/" + safeWidth + " | " + text);
        return width <= safeWidth ? 0 : 1;
    }
}
