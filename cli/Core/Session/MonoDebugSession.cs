using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Mono.Debugger.Soft;
using Mono.Debugging.Client;
using Mono.Debugging.Soft;
using Mono.Debugging.Evaluation;

using SdbStackFrame = Mono.Debugger.Soft.StackFrame;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Mono SDB session built on SoftDebuggerSession.
   /// Overrides OnContinue/HandleException to support Unity's
   /// suspend=n mode. Provides synchronous WaitForEvent and
   /// full C# expression evaluation via Roslyn.
   /// </summary>
   // ============================================================
   class MonoDebugSession : SoftDebuggerSession
   {

   #region Fields

      private readonly BlockingCollection<TargetEventArgs> eventQueue
         = new BlockingCollection<TargetEventArgs>();

      private readonly ManualResetEventSlim readyEvent
         = new ManualResetEventSlim(false);

      private ObjectMirror lastException;

   #endregion

   #region Properties

      // ------------------------------------------------------------
      /// <summary>
      /// The underlying SDB virtual machine instance.
      /// </summary>
      // ------------------------------------------------------------
      public VirtualMachine VM => VirtualMachine;

      // ------------------------------------------------------------
      /// <summary>
      /// Whether the VM is currently suspended.
      /// </summary>
      // ------------------------------------------------------------
      public bool IsSuspended { get; private set; }

      // ------------------------------------------------------------
      /// <summary>
      /// The thread that triggered the last stop event.
      /// </summary>
      // ------------------------------------------------------------
      public ThreadMirror StoppedThread { get; private set; }

      // ------------------------------------------------------------
      /// <summary>
      /// The port used for SDB connection.
      /// </summary>
      // ------------------------------------------------------------
      public int Port { get; private set; }

      // ------------------------------------------------------------
      /// <summary>
      /// The host used for SDB connection.
      /// </summary>
      // ------------------------------------------------------------
      public string Host { get; private set; } = "localhost";

   #endregion

   #region Constructor

      // ------------------------------------------------------------
      /// <summary>
      /// Creates a new MonoDebugSession and subscribes to events.
      /// </summary>
      // ------------------------------------------------------------
      public MonoDebugSession()
      {
         // Enqueue relevant events for WaitForEvent
         TargetReady              += OnEnqueue;
         TargetHitBreakpoint      += OnEnqueue;
         TargetStopped            += OnEnqueue;
         TargetInterrupted        += OnEnqueue;
         TargetExceptionThrown    += OnEnqueue;
         TargetUnhandledException += OnEnqueue;

         // Connection ready signal
         TargetReady += OnTargetReady;

         // Track suspended state
         TargetHitBreakpoint      += OnStopped;
         TargetStopped            += OnStopped;
         TargetInterrupted        += OnStopped;
         TargetExceptionThrown    += OnException;
         TargetUnhandledException += OnException;
      }

   #endregion

   #region Connection

      // ------------------------------------------------------------
      /// <summary>
      /// Connects to a Mono SDB debugger at the given port and
      /// host. Blocks until connected or timeout.
      /// </summary>
      // ------------------------------------------------------------
      public bool Connect(int port, string host = "localhost")
      {
         try
         {
            Port = port;
            Host = host;

            var address = IPAddress.Parse
            (
               host == "localhost" ? "127.0.0.1" : host
            );

            var connectArgs = new SoftDebuggerConnectArgs
            (
               "monodebug", address, port
            );

            connectArgs.MaxConnectionAttempts = 3;
            connectArgs.TimeBetweenConnectionAttempts = 1000;

            var startInfo = new SoftDebuggerStartInfo(connectArgs);
            var options   = new DebuggerSessionOptions();

            options.EvaluationOptions
               = EvaluationOptions.DefaultOptions.Clone();

            options.EvaluationOptions.AllowTargetInvoke = true;
            options.EvaluationOptions.AllowMethodEvaluation = true;
            options.EvaluationOptions.EvaluationTimeout = 5000;

            readyEvent.Reset();

            Run(startInfo, options);

            return readyEvent.Wait(Constants.ConnectTimeout);
         }
         catch
         {
            return false;
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Disconnects from the VM without terminating the debuggee.
      /// </summary>
      // ------------------------------------------------------------
      public void Disconnect()
      {
         try
         {
            if (VirtualMachine != null)
            {
               VirtualMachine.Disconnect();
            }
         }
         catch { }

         IsSuspended   = false;
         StoppedThread = null;
         lastException = null;
      }

   #endregion

   #region SoftDebuggerSession Overrides

      // ------------------------------------------------------------
      /// <summary>
      /// Override OnContinue to handle Unity's suspend=n mode.
      /// Catches NOT_SUSPENDED errors when VM is already running.
      /// </summary>
      // ------------------------------------------------------------
      protected override void OnContinue()
      {
         ThreadPool.QueueUserWorkItem(delegate
         {
            try
            {
               Adaptor.CancelAsyncOperations();
               OnResumed();
               VirtualMachine.Resume();
            }
            catch (Mono.Debugger.Soft.CommandException)
            {
               // NOT_SUSPENDED — VM already running (suspend=n)
            }
            catch (Exception ex)
            {
               if (!HandleException(ex))
               {
                  OnDebuggerOutput(true, ex.ToString());
               }
            }
         });
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Override HandleException to be more lenient with
      /// connection errors that may occur with suspend=n.
      /// </summary>
      // ------------------------------------------------------------
      protected override bool HandleException(Exception ex)
      {
         if (ex is Mono.Debugger.Soft.CommandException)
         {
            // Swallow SDB command errors (NOT_SUSPENDED, etc.)
            return true;
         }

         return base.HandleException(ex);
      }

   #endregion

   #region Event Handlers

      // ------------------------------------------------------------
      /// <summary>
      /// Called when the session is ready (VM connected).
      /// </summary>
      // ------------------------------------------------------------
      private void OnTargetReady(object sender, TargetEventArgs e)
      {
         readyEvent.Set();
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Enqueues relevant events for WaitForEvent.
      /// </summary>
      // ------------------------------------------------------------
      private void OnEnqueue(object sender, TargetEventArgs e)
      {
         eventQueue.Add(e);
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Handles stop events (breakpoint, step, pause).
      /// </summary>
      // ------------------------------------------------------------
      private void OnStopped(object sender, TargetEventArgs e)
      {
         IsSuspended = true;

         long tid = 0;

         if (e.Thread != null)
         {
            tid = e.Thread.Id;
         }
         else if (ActiveThread != null)
         {
            tid = ActiveThread.Id;
         }

         if (tid > 0 && VirtualMachine != null)
         {
            StoppedThread = ResolveThread(tid);
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Handles exception events.
      /// </summary>
      // ------------------------------------------------------------
      private void OnException(object sender, TargetEventArgs e)
      {
         IsSuspended = true;

         if (e.Thread != null && VirtualMachine != null)
         {
            StoppedThread = ResolveThread(e.Thread.Id);

            try
            {
               var frames = StoppedThread?.GetFrames();

               if (frames != null && frames.Length > 0)
               {
                  if (frames[0].GetThis() is ObjectMirror obj)
                  {
                     lastException = obj;
                  }
               }
            }
            catch { }
         }
      }

   #endregion

   #region WaitForEvent

      // ------------------------------------------------------------
      /// <summary>
      /// Waits for the next debug event with a timeout.
      /// </summary>
      // ------------------------------------------------------------
      public Dictionary<string, object> WaitForEvent
      (
         int timeoutMs = 30000
      )
      {
         if (VirtualMachine == null && !IsConnected)
         {
            return null;
         }

         if (eventQueue.TryTake(out var args, timeoutMs))
         {
            return EventToDict(args);
         }

         if (!IsConnected)
         {
            return new Dictionary<string, object>
            {
               ["reason"] = "disconnected"
            };
         }

         return new Dictionary<string, object>
         {
            ["reason"] = "timeout"
         };
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Converts a TargetEventArgs to a dictionary.
      /// </summary>
      // ------------------------------------------------------------
      private Dictionary<string, object> EventToDict
      (
         TargetEventArgs args
      )
      {
         var dict = new Dictionary<string, object>();

         switch (args.Type)
         {
            case TargetEventType.TargetReady:
               dict["reason"] = "vmstart";
               break;

            case TargetEventType.TargetHitBreakpoint:
               dict["reason"] = "breakpoint";
               FillStopEvent(dict, args);
               break;

            case TargetEventType.TargetStopped:
               dict["reason"] = "step";
               FillStopEvent(dict, args);
               break;

            case TargetEventType.TargetInterrupted:
               dict["reason"] = "pause";
               FillStopEvent(dict, args);
               break;

            case TargetEventType.ExceptionThrown:
            case TargetEventType.UnhandledException:
               dict["reason"] = "exception";
               FillStopEvent(dict, args);

               if (lastException != null)
               {
                  dict["exception"] = ExceptionHelper.GetMessage
                  (
                     lastException
                  );
               }
               break;

            case TargetEventType.TargetExited:
               dict["reason"] = "disconnected";
               break;

            default:
               dict["reason"] = args.Type.ToString();
               break;
         }

         return dict;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Fills stop event details (thread, method, file, line).
      /// </summary>
      // ------------------------------------------------------------
      private void FillStopEvent
      (
         Dictionary<string, object> dict,
         TargetEventArgs args
      )
      {
         if (args.Thread != null)
         {
            dict["thread"] = args.Thread.Id;
         }

         if (StoppedThread == null || VirtualMachine == null)
         {
            return;
         }

         try
         {
            var frames = StoppedThread.GetFrames();

            if (frames.Length > 0)
            {
               var frame = frames[0];

               dict["method"] = frame.Method.FullName;

               if (frame.Location != null)
               {
                  dict["file"] = frame.Location.SourceFile;
                  dict["line"] = frame.Location.LineNumber;
               }
            }
         }
         catch { }
      }

   #endregion

   #region Flow Control

      // ------------------------------------------------------------
      /// <summary>
      /// Resumes VM execution.
      /// </summary>
      // ------------------------------------------------------------
      public void Resume()
      {
         IsSuspended   = false;
         StoppedThread = null;

         try
         {
            Continue();
         }
         catch { }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Suspends VM execution.
      /// </summary>
      // ------------------------------------------------------------
      public new void Suspend()
      {
         if (VirtualMachine == null)
         {
            return;
         }

         try
         {
            // Directly suspend VM and find a thread with user frames
            VirtualMachine.Suspend();
            IsSuspended = true;

            foreach (var t in VirtualMachine.GetThreads())
            {
               try
               {
                  var frames = t.GetFrames();

                  if (frames.Length > 0
                     && frames[0].Location != null
                     && frames[0].Location.SourceFile != null)
                  {
                     StoppedThread = t;
                     return;
                  }
               }
               catch { }
            }

            // No user frame found — pick first thread with any frames
            foreach (var t in VirtualMachine.GetThreads())
            {
               try
               {
                  if (t.GetFrames().Length > 0)
                  {
                     StoppedThread = t;
                     return;
                  }
               }
               catch { }
            }
         }
         catch { }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Steps in the given direction on the stopped thread.
      /// </summary>
      // ------------------------------------------------------------
      public void Step(ThreadMirror thread, StepDepth depth)
      {
         IsSuspended = false;

         try
         {
            switch (depth)
            {
               case StepDepth.Over:
                  NextLine();
                  break;

               case StepDepth.Into:
                  StepLine();
                  break;

               case StepDepth.Out:
                  Finish();
                  break;
            }
         }
         catch { }
      }

   #endregion

   #region Set IP

      // ------------------------------------------------------------
      /// <summary>
      /// Sets the instruction pointer to file:line on the stopped
      /// thread.
      /// </summary>
      // ------------------------------------------------------------
      public bool SetIP(string file, int line)
      {
         if (StoppedThread == null || VirtualMachine == null)
         {
            return false;
         }

         var location = DebugProfile.ResolveLocation
         (
            VirtualMachine, file, line
         );

         if (location == null)
         {
            return false;
         }

         try
         {
            StoppedThread.SetIP(location);
            return true;
         }
         catch
         {
            return false;
         }
      }

   #endregion

   #region Static Fields

      // ------------------------------------------------------------
      /// <summary>
      /// Gets static fields of a type by name.
      /// </summary>
      // ------------------------------------------------------------
      public Dictionary<string, object> GetStaticFields
      (
         string typeName
      )
      {
         if (VirtualMachine == null)
         {
            return null;
         }

         try
         {
            var types = VirtualMachine.GetTypes(typeName, false);

            if (types.Count == 0)
            {
               types = VirtualMachine.GetTypes(typeName, true);
            }

            if (types.Count == 0)
            {
               return null;
            }

            var type   = types[0];
            var result = new Dictionary<string, object>();

            foreach (var field in type.GetFields())
            {
               if (!field.IsStatic)
               {
                  continue;
               }

               try
               {
                  var val = type.GetValue(field);
                  result[field.Name] = ValueFormatter.Format(val, 1);
               }
               catch
               {
                  result[field.Name] = "(error)";
               }
            }

            return result;
         }
         catch
         {
            return null;
         }
      }

   #endregion

   #region Set Variable

      // ------------------------------------------------------------
      /// <summary>
      /// Sets a local variable or field value in the current frame.
      /// </summary>
      // ------------------------------------------------------------
      public bool SetVariable
      (
         long threadId, int frameIndex,
         string name, string value
      )
      {
         try
         {
            var thread = ResolveThread(threadId);

            if (thread == null)
            {
               return false;
            }

            var frames = thread.GetFrames();

            if (frameIndex >= frames.Length)
            {
               return false;
            }

            var frame  = frames[frameIndex];
            var method = frame.Method;

            // Try locals
            foreach (var local in method.GetLocals())
            {
               if (local.Name == name)
               {
                  var val = ParseValue
                  (
                     VirtualMachine, local.Type, value
                  );

                  if (val != null)
                  {
                     frame.SetValue(local, val);
                     return true;
                  }

                  return false;
               }
            }

            // Try parameters
            var parameters = method.GetParameters();

            for (int i = 0; i < parameters.Length; i++)
            {
               if (parameters[i].Name == name)
               {
                  var val = ParseValue
                  (
                     VirtualMachine,
                     parameters[i].ParameterType, value
                  );

                  if (val != null)
                  {
                     frame.SetValue(parameters[i], val);
                     return true;
                  }

                  return false;
               }
            }

            // Try this fields
            var thisObj = frame.GetThis() as ObjectMirror;

            if (thisObj != null)
            {
               var field = thisObj.Type.GetField(name);

               if (field != null)
               {
                  var val = ParseValue
                  (
                     VirtualMachine, field.FieldType, value,
                     thisObj.Domain
                  );

                  if (val != null)
                  {
                     thisObj.SetValue(field, val);
                     return true;
                  }
               }
            }

            return false;
         }
         catch (Exception ex)
         {
            Console.Error.WriteLine
            (
               $"SetVariable error: {ex.Message}"
            );

            return false;
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Parses a string value into a SDB Value based on type.
      /// </summary>
      // ------------------------------------------------------------
      private static Value ParseValue
      (
         VirtualMachine vm, TypeMirror type, string value,
         AppDomainMirror context = null
      )
      {
         try
         {
            string typeName = type.FullName;

            if (typeName == "System.Int32"
               && int.TryParse(value, out int i))
            {
               return vm.CreateValue(i);
            }

            if (typeName == "System.Single"
               && float.TryParse(value, out float f))
            {
               return vm.CreateValue(f);
            }

            if (typeName == "System.Double"
               && double.TryParse(value, out double d))
            {
               return vm.CreateValue(d);
            }

            if (typeName == "System.Boolean")
            {
               if (bool.TryParse(value, out bool b))
               {
                  return vm.CreateValue(b);
               }
            }

            if (typeName == "System.Int64"
               && long.TryParse(value, out long l))
            {
               return vm.CreateValue(l);
            }

            if (typeName == "System.String")
            {
               if (context != null)
               {
                  return context.CreateString(value);
               }

               return vm.RootDomain.CreateString(value);
            }
         }
         catch (Exception ex)
         {
            Console.Error.WriteLine
            (
               $"ParseValue error: {ex.Message}"
            );
         }

         return null;
      }

   #endregion

   #region Exception Info

      // ----------------------------------------------------------------------
      /// <summary>
      /// Gets exception details from the last caught exception.
      /// </summary>
      // ----------------------------------------------------------------------
      public Dictionary<string, object> GetExceptionInfo
      (
         bool includeStack = false, int innerDepth = 0
      )
      {
         if (StoppedThread == null || lastException == null)
         {
            return null;
         }

         try
         {
            return ExceptionHelper.GetInfo
            (
               lastException, includeStack, innerDepth
            );
         }
         catch
         {
            return null;
         }
      }

   #endregion

   #region Expression Evaluation

      // ----------------------------------------------------------------------
      /// <summary>
      /// <br/> Evaluates a C# expression in the context of the
      /// <br/> given thread and frame. Uses Roslyn-based evaluator
      /// <br/> via SoftDebuggerSession, falls back to simple
      /// <br/> name resolution on failure.
      /// </summary>
      // ----------------------------------------------------------------------
      public object Evaluate
      (
         long threadId, int frameIndex, string expression
      )
      {
         var sdbFrame = GetFrame(threadId, frameIndex);

         if (sdbFrame == null)
         {
            return null;
         }

         try
         {
            var evalOptions
               = EvaluationOptions.DefaultOptions.Clone();

            evalOptions.AllowTargetInvoke = true;
            evalOptions.AllowMethodEvaluation = true;
            evalOptions.EvaluationTimeout = 5000;

            var ctx = new SoftEvaluationContext
            (
               this, sdbFrame, evalOptions
            );

            var result = ctx.Evaluator.Evaluate(ctx, expression);

            if (result is ValueReference valRef)
            {
               var objVal = valRef.CreateObjectValue(false);
               return FormatObjectValue(objVal);
            }

            return result;
         }
         catch
         {
            return null;
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Formats an ObjectValue to a JSON-friendly object.
      /// </summary>
      // ------------------------------------------------------------
      private static object FormatObjectValue(ObjectValue objVal)
      {
         if (objVal == null)
         {
            return null;
         }

         if (objVal.HasFlag(ObjectValueFlags.Primitive))
         {
            return objVal.Value;
         }

         return objVal.DisplayValue ?? objVal.Value;
      }

   #endregion

   #region Stack / Thread / Variables

      // ------------------------------------------------------------
      /// <summary>
      /// Gets stack frames for a thread.
      /// </summary>
      // ------------------------------------------------------------
      public List<Dictionary<string, object>> GetStackFrames
      (
         long threadId, bool full = false
      )
      {
         var result = new List<Dictionary<string, object>>();
         var thread = ResolveThread(threadId);

         if (thread == null)
         {
            return result;
         }

         try
         {
            var frames = thread.GetFrames();

            for (int i = 0; i < frames.Length; i++)
            {
               var frame = frames[i];
               var dict  = new Dictionary<string, object>
               {
                  ["index"]  = i,
                  ["method"] = frame.Method.FullName
               };

               if (frame.Location != null)
               {
                  dict["file"] = frame.Location.SourceFile;
                  dict["line"] = frame.Location.LineNumber;
               }

               if (full)
               {
                  dict["this"]   = StackInspector.GetThisValue
                  (
                     frame, 1
                  );

                  dict["args"]   = StackInspector.GetArgValues
                  (
                     frame, 1
                  );

                  dict["locals"] = StackInspector.GetLocalValues
                  (
                     frame, 1
                  );
               }

               result.Add(dict);
            }
         }
         catch { }

         return result;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Gets variable info for a specific frame.
      /// </summary>
      // ------------------------------------------------------------
      public Dictionary<string, object> GetFrameVariables
      (
         long threadId, int frameIndex, int depth = 1
      )
      {
         var frame = GetFrame(threadId, frameIndex);

         if (frame == null)
         {
            return null;
         }

         try
         {
            return new Dictionary<string, object>
            {
               ["this"]   = StackInspector.GetThisValue
               (
                  frame, Math.Max(depth, 1)
               ),

               ["args"]   = StackInspector.GetArgValues
               (
                  frame, depth
               ),

               ["locals"] = StackInspector.GetLocalValues
               (
                  frame, depth
               )
            };
         }
         catch
         {
            return null;
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Gets all threads as a list of dictionaries.
      /// </summary>
      // ------------------------------------------------------------
      public List<Dictionary<string, object>> GetThreads()
      {
         var result = new List<Dictionary<string, object>>();

         if (VirtualMachine == null)
         {
            return result;
         }

         try
         {
            foreach (var thread in VirtualMachine.GetThreads())
            {
               result.Add(new Dictionary<string, object>
               {
                  ["id"]    = thread.Id,
                  ["name"]  = thread.Name ?? "",
                  ["state"] = thread.ThreadState.ToString()
               });
            }
         }
         catch { }

         return result;
      }

   #endregion

   #region Helpers

      // ------------------------------------------------------------
      /// <summary>
      /// Gets a raw SDB StackFrame for the given thread/frame.
      /// </summary>
      // ------------------------------------------------------------
      private SdbStackFrame GetFrame(long threadId, int frameIndex)
      {
         var thread = ResolveThread(threadId);

         if (thread == null)
         {
            return null;
         }

         try
         {
            var frames = thread.GetFrames();

            if (frameIndex < frames.Length)
            {
               return frames[frameIndex];
            }
         }
         catch { }

         return null;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Resolves thread by ID.
      /// </summary>
      // ------------------------------------------------------------
      private ThreadMirror ResolveThread(long threadId)
      {
         if (StoppedThread != null && StoppedThread.Id == threadId)
         {
            return StoppedThread;
         }

         if (VirtualMachine == null)
         {
            return null;
         }

         try
         {
            foreach (var thread in VirtualMachine.GetThreads())
            {
               if (thread.Id == threadId)
               {
                  return thread;
               }
            }
         }
         catch { }

         return null;
      }

   #endregion

   }
}
