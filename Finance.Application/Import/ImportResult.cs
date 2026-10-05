namespace Finance.Application.Import;

/// <param name="Balance">The account's value the statement gave, now recorded as its balance that day.</param>
public record ImportResult(int Read, int Added, int Skipped, StatementBalance? Balance = null);
