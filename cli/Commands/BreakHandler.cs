using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using InoIPC;

using InoCLI;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Handles breakpoint and exception catchpoint commands.
   /// </summary>
   // ============================================================
   static class BreakHandler
   {

   #region Break Set

      // ------------------------------------------------------------
      /// <summary>
      /// Creates a new location breakpoint.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("break", "set", description = "Set breakpoint")]
      public static string BreakSet(CommandArgs args)
      {
         var context = DebugContext.Current;

         if (args.Count < 2)
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs,
               "Usage: break set <file> <line>"
            ).RawJson;
         }

         string file = args[0];
         int    line = args.GetInt(1, 0);

         if (line <= 0)
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs,
               "Usage: break set <file> <line>"
            ).RawJson;
         }

         string condition   = args.Get("condition");
         int    hitCount    = args.GetInt("hit-count", 0);
         long   thread      = args.GetLong("thread", 0);
         bool   temp        = args.Has("temp");
         string profileName = args.Get("profile", "default");
         string desc        = args.Get("desc");

         List<string> evals = null;

         if (args.Has("eval"))
         {
            evals = args.All("eval", new List<string>());
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

   #region Break Remove

      // ------------------------------------------------------------
      /// <summary>
      /// Removes a breakpoint by ID, or all with --all.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("break", "remove", description = "Remove breakpoint")]
      public static string BreakRemove(CommandArgs args)
      {
         var context = DebugContext.Current;

         if (args.Has("all"))
         {
            string profileName = args.Get("profile");
            int    count       = 0;

            if (!string.IsNullOrEmpty(profileName))
            {
               var profile = context.Profiles.Get(profileName);

               if (profile != null)
               {
                  count = profile.RemoveAllBreakPoints();
               }
            }
            else
            {
               foreach (var profile in context.Profiles.List())
               {
                  count += profile.RemoveAllBreakPoints();
               }
            }

            return IpcResponse.Success($"Removed {count} breakpoints.").RawJson;
         }

         if (!int.TryParse(args[0], out int id))
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, "Breakpoint ID required.").RawJson;
         }

         if (!context.Profiles.RemovePoint(id))
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, $"Breakpoint #{id} not found.").RawJson;
         }

         return IpcResponse.Success($"Removed breakpoint #{id}.").RawJson;
      }

   #endregion

   #region Break List

      // ------------------------------------------------------------
      /// <summary>
      /// Lists breakpoints, optionally filtered by --profile.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("break", "list", description = "List breakpoints")]
      public static string BreakList(CommandArgs args)
      {
         var context = DebugContext.Current;

         string profileName = args.Get("profile");

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

   #endregion

   #region Break Enable / Disable

      // ------------------------------------------------------------
      /// <summary>
      /// Enables a breakpoint by ID.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("break", "enable", description = "Enable breakpoint")]
      public static string BreakEnable(CommandArgs args)
      {
         var context = DebugContext.Current;

         if (!int.TryParse(args[0], out int id))
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

      // ------------------------------------------------------------
      /// <summary>
      /// Disables a breakpoint by ID.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("break", "disable", description = "Disable breakpoint")]
      public static string BreakDisable(CommandArgs args)
      {
         var context = DebugContext.Current;

         if (!int.TryParse(args[0], out int id))
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

   #endregion

   #region Catch Set

      // ------------------------------------------------------------
      /// <summary>
      /// Creates a new exception catchpoint.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("catch", "set", description = "Set catchpoint")]
      public static string CatchSet(CommandArgs args)
      {
         var context = DebugContext.Current;

         string typeName  = args[0] ?? "";
         bool   all       = args.Has("all");
         bool   unhandled = args.Has("unhandled");

         if (all)
         {
            typeName = "(all)";
         }

         string condition   = args.Get("condition");
         int    hitCount    = args.GetInt("hit-count", 0);
         long   thread      = args.GetLong("thread", 0);
         string profileName = args.Get("profile", "default");
         string desc        = args.Get("desc");

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

   #region Catch Remove

      // ------------------------------------------------------------
      /// <summary>
      /// Removes a catchpoint by ID, or all with --all.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("catch", "remove", description = "Remove catchpoint")]
      public static string CatchRemove(CommandArgs args)
      {
         var context = DebugContext.Current;

         if (args.Has("all"))
         {
            string profileName = args.Get("profile");
            int    count       = 0;

            if (!string.IsNullOrEmpty(profileName))
            {
               var profile = context.Profiles.Get(profileName);

               if (profile != null)
               {
                  count = profile.RemoveAllCatchPoints();
               }
            }
            else
            {
               foreach (var profile in context.Profiles.List())
               {
                  count += profile.RemoveAllCatchPoints();
               }
            }

            return IpcResponse.Success($"Removed {count} catchpoints.").RawJson;
         }

         if (!int.TryParse(args[0], out int id))
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, "Catchpoint ID required.").RawJson;
         }

         if (!context.Profiles.RemovePoint(id))
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, $"Catchpoint #{id} not found.").RawJson;
         }

         return IpcResponse.Success($"Removed catchpoint #{id}.").RawJson;
      }

   #endregion

   #region Catch List

      // ------------------------------------------------------------
      /// <summary>
      /// Lists all catchpoints.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("catch", "list", description = "List catchpoints")]
      public static string CatchList(CommandArgs args)
      {
         var context = DebugContext.Current;

         var list = context.Profiles.AllCatchPoints()
            .Select(c => c.ToDict())
            .ToList();

         return IpcResponse.Success("catchpoints", list).RawJson;
      }

   #endregion

   #region Catch Enable / Disable

      // ------------------------------------------------------------
      /// <summary>
      /// Enables a catchpoint by ID.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("catch", "enable", description = "Enable catchpoint")]
      public static string CatchEnable(CommandArgs args)
      {
         var context = DebugContext.Current;

         if (!int.TryParse(args[0], out int id))
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

      // ------------------------------------------------------------
      /// <summary>
      /// Disables a catchpoint by ID.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("catch", "disable", description = "Disable catchpoint")]
      public static string CatchDisable(CommandArgs args)
      {
         var context = DebugContext.Current;

         if (!int.TryParse(args[0], out int id))
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

   #endregion

   #region Catch Info

      // ------------------------------------------------------------
      /// <summary>
      /// Shows current exception info.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("catch", "info", description = "Exception info")]
      public static string CatchInfo(CommandArgs args)
      {
         var context = DebugContext.Current;

         bool hasInner = args.Has("inner");
         int  innerDep = hasInner ? args.GetInt("inner", 0) : 0;
         bool hasStack = args.Has("stack");

         var info = context.Session.GetExceptionInfo(hasStack, innerDep);

         if (info == null)
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, "No current exception.").RawJson;
         }

         return IpcResponse.Success("exception", info).RawJson;
      }

   #endregion

   }
}
