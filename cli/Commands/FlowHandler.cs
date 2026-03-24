using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

using Mono.Debugger.Soft;

using InoIPC;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Handles flow control commands: wait, continue, next, step,
   /// out, until, pause, goto.
   /// </summary>
   // ============================================================
   static class FlowHandler
   {

   #region Dispatch

      // ------------------------------------------------------------
      /// <summary>
      /// Dispatches a flow control command and returns a JSON result.
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
            case "wait":
            {
               return HandleWait(context, optionals);
            }

            case "continue":
            {
               return HandleContinue(context);
            }

            case "next":
            case "step":
            case "out":
            {
               return HandleStep(context, command, optionals);
            }

            case "until":
            {
               return HandleUntil(context, rest);
            }

            case "goto":
            {
               return HandleGoto(context, rest);
            }

            case "pause":
            {
               return HandlePause(context);
            }

            default:
            {
               return IpcResponse.Error
               (
                  Constants.Error.InvalidArgs,
                  $"Unknown flow command: {command}"
               ).RawJson;
            }
         }
      }

   #endregion

   #region Wait

      // ------------------------------------------------------------
      /// <summary>
      /// Waits for the next debug event with a timeout.
      /// On disconnect, attempts to reconnect and restore breakpoints.
      /// </summary>
      // ------------------------------------------------------------
      private static string HandleWait
      (
         DebugContext context,
         Dictionary<string, JsonElement> optionals
      )
      {
         int timeout = optionals.GetInt("timeout", 30000);

         var evt = context.Session.WaitForEvent(timeout);

         if (evt == null)
         {
            return IpcResponse.Error
            (
               Constants.Error.NoSession, "Not connected."
            ).RawJson;
         }

         string reason = evt.ContainsKey("reason")
            ? evt["reason"]?.ToString() : "";

         // Domain reload detection (disconnect with dead VM)
         if ((reason == "disconnected" || reason == "timeout")
            && !context.Session.IsConnected)
         {
            return HandleDomainReload(context);
         }

         // Timeout with live connection — just return
         if (reason == "timeout")
         {
            evt["success"] = true;
            return JsonSerializer.Serialize(evt);
         }

         // Update context with stopped thread
         if (evt.ContainsKey("thread") && evt["thread"] is long tid)
         {
            context.CurrentThreadId = tid;
            context.CurrentFrame    = 0;
         }

         evt["success"] = true;
         return JsonSerializer.Serialize(evt);
      }

   #endregion

   #region Continue

      // ------------------------------------------------------------
      /// <summary>
      /// Resumes VM execution.
      /// </summary>
      // ------------------------------------------------------------
      private static string HandleContinue(DebugContext context)
      {
         context.Session.Resume();
         return IpcResponse.Success("Resumed.").RawJson;
      }

   #endregion

   #region Step

      // ------------------------------------------------------------
      /// <summary>
      /// Performs a step operation (next, step, out) with optional
      /// --count for repeated stepping.
      /// </summary>
      // ------------------------------------------------------------
      private static string HandleStep
      (
         DebugContext context, string command,
         Dictionary<string, JsonElement> optionals
      )
      {
         if (!context.Session.IsSuspended)
         {
            return IpcResponse.Error
            (
               Constants.Error.NotStopped, "VM is running."
            ).RawJson;
         }

         var thread = context.Session.StoppedThread;

         if (thread == null)
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs, "No active thread."
            ).RawJson;
         }

         var depth = command switch
         {
            "next" => StepDepth.Over,
            "step" => StepDepth.Into,
            "out"  => StepDepth.Out,
            _      => StepDepth.Over
         };

         int count = optionals.GetInt("count", 1);

         if (count < 1)
         {
            count = 1;
         }

         // Execute repeated steps: step + wait for each iteration
         for (int i = 0; i < count; i++)
         {
            context.Session.Step(thread, depth);

            // For multi-step, wait for each intermediate step to complete
            if (i < count - 1)
            {
               var evt = context.Session.WaitForEvent(10000);

               if (evt == null || !context.Session.IsSuspended)
               {
                  break;
               }

               // Refresh thread reference after stop
               thread = context.Session.StoppedThread ?? thread;
            }
         }

         return IpcResponse.Success($"Step {command}.").RawJson;
      }

   #endregion

   #region Until

      // ------------------------------------------------------------
      /// <summary>
      /// Sets a temporary breakpoint at the target line and resumes.
      /// Args: [file] line.
      /// </summary>
      // ------------------------------------------------------------
      private static string HandleUntil
      (
         DebugContext context, List<string> args
      )
      {
         if (!context.Session.IsSuspended)
         {
            return IpcResponse.Error
            (
               Constants.Error.NotStopped, "VM is running."
            ).RawJson;
         }

         string file = "";
         int    line = 0;

         if (args.Count == 1)
         {
            int.TryParse(args[0], out line);
         }
         else if (args.Count >= 2)
         {
            file = args[0];
            int.TryParse(args[1], out line);
         }

         if (line <= 0)
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs, "Line number required."
            ).RawJson;
         }

         var profile = context.Profiles.Get("default");

         if (profile == null)
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs, "Default profile not found."
            ).RawJson;
         }

         var bp = profile.SetBreak
         (
            context.Session, context.Profiles.NextId(),
            file, line, isTemp: true
         );

         if (bp == null)
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs,
               $"Cannot set breakpoint at {file}:{line}"
            ).RawJson;
         }

         context.Session.Resume();
         return IpcResponse.Success($"Running to {file}:{line}.").RawJson;
      }

   #endregion

   #region Goto

      // ------------------------------------------------------------
      /// <summary>
      /// Sets the instruction pointer on the current thread.
      /// </summary>
      // ------------------------------------------------------------
      private static string HandleGoto
      (
         DebugContext context, List<string> args
      )
      {
         if (!context.Session.IsSuspended)
         {
            return IpcResponse.Error
            (
               Constants.Error.NotStopped, "VM is running."
            ).RawJson;
         }

         string file = "";
         int    line = 0;

         if (args.Count == 1)
         {
            int.TryParse(args[0], out line);
         }
         else if (args.Count >= 2)
         {
            file = args[0];
            int.TryParse(args[1], out line);
         }

         if (line <= 0)
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs,
               "Usage: flow goto [file] <line>"
            ).RawJson;
         }

         if (!context.Session.SetIP(file, line))
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs,
               $"Cannot set IP to {file}:{line}"
            ).RawJson;
         }

         return IpcResponse.Success
         (
            $"Set IP to {file}:{line}."
         ).RawJson;
      }

   #endregion

   #region Pause

      // ------------------------------------------------------------
      /// <summary>
      /// Suspends VM execution.
      /// </summary>
      // ------------------------------------------------------------
      private static string HandlePause(DebugContext context)
      {
         context.Session.Suspend();
         return IpcResponse.Success("Suspended.").RawJson;
      }

   #endregion

   #region Domain Reload

      // ------------------------------------------------------------
      /// <summary>
      /// Handles domain reload by reconnecting and restoring
      /// breakpoints on the new VM connection.
      /// </summary>
      // ------------------------------------------------------------
      private static string HandleDomainReload(DebugContext context)
      {
         Console.Error.WriteLine("Domain reload detected. Reconnecting...");

         if (context.Session.Reconnect())
         {
            Console.Error.WriteLine
            (
               $"Reconnected on port {context.Session.Port}."
            );

            context.Profiles.RebuildAll(context.Session);

            return IpcResponse.Success(new Dictionary<string, object>
            {
               ["reason"] = "domain_reload",
               ["port"]   = context.Session.Port
            }).RawJson;
         }

         return IpcResponse.Error
         (
            Constants.Error.ConnectFailed,
            "Failed to reconnect after domain reload."
         ).RawJson;
      }

   #endregion

   }
}
