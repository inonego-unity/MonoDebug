using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Mono.Debugger.Soft;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Mono SDB session. Manages VM connection, events, stepping,
   /// expression evaluation, and stack inspection.
   /// </summary>
   // ============================================================
   class MonoDebugSession : IDisposable
   {

   #region Fields

      private VirtualMachine    vm;
      private StepEventRequest  activeStepRequest;
      private ObjectMirror      lastException;

   #endregion

   #region Properties

      // ------------------------------------------------------------
      /// <summary>
      /// The underlying SDB virtual machine instance.
      /// </summary>
      // ------------------------------------------------------------
      public VirtualMachine VM => vm;

      // ------------------------------------------------------------
      /// <summary>
      /// Whether the VM is currently connected.
      /// </summary>
      // ------------------------------------------------------------
      public bool IsConnected => vm != null;

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

   #region Connection

      // ------------------------------------------------------------
      /// <summary>
      /// Connects to a Mono SDB debugger at the given port and host.
      /// </summary>
      // ------------------------------------------------------------
      public bool Connect(int port, string host = "localhost")
      {
         try
         {
            var ep = new IPEndPoint
            (
               IPAddress.Parse(ResolveHost(host)), port
            );

            var task = Task.Run(() => VirtualMachineManager.Connect(ep));

            if (task.Wait(Constants.ConnectTimeout))
            {
               vm   = task.Result;
               Port = port;
               Host = host;

               SetupEventHandlers();

               return true;
            }
         }
         catch { }

         return false;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Disconnects from the VM and resets all session state.
      /// </summary>
      // ------------------------------------------------------------
      public void Disconnect()
      {
         if (vm == null)
         {
            return;
         }

         try { vm.Disconnect(); } catch { }

         vm                = null;
         IsSuspended       = false;
         StoppedThread     = null;
         activeStepRequest = null;
         lastException     = null;
      }

      // ----------------------------------------------------------------------
      /// <summary>
      /// <br/> Reconnects to the same host and port from the last
      /// <br/> successful Connect call. Retries up to maxRetries times.
      /// </summary>
      // ----------------------------------------------------------------------
      public bool Reconnect(int maxRetries = 30, int intervalMs = 1000)
      {
         int    savedPort = Port;
         string savedHost = Host;

         Disconnect();

         for (int i = 0; i < maxRetries; i++)
         {
            Thread.Sleep(intervalMs);

            if (Connect(savedPort, savedHost))
            {
               return true;
            }
         }

         return false;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Releases VM connection resources.
      /// </summary>
      // ------------------------------------------------------------
      public void Dispose()
      {
         Disconnect();
      }

   #endregion

   #region Event Handling

      // ------------------------------------------------------------
      /// <summary>
      /// Enables core VM lifecycle events.
      /// </summary>
      // ------------------------------------------------------------
      private void SetupEventHandlers()
      {
         vm.EnableEvents
         (
            EventType.VMStart,
            EventType.VMDeath,
            EventType.VMDisconnect
         );
      }

      // ----------------------------------------------------------------------
      /// <summary>
      /// <br/> Waits for the next debug event from the VM.
      /// <br/> Returns a serializable dictionary describing the event,
      /// <br/> or a timeout/disconnected reason.
      /// </summary>
      // ----------------------------------------------------------------------
      public Dictionary<string, object> WaitForEvent(int timeoutMs = 30000)
      {
         if (vm == null)
         {
            return null;
         }

         int actualTimeout = timeoutMs > 0 ? timeoutMs : 30000;

         try
         {
            var      task = Task.Run(() => vm.GetNextEventSet());
            EventSet es   = null;

            if (task.Wait(actualTimeout))
            {
               es = task.Result;
            }
            else if (task.IsFaulted
               && task.Exception?.InnerException is VMDisconnectedException)
            {
               return OnDisconnected();
            }

            if (es == null || es.Events.Length == 0)
            {
               return new Dictionary<string, object> { ["reason"] = "timeout" };
            }

            IsSuspended = true;

            return EventToDict(es.Events[0]);
         }
         catch (VMDisconnectedException)
         {
            return OnDisconnected();
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Converts a debug event to a serializable dictionary.
      /// </summary>
      // ------------------------------------------------------------
      private Dictionary<string, object> EventToDict(Event evt)
      {
         var dict = new Dictionary<string, object>
         {
            ["reason"] = evt.EventType.ToString().ToLowerInvariant()
         };

         if (evt is BreakpointEvent bpe)
         {
            FillStopEvent(dict, bpe.Thread, bpe.Method);
         }
         else if (evt is StepEvent se)
         {
            FillStopEvent(dict, se.Thread, se.Method);
            CancelStep();
         }
         else if (evt is ExceptionEvent ee)
         {
            StoppedThread     = ee.Thread;
            lastException     = ee.Exception;
            dict["thread"]    = ee.Thread.Id;
            dict["exception"] = ee.Exception?.Type?.FullName ?? "";

            dict["message"] = ee.Exception != null
               ? ExceptionHelper.GetMessage(ee.Exception)
               : "";

            FillLocation(dict, ee.Thread);
         }

         return dict;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Fills common stop-event fields for break/step events.
      /// </summary>
      // ------------------------------------------------------------
      private void FillStopEvent
      (
         Dictionary<string, object> dict,
         ThreadMirror thread, MethodMirror method
      )
      {
         StoppedThread  = thread;
         dict["thread"] = thread.Id;
         dict["method"] = method?.FullName ?? "";

         FillLocation(dict, thread);
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Resets state on VM disconnection.
      /// </summary>
      // ------------------------------------------------------------
      private Dictionary<string, object> OnDisconnected()
      {
         vm          = null;
         IsSuspended = false;

         return new Dictionary<string, object> { ["reason"] = "disconnected" };
      }

   #endregion

   #region Flow Control

      // ------------------------------------------------------------
      /// <summary>
      /// Resumes VM execution and cancels any active step request.
      /// </summary>
      // ------------------------------------------------------------
      public void Resume()
      {
         if (vm == null)
         {
            return;
         }

         CancelStep();

         vm.Resume();

         IsSuspended = false;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Suspends VM execution.
      /// </summary>
      // ------------------------------------------------------------
      public void Suspend()
      {
         if (vm == null)
         {
            return;
         }

         vm.Suspend();

         IsSuspended = true;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Creates a step request and resumes execution.
      /// </summary>
      // ------------------------------------------------------------
      public void Step(ThreadMirror thread, StepDepth depth)
      {
         if (vm == null || thread == null)
         {
            return;
         }

         CancelStep();

         var req   = vm.CreateStepRequest(thread);
         req.Depth = depth;
         req.Size  = StepSize.Line;
         req.Enable();

         activeStepRequest = req;

         vm.Resume();

         IsSuspended = false;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Cancels any active step request.
      /// </summary>
      // ------------------------------------------------------------
      public void CancelStep()
      {
         if (activeStepRequest == null)
         {
            return;
         }

         try { activeStepRequest.Disable(); } catch { }

         activeStepRequest = null;
      }

   #endregion

   #region Set IP

      // ------------------------------------------------------------
      /// <summary>
      /// Sets the instruction pointer to file:line on the stopped
      /// thread. Returns true on success.
      /// </summary>
      // ------------------------------------------------------------
      public bool SetIP(string file, int line)
      {
         if (StoppedThread == null || vm == null)
         {
            return false;
         }

         var location = DebugProfile.ResolveLocation(vm, file, line);

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
      public Dictionary<string, object> GetStaticFields(string typeName)
      {
         if (vm == null)
         {
            return null;
         }

         try
         {
            var types = vm.GetTypes(typeName, false);

            if (types.Count == 0)
            {
               // Try partial match
               types = vm.GetTypes(typeName, true);
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
      /// Supports primitives (int, float, string, bool).
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
                  var val = ParseValue(vm, local.Type, value);

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
                     vm, parameters[i].ParameterType, value
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
                     vm, field.FieldType, value,
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
            Console.Error.WriteLine($"SetVariable error: {ex.Message}");
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

            if (typeName == "System.Int32" && int.TryParse(value, out int i))
            {
               return vm.CreateValue(i);
            }

            if (typeName == "System.Single" && float.TryParse(value, out float f))
            {
               return vm.CreateValue(f);
            }

            if (typeName == "System.Double" && double.TryParse(value, out double d))
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

            if (typeName == "System.Int64" && long.TryParse(value, out long l))
            {
               return vm.CreateValue(l);
            }

            if (typeName == "System.String")
            {
               // Use target object's domain if available
               if (context != null)
               {
                  return context.CreateString(value);
               }

               return vm.RootDomain.CreateString(value);
            }
         }
         catch (Exception ex)
         {
            Console.Error.WriteLine($"ParseValue error: {ex.Message}");
         }

         return null;
      }

   #endregion

   #region Exception Info

      // ----------------------------------------------------------------------
      /// <summary>
      /// <br/> Gets exception details from the last caught exception.
      /// <br/> Optionally includes stack trace and inner exceptions.
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
            if (StoppedThread.GetFrames().Length == 0)
            {
               return null;
            }

            return ExceptionHelper.GetInfo(lastException, includeStack, innerDepth);
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
      /// <br/> Evaluates an expression in the context of the given
      /// <br/> thread and frame. Resolves in order:
      /// <br/> local variable → this.field → field on this → argument.
      /// </summary>
      // ----------------------------------------------------------------------
      public object Evaluate(long threadId, int frameIndex, string expression)
      {
         var frame = GetFrame(threadId, frameIndex);

         if (frame == null)
         {
            return null;
         }

         return TryResolveLocal(frame, expression)
            ??  TryResolveThisField(frame, expression)
            ??  TryResolveArg(frame, expression);
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Tries to resolve expression as a local variable.
      /// </summary>
      // ------------------------------------------------------------
      private object TryResolveLocal(StackFrame frame, string expression)
      {
         try
         {
            foreach (var local in frame.GetVisibleVariables())
            {
               if (local.Name == expression)
               {
                  return ValueFormatter.Format(frame.GetValue(local), 1);
               }
            }
         }
         catch { }

         return null;
      }

      // ----------------------------------------------------------------------
      /// <summary>
      /// <br/> Tries to resolve expression as a field on 'this'.
      /// <br/> Handles both explicit "this.field" and implicit "field".
      /// </summary>
      // ----------------------------------------------------------------------
      private object TryResolveThisField(StackFrame frame, string expression)
      {
         // this.field (explicit)
         if (expression.StartsWith("this."))
         {
            try
            {
               if (frame.GetThis() is ObjectMirror om)
               {
                  var f = om.Type.GetField(expression[5..]);

                  if (f != null)
                  {
                     return ValueFormatter.Format(om.GetValue(f), 1);
                  }
               }
            }
            catch { }
         }

         // Field on 'this' (implicit)
         try
         {
            if (frame.GetThis() is ObjectMirror om)
            {
               var f = om.Type.GetField(expression);

               if (f != null)
               {
                  return ValueFormatter.Format(om.GetValue(f), 1);
               }
            }
         }
         catch { }

         return null;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Tries to resolve expression as a method argument.
      /// </summary>
      // ------------------------------------------------------------
      private object TryResolveArg(StackFrame frame, string expression)
      {
         try
         {
            var parms = frame.Method.GetParameters();

            for (int i = 0; i < parms.Length; i++)
            {
               if (parms[i].Name == expression)
               {
                  return ValueFormatter.Format(frame.GetArgument(i), 1);
               }
            }
         }
         catch { }

         return null;
      }

   #endregion

   #region Stack / Thread

      // ------------------------------------------------------------
      /// <summary>
      /// Gets a thread mirror by its ID.
      /// </summary>
      // ------------------------------------------------------------
      public ThreadMirror GetThread(long id)
      {
         if (vm == null)
         {
            return null;
         }

         return vm.GetThreads().FirstOrDefault(t => t.Id == id);
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Returns all threads with ID, name, and state.
      /// </summary>
      // ------------------------------------------------------------
      public List<Dictionary<string, object>> GetThreads()
      {
         if (vm == null)
         {
            return new List<Dictionary<string, object>>();
         }

         return vm.GetThreads().Select
         (
            t => new Dictionary<string, object>
            {
               ["id"]    = t.Id,
               ["name"]  = t.Name ?? "",
               ["state"] = t.ThreadState.ToString()
            }
         ).ToList();
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Returns stack frames for the given thread.
      /// </summary>
      // ------------------------------------------------------------
      public List<Dictionary<string, object>> GetStackFrames
      (
         long threadId, bool full = false
      )
      {
         if (vm == null)
         {
            return new List<Dictionary<string, object>>();
         }

         var thread = ResolveThread(threadId);

         if (thread == null)
         {
            return new List<Dictionary<string, object>>();
         }

         var frames = thread.GetFrames();
         var result = new List<Dictionary<string, object>>();

         for (int i = 0; i < frames.Length; i++)
         {
            var f    = frames[i];
            var dict = new Dictionary<string, object>
            {
               ["index"]  = i,
               ["method"] = f.Method?.FullName ?? "",
               ["file"]   = f.FileName ?? "",
               ["line"]   = f.LineNumber
            };

            if (full)
            {
               dict["this"]   = StackInspector.GetThisValue(f, 1);
               dict["args"]   = StackInspector.GetArgValues(f);
               dict["locals"] = StackInspector.GetLocalValues(f);
            }

            result.Add(dict);
         }

         return result;
      }

      // ----------------------------------------------------------------------
      /// <summary>
      /// <br/> Returns variables for a single frame as this/args/locals.
      /// <br/> Replaces the former GetVariablesStructured method.
      /// </summary>
      // ----------------------------------------------------------------------
      public Dictionary<string, object> GetFrameVariables
      (
         long threadId, int frameIndex, int depth = 0
      )
      {
         var frame = GetFrame(threadId, frameIndex);

         if (frame == null)
         {
            return null;
         }

         return new Dictionary<string, object>
         {
            ["this"]   = StackInspector.GetThisValue(frame, Math.Max(depth, 1)),
            ["args"]   = StackInspector.GetArgValues(frame, depth),
            ["locals"] = StackInspector.GetLocalValues(frame, depth)
         };
      }

   #endregion

   #region Helpers

      // ------------------------------------------------------------
      /// <summary>
      /// Resolves a hostname to an IP address string.
      /// </summary>
      // ------------------------------------------------------------
      private static string ResolveHost(string host)
      {
         if (host == "localhost")
         {
            return "127.0.0.1";
         }

         return host;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Resolves thread by ID, preferring StoppedThread.
      /// </summary>
      // ------------------------------------------------------------
      private ThreadMirror ResolveThread(long threadId)
      {
         if (StoppedThread != null && StoppedThread.Id == threadId)
         {
            return StoppedThread;
         }

         return vm.GetThreads().FirstOrDefault(t => t.Id == threadId);
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Resolves a stack frame by thread ID and index.
      /// </summary>
      // ------------------------------------------------------------
      private StackFrame GetFrame(long threadId, int frameIndex)
      {
         if (vm == null)
         {
            return null;
         }

         var thread = ResolveThread(threadId);

         if (thread == null)
         {
            return null;
         }

         var frames = thread.GetFrames();

         if (frameIndex >= 0 && frameIndex < frames.Length)
         {
            return frames[frameIndex];
         }

         return null;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Fills file and line from the top frame of a thread.
      /// </summary>
      // ------------------------------------------------------------
      private void FillLocation
      (
         Dictionary<string, object> dict, ThreadMirror thread
      )
      {
         try
         {
            var frames = thread.GetFrames();

            if (frames.Length > 0)
            {
               dict["file"] = frames[0].FileName ?? "";
               dict["line"] = frames[0].LineNumber;
            }
         }
         catch { }
      }

   #endregion

   }
}
