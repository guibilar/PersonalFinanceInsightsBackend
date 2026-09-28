using Finance.Application.Abstractions;
using Finance.Application.Models;
using Finance.Application.Services.Interfaces;
using Finance.Domain.Entities;
using Finance.Domain.Enums;
using Finance.Domain.Exceptions;

namespace Finance.Application.Services;

public class CategoryRuleService(
    ICategoryRuleRepository ruleRepository,
    ITransactionRepository transactionRepository) : ICategoryRuleService
{
    private const int PreviewExamples = 5;

    public Task<List<CategoryRule>> GetAllAsync(CancellationToken cancellationToken = default) =>
        ruleRepository.GetAllAsync(cancellationToken);

    public async Task<CategoryRule> AddAsync(string keyword, Category category, int? position,
        CancellationToken cancellationToken = default)
    {
        var rules = await ruleRepository.GetAllAsync(cancellationToken);
        var rule = new CategoryRule { Keyword = CategoryRule.NormalizeKeyword(keyword), Category = category };
        EnsureUnique(rules, rule.Keyword, rule.Id);

        rules.Insert(Math.Min(position ?? rules.Count, rules.Count), rule);
        Renumber(rules);
        await ruleRepository.AddAsync(rule, cancellationToken);
        return rule;
    }

    public async Task<CategoryRule> UpdateAsync(Guid id, string keyword, Category category,
        CancellationToken cancellationToken = default)
    {
        var rules = await ruleRepository.GetAllAsync(cancellationToken);
        var rule = rules.SingleOrDefault(r => r.Id == id) ?? throw new NotFoundException(nameof(CategoryRule), id);
        var normalized = CategoryRule.NormalizeKeyword(keyword);
        EnsureUnique(rules, normalized, id);

        rule.Keyword = normalized;
        rule.Category = category;
        await ruleRepository.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rules = await ruleRepository.GetAllAsync(cancellationToken);
        var rule = rules.SingleOrDefault(r => r.Id == id) ?? throw new NotFoundException(nameof(CategoryRule), id);

        rules.Remove(rule);
        Renumber(rules);
        await ruleRepository.RemoveAsync(rule, cancellationToken);
    }

    public async Task<List<CategoryRule>> MoveAsync(Guid id, int position, CancellationToken cancellationToken = default)
    {
        var rules = await ruleRepository.GetAllAsync(cancellationToken);
        var rule = rules.SingleOrDefault(r => r.Id == id) ?? throw new NotFoundException(nameof(CategoryRule), id);

        rules.Remove(rule);
        rules.Insert(Math.Min(position, rules.Count), rule);
        Renumber(rules);
        await ruleRepository.SaveChangesAsync(cancellationToken);
        return rules;
    }

    public async Task<CategoryRulePreview> PreviewAsync(string keyword, Category category, Guid? excludeTransactionId,
        CancellationToken cancellationToken = default)
    {
        var normalized = CategoryRule.NormalizeKeyword(keyword);
        // As the rule would be checked first, it decides every description containing its keyword.
        var changed = (await transactionRepository.GetRuleCategorizedAsync(cancellationToken))
            .Where(t => t.Id != excludeTransactionId && t.Category != category &&
                        t.Description.ToUpperInvariant().Contains(normalized, StringComparison.Ordinal))
            .ToList();

        var examples = changed.OrderByDescending(t => t.Date)
            .Select(t => t.Description)
            .Distinct()
            .Take(PreviewExamples)
            .ToList();
        return new CategoryRulePreview(changed.Count, examples);
    }

    // The database's unique index is the backstop for concurrent requests; this gives the usual case a clear message.
    private static void EnsureUnique(List<CategoryRule> rules, string keyword, Guid id)
    {
        if (rules.Any(r => r.Keyword == keyword && r.Id != id))
        {
            throw new ConflictException($"A rule for \"{keyword}\" already exists.");
        }
    }

    private static void Renumber(List<CategoryRule> rules)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            rules[i].Position = i;
        }
    }
}
