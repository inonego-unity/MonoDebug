using System;
using System.Collections;
using System.Collections.Generic;

using Xunit;

namespace MonoDebug.TEST
{
   // ============================================================
   /// <summary>
   /// Tests for DebugPoint, BreakPoint, CatchPoint.
   /// </summary>
   // ============================================================
   public class TEST_DebugPoint
   {

   #region BreakPoint

      [Fact]
      public void BreakPoint_ToDict_ContainsBase()
      {
         var bp = new BreakPoint
         {
            Id      = 1,
            File    = "/path/to/Test.cs",
            Line    = 42,
            Enabled = true,
            Hits    = 3
         };

         var dict = bp.ToDict();

         Assert.Equal(1, dict["id"]);
         Assert.Equal(true, dict["enabled"]);
         Assert.Equal(3, dict["hits"]);
         Assert.Equal("/path/to/Test.cs", dict["file"]);
         Assert.Equal(42, dict["line"]);
      }

      [Fact]
      public void BreakPoint_ToDict_OmitsDefaults()
      {
         var bp = new BreakPoint
         {
            Id      = 1,
            File    = "/path/to/Test.cs",
            Line    = 10,
            Enabled = true
         };

         var dict = bp.ToDict();

         Assert.False(dict.ContainsKey("desc"));
         Assert.False(dict.ContainsKey("condition"));
         Assert.False(dict.ContainsKey("hitCount"));
         Assert.False(dict.ContainsKey("threadFilter"));
         Assert.False(dict.ContainsKey("isTemp"));
         Assert.False(dict.ContainsKey("evalExpressions"));
      }

      [Fact]
      public void BreakPoint_ToDict_IncludesOptionals()
      {
         var bp = new BreakPoint
         {
            Id              = 1,
            File            = "/path/to/Test.cs",
            Line            = 10,
            Enabled         = true,
            Desc            = "test bp",
            Condition       = "x > 0",
            HitCount        = 5,
            ThreadFilter    = 3,
            IsTemp          = true,
            EvalExpressions = new List<string> { "a", "b" }
         };

         var dict = bp.ToDict();

         Assert.Equal("test bp", dict["desc"]);
         Assert.Equal("x > 0", dict["condition"]);
         Assert.Equal(5, dict["hitCount"]);
         Assert.Equal(3L, dict["threadFilter"]);
         Assert.Equal(true, dict["isTemp"]);
         Assert.IsType<List<string>>(dict["evalExpressions"]);
      }

   #endregion

   #region CatchPoint

      [Fact]
      public void CatchPoint_ToDict_ContainsType()
      {
         var cp = new CatchPoint
         {
            Id       = 2,
            TypeName = "NullReferenceException",
            Enabled  = true
         };

         var dict = cp.ToDict();

         Assert.Equal(2, dict["id"]);
         Assert.Equal("NullReferenceException", dict["typeName"]);
      }

      [Fact]
      public void CatchPoint_ToDict_CaughtOnly()
      {
         var cp = new CatchPoint
         {
            Id         = 3,
            TypeName   = "Exception",
            CaughtOnly = true,
            Enabled    = true
         };

         var dict = cp.ToDict();

         Assert.Equal(true, dict["caughtOnly"]);
         Assert.False(dict.ContainsKey("uncaughtOnly"));
      }

   #endregion

   }
}
