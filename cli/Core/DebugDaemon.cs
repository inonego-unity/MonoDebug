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
      private DebugContext     context;

      private NamedPipeServer  server;

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

         // Initialize context
         var profiles = new ProfileCollection(profilesPath);
         context = new DebugContext(session, profiles);

         // Restore saved debug points from profiles
         profiles.Load();
         profiles.RebuildAll(session);

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

   #region Status

      // ------------------------------------------------------------
      /// <summary>
      /// Returns daemon and session status as JSON.
      /// </summary>
      // ------------------------------------------------------------
      private string HandleStatus(bool full)
      {
         var result = new Dictionary<string, object>
         {
            ["connected"] = context.Session.IsConnected,
            ["suspended"] = context.Session.IsSuspended,
            ["port"]      = port,
            ["host"]      = host
         };

         if (full)
         {
            result["pipe"]    = $"{Constants.PipePrefix}{port}";
            result["threads"] = context.Session.GetThreads();
         }

         return IpcResponse.Success(result).RawJson;
      }

   #endregion

   #region Dispatch

      // ------------------------------------------------------------
      /// <summary>
      /// Parses the JSON request and dispatches to the appropriate
      /// handler based on positionals[0].
      /// </summary>
      // ------------------------------------------------------------
      private string Dispatch(string requestJson)
      {
         var doc  = JsonDocument.Parse(requestJson);
         var root = doc.RootElement;

         var positionals = new List<string>();
         var optionals   = new Dictionary<string, JsonElement>();

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
               optionals[p.Name] = p.Value;
            }
         }

         string group = positionals.Count > 0 ? positionals[0] : "";
         var    args  = positionals.Count > 1
            ? positionals.GetRange(1, positionals.Count - 1)
            : new List<string>();

         switch (group)
         {
            case "status":
            {
               return HandleStatus(optionals.ContainsKey("full"));
            }

            case "detach":
            {
               context.Profiles.Save();

               context.Session.Disconnect();

               server?.Stop();

               return IpcResponse.Success("Detached.").RawJson;
            }

            case "flow":
            {
               return FlowHandler.Handle
               (
                  context, args, optionals
               );
            }

            case "stack":
            {
               return InspectHandler.HandleStack
               (
                  context, args, optionals
               );
            }

            case "thread":
            {
               return InspectHandler.HandleThread
               (
                  context, args, optionals
               );
            }

            case "vars":
            {
               return InspectHandler.HandleVars
               (
                  context, args, optionals
               );
            }

            case "eval":
            {
               return InspectHandler.HandleEval
               (
                  context, args, optionals
               );
            }

            case "break":
            {
               return BreakHandler.HandleBreak
               (
                  context, args, optionals
               );
            }

            case "catch":
            {
               return BreakHandler.HandleCatch
               (
                  context, args, optionals
               );
            }

            case "profile":
            {
               return ProfileHandler.Handle
               (
                  context, args, optionals
               );
            }

            default:
            {
               return IpcResponse.Error
               (
                  Constants.Error.InvalidArgs,
                  $"Unknown command group: {group}"
               ).RawJson;
            }
         }
      }

   #endregion

   }
}
