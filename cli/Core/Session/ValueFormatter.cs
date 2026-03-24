using System;
using System.Collections;
using System.Collections.Generic;

using Mono.Debugger.Soft;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Formats Mono.Debugger.Soft Value objects into
   /// JSON-serializable dictionaries and primitives.
   /// </summary>
   // ============================================================
   static class ValueFormatter
   {

   #region Format

      // ------------------------------------------------------------
      /// <summary>
      /// Formats a Value into a JSON-serializable object.
      /// </summary>
      // ------------------------------------------------------------
      public static object Format(Value val, int depth = 0)
      {
         if (val == null)
         {
            return null;
         }

         if (val is PrimitiveValue pv)
         {
            return pv.Value;
         }

         if (val is StringMirror sm)
         {
            return sm.Value;
         }

         if (val is ObjectMirror om)
         {
            if (depth <= 0)
            {
               return $"{om.Type.FullName}@0x{om.Address:X}";
            }

            return ObjectToDict(om, depth);
         }

         if (val is StructMirror stm)
         {
            if (depth <= 0)
            {
               return stm.Type.FullName;
            }

            var dict   = new Dictionary<string, object> { ["type"] = stm.Type.FullName };
            var fields = stm.Type.GetFields();

            for (int i = 0; i < fields.Length && i < stm.Fields.Length; i++)
            {
               dict[fields[i].Name] = Format(stm.Fields[i], depth - 1);
            }

            return dict;
         }

         if (val is EnumMirror em)
         {
            return em.StringValue;
         }

         if (val is ArrayMirror am)
         {
            return $"{am.Type.GetElementType().FullName}[{am.Length}]";
         }

         return val.ToString();
      }

   #endregion

   #region Object Conversion

      // ------------------------------------------------------------
      /// <summary>
      /// Converts an ObjectMirror to a dictionary with type,
      /// address, and optionally expanded fields.
      /// </summary>
      // ------------------------------------------------------------
      public static Dictionary<string, object> ObjectToDict(ObjectMirror om, int depth)
      {
         var dict = new Dictionary<string, object>
         {
            ["type"]    = om.Type.FullName,
            ["address"] = $"0x{om.Address:X}"
         };

         if (depth > 0)
         {
            var fields = new Dictionary<string, object>();

            foreach (var field in om.Type.GetFields())
            {
               try
               {
                  var fval = om.GetValue(field);
                  fields[field.Name] = Format(fval, depth - 1);
               }
               catch
               {
                  fields[field.Name] = "<error>";
               }
            }

            dict["fields"] = fields;
         }

         return dict;
      }

   #endregion

   }
}
