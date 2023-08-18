using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Cecilifier.Core.Tests.Tests.Unit;

[TestFixture]
public partial class MiscellaneousStatements : CecilifierUnitTestBase
{
    [Test]
    public void BreakInForBody()
    {
        var cecilified = RunCecilifier(
            """
                void M()
                {
                    for (int j = 0; j < 10; j++)
                        break;
                    
                    System.Console.WriteLine("END");
                }
                """);
        
        Assert.That(
            cecilified.GeneratedCode.ReadToEnd(), 
            Does.Match(
                 """
                 \s+var (lbl_fel_\d+) = il_M_7.Create\(OpCodes.Nop\);
                 \s+var nop_10 = il_M_7.Create\(OpCodes.Nop\);
                 \s+il_M_7.Append\(nop_10\);
                 \s+il_M_7.Emit\(OpCodes.Ldloc, l_j_8\);
                 \s+il_M_7.Emit\(OpCodes.Ldc_I4, 10\);
                 \s+il_M_7.Emit\(OpCodes.Clt\);
                 \s+il_M_7.Emit\(OpCodes.Brfalse, \1\);
                 \s+il_M_7.Emit\(OpCodes.Br, \1\);
                 \s+il_M_7.Emit\(OpCodes.Ldloc, l_j_8\);
                 \s+il_M_7.Emit\(OpCodes.Dup\);
                 \s+il_M_7.Emit\(OpCodes.Ldc_I4_1\);
                 \s+il_M_7.Emit\(OpCodes.Add\);
                 \s+il_M_7.Emit\(OpCodes.Stloc, l_j_8\);
                 \s+il_M_7.Emit\(OpCodes.Pop\);
                 \s+il_M_7.Emit\(OpCodes.Br, nop_10\);
                 \s+il_M_7.Append\(\1\);
                 """));
    }
    
    [Test]
    public void MultipleBreaks_SingleTarget()
    {
        var result = RunCecilifier(
            """
                void M()
                {
                    for (int j = 0; j < 10; j++)
                    {
                        if  (j > 2)
                        {
                            System.Console.WriteLine("IF");
                            break;
                        }
                        else
                        {
                            System.Console.WriteLine("ELSE");
                            break;
                        }
                    }
                    System.Console.WriteLine("END");
                }
                """);

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        var match =MultipleBreaks_SingleTargetRegex().Matches(cecilifiedCode);
        Assert.That(match.Count, Is.EqualTo(2), $"Generated code \n{cecilifiedCode}\n\ndoes not match:\n{MultipleBreaks_SingleTargetRegex()}");
        
        Assert.That(cecilifiedCode, Does.Match(
            """
            \s+il_M_7.Append\(lbl_fel_9\);
            
            \s+//System.Console.WriteLine\("END"\);
            \s+il_M_7.Emit\(OpCodes.Ldstr, "END"\);
            """));
    }
    
    [Test]
    public void MultipleBreaks_MultipleLevels()
    {
        var result = RunCecilifier(
            """
                void M()
                {
                    for (int j = 0; j < 10; j++)
                    {
                        if  (j > 5)
                        {
                            System.Console.WriteLine("IF");
                            break;
                        }
                        switch(j)
                        {
                            case 1: System.Console.WriteLine("C1"); break;
                            case 2: System.Console.WriteLine("C2"); break;
                            default: System.Console.WriteLine("CD"); break;
                        }
                    }
                    System.Console.WriteLine("END");
                }
                """);

        var cecilifiedCode = result.GeneratedCode.ReadToEnd();

        Assert.That(cecilifiedCode, Does.Match(
            """
            \s+//case 1: \(condition\)
            \s+il_M_7.Emit\(OpCodes.Ldloc, l_switchCondition_13\);
            \s+il_M_7.Emit\(OpCodes.Ldc_I4, 1\);
            \s+il_M_7.Emit\(OpCodes.Beq_S, lbl_caseCode_0_15\);
            
            \s+//case 2: \(condition\)
            \s+il_M_7.Emit\(OpCodes.Ldloc, l_switchCondition_13\);
            \s+il_M_7.Emit\(OpCodes.Ldc_I4, 2\);
            \s+il_M_7.Emit\(OpCodes.Beq_S, lbl_caseCode_1_16\);
            \s+il_M_7.Emit\(OpCodes.Br, lbl_caseCode_2_17\);
            
            \s+//case 1: \(code\)
            \s+il_M_7.Append\(lbl_caseCode_0_15\);
            \s+il_M_7.Emit\(OpCodes.Ldstr, "C1"\);
            \s+il_M_7.Emit\(OpCodes.Call, .+System.Console.+WriteLine.+\);
            \s+il_M_7.Emit\(OpCodes.Br, lbl_endOfSwitch_14\);
            
            \s+//case 2: \(code\)
            \s+il_M_7.Append\(lbl_caseCode_1_16\);
            \s+il_M_7.Emit\(OpCodes.Ldstr, "C2"\);
            \s+il_M_7.Emit\(OpCodes.Call, .+System.Console.+WriteLine.+\);
            \s+il_M_7.Emit\(OpCodes.Br, lbl_endOfSwitch_14\);
            
            \s+//default: \(code\)
            \s+il_M_7.Append\(lbl_caseCode_2_17\);
            \s+il_M_7.Emit\(OpCodes.Ldstr, "CD"\);
            \s+il_M_7.Emit\(OpCodes.Call, .+System.Console.+WriteLine.+\);
            \s+il_M_7.Emit\(OpCodes.Br, lbl_endOfSwitch_14\);
            
            \s+//End of switch
            \s+il_M_7.Append\(lbl_endOfSwitch_14\);
            """));
    }

    [GeneratedRegex(
        """
        //System\.Console\.WriteLine\("(IF|ELSE)"\);
        (\s+il_M_\d+\.Emit\(OpCodes\.)Ldstr, "\1"\);
        \2Call, .+"WriteLine".+\);
        
        \s+//break;
        \2Br, (?<breakTarget>lbl_fel_9)\);
        """)]
    private static partial Regex MultipleBreaks_SingleTargetRegex();
}
