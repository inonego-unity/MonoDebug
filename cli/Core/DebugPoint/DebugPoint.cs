using System;
using System.Collections;
using System.Collections.Generic;

using Mono.Debugger.Soft;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Abstract base for BreakPoint and CatchPoint.
   /// </summary>
   // ============================================================
   abstract class DebugPoint
   {

   #region Fields

      public int    Id           { get; set; }
      public bool   Enabled      { get; set; }
      public int    Hits         { get; set; }
      public string Desc         { get; set; }
      public string Condition    { get; set; }
      public int    HitCount     { get; set; }
      public long   ThreadFilter { get; set; }

      // SDB reference (not serialized)
      public EventRequest Request { get; set; }

   #endregion

   #region Serialization

      // ------------------------------------------------------------
      /// <summary>
      /// Converts to a serializable dictionary.
      /// </summary>
      // ------------------------------------------------------------
      public virtual Dictionary<string, object> ToDict()
      {
         var dict = new Dictionary<string, object>
         {
            ["id"]      = Id,
            ["enabled"] = Enabled,
            ["hits"]    = Hits
         };

         if (!string.IsNullOrEmpty(Desc))
         {
            dict["desc"] = Desc;
         }

         if (!string.IsNullOrEmpty(Condition))
         {
            dict["condition"] = Condition;
         }

         if (HitCount > 0)
         {
            dict["hitCount"] = HitCount;
         }

         if (ThreadFilter > 0)
         {
            dict["threadFilter"] = ThreadFilter;
         }

         return dict;
      }

   #endregion

   }
}
