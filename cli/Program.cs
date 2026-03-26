using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

using InoCLI;

using InoIPC;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// MonoDebug CLI entry point. Parses arguments, routes commands
   /// to local handlers or to the daemon via Named Pipe.
   /// </summary>
   // ============================================================
   class Program
   {

   #region Entry

      // ------------------------------------------------------------
      /// <summary>
      /// Parses arguments and routes to the appropriate handler.
      /// </summary>
      // ------------------------------------------------------------
      static int Main(string[] args)
      {
         if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
         {
            PrintHelp();
            return 0;
         }

         var parsed = new ArgParser().Parse(args);

         switch (parsed[0])
         {
            case "attach": return HandleAttach(parsed);
            case "daemon": return HandleDaemon(parsed);
            default:       return SendToDaemon(BuildRequest(parsed));
         }
      }

   #endregion

   #region Help

      // ------------------------------------------------------------
      /// <summary>
      /// Prints usage information.
      /// </summary>
      // ------------------------------------------------------------
      static void PrintHelp()
      {
         string exeDir    = AppContext.BaseDirectory;
         string claudeMd  = Path.GetFullPath(Path.Combine(exeDir, ".claude", "CLAUDE.md"));

         Console.WriteLine
         (
$@"MonoDebug v{Constants.Version} — Mono SDB Debugger CLI for AI Agents

Reference: {claudeMd}"
         );
      }

   #endregion

   #region Attach / Daemon

      // ------------------------------------------------------------
      /// <summary>
      /// Starts the daemon as a background process, waits for the
      /// ready signal.
      /// </summary>
      // ------------------------------------------------------------
      static int HandleAttach(CommandArgs parsed)
      {
         if (!parsed.Has(1))
         {
            Console.WriteLine
            (
               IpcResponse.Error
               (
                  Constants.Error.InvalidArgs,
                  "Port required. Usage: monodebug attach <port>"
               ).RawJson
            );

            return 1;
         }

         int port = parsed.GetInt(1);

         var sb = new StringBuilder($"daemon {port}");

         if (parsed.Has("host"))     { sb.Append($" --host \"{parsed["host"]}\""); }
         if (parsed.Has("profiles")) { sb.Append($" --profiles \"{parsed["profiles"]}\""); }

         var si = new ProcessStartInfo
         {
            FileName               = Environment.ProcessPath,
            Arguments              = sb.ToString(),
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
         };

         Process daemon;

         try
         {
            daemon = Process.Start(si);
         }
         catch (Exception ex)
         {
            Console.WriteLine
            (
               IpcResponse.Error
               (
                  Constants.Error.ConnectFailed,
                  $"Failed to start daemon: {ex.Message}"
               ).RawJson
            );

            return 1;
         }

         // Wait for ready signal from daemon stdout
         string readyLine = null;

         try
         {
            var task = daemon.StandardOutput.ReadLineAsync();

            if (task.Wait(Constants.DaemonTimeout))
            {
               readyLine = task.Result;
            }
         }
         catch (Exception) { }

         if (readyLine == null || !readyLine.Contains("success"))
         {
            try { daemon.Kill(); } catch { }

            Console.WriteLine
            (
               IpcResponse.Error
               (
                  Constants.Error.ConnectFailed,
                  "Daemon did not become ready in time."
               ).RawJson
            );

            return 1;
         }

         Console.WriteLine(readyLine);
         return 0;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Runs the daemon in foreground (internal, invoked by attach).
      /// </summary>
      // ------------------------------------------------------------
      static int HandleDaemon(CommandArgs parsed)
      {
         if (!parsed.Has(1))
         {
            Console.WriteLine
            (
               IpcResponse.Error
               (
                  Constants.Error.InvalidArgs,
                  "Port required for daemon mode."
               ).RawJson
            );

            return 1;
         }

         int    port     = parsed.GetInt(1);
         string host     = parsed["host"] ?? "127.0.0.1";
         string profiles = parsed["profiles"];

         using (var d = new DebugDaemon(port, host, profiles))
         {
            return d.Run();
         }
      }

   #endregion

   #region Pipe Communication

      // ------------------------------------------------------------
      /// <summary>
      /// Builds a JSON request from parsed args.
      /// </summary>
      // ------------------------------------------------------------
      static string BuildRequest(CommandArgs parsed)
      {
         var req = new Dictionary<string, object>
         {
            ["positionals"] = parsed.Positionals,
            ["optionals"]   = parsed.Optionals
         };

         return JsonSerializer.Serialize(req);
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Discovers active daemon pipe, sends request, prints response.
      /// </summary>
      // ------------------------------------------------------------
      static int SendToDaemon(string requestJson)
      {
         string pipeName = NamedPipeTransport.Find(Constants.PipePrefix);

         if (pipeName == null)
         {
            Console.WriteLine
            (
               IpcResponse.Error
               (
                  Constants.Error.ConnectFailed,
                  "No active MonoDebug daemon found."
               ).RawJson
            );

            return 1;
         }

         try
         {
            using (var transport = new NamedPipeTransport(pipeName))
            {
               var conn     = new IpcConnection(transport);
               var response = conn.Request(requestJson);

               Console.WriteLine(response.RawJson);

               return response.IsSuccess ? 0 : 1;
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine
            (
               IpcResponse.Error
               (
                  Constants.Error.ConnectFailed, ex.Message
               ).RawJson
            );

            return 1;
         }
      }

   #endregion


   }
}
