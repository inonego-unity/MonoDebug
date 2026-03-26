using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

using Mono.Debugger.Soft;

using InoIPC;

using InoCLI;

using MonoDebug.Commands;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Named Pipe server daemon. Accepts CLI client connections
   /// and dispatches commands to the appropriate handlers.
   /// </summary>
   // ============================================================
   class DebugDaemon : IDisposable
   {

   #region Fields

      private readonly int    port;
      private readonly string host;
      private readonly string profilesPath;

      private MonoDebugSession session;

      private NamedPipeServer   server;
      private CommandRegistry   registry;

   #endregion

   #region Constructor

      // ------------------------------------------------------------
      /// <summary>
      /// Creates a daemon targeting the given host and port.
      /// </summary>
      // ------------------------------------------------------------
      public DebugDaemon(int port, string host, string profilesPath)
      {
         this.port         = port;
         this.host         = host;
         this.profilesPath = profilesPath;
      }

   #endregion

   #region Lifecycle

      // ------------------------------------------------------------
      /// <summary>
      /// Starts the daemon: connects to the Mono VM, restores
      /// saved state, and enters the Named Pipe accept loop.
      /// </summary>
      // ------------------------------------------------------------
      public int Run()
      {
         // Connect to Mono VM via SDB
         session = new MonoDebugSession();
         Console.Error.WriteLine($"Connecting to {host}:{port}...");

         if (!session.Connect(port, host))
         {
            Console.Error.WriteLine("Failed to connect. Is the target running with SDB enabled?");
            return 1;
         }

         Console.Error.WriteLine($"Connected to {host}:{port}.");

         // Initialize context (singleton for command handlers)
         var profiles = new ProfileCollection(profilesPath);

         DebugContext.Current = new DebugContext(session, profiles);

         // Restore saved debug points from profiles
         profiles.Load();
         profiles.RebuildAll(session);

         // Initialize command registry
         registry = new CommandRegistry();
         registry.Initialize(typeof(DebugDaemon).Assembly);

         // Write ready signal to stdout (CLI reads this)
         Console.WriteLine
         (
            IpcResponse.Success
            (
               new Dictionary<string, object>
               {
                  ["port"]      = port,
                  ["host"]      = host,
                  ["connected"] = true
               }
            ).RawJson
         );

         Console.Out.Flush();

         // Start Named Pipe server
         string pipeName = $"{Constants.PipePrefix}{port}";

         Console.Error.WriteLine($"Listening on pipe: {pipeName}");

         server = new NamedPipeServer(pipeName);

         server.Start(conn =>
         {
            HandleClient(conn);
         });

         Dispose();
         return 0;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Releases all resources held by the daemon.
      /// </summary>
      // ------------------------------------------------------------
      public void Dispose()
      {

         try { server?.Stop(); } catch {}
         try { session?.Dispose(); } catch {}
      }

   #endregion

   #region Client Handling

      // ------------------------------------------------------------
      /// <summary>
      /// Handles a single client connection. Reads request,
      /// dispatches, writes response.
      /// </summary>
      // ------------------------------------------------------------
      private void HandleClient(IpcConnection conn)
      {
         try
         {
            string requestJson  = conn.Receive();
            string responseJson = Dispatch(requestJson);

            conn.Send(responseJson);
         }
         catch (Exception ex)
         {
            try
            {
               conn.Send(IpcResponse.Error(Constants.Error.ConnectFailed, ex.Message));
            }
            catch {}
         }
      }

   #endregion

   #region Status / Detach

      // ------------------------------------------------------------
      /// <summary>
      /// Returns daemon and session status as JSON.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("status", description = "Daemon status")]
      public static string HandleStatus(CommandArgs args)
      {
         var context = DebugContext.Current;

         var result = new Dictionary<string, object>
         {
            ["connected"] = context.Session.IsConnected,
            ["suspended"] = context.Session.IsSuspended,
            ["port"]      = context.Session.Port,
            ["host"]      = context.Session.Host
         };

         if (args.Has("full"))
         {
            result["pipe"]    = $"{Constants.PipePrefix}{context.Session.Port}";
            result["threads"] = context.Session.GetThreads();
         }

         return IpcResponse.Success(result).RawJson;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Detaches from the VM, saves profiles, and stops server.
      /// </summary>
      // ------------------------------------------------------------
      [CLICommand("detach", description = "Detach from VM")]
      public static string HandleDetach(CommandArgs args)
      {
         var context = DebugContext.Current;

         context.Profiles.Save();
         context.Session.Disconnect();

         return IpcResponse.Success("Detached.").RawJson;
      }

   #endregion

   #region Dispatch

      // ----------------------------------------------------------------------
      /// <summary>
      /// <br/> Parses the JSON request into CommandArgs and resolves
      /// <br/> via CommandRegistry.
      /// </summary>
      // ----------------------------------------------------------------------
      private string Dispatch(string requestJson)
      {
         var parsed = ParseRequest(requestJson);

         try
         {
            var (info, args) = registry.Resolve(parsed);

            string result = (string)info.Method.Invoke(null, new object[] { args });

            // detach command: also stop the server
            if (info.Key == "detach")
            {
               server?.Stop();
            }

            return result;
         }
         catch (ArgumentException ex)
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs, ex.Message
            ).RawJson;
         }
         catch (System.Reflection.TargetInvocationException ex)
         {
            var inner = ex.InnerException ?? ex;
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs, inner.Message
            ).RawJson;
         }
         catch (Exception ex)
         {
            return IpcResponse.Error
            (
               Constants.Error.InvalidArgs, ex.Message
            ).RawJson;
         }
      }

      // ----------------------------------------------------------------------
      /// <summary>
      /// <br/> Parses JSON request into CommandArgs.
      /// <br/> Format: {"positionals":[...],"optionals":{...}}
      /// </summary>
      // ----------------------------------------------------------------------
      private static CommandArgs ParseRequest(string json)
      {
         var doc  = JsonDocument.Parse(json);
         var root = doc.RootElement;

         var positionals = new List<string>();
         var optionals   = new Dictionary<string, List<string>>();

         if (root.TryGetProperty("positionals", out var posProp))
         {
            foreach (var p in posProp.EnumerateArray())
            {
               positionals.Add(p.GetString());
            }
         }

         if (root.TryGetProperty("optionals", out var optProp))
         {
            foreach (var p in optProp.EnumerateObject())
            {
               var values = new List<string>();

               if (p.Value.ValueKind == JsonValueKind.Array)
               {
                  foreach (var v in p.Value.EnumerateArray())
                  {
                     values.Add(v.GetString());
                  }
               }
               else if (p.Value.ValueKind != JsonValueKind.Null)
               {
                  values.Add(p.Value.ToString());
               }

               optionals[p.Name] = values;
            }
         }

         return new CommandArgs
         {
            Positionals = positionals,
            Optionals   = optionals
         };
      }

   #endregion

   }
}
