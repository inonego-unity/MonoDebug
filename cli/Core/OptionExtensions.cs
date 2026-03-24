using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

using InoIPC;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Extension methods for Dictionary&lt;string, JsonElement&gt;
   /// used by command handlers to extract optional arguments.
   /// </summary>
   // ============================================================
   static class OptionExtensions
   {

   #region Has

      // ------------------------------------------------------------
      /// <summary>
      /// Returns true if the key exists.
      /// </summary>
      // ------------------------------------------------------------
      public static bool Has
      (
         this Dictionary<string, JsonElement> optionals,
         string key
      )
      {
         return optionals.ContainsKey(key);
      }

   #endregion

   #region Get

      // ------------------------------------------------------------
      /// <summary>
      /// Gets a string value, or fallback if missing.
      /// </summary>
      // ------------------------------------------------------------
      public static string GetString
      (
         this Dictionary<string, JsonElement> optionals,
         string key, string fallback = null
      )
      {
         if (optionals.TryGetValue(key, out var el))
         {
            return JsonHelper.GetString(el, fallback);
         }

         return fallback;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Gets an int value, or fallback if missing.
      /// </summary>
      // ------------------------------------------------------------
      public static int GetInt
      (
         this Dictionary<string, JsonElement> optionals,
         string key, int fallback = 0
      )
      {
         if (optionals.TryGetValue(key, out var el))
         {
            return JsonHelper.GetInt(el, fallback);
         }

         return fallback;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Gets a long value, or fallback if missing.
      /// </summary>
      // ------------------------------------------------------------
      public static long GetLong
      (
         this Dictionary<string, JsonElement> optionals,
         string key, long fallback = 0
      )
      {
         if (optionals.TryGetValue(key, out var el))
         {
            return JsonHelper.GetLong(el, fallback);
         }

         return fallback;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Gets a bool value, or fallback if missing.
      /// </summary>
      // ------------------------------------------------------------
      public static bool GetBool
      (
         this Dictionary<string, JsonElement> optionals,
         string key, bool fallback = false
      )
      {
         if (optionals.TryGetValue(key, out var el))
         {
            return JsonHelper.GetBool(el, fallback);
         }

         return fallback;
      }

   #endregion

   #region Args

      // ------------------------------------------------------------
      /// <summary>
      /// Tries to parse the first arg as an int ID.
      /// </summary>
      // ------------------------------------------------------------
      public static bool TryParseId(this List<string> args, out int id)
      {
         if (args.Count > 0 && int.TryParse(args[0], out id))
         {
            return true;
         }

         id = 0;
         return false;
      }

   #endregion

   }
}
