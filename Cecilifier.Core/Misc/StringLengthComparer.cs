using System.Collections.Generic;

namespace Cecilifier.Core.Misc;

public class StringLengthComparer : IComparer<string>
{
    public static IComparer<string> Instance = new StringLengthComparer();
    
    public int Compare(string x, string y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        return x.Length - y.Length;
    }
}
