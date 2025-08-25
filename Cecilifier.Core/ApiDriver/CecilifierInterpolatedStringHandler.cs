using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cecilifier.Core.ApiDriver;

/// <summary>
/// Interpolated string handler used to produce correctly indented strings.
///
/// TODO: What else? Why it is needed? How does it work? 
/// </summary>
[InterpolatedStringHandler]
public struct CecilifierInterpolatedStringHandler
{
    int lastIndent = 0;
    public CecilifierInterpolatedStringHandler(int x, int holes)
    {
        _sb = new StringBuilder(x + holes * 16);
    }

    public static int BaseIndentation { get; set; } = 0;

    public string Result => _sb.ToString();
    
    private StringBuilder _sb;
    
    public void AppendLiteral(string value)
    {
        lastIndent = WriteString(value, computeIndent: true);
    }
    
    public void AppendFormatted<T>(T value) where T : notnull
    {
        WriteString(value.ToString(), typeof(T) == typeof(string));
        lastIndent = 0;
    }

    private int WriteString(ReadOnlySpan<char> value, bool forceNewLineAtFirstLine = true, bool computeIndent = false)
    {
        var newLinesCount = value.Count('\n');
        Debug.Assert(newLinesCount < 256);
        
        Span<Range> ranges = stackalloc Range[newLinesCount + 1];
        value.Split(ranges, '\n');
        
        _sb.Append(value[ranges[0]]);
        if (value.Length > 0 && forceNewLineAtFirstLine && newLinesCount > 0)
        {
            _sb.AppendLine();
        }
        
        Debug.Assert(lastIndent + BaseIndentation<= 512);
        Span<char> currentIndentation = stackalloc char[lastIndent + BaseIndentation];
        currentIndentation.Slice(0 , lastIndent).Fill(' ');
        currentIndentation.Slice(lastIndent).Fill('\t');
        
        for(int i = 1; i < newLinesCount; i++)
        {
            _sb.Append(currentIndentation);
            _sb.Append(value[ranges[i]]);
            _sb.AppendLine();
        }
        
        if (newLinesCount > 0)
        {
            _sb.Append(currentIndentation);
            _sb.Append(value[ranges[newLinesCount]]);
        }

        if (!computeIndent)
            return -1;
        
        return value[ranges[newLinesCount]].Length;
    }
}
