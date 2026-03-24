using System;
using System.Collections;
using System.Collections.Generic;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Exception catchpoint.
   /// </summary>
   // ============================================================
   class CatchPoint : DebugPoint
   {

   #region Fields

      public string TypeName     { get; set; }
      public bool   CaughtOnly   { get; set; }
      public bool   UncaughtOnly { get; set; }

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

         dict["typeName"] = TypeName;

         if (CaughtOnly)
         {
            dict["caughtOnly"] = true;
         }

         if (UncaughtOnly)
         {
            dict["uncaughtOnly"] = true;
         }

         return dict;
      }

   #endregion

   }
}
