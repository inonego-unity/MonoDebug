using System;
using System.Collections;
using System.Collections.Generic;

using Mono.Debugger.Soft;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Static helpers for inspecting stack frame contents:
   /// this reference, arguments, and local variables.
   /// </summary>
   // ============================================================
   static class StackInspector
   {

   #region This

      // ------------------------------------------------------------
      /// <summary>
      /// Returns the 'this' reference as a dictionary.
      /// </summary>
      // ------------------------------------------------------------
      public static Dictionary<string, object> GetThisValue
      (
         StackFrame frame, int depth = 1
      )
      {
         try
         {
            var obj = frame.GetThis();

            return obj is ObjectMirror om
               ? ValueFormatter.ObjectToDict(om, depth)
               : null;
         }
         catch
         {
            return null;
         }
      }

   #endregion

   #region Args

      // ------------------------------------------------------------
      /// <summary>
      /// Returns method arguments as dictionaries.
      /// </summary>
      // ------------------------------------------------------------
      public static List<Dictionary<string, object>> GetArgValues
      (
         StackFrame frame, int depth = 0
      )
      {
         var result = new List<Dictionary<string, object>>();

         try
         {
            var parms = frame.Method.GetParameters();

            for (int i = 0; i < parms.Length; i++)
            {
               try
               {
                  result.Add
                  (
                     new Dictionary<string, object>
                     {
                        ["name"]  = parms[i].Name,
                        ["type"]  = parms[i].ParameterType.FullName,
                        ["value"] = ValueFormatter.Format(frame.GetArgument(i), depth)
                     }
                  );
               }
               catch { }
            }
         }
         catch { }

         return result;
      }

   #endregion

   #region Locals

      // ------------------------------------------------------------
      /// <summary>
      /// Returns local variables as dictionaries.
      /// </summary>
      // ------------------------------------------------------------
      public static List<Dictionary<string, object>> GetLocalValues
      (
         StackFrame frame, int depth = 0
      )
      {
         var result = new List<Dictionary<string, object>>();

         try
         {
            foreach (var local in frame.GetVisibleVariables())
            {
               result.Add
               (
                  new Dictionary<string, object>
                  {
                     ["name"]  = local.Name,
                     ["type"]  = local.Type.FullName,
                     ["value"] = ValueFormatter.Format(frame.GetValue(local), depth)
                  }
               );
            }
         }
         catch { }

         return result;
      }

   #endregion

   }
}
