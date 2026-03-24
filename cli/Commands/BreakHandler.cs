using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

using Mono.Debugger.Soft;

using InoIPC;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Handles breakpoint and exception catchpoint commands.
   /// </summary>
   // ============================================================
   static class BreakHandler
   {

   #region Break

      // ------------------------------------------------------------
      /// <summary>
      /// Handles break subcommands (set, remove, list, enable,
      /// disable) for location breakpoints.
      /// </summary>
      // ------------------------------------------------------------
      public static string HandleBreak
      (
         DebugContext context,
         string command, List<string> args,
         Dictionary<string, JsonElement> optionals
      )
      {
         switch (command)
         {
            case "remove":
            {
               if (optionals.Has("all"))
               {
                  int count = 0;

                  foreach (var profile in context.Profiles.List())
                  {
                     count += profile.RemoveAllBreakPoints();
                  }

                  return IpcResponse.Success($"Removed {count} breakpoints.").RawJson;
               }

               if (!args.TryParseId(out int id))
               {
                  return IpcResponse.Error(Constants.Error.InvalidArgs, "Breakpoint ID required.").RawJson;
               }

               if (!context.Profiles.RemovePoint(id))
               {
                  return IpcResponse.Error(Constants.Error.InvalidArgs, $"Breakpoint #{id} not found.").RawJson;
               }

               return IpcResponse.Success($"Removed breakpoint #{id}.").RawJson;
            }

            case "list":
            {
               string profileName = optionals.GetString("profile");

               List<BreakPoint> bps;

               if (!string.IsNullOrEmpty(profileName))
               {
                  var profile = context.Profiles.Get(profileName);
                  bps = profile != null
                     ? profile.BreakPoints.Values.ToList()
                     : new List<BreakPoint>();
               }
               else
               {
                  bps = context.Profiles.AllBreakPoints();
               }

               var list = bps.Select(b => b.ToDict()).ToList();
               return IpcResponse.Success("breakpoints", list).RawJson;
            }

            case "enable":
            {
               if (!args.TryParseId(out int id))
               {
                  return IpcResponse.Error(Constants.Error.InvalidArgs, "Breakpoint ID required.").RawJson;
               }

               foreach (var profile in context.Profiles.List())
               {
                  if (profile.Enable(id))
                  {
                     return IpcResponse.Success($"Enabled breakpoint #{id}.").RawJson;
                  }
               }

               return IpcResponse.Error(Constants.Error.InvalidArgs, $"Breakpoint #{id} not found.").RawJson;
            }

            case "disable":
            {
               if (!args.TryParseId(out int id))
               {
                  return IpcResponse.Error(Constants.Error.InvalidArgs, "Breakpoint ID required.").RawJson;
               }

               foreach (var profile in context.Profiles.List())
               {
                  if (profile.Disable(id))
                  {
                     return IpcResponse.Success($"Disabled breakpoint #{id}.").RawJson;
                  }
               }

               return IpcResponse.Error(Constants.Error.InvalidArgs, $"Breakpoint #{id} not found.").RawJson;
            }

            case "set":
               return SetBreakpoint(context, args, optionals);

            default:
            {
               return IpcResponse.Error
               (
                  Constants.Error.InvalidArgs,
                  $"Unknown break command: {command}"
               ).RawJson;
            }
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Parses file/line arguments and optionals, then creates a
      /// new breakpoint entry via the owning DebugProfile.
      /// </summary>
      // ------------------------------------------------------------
      private static string SetBreakpoint
      (
         DebugContext context, List<string> args,
         Dictionary<string, JsonElement> optionals
      )
      {
         if (args.Count < 2)
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs,
               "Usage: break set <file> <line>"
            ).RawJson;
         }

         string file = args[0];
         int    line = 0;

         int.TryParse(args[1], out line);

         if (line <= 0)
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs,
               "Usage: break set <file> <line>"
            ).RawJson;
         }

         // Extract optionals
         string condition   = optionals.GetString("condition");
         int    hitCount    = optionals.GetInt("hit-count");
         long   thread      = optionals.GetLong("thread");
         bool   temp        = optionals.Has("temp");
         string profileName = optionals.GetString("profile", "default");
         string desc        = optionals.GetString("desc");

         List<string> evals = null;

         if (optionals.Has("eval"))
         {
            var evalProp = optionals["eval"];

            if (evalProp.ValueKind == JsonValueKind.Array)
            {
               evals = new List<string>();

               foreach (var e in evalProp.EnumerateArray())
               {
                  evals.Add(e.GetString());
               }
            }
            else
            {
               evals = new List<string> { evalProp.GetString() };
            }
         }

         var profile = context.Profiles.Get(profileName ?? "default");

         if (profile == null)
         {
            return IpcResponse.Error(Constants.Error.NotFound, $"Profile '{profileName}' not found.").RawJson;
         }

         var bp = profile.SetBreak
         (
            context.Session, context.Profiles.NextId(),
            file, line, condition, hitCount,
            thread, temp, desc, evals
         );

         if (bp == null)
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, $"Cannot resolve {file}:{line}.").RawJson;
         }

         return IpcResponse.Success("breakpoint", bp.ToDict()).RawJson;
      }

   #endregion

   #region Catch

      // ------------------------------------------------------------
      /// <summary>
      /// Handles catch subcommands (set, remove, list, enable,
      /// disable, info) for exception catchpoints.
      /// </summary>
      // ------------------------------------------------------------
      public static string HandleCatch
      (
         DebugContext context,
         string command, List<string> args,
         Dictionary<string, JsonElement> optionals
      )
      {
         switch (command)
         {
            case "remove":
            {
               if (optionals.Has("all"))
               {
                  int count = 0;

                  foreach (var profile in context.Profiles.List())
                  {
                     count += profile.RemoveAllCatchPoints();
                  }

                  return IpcResponse.Success($"Removed {count} catchpoints.").RawJson;
               }

               if (!args.TryParseId(out int id))
               {
                  return IpcResponse.Error(Constants.Error.InvalidArgs, "Catchpoint ID required.").RawJson;
               }

               if (!context.Profiles.RemovePoint(id))
               {
                  return IpcResponse.Error(Constants.Error.InvalidArgs, $"Catchpoint #{id} not found.").RawJson;
               }

               return IpcResponse.Success($"Removed catchpoint #{id}.").RawJson;
            }

            case "list":
            {
               var list = context.Profiles.AllCatchPoints()
                  .Select(c => c.ToDict())
                  .ToList();

               return IpcResponse.Success("catchpoints", list).RawJson;
            }

            case "enable":
            {
               if (!args.TryParseId(out int id))
               {
                  return IpcResponse.Error(Constants.Error.InvalidArgs, "Catchpoint ID required.").RawJson;
               }

               foreach (var profile in context.Profiles.List())
               {
                  if (profile.Enable(id))
                  {
                     return IpcResponse.Success($"Enabled catchpoint #{id}.").RawJson;
                  }
               }

               return IpcResponse.Error(Constants.Error.InvalidArgs, $"Catchpoint #{id} not found.").RawJson;
            }

            case "disable":
            {
               if (!args.TryParseId(out int id))
               {
                  return IpcResponse.Error(Constants.Error.InvalidArgs, "Catchpoint ID required.").RawJson;
               }

               foreach (var profile in context.Profiles.List())
               {
                  if (profile.Disable(id))
                  {
                     return IpcResponse.Success($"Disabled catchpoint #{id}.").RawJson;
                  }
               }

               return IpcResponse.Error(Constants.Error.InvalidArgs, $"Catchpoint #{id} not found.").RawJson;
            }

            case "info":
            {
               bool hasInner  = optionals.Has("inner");
               int  innerDep  = hasInner ? optionals.GetInt("inner") : 0;
               bool hasStack  = optionals.Has("stack");

               var info = context.Session.GetExceptionInfo(hasStack, innerDep);

               if (info == null)
               {
                  return IpcResponse.Error(Constants.Error.InvalidArgs, "No current exception.").RawJson;
               }

               return IpcResponse.Success("exception", info).RawJson;
            }

            case "set":
               return SetCatchpoint(context, args, optionals);

            default:
            {
               return IpcResponse.Error
               (
                  Constants.Error.InvalidArgs,
                  $"Unknown catch command: {command}"
               ).RawJson;
            }
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Parses the exception type and optionals, then creates a
      /// new catchpoint entry via the owning DebugProfile.
      /// </summary>
      // ------------------------------------------------------------
      private static string SetCatchpoint
      (
         DebugContext context, List<string> args,
         Dictionary<string, JsonElement> optionals
      )
      {
         string typeName = args.Count > 0 ? args[0] : "";
         bool   all        = optionals.Has("all");
         bool   unhandled  = optionals.Has("unhandled");

         if (all)
         {
            typeName = "(all)";
         }

         // Extract optionals
         string condition   = optionals.GetString("condition");
         int    hitCount    = optionals.GetInt("hit-count");
         long   thread      = optionals.GetLong("thread");
         string profileName = optionals.GetString("profile", "default");
         string desc        = optionals.GetString("desc");

         bool caughtOnly   = !unhandled && !all;
         bool uncaughtOnly = unhandled;

         var profile = context.Profiles.Get(profileName ?? "default");

         if (profile == null)
         {
            return IpcResponse.Error(Constants.Error.NotFound, $"Profile '{profileName}' not found.").RawJson;
         }

         var cp = profile.SetCatch
         (
            context.Session, context.Profiles.NextId(),
            typeName,
            caughtOnly, uncaughtOnly,
            condition, hitCount, thread, desc
         );

         if (cp == null)
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, $"Cannot set catchpoint for '{typeName}'.").RawJson;
         }

         return IpcResponse.Success("catchpoint", cp.ToDict()).RawJson;
      }

   #endregion

   }
}
