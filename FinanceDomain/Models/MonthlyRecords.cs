namespace FinanceDomain.Models
{
    public class MonthlyRecords
    {
        public DateTime YearMonth { get; set; }
        public required Dictionary<string, decimal> Categories { get; set; }
        public decimal Total { get; set; }
    }
}