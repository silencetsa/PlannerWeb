namespace Planer.Core;

public sealed partial class PlannerEngine
{
    public const string BudgetExceededMessage = "не хватает бюджета, уменьши сумму";
    public const int MaximumBudgetCategories = 7;

    public BudgetSummary Budget => new(State.BudgetIncome,
        State.Payments.Where(p => p.IsActive(Today)).Select(p => p.RevisionOn(Today)!)
            .Where(r => r.Repeat == Repeat.Monthly).Sum(r => r.Amount),
        State.BudgetCategories.Sum(c => c.Limit));

    // A lower income can reveal an overallocated plan; it never silently changes existing limits.
    public void SetBudgetIncome(decimal income) => Commit(() => { BudgetMoney.Validate(income); State.BudgetIncome = income; });

    public BudgetCategory SaveBudgetCategory(Guid? id, string name, decimal limit)
    {
        BudgetCategory? result = null;
        Commit(() =>
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
                throw new ArgumentException("Название — от 1 до 120 знаков");
            BudgetMoney.Validate(limit);
            var current = id is { } key ? State.BudgetCategories.FirstOrDefault(c => c.Id == key)
                ?? throw new ArgumentException("Категория больше не существует") : null;
            if (current is null && State.BudgetCategories.Count >= MaximumBudgetCategories)
                throw new ArgumentException("Не более 7 категорий");
            if (limit > Budget.Income - Budget.MandatoryPayments - State.BudgetCategories.Where(c => c.Id != id).Sum(c => c.Limit))
                throw new ArgumentException(BudgetExceededMessage);
            if (current is null)
            {
                current = new BudgetCategory
                {
                    Color = Palette.Elements.Take(MaximumBudgetCategories).First(color => !State.BudgetCategories.Any(c => c.Color == color)),
                    Order = State.BudgetCategories.Count == 0 ? 0 : State.BudgetCategories.Max(c => c.Order) + 1
                };
                State.BudgetCategories.Add(current);
            }
            current.Name = name.Trim(); current.Limit = limit; result = current;
        });
        return result!;
    }

    public void DeleteBudgetCategory(Guid id) => Commit(() => State.BudgetCategories.RemoveAll(c => c.Id == id));
}
