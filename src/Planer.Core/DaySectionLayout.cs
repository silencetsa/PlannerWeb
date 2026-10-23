namespace Planer.Core;

/// <summary>Distributes a fixed card height, borrowing only from empty day sections.</summary>
public static class DaySectionLayout
{
    public static double[] Allocate(double height, IReadOnlyList<double> desired, IReadOnlyList<bool> occupied)
    {
        if (desired.Count != 3 || occupied.Count != 3) throw new ArgumentException("A day has three sections");
        height = Math.Max(0, height);
        double baseline = height / 3;
        double[] result = [baseline, baseline, baseline];
        double[] need = new double[3], spare = new double[3];
        for (int i = 0; i < 3; i++)
        {
            if (occupied[i]) need[i] = Math.Max(0, desired[i] - baseline);
            else spare[i] = Math.Max(0, baseline - desired[i]);
        }
        double totalNeed = need.Sum(), totalSpare = spare.Sum();
        if (totalNeed <= 0 || totalSpare <= 0) return result;
        double transferred = Math.Min(totalNeed, totalSpare);
        for (int i = 0; i < 3; i++) result[i] += transferred * need[i] / totalNeed - transferred * spare[i] / totalSpare;
        return result;
    }
}
