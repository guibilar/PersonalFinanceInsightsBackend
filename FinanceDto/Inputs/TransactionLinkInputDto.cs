namespace FinanceDto.Inputs;

public class TransactionLinkInputDto
{
    public Guid TransactionId { get; set; }
    public Guid BudgetId { get; set; }
}