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
   /// Handles profile management commands: create, remove, edit,
   /// switch, enable, disable, list, info.
   /// </summary>
   // ============================================================
   static class ProfileHandler
   {

   #region Create

      // ------------------------------------------------------------
      /// <summary>
      /// Creates a new profile with optional description.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("profile", "create", description = "Create profile")]
      public static string Create(CommandArgs args)
      {
         var context = DebugContext.Current;

         string name = args[0] ?? "";

         if (string.IsNullOrEmpty(name))
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, "Profile name required.").RawJson;
         }

         string desc = args.Get("desc");
         var    prof = context.Profiles.Create(name, desc);

         if (prof == null)
         {
            return IpcResponse.Error(Constants.Error.AlreadyExists, $"Profile '{name}' already exists.").RawJson;
         }

         return IpcResponse.Success(new Dictionary<string, object> { ["profile"] = prof.ToDict() }).RawJson;
      }

   #endregion

   #region Remove

      // ------------------------------------------------------------
      /// <summary>
      /// Removes a profile and cascade-deletes its breakpoints and
      /// catchpoints.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("profile", "remove", description = "Remove profile")]
      public static string Remove(CommandArgs args)
      {
         var context = DebugContext.Current;

         string name = args[0] ?? "";

         if (string.IsNullOrEmpty(name))
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, "Profile name required.").RawJson;
         }

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

   #endregion

   #region Edit

      // ------------------------------------------------------------
      /// <summary>
      /// Edits a profile's description or name.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("profile", "edit", description = "Edit profile")]
      public static string Edit(CommandArgs args)
      {
         var context = DebugContext.Current;

         string name   = args[0] ?? "";
         string desc   = args.Get("desc");
         string rename = args.Get("rename");

         var prof = context.Profiles.Edit(name, desc, rename);

         if (prof == null)
         {
            return IpcResponse.Error(Constants.Error.InvalidArgs, $"Profile '{name}' not found or rename conflict.").RawJson;
         }

         context.Profiles.Save();

         return IpcResponse.Success(new Dictionary<string, object> { ["profile"] = prof.ToDict() }).RawJson;
      }

   #endregion

   #region Switch

      // ------------------------------------------------------------
      /// <summary>
      /// Switches to a profile: activates it, deactivates all
      /// others.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("profile", "switch", description = "Switch active profile")]
      public static string Switch(CommandArgs args)
      {
         var context = DebugContext.Current;

         string name = args[0] ?? "";

         if (!context.Profiles.Exists(name))
         {
            return IpcResponse.Error(Constants.Error.NotFound, $"Profile '{name}' not found.").RawJson;
         }

         context.Profiles.Switch(name);
         ApplyProfileState(context);
         return IpcResponse.Success($"Switched to profile '{name}'.").RawJson;
      }

   #endregion

   #region Enable

      // ------------------------------------------------------------
      /// <summary>
      /// Enables a profile without affecting others.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("profile", "enable", description = "Enable profile")]
      public static string Enable(CommandArgs args)
      {
         var context = DebugContext.Current;

         string name = args[0] ?? "";

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

   #endregion

   #region Disable

      // ------------------------------------------------------------
      /// <summary>
      /// Disables a profile or all profiles.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("profile", "disable", description = "Disable profile")]
      public static string Disable(CommandArgs args)
      {
         var context = DebugContext.Current;

         if (args.Has("all"))
         {
            context.Profiles.DisableAll();
            ApplyProfileState(context);
            return IpcResponse.Success("Disabled all profiles.").RawJson;
         }

         string name = args[0] ?? "";

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

   #endregion

   #region List

      // ------------------------------------------------------------
      /// <summary>
      /// Lists all profiles with breakpoint and catchpoint counts.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("profile", "list", description = "List profiles")]
      public static string ListAll(CommandArgs args)
      {
         var context = DebugContext.Current;

         var list = context.Profiles.List().Select(p =>
         {
            var dict = p.ToDict();
            dict["breakpoints"] = p.BreakPoints.Count;
            dict["catchpoints"] = p.CatchPoints.Count;
            return dict;
         }).ToList();

         return IpcResponse.Success(new Dictionary<string, object> { ["profiles"] = list }).RawJson;
      }

   #endregion

   #region Info

      // ------------------------------------------------------------
      /// <summary>
      /// Shows profile detail with its breakpoints and catchpoints.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("profile", "info", description = "Profile details")]
      public static string Info(CommandArgs args)
      {
         var context = DebugContext.Current;

         string name = args[0];

         if (string.IsNullOrEmpty(name))
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs, "Profile name required."
            ).RawJson;
         }

         var prof = context.Profiles.Get(name);

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
