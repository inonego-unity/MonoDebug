using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

using InoIPC;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Handles profile management commands: create, remove, edit,
   /// switch, enable, disable, list, info.
   /// </summary>
   // ============================================================
   static class ProfileHandler
   {

   #region Dispatch

      // ------------------------------------------------------------
      /// <summary>
      /// Dispatches a profile command and returns a JSON result.
      /// </summary>
      // ------------------------------------------------------------
      public static string Handle
      (
         DebugContext context, List<string> args,
         Dictionary<string, JsonElement> optionals
      )
      {
         string command = args.Count > 0 ? args[0] : "";
         var    rest    = args.Count > 1
            ? args.GetRange(1, args.Count - 1)
            : new List<string>();

         switch (command)
         {
            case "create":
            {
               return Create(context, rest, optionals);
            }

            case "remove":
            {
               return Remove(context, rest);
            }

            case "edit":
            {
               return Edit(context, rest, optionals);
            }

            case "switch":
            {
               return Switch(context, rest);
            }

            case "enable":
            {
               return Enable(context, rest);
            }

            case "disable":
            {
               return Disable(context, rest, optionals);
            }

            case "list":
            {
               return ListAll(context);
            }

            case "info":
            {
               return Info(context, rest);
            }

            default:
            {
               return IpcResponse.Error
               (
                  Constants.Error.InvalidArgs,
                  $"Unknown profile command: {command}"
               ).RawJson;
            }
         }
      }

   #endregion

   #region Commands

      // ------------------------------------------------------------
      /// <summary>
      /// Creates a new profile with optional description.
      /// </summary>
      // ------------------------------------------------------------
      private static string Create(DebugContext context, List<string> args, Dictionary<string, JsonElement> optionals)
      {
         string name = args.Count > 0 ? args[0] : "";

         if (string.IsNullOrEmpty(name))
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, "Profile name required.").RawJson;
         }

         string desc = optionals.GetString("desc");
         var    prof = context.Profiles.Create(name, desc);

         if (prof == null)
         {
            return IpcResponse.Error(Constants.Error.AlreadyExists, $"Profile '{name}' already exists.").RawJson;
         }

         return IpcResponse.Success(new Dictionary<string, object> { ["profile"] = prof.ToDict() }).RawJson;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Removes a profile and cascade-deletes its breakpoints and
      /// catchpoints.
      /// </summary>
      // ------------------------------------------------------------
      private static string Remove(DebugContext context, List<string> args)
      {
         string name = args.Count > 0 ? args[0] : "";

         if (string.IsNullOrEmpty(name))
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, "Profile name required.").RawJson;
         }

         // Profile owns its points; removal cascades via ProfileCollection
         var profile = context.Profiles.Get(name);

         if (profile != null)
         {
            profile.RemoveAll();
         }

         if (!context.Profiles.Remove(name))
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, $"Cannot remove profile '{name}'.").RawJson;
         }

         context.Profiles.Save();
         return IpcResponse.Success($"Removed profile '{name}' and its breakpoints.").RawJson;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Edits a profile's description or name.
      /// </summary>
      // ------------------------------------------------------------
      private static string Edit(DebugContext context, List<string> args, Dictionary<string, JsonElement> optionals)
      {
         string name   = args.Count > 0 ? args[0] : "";
         string desc   = optionals.GetString("desc");
         string rename = optionals.GetString("rename");

         var prof = context.Profiles.Edit(name, desc, rename);

         if (prof == null)
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, $"Profile '{name}' not found or rename conflict.").RawJson;
         }

         context.Profiles.Save();

         return IpcResponse.Success(new Dictionary<string, object> { ["profile"] = prof.ToDict() }).RawJson;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Switches to a profile: activates it, deactivates all
      /// others.
      /// </summary>
      // ------------------------------------------------------------
      private static string Switch(DebugContext context, List<string> args)
      {
         string name = args.Count > 0 ? args[0] : "";

         if (!context.Profiles.Exists(name))
         {
            return IpcResponse.Error(Constants.Error.NotFound, $"Profile '{name}' not found.").RawJson;
         }

         context.Profiles.Switch(name);
         ApplyProfileState(context);
         return IpcResponse.Success($"Switched to profile '{name}'.").RawJson;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Enables a profile without affecting others.
      /// </summary>
      // ------------------------------------------------------------
      private static string Enable(DebugContext context, List<string> args)
      {
         string name = args.Count > 0 ? args[0] : "";

         if (!context.Profiles.Exists(name))
         {
            return IpcResponse.Error
            (
               Constants.Error.NotFound,
               $"Profile '{name}' not found."
            ).RawJson;
         }

         context.Profiles.Enable(name);
         ApplyProfileState(context);
         return IpcResponse.Success($"Enabled profile '{name}'.").RawJson;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Disables a profile or all profiles.
      /// </summary>
      // ------------------------------------------------------------
      private static string Disable(DebugContext context, List<string> args, Dictionary<string, JsonElement> optionals)
      {
         if (optionals.Has("all"))
         {
            context.Profiles.DisableAll();
            ApplyProfileState(context);
            return IpcResponse.Success("Disabled all profiles.").RawJson;
         }

         string name = args.Count > 0 ? args[0] : "";

         if (!context.Profiles.Exists(name))
         {
            return IpcResponse.Error
            (
               Constants.Error.NotFound,
               $"Profile '{name}' not found."
            ).RawJson;
         }

         context.Profiles.Disable(name);
         ApplyProfileState(context);
         return IpcResponse.Success($"Disabled profile '{name}'.").RawJson;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Lists all profiles with breakpoint and catchpoint counts.
      /// </summary>
      // ------------------------------------------------------------
      private static string ListAll(DebugContext context)
      {
         var list = context.Profiles.List().Select(p =>
         {
            var dict = p.ToDict();
            dict["breakpoints"] = p.BreakPoints.Count;
            dict["catchpoints"] = p.CatchPoints.Count;
            return dict;
         }).ToList();

         return IpcResponse.Success(new Dictionary<string, object> { ["profiles"] = list }).RawJson;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Shows profile detail with its breakpoints and catchpoints.
      /// </summary>
      // ------------------------------------------------------------
      private static string Info(DebugContext context, List<string> args)
      {
         if (args.Count == 0)
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs, "Profile name required."
            ).RawJson;
         }

         string name = args[0];
         var    prof = context.Profiles.Get(name);

         if (prof == null)
         {
            return IpcResponse.Error
            (
               Constants.Error.NotFound,
               $"Profile '{name}' not found."
            ).RawJson;
         }

         var bps = prof.BreakPoints.Values.Select(b => b.ToDict()).ToList();
         var cps = prof.CatchPoints.Values.Select(c => c.ToDict()).ToList();

         return IpcResponse.Success(new Dictionary<string, object>
         {
            ["profile"]     = prof.ToDict(),
            ["breakpoints"] = bps,
            ["catchpoints"] = cps
         }).RawJson;
      }

   #endregion

   #region State Application

      // ------------------------------------------------------------
      /// <summary>
      /// Applies profile activation state to all breakpoints and
      /// catchpoints. Enables/disables them based on whether their
      /// owning profile is active.
      /// </summary>
      // ------------------------------------------------------------
      public static void ApplyProfileState(DebugContext context)
      {
         foreach (var profile in context.Profiles.List())
         {
            bool active = profile.Active;

            // Apply to breakpoints
            foreach (var bp in profile.BreakPoints.Values)
            {
               if (active && !bp.Enabled)
               {
                  profile.Enable(bp.Id);
               }
               else if (!active && bp.Enabled)
               {
                  profile.Disable(bp.Id);
               }
            }

            // Apply to catchpoints
            foreach (var cp in profile.CatchPoints.Values)
            {
               if (active && !cp.Enabled)
               {
                  profile.Enable(cp.Id);
               }
               else if (!active && cp.Enabled)
               {
                  profile.Disable(cp.Id);
               }
            }
         }

         context.Profiles.Save();
      }

   #endregion

   }
}
