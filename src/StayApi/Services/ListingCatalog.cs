namespace StayApi.Services;

/// <summary>Static catalog. Swap for Cosmos DB or SQL; the doors do not change.</summary>
public sealed class ListingCatalog
{
    private static readonly Listing[] Listings =
    [
        new("ams-01", "Amsterdam", "Canal house loft", 2, 185m, "EUR"),
        new("ams-02", "Amsterdam", "Jordaan family apartment", 5, 240m, "EUR"),
        new("ams-03", "Amsterdam", "Houseboat on the Amstel", 4, 210m, "EUR"),
        new("utr-01", "Utrecht", "Dom tower studio", 2, 120m, "EUR"),
        new("utr-02", "Utrecht", "Wharf cellar apartment", 3, 150m, "EUR"),
        new("lis-01", "Lisbon", "Alfama rooftop", 2, 110m, "EUR"),
        new("lis-02", "Lisbon", "Belem riverside flat", 4, 165m, "EUR"),
        new("ber-01", "Berlin", "Kreuzberg Altbau", 3, 130m, "EUR"),
        new("ber-02", "Berlin", "Prenzlauer Berg garden house", 6, 220m, "EUR"),
    ];

    public IEnumerable<Listing> Search(string? city, int guests)
        => Listings.Where(l =>
            (string.IsNullOrWhiteSpace(city) || l.City.Equals(city.Trim(), StringComparison.OrdinalIgnoreCase))
            && l.MaxGuests >= Math.Max(1, guests));

    public Listing? Get(string id) => Listings.FirstOrDefault(l => l.Id == id);
}
