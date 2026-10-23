using System.Globalization;

namespace Planer.Core;

public sealed class BudgetCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public decimal Limit { get; set; }
    public string Color { get; set; } = Palette.Elements[0];
    public long Order { get; set; }
}

public sealed record BudgetSummary(decimal Income, decimal MandatoryPayments, decimal CategoryLimits)
{
    public decimal Total => MandatoryPayments + CategoryLimits;
    public decimal Remaining => Income - Total;
    public decimal Percent(decimal amount) => Income > 0 ? amount / Income * 100m : 0m;
}

/// <summary>Money input accepts a decimal comma or point, without grouping or rounding away cents.</summary>
public static class BudgetMoney
{
    public const decimal Maximum = 999_999_999m;
    public static bool IsInput(string text)
    {
        int separator = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is ',' or '.') { if (separator >= 0 || i == 0) return false; separator = i; }
            else if (c is < '0' or > '9') return false;
        }
        return separator < 0 || text.Length - separator - 1 <= 2;
    }
    public static bool TryParse(string text, out decimal amount)
    {
        amount = 0;
        return text.Length > 0 && IsInput(text)
            && decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)
            && amount <= Maximum;
    }
    public static void Validate(decimal amount)
    {
        if (amount < 0 || amount > Maximum || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("Введите сумму от 0 до 999 999 999, не более двух знаков после запятой");
    }
}
