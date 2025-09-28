namespace CrossCutting.Helpers
{
    public static class CategorizationHelper
    {

        private static readonly (string Category, string[] Keywords)[] Categories =
        {
            ("Groceries", new[] { "METRO", "GROCERY", "SUPERMARKET", "DYLAN'S NF", "LOBLAWS", "GIANT TIGER", "FARM BOY", "FRESHCO", "WAL-MART", "SOBEYS", "HALENDA'S MEATS" }),
            ("Dining", new[] { "BURRITO", "RESTAURANT", "PIZZA", "CAFE", "SUBWAY", "TIM HORTONS", "MCDONALD", "STARBUCKS", "HALIBUT HOUSE", "DIVINO CHURRASCO", "SUSHI",
                        "SHANGHAI 360", "FIVE GUYS", "A&W", "ARBYS", "RIO 40 DEGREES", "FIREHOUSE", "SAMBA BRAZIL EATERY", "POULET ROUGE", "MONTANAS COOKHOUSE", "PUB",
                        "THE PLAYERS BENCH SPORTS" }),
            ("Utilities", new[] { "WYSE METER", "BELL", "FIDO", "COINAMATIC" }),
            ("Rent", new[] { "VALIANT" }),
            ("Pet", new[] { "PETSMART", "SIMCOE ST. N ANIMAL HOSPI OSHAWA", "PET VALU", "DEBIT TRUPANION" }),
            ("Transportation", new[] { "UBER", "LYFT", "TAXI", "PRESTO" }),
            ("Shopping", new[] { "AMAZON", "WALMART", "SHOPPERS", "CANADIAN TIRE", "DOLLARAMA", "COSTCO", "NEWEGG", "BEST BUY", "GAMESTOP",
            "WINNERS", "HOT TOPIC", "VALUE VILLAGE", "NOW AND THEN", "JYSK", "SHOE COMPANY", "SPENCER GIFTS" }),
            ("Entertainment", new[] { "STEAMGAMES", "TSO TORONTO", "HYTTOOFO LOVENSE" }),
            ("Health", new[] { "DENTISTRY ON RO", "LIFELABS", "LA FITNESS" }),
            ("Subscriptions", new[] { "NETFLIX", "SPOTIFY", "DISNEY", "SUBSCRIPTION", "GOOGLE ONE" }),
        };

        public static string Categorize(string desc)
        {
            desc = desc.ToUpper();

            foreach (var (category, keywords) in Categories)
            {
                if (keywords.Any(k => desc.Contains(k)))
                    return category;
            }

            return "Other";
        }
    }

}
