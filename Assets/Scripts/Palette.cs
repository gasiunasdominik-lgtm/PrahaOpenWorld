using UnityEngine;

// ART BIBLE — "Stověžatá"
// Style:   Flat-shaded low-poly Prague at golden hour: chunky facades, terracotta roofs,
//          verdigris copper spires, dark gothic stone landmarks.
// Palette: the colors below — every color in the project reads from here.
// Mood:    low late-afternoon sun from the west-southwest, warm on facades;
//          cool slate-blue shadow bounce; faint amber haze over the river.
public static class Palette
{
    static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

    // Ground & streets
    public static readonly Color Cobble      = H("#7A6C60"); // pavements, squares
    public static readonly Color CobbleDark  = H("#5C5049"); // bridge deck, grout
    public static readonly Color Asphalt     = H("#3E3D44"); // roads
    public static readonly Color RoadLine    = H("#E9DFC8"); // road markings
    public static readonly Color Grass       = H("#6F8F4E"); // parks
    public static readonly Color Foliage     = H("#4F7A45"); // tree canopies
    public static readonly Color Trunk       = H("#5A4232");

    // Facades
    public static readonly Color Sandstone   = H("#E6D3B0");
    public static readonly Color Ochre       = H("#D9A86C");
    public static readonly Color DustyPink   = H("#D29A86");
    public static readonly Color PaleSage    = H("#B9C4A8");
    public static readonly Color Butter      = H("#EBCB7E");
    public static readonly Color Cornice     = H("#F3E8D3"); // trims, cornices
    public static readonly Color Plinth      = H("#8C7D6E"); // ground-floor bands

    // Roofs & landmarks
    public static readonly Color Terracotta  = H("#B5523B");
    public static readonly Color Copper      = H("#5E9C8A"); // verdigris domes & spires
    public static readonly Color GothicStone = H("#4A4744"); // sooty sandstone of bridge, Týn, towers
    public static readonly Color Slate       = H("#3A3E4A"); // dark spire roofs
    public static readonly Color Concrete    = H("#C9CDD1"); // Žižkov tower
    public static readonly Color Gold        = H("#D9A441"); // clock, ornaments

    // Water & sky
    public static readonly Color River       = H("#3E7C8C");
    public static readonly Color SkyTop      = H("#7FA8D6");
    public static readonly Color Horizon     = H("#F2C594");
    public static readonly Color GroundBounce= H("#5A4A3C");
    public static readonly Color Fog         = H("#E8C9A4");

    // Characters & vehicles
    public static readonly Color Jacket      = H("#2F4A6B"); // character primary
    public static readonly Color Skin        = H("#E8C6A0"); // character secondary
    public static readonly Color Jeans       = H("#3B4252");
    public static readonly Color Hair        = H("#4A3426");
    public static readonly Color Glass       = H("#2B3445");
    public static readonly Color Tire        = H("#24242A");
    public static readonly Color Chrome      = H("#B8BCC2");
    public static readonly Color HeadLight   = H("#FFF2DC");
    public static readonly Color TailLight   = H("#E8402F");

    // Light
    public static readonly Color SunKey      = H("#FFE2B8");
    public static readonly Color Shadow      = H("#3D4660"); // cool slate shadow / UI dark
    public static readonly Color UIDark      = H("#262B3A");
    public static readonly Color UILight     = H("#F3E8D3");

    // Accent — Prague tram red: UI highlights, scarf, hero car
    public static readonly Color Accent      = H("#E8402F");

    public static readonly Color[] Facades = { Sandstone, Ochre, DustyPink, PaleSage, Butter };
    public static readonly Color[] CarPaints = { Accent, Sandstone, Copper, H("#2E5E9E"), H("#2A2A30"), Butter };

    public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
}
