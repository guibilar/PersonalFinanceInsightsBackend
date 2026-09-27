

using Finance.Domain.Enums;

namespace Finance.Infrastructure.Import{
    public static class CategorizationHelper
    {
        private static readonly (Category Category, string[] Keywords)[] Categories =
        [
            (Category.Groceries, [
                "METRO", "GROCERY", "SUPERMARKET", "DYLAN'S NF", "LOBLAWS", "GIANT TIGER", "FARM BOY", "FRESHCO",
                "WAL-MART", "SOBEYS", "HALENDA'S MEATS"
            ]),
            (Category.Dining, [
                "BURRITO", "RESTAURANT", "PIZZA", "CAFE", "SUBWAY", "TIM HORTONS", "MCDONALD", "STARBUCKS",
                "HALIBUT HOUSE", "DIVINO CHURRASCO", "SUSHI",
                "SHANGHAI 360", "FIVE GUYS", "A&W", "ARBYS", "RIO 40 DEGREES", "FIREHOUSE", "SAMBA BRAZIL EATERY",
                "POULET ROUGE", "MONTANAS COOKHOUSE", "PUB",
                "THE PLAYERS BENCH SPORTS"
            ]),
            (Category.Utilities, ["WYSE METER", "BELL", "FIDO", "COINAMATIC"]),
            (Category.Rent, ["VALIANT"]),
            (Category.Pet, ["PETSMART", "SIMCOE ST. N ANIMAL HOSPI OSHAWA", "PET VALU", "DEBIT TRUPANION"]),
            (Category.Transportation, ["UBER", "LYFT", "TAXI", "PRESTO"]),
            (Category.Shopping, [
                "AMAZON", "WALMART", "SHOPPERS", "CANADIAN TIRE", "DOLLARAMA", "COSTCO", "NEWEGG", "BEST BUY",
                "GAMESTOP",
                "WINNERS", "HOT TOPIC", "VALUE VILLAGE", "NOW AND THEN", "JYSK", "SHOE COMPANY", "SPENCER GIFTS"
            ]),
            (Category.Entertainment, ["STEAMGAMES", "TSO TORONTO", "HYTTOOFO LOVENSE"]),
            (Category.Health, ["DENTISTRY ON RO", "LIFELABS", "LA FITNESS"]),
            (Category.Subscriptions, ["NETFLIX", "SPOTIFY", "DISNEY", "SUBSCRIPTION", "GOOGLE ONE"])
        ];

        public static Category Categorize(string desc)
        {
            desc = desc.ToUpper();

            foreach (var (category, keywords) in Categories)
            {
                if (keywords.Any(k => desc.Contains(k)))
                    return category;
            }

            return Category.Other;
        }
    }
}