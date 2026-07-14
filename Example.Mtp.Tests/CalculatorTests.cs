using Mutannot.Annotations;
using Xunit;

namespace Example.Mtp;

[ShouldCatch("""
--- a/Example.CSharp/Calculator.cs
+++ b/Example.CSharp/Calculator.cs
@@ -1,6 +1,6 @@
 namespace Example;

 public static class Calculator
 {
-    public static int Add(int x, int y) => x + y;
+    public static int Add(int x, int y) => x - y;
 }
""")]
public class CalculatorTests
{
    [Fact]
    public void Add_Returns_Sum()
    {
        Assert.Equal(5, Calculator.Add(2, 3));
    }
}
