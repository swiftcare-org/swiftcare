namespace AuthService.Diagnostics;

// Temporary code for the SWC-149 quality gate failure test. Never merge this file.
public static class QualityGateProbe
{
    public static int ScoreFirst(int[] values)
    {
        var total = 0;
        foreach (var value in values)
        {
            if (value % 2 == 0)
            {
                total += value;
            }

            if (value > 100)
            {
                total -= 10;
            }
            else if (value > 50)
            {
                total -= 5;
            }
            else if (value > 25)
            {
                total += 2;
            }
            else
            {
                total += 1;
            }
        }

        return total;
    }

    public static int ScoreSecond(int[] values)
    {
        var total = 0;
        foreach (var value in values)
        {
            if (value % 2 == 0)
            {
                total += value;
            }

            if (value > 100)
            {
                total -= 10;
            }
            else if (value > 50)
            {
                total -= 5;
            }
            else if (value > 25)
            {
                total += 2;
            }
            else
            {
                total += 1;
            }
        }

        return total;
    }
}
