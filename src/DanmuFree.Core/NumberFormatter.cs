namespace DanmuFree.Core;

public static class NumberFormatter
{
    public static string Format(long n)
    {
        if (n < 10000) return n.ToString();
        if (n < 100_000_000) return Trim(n / 10000.0) + "万";
        return Trim(n / 100_000_000.0) + "亿";
    }

    private static string Trim(double v)
    {
        var r = Math.Round(v, 1);
        return r == (int)r ? ((int)r).ToString() : r.ToString();
    }
}
