using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;

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

         // Process breakpoint hit
         if (reason == "breakpoint")
         {
            var hitBp = FindHitBreakpoint(context, evt);

            if (hitBp != null)
            {
               // ThreadFilter check
               if (hitBp.ThreadFilter > 0
                  && evt.ContainsKey("thread")
                  && evt["thread"] is long threadId
                  && threadId != hitBp.ThreadFilter)
               {
                  context.Session.Resume();
                  return HandleWait(context, optionals);
               }

               // Condition check
               if (!string.IsNullOrEmpty(hitBp.Condition))
               {
                  var condResult = context.Session.Evaluate
                  (
                     context.CurrentThreadId,
                     context.CurrentFrame,
                     hitBp.Condition
                  );

                  if (condResult == null
                     || condResult.ToString() == "false"
                     || condResult.ToString() == "False")
                  {
                     context.Session.Resume();
                     return HandleWait(context, optionals);
                  }
               }

               // ProcessHit (hits++, temp remove)
               foreach (var profile in context.Profiles.List())
               {
                  if (profile.Find(hitBp.Id) != null)
                  {
                     profile.ProcessHit(hitBp);
                     break;
                  }
               }
            }

            EvalBreakpointExpressions(context, evt, hitBp);
         }

         evt["success"] = true;
         return JsonSerializer.Serialize(evt);
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Finds the breakpoint that was hit by matching file:line.
      /// </summary>
      // ------------------------------------------------------------
      private static BreakPoint FindHitBreakpoint
      (
         DebugContext context,
         Dictionary<string, object> evt
      )
      {
         string file = evt.ContainsKey("file")
            ? evt["file"]?.ToString() : "";

         int line = evt.ContainsKey("line") && evt["line"] is int ln
            ? ln : 0;

         if (string.IsNullOrEmpty(file) || line <= 0)
         {
            return null;
         }

         foreach (var bp in context.Profiles.AllBreakPoints())
         {
            if (line == bp.Line
               && file.EndsWith
               (
                  bp.File,
                  System.StringComparison.OrdinalIgnoreCase
               ))
            {
               return bp;
            }
         }

         return null;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Evaluates EvalExpressions attached to the hit breakpoint
      /// and adds results to the event dictionary.
      /// </summary>
      // ------------------------------------------------------------
      private static void EvalBreakpointExpressions
      (
         DebugContext context,
         Dictionary<string, object> evt,
         BreakPoint hitBp
      )
      {
         if (hitBp == null
            || hitBp.EvalExpressions == null
            || hitBp.EvalExpressions.Count == 0)
         {
            return;
         }

         var results = new Dictionary<string, object>();

         foreach (var expr in hitBp.EvalExpressions)
         {
            var val = context.Session.Evaluate
            (
               context.CurrentThreadId,
               context.CurrentFrame,
               expr
            );

            results[expr] = val;
         }

         evt["eval"] = results;
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
      // ------------------------------------------------------------
      /// <summary>
      /// Parses [file] line from args.
      /// </summary>
      // ------------------------------------------------------------
      private static void ParseFileLine
      (
         List<string> args, out string file, out int line
      )
      {
         file = "";
         line = 0;

         if (args.Count == 1)
         {
            int.TryParse(args[0], out line);
         }
         else if (args.Count >= 2)
         {
            file = args[0];
            int.TryParse(args[1], out line);
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Sets a temporary breakpoint at the target line and resumes.
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

         ParseFileLine(args, out string file, out int line);

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

         ParseFileLine(args, out string file, out int line);

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

         if (context.Session.StoppedThread != null)
         {
            context.CurrentThreadId = context.Session.StoppedThread.Id;
            context.CurrentFrame    = 0;
         }

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

         int    port = context.Session.Port;
         string host = context.Session.Host;

         // Dispose old session — SoftDebuggerSession can't be reused
         try { context.Session.Disconnect(); } catch { }

         // Create new session and reconnect
         for (int i = 0; i < 30; i++)
         {
            Thread.Sleep(1000);

            var session = new MonoDebugSession();

            if (session.Connect(port, host))
            {
               context.Session = session;

               Console.Error.WriteLine
               (
                  $"Reconnected on port {port}."
               );

               context.Profiles.RebuildAll(session);

               return IpcResponse.Success(new Dictionary<string, object>
               {
                  ["reason"] = "domain_reload",
                  ["port"]   = port
               }).RawJson;
            }
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
