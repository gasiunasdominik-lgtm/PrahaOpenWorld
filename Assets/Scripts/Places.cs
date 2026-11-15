using UnityEngine;

// Atom "Místo": a real place (pub, café, shop, office…) from OSM, with what it is and when it is open.
public static class Places
{
    // shortcut: hours by kind, not parsed from OSM opening_hours; parse the tag once hours drive gameplay
    public static bool IsOpen(string kind, float h) => kind switch
    {
        "pub" or "bar" or "biergarten" => h >= 16f || h < 2f,
        "restaurant" => h >= 11f && h < 23f,
        "cafe" => h >= 8f && h < 22f,
        "fast_food" => h >= 9f || h < 1f,
        "shop" => h >= 8f && h < 19f,
        "office" => h >= 8f && h < 17f,
        _ => h >= 9f && h < 18f,
    };

    // What people out on the street are doing at this hour (atom "Rozvrh").
    public static string Purpose(float h) =>
        h < 5f ? "domů" : h < 10f ? "do práce" : h < 14f ? "na oběd" : h < 17f ? "nakoupit" : h < 22.5f ? "za zábavou" : "domů";

    static bool Fits(string purpose, string kind) => purpose switch
    {
        "do práce" => kind is "office" or "shop" or "cafe",
        "na oběd" => kind is "restaurant" or "fast_food" or "cafe",
        "nakoupit" => kind is "shop",
        "za zábavou" => kind is "pub" or "bar" or "restaurant" or "biergarten",
        _ => false,
    };

    public static MapBuilder.Poly PickDestination(Vector3 near, string purpose, float hour, System.Random rng, float radius = 600f)
    {
        MapBuilder.Poly pick = null; int seen = 0; float r2 = radius * radius;
        foreach (var p in MapBuilder.D.places)
        {
            float dx = p.p[0] - near.x, dz = p.p[1] - near.z;
            if (dx * dx + dz * dz > r2 || !Fits(purpose, p.k) || !IsOpen(p.k, hour)) continue;
            if (rng.Next(++seen) == 0) pick = p; // reservoir sampling: uniform among the matches
        }
        return pick;
    }

    public static MapBuilder.Poly PickOpenNear(Vector3 near, float hour, System.Random rng, float radius)
    {
        MapBuilder.Poly pick = null; int seen = 0; float r2 = radius * radius;
        foreach (var p in MapBuilder.D.places)
        {
            float dx = p.p[0] - near.x, dz = p.p[1] - near.z;
            if (dx * dx + dz * dz > r2 || !IsOpen(p.k, hour)) continue;
            if (rng.Next(++seen) == 0) pick = p;
        }
        return pick;
    }

    public static int OpenNear(Vector3 near, float hour, float radius = 150f)
    {
        int n = 0; float r2 = radius * radius;
        foreach (var p in MapBuilder.D.places)
        {
            float dx = p.p[0] - near.x, dz = p.p[1] - near.z;
            if (dx * dx + dz * dz <= r2 && IsOpen(p.k, hour)) n++;
        }
        return n;
    }
}
