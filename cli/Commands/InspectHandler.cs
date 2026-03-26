using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using InoIPC;

using InoCLI;

namespace MonoDebug.Commands
{
   // ============================================================
   /// <summary>
   /// Handles inspection commands: stack, thread, vars, eval.
   /// </summary>
   // ============================================================
   static class InspectHandler
   {

   #region Stack

      // ------------------------------------------------------------
      /// <summary>
      /// Handles stack-related commands including frame selection
      /// and stack trace retrieval.
      /// <br/> stack [--full] [--all]
      /// <br/> stack frame &lt;n&gt;
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("stack", description = "Stack trace and frame selection")]
      public static string HandleStack(CommandArgs args)
      {
         var context = DebugContext.Current;

         if (!context.Session.IsSuspended)
         {
            return IpcResponse.Error
            (
               Constants.Error.NotStopped,
               "VM is running. Use: flow pause"
            ).RawJson;
         }

         string command = args[0] ?? "";

         // stack frame <n>
         if (command == "frame")
         {
            if (!int.TryParse(args[1], out int n))
            {
               return IpcResponse.Error
               (
                  Constants.Error.InvalidArgs,
                  "Frame index required."
               ).RawJson;
            }

            context.CurrentFrame = n;
            return IpcResponse.Success("frame", context.CurrentFrame).RawJson;
         }

         bool full = args.Has("full");
         bool all  = args.Has("all");

         // stack --all : all threads
         if (all)
         {
            var threads = context.Session.GetThreads();
            var stacks  = new List<object>();

            foreach (var t in threads)
            {
               long tid = Convert.ToInt64(t["id"]);

               stacks.Add(new Dictionary<string, object>
               {
                  ["thread"] = t,
                  ["frames"] = context.Session.GetStackFrames(tid, full)
               });
            }

            return IpcResponse.Success("stacks", stacks).RawJson;
         }

         // stack [--full] : current thread
         var frames = context.Session.GetStackFrames(context.CurrentThreadId, full);

         return IpcResponse.Success(new Dictionary<string, object>
         {
            ["thread"] = context.CurrentThreadId,
            ["frames"] = frames
         }).RawJson;
      }

   #endregion

   #region Thread

      // ------------------------------------------------------------
      /// <summary>
      /// Handles thread-related commands including listing and
      /// switching threads.
      /// <br/> thread list
      /// <br/> thread &lt;id&gt;
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("thread", description = "Thread listing and switching")]
      public static string HandleThread(CommandArgs args)
      {
         var context = DebugContext.Current;

         string command = args[0] ?? "";

         // thread list
         if (command == "list")
         {
            return IpcResponse.Success("threads", context.Session.GetThreads()).RawJson;
         }

         // thread <id> : switch thread
         if (!long.TryParse(command, out long tid))
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs,
               "Thread ID required."
            ).RawJson;
         }

         context.CurrentThreadId = tid;
         context.CurrentFrame    = 0;

         return IpcResponse.Success("thread", context.CurrentThreadId).RawJson;
      }

   #endregion

   #region Variables

      // -----------------------------------------------------------------------
      /// <summary>
      /// Handles variable inspection commands.
      /// <br/> vars [--args] [--locals] [--depth N] [--static '&lt;type&gt;']
      /// <br/> vars set &lt;var&gt; &lt;value&gt; [--static]
      /// </summary>
      // -----------------------------------------------------------------------
      [CLICommand("vars", description = "Variable inspection and modification")]
      public static string HandleVars(CommandArgs args)
      {
         var context = DebugContext.Current;

         if (!context.Session.IsSuspended)
         {
            return IpcResponse.Error
            (
               Constants.Error.NotStopped,
               "VM is running. Use: flow pause"
            ).RawJson;
         }

         string command = args[0] ?? "";

         // vars set <var> <value>
         if (command == "set")
         {
            if (args.Count < 3)
            {
               return IpcResponse.Error
               (
                  Constants.Error.InvalidArgs,
                  "Usage: vars set <name> <value>"
               ).RawJson;
            }

            string name  = args[1];
            string value = args[2];

            if (!context.Session.SetVariable
            (
               context.CurrentThreadId, context.CurrentFrame,
               name, value
            ))
            {
               return IpcResponse.Error
               (
                  Constants.Error.EvalError,
                  $"Cannot set '{name}' to '{value}'."
               ).RawJson;
            }

            return IpcResponse.Success
            (
               $"Set {name} = {value}."
            ).RawJson;
         }

         // --static '<type>' : static field inspection
         if (args.Has("static"))
         {
            string typeName = args.Get("static");

            if (string.IsNullOrEmpty(typeName))
            {
               return IpcResponse.Error
               (
                  Constants.Error.InvalidArgs,
                  "Type name required. Use: vars --static '<type>'"
               ).RawJson;
            }

            var fields = context.Session.GetStaticFields(typeName);

            if (fields == null)
            {
               return IpcResponse.Error
               (
                  Constants.Error.InvalidArgs,
                  $"Type '{typeName}' not found."
               ).RawJson;
            }

            return IpcResponse.Success
            (
               new Dictionary<string, object>
               {
                  ["type"]   = typeName,
                  ["fields"] = fields
               }
            ).RawJson;
         }

         int depth = args.GetInt("depth", 1);

         var data = context.Session.GetFrameVariables
         (
            context.CurrentThreadId, context.CurrentFrame, depth
         );

         if (data == null)
         {
            return IpcResponse.Error
            (
               Constants.Error.NotStopped,
               "Cannot get variables."
            ).RawJson;
         }

         // Build result with separate this/args/locals
         var result = new Dictionary<string, object>
         {
            ["thread"] = context.CurrentThreadId,
            ["frame"]  = context.CurrentFrame
         };

         if (args.Has("args"))
         {
            result["args"] = data["args"];
         }
         else if (args.Has("locals"))
         {
            result["locals"] = data["locals"];
         }
         else
         {
            result["this"]   = data["this"];
            result["args"]   = data["args"];
            result["locals"] = data["locals"];
         }

         return IpcResponse.Success(result).RawJson;
      }

   #endregion

   #region Eval

      // ------------------------------------------------------------
      /// <summary>
      /// Handles expression evaluation in the current debugging
      /// context. Supports full C# expressions via Roslyn.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("eval", description = "Expression evaluation")]
      public static string HandleEval(CommandArgs args)
      {
         var context = DebugContext.Current;

         if (!context.Session.IsSuspended)
         {
            return IpcResponse.Error
            (
               Constants.Error.NotStopped,
               "VM is running. Use: flow pause"
            ).RawJson;
         }

         string expression = string.Join(" ", args.Positionals).Trim();

         if (string.IsNullOrEmpty(expression))
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs,
               "Expression required. Use: eval '<expr>'"
            ).RawJson;
         }

         var value = context.Session.Evaluate
         (
            context.CurrentThreadId, context.CurrentFrame, expression
         );

         if (value == null)
         {
            return IpcResponse.Error
            (
               Constants.Error.EvalError,
               $"Cannot evaluate: {expression}"
            ).RawJson;
         }

         return IpcResponse.Success(new Dictionary<string, object>
         {
            ["expression"] = expression,
            ["value"]      = value
         }).RawJson;
      }

   #endregion

   }
}
