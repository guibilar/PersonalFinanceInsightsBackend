

namespace Finance.Application.Reports{
    public class MonthlyRecords
    {
        public DateTime YearMonth { get; set; }
        /// <summary>By category id.</summary>
        public required Dictionary<Guid, decimal> Categories { get; set; }
        public decimal Total { get; set; }
    }
}