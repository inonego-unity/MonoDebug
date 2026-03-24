using System;
using System.Collections;
using System.Collections.Generic;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Location breakpoint.
   /// </summary>
   // ============================================================
   class BreakPoint : DebugPoint
   {

   #region Fields

      public string       File            { get; set; }
      public int          Line            { get; set; }
      public bool         IsTemp          { get; set; }
      public List<string> EvalExpressions { get; set; }

   #endregion

   #region Serialization

      // ------------------------------------------------------------
      /// <summary>
      /// Converts to a serializable dictionary.
      /// </summary>
      // ------------------------------------------------------------
      public override Dictionary<string, object> ToDict()
      {
         var dict = base.ToDict();

         dict["file"] = File;
         dict["line"] = Line;

         if (IsTemp)
         {
            dict["isTemp"] = true;
         }

         if (EvalExpressions != null && EvalExpressions.Count > 0)
         {
            dict["evalExpressions"] = EvalExpressions;
         }

         return dict;
      }

   #endregion

   }
}
