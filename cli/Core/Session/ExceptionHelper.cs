using System;
using System.Collections;
using System.Collections.Generic;

using Mono.Debugger.Soft;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Extracts exception details from ObjectMirror references.
   /// </summary>
   // ============================================================
   static class ExceptionHelper
   {

   #region Info Extraction

      // ------------------------------------------------------------
      /// <summary>
      /// Builds a dictionary of exception details.
      /// </summary>
      // ------------------------------------------------------------
      public static Dictionary<string, object> GetInfo
      (
         ObjectMirror exception,
         bool includeStack = false,
         int innerDepth = 0
      )
      {
         if (exception == null)
         {
            return null;
         }

         var result = new Dictionary<string, object>
         {
            ["type"]    = exception.Type?.FullName ?? "",
            ["message"] = GetMessage(exception)
         };

         if (includeStack)
         {
            result["stackTrace"] = GetStackTrace(exception);
         }

         if (innerDepth > 0)
         {
            result["innerExceptions"] = GetInnerExceptions(exception, innerDepth);
         }

         return result;
      }

   #endregion

   #region Field Accessors

      // ------------------------------------------------------------
      /// <summary>
      /// Reads the _message field from an exception ObjectMirror.
      /// </summary>
      // ------------------------------------------------------------
      public static string GetMessage(ObjectMirror ex)
      {
         return GetStringField(ex, "_message");
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Reads the _stackTraceString field from an exception
      /// ObjectMirror.
      /// </summary>
      // ------------------------------------------------------------
      public static string GetStackTrace(ObjectMirror ex)
      {
         return GetStringField(ex, "_stackTraceString");
      }

   #endregion

   #region Inner Exceptions

      // ------------------------------------------------------------
      /// <summary>
      /// Traverses the InnerException chain up to the given depth.
      /// </summary>
      // ------------------------------------------------------------
      public static List<Dictionary<string, object>> GetInnerExceptions
      (
         ObjectMirror ex,
         int depth
      )
      {
         var result = new List<Dictionary<string, object>>();

         try
         {
            var field = ex.Type.GetField("_innerException");

            if (field == null)
            {
               return result;
            }

            var current = ex;
            int max     = depth > 0 ? depth : 10;

            for (int i = 0; i < max; i++)
            {
               var inner = current.GetValue(field);

               if (inner == null || inner is PrimitiveValue)
               {
                  break;
               }

               if (inner is ObjectMirror innerObj)
               {
                  result.Add(new Dictionary<string, object>
                  {
                     ["type"]    = innerObj.Type?.FullName ?? "",
                     ["message"] = GetMessage(innerObj)
                  });

                  current = innerObj;
               }
               else
               {
                  break;
               }
            }
         }
         catch
         {
         }

         return result;
      }

   #endregion

   #region Helpers

      // ------------------------------------------------------------
      /// <summary>
      /// Reads a string field value from an ObjectMirror by name.
      /// </summary>
      // ------------------------------------------------------------
      private static string GetStringField(ObjectMirror obj, string fieldName)
      {
         try
         {
            var field = obj.Type.GetField(fieldName);

            if (field != null)
            {
               var val = obj.GetValue(field);

               if (val is StringMirror sm)
               {
                  return sm.Value;
               }
            }
         }
         catch
         {
         }

         return "";
      }

   #endregion

   }
}
