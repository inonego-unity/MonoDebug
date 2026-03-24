using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

using Mono.Debugger.Soft;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// <br/> Manages a collection of debug profiles.
   /// <br/> Profiles group breakpoints and catchpoints for
   /// <br/> scenario-based debugging.
   /// <br/> Auto-saves to .monodebug/profiles/ in the project
   /// <br/> directory.
   /// </summary>
   // ============================================================
   class ProfileCollection
   {

   #region Fields

      private readonly Dictionary<string, DebugProfile> profiles = new();
      private readonly string projectPath;
      private int nextId = 1;

   #endregion

   #region Constructor

      // ------------------------------------------------------------
      /// <summary>
      /// Initializes a new ProfileCollection for the given project
      /// path. Ensures a "default" profile exists.
      /// </summary>
      // ------------------------------------------------------------
      public ProfileCollection(string projectPath)
      {
         this.projectPath = projectPath;

         if (!profiles.ContainsKey("default"))
         {
            var defaultProfile = new DebugProfile("default", "Default profile")
            {
               Active = true
            };

            profiles["default"] = defaultProfile;
         }
      }

   #endregion

   #region ID Counter

      // ------------------------------------------------------------
      /// <summary>
      /// Returns the next global ID and increments the counter.
      /// </summary>
      // ------------------------------------------------------------
      public int NextId() => Interlocked.Increment(ref nextId);

   #endregion

   #region CRUD

      // ------------------------------------------------------------
      /// <summary>
      /// Creates a new profile with the given name.
      /// Returns null if the name already exists.
      /// </summary>
      // ------------------------------------------------------------
      public DebugProfile Create(string name, string desc = null)
      {
         if (profiles.ContainsKey(name))
         {
            return null;
         }

         var profile = new DebugProfile(name, desc ?? "");

         profiles[name] = profile;

         Save();
         
         return profile;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Removes a profile by name. The "default" profile cannot
      /// be removed.
      /// </summary>
      // ------------------------------------------------------------
      public bool Remove(string name)
      {
         if (name == "default")
         {
            return false;
         }

         if (!profiles.Remove(name))
         {
            return false;
         }

         Save();

         return true;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// <br/> Edits a profile's description or name.
      /// <br/> Returns null if the profile is not found or the new
      /// <br/> name conflicts.
      /// </summary>
      // ------------------------------------------------------------
      public DebugProfile Edit
      (
         string name,
         string newDesc = null,
         string newName = null
      )
      {
         if (!profiles.TryGetValue(name, out var profile))
         {
            return null;
         }

         if (newDesc != null)
         {
            profile.Desc = newDesc;
         }

         if (!string.IsNullOrEmpty(newName) && newName != name)
         {
            if (profiles.ContainsKey(newName))
            {
               return null;
            }

            profiles.Remove(name);
            profile.Name = newName;
            profiles[newName] = profile;
         }

         Save();
         return profile;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Returns true if a profile with the given name exists.
      /// </summary>
      // ------------------------------------------------------------
      public bool Exists(string name) => profiles.ContainsKey(name);

      // ------------------------------------------------------------
      /// <summary>
      /// Gets a profile by name, or null if not found.
      /// </summary>
      // ------------------------------------------------------------
      public DebugProfile Get(string name)
      {
         profiles.TryGetValue(name, out var profile);
         return profile;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Lists all profiles.
      /// </summary>
      // ------------------------------------------------------------
      public List<DebugProfile> List() => profiles.Values.ToList();

   #endregion

   #region Activation

      // ------------------------------------------------------------
      /// <summary>
      /// Switch: activate only the named profile, deactivate all
      /// others.
      /// </summary>
      // ------------------------------------------------------------
      public void Switch(string name)
      {
         foreach (var kvp in profiles)
         {
            kvp.Value.Active = (kvp.Key == name);
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Enables (activates) the named profile.
      /// </summary>
      // ------------------------------------------------------------
      public void Enable(string name)
      {
         if (profiles.TryGetValue(name, out var profile))
         {
            profile.Active = true;
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Disables (deactivates) the named profile.
      /// </summary>
      // ------------------------------------------------------------
      public void Disable(string name)
      {
         if (profiles.TryGetValue(name, out var profile))
         {
            profile.Active = false;
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Disables all profiles.
      /// </summary>
      // ------------------------------------------------------------
      public void DisableAll()
      {
         foreach (var profile in profiles.Values)
         {
            profile.Active = false;
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Returns true if the named profile is currently active.
      /// </summary>
      // ------------------------------------------------------------
      public bool IsActive(string name)
      {
         if (profiles.TryGetValue(name, out var profile))
         {
            return profile.Active;
         }

         return false;
      }

   #endregion

   #region Global Search

      // ------------------------------------------------------------
      /// <summary>
      /// Finds a debug point by ID across all profiles.
      /// </summary>
      // ------------------------------------------------------------
      public DebugPoint FindById(int id)
      {
         foreach (var profile in profiles.Values)
         {
            var point = profile.Find(id);

            if (point != null)
            {
               return point;
            }
         }

         return null;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Finds a debug point by its SDB EventRequest across all
      /// profiles.
      /// </summary>
      // ------------------------------------------------------------
      public DebugPoint FindByRequest(EventRequest request)
      {
         foreach (var profile in profiles.Values)
         {
            var point = profile.FindByRequest(request);

            if (point != null)
            {
               return point;
            }
         }

         return null;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Returns all breakpoints from all profiles.
      /// </summary>
      // ------------------------------------------------------------
      public List<BreakPoint> AllBreakPoints()
      {
         var result = new List<BreakPoint>();

         foreach (var profile in profiles.Values)
         {
            result.AddRange(profile.BreakPoints.Values);
         }

         return result;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Returns all catchpoints from all profiles.
      /// </summary>
      // ------------------------------------------------------------
      public List<CatchPoint> AllCatchPoints()
      {
         var result = new List<CatchPoint>();

         foreach (var profile in profiles.Values)
         {
            result.AddRange(profile.CatchPoints.Values);
         }

         return result;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Removes a debug point by ID across all profiles.
      /// </summary>
      // ------------------------------------------------------------
      public bool RemovePoint(int id)
      {
         foreach (var profile in profiles.Values)
         {
            if (profile.Remove(id))
            {
               return true;
            }
         }

         return false;
      }

   #endregion

   #region Rebuild

      // ------------------------------------------------------------
      /// <summary>
      /// Rebuilds all debug points in all profiles on the current
      /// VM session.
      /// </summary>
      // ------------------------------------------------------------
      public void RebuildAll(MonoDebugSession session)
      {
         foreach (var profile in profiles.Values)
         {
            profile.Rebuild(session);
         }
      }

   #endregion

   #region Persistence

      // ------------------------------------------------------------
      /// <summary>
      /// Saves all profiles to .monodebug/profiles/.
      /// Each profile is serialized with its breakpoints and
      /// catchpoints.
      /// </summary>
      // ------------------------------------------------------------
      public void Save()
      {
         if (string.IsNullOrEmpty(projectPath))
         {
            return;
         }

         string dir = Path.Combine(projectPath, ".monodebug", "profiles");

         try
         {
            Directory.CreateDirectory(dir);

            foreach (var profile in profiles.Values)
            {
               var dict = profile.ToDict();

               // Include breakpoint definitions
               dict["breakpoints"] = profile.BreakPoints.Values
                  .Select(b => new Dictionary<string, object>
                  {
                     ["file"]         = b.File,
                     ["line"]         = b.Line,
                     ["condition"]    = b.Condition ?? "",
                     ["hitCount"]     = b.HitCount,
                     ["threadFilter"] = b.ThreadFilter,
                     ["desc"]         = b.Desc ?? ""
                  })
                  .ToList();

               // Include catchpoint definitions
               dict["catchpoints"] = profile.CatchPoints.Values
                  .Select(c => new Dictionary<string, object>
                  {
                     ["typeName"]     = c.TypeName,
                     ["caughtOnly"]   = c.CaughtOnly,
                     ["uncaughtOnly"] = c.UncaughtOnly,
                     ["desc"]         = c.Desc ?? ""
                  })
                  .ToList();

               string file = Path.Combine(dir, profile.Name + ".json");
               string json = JsonSerializer.Serialize
               (
                  dict,
                  new JsonSerializerOptions { WriteIndented = true }
               );
               File.WriteAllText(file, json);
            }
         }
         catch (Exception ex)
         {
            Console.Error.WriteLine($"Save error: {ex}");
         }
      }

      // ------------------------------------------------------------
      /// <summary>
      /// <br/> Loads profiles from .monodebug/profiles/.
      /// <br/> Returns saved breakpoint and catchpoint definitions
      /// <br/> for restoration.
      /// </summary>
      // ------------------------------------------------------------
      public (List<BreakPoint> breakpoints, List<CatchPoint> catchpoints) Load()
      {
         var savedBps = new List<BreakPoint>();
         var savedCps = new List<CatchPoint>();

         if (string.IsNullOrEmpty(projectPath))
         {
            return (savedBps, savedCps);
         }

         string dir = Path.Combine(projectPath, ".monodebug", "profiles");

         if (!Directory.Exists(dir))
         {
            return (savedBps, savedCps);
         }

         try
         {
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
               string json = File.ReadAllText(file);
               var    doc  = JsonDocument.Parse(json);
               var    root = doc.RootElement;

               string name = root.TryGetProperty("name", out var n) ? n.GetString() : "";
               string desc = root.TryGetProperty("desc", out var d) ? d.GetString() : "";
               bool active = root.TryGetProperty("active", out var a) && a.GetBoolean();

               if (string.IsNullOrEmpty(name))
               {
                  continue;
               }

               if (profiles.TryGetValue(name, out var existing))
               {
                  existing.Desc   = desc;
                  existing.Active = active;
               }
               else
               {
                  var profile = new DebugProfile(name, desc)
                  {
                     Active = active
                  };

                  profiles[name] = profile;
               }

               // Load saved breakpoints
               if (root.TryGetProperty("breakpoints", out var bpsArr))
               {
                  foreach (var bp in bpsArr.EnumerateArray())
                  {
                     int id = NextId();

                     var breakPoint = new BreakPoint
                     {
                        Id           = id,
                        File         = bp.TryGetProperty("file", out var f) ? f.GetString() : "",
                        Line         = bp.TryGetProperty("line", out var l) ? l.GetInt32() : 0,
                        Condition    = bp.TryGetProperty("condition", out var c) ? c.GetString() : "",
                        HitCount     = bp.TryGetProperty("hitCount", out var h) ? h.GetInt32() : 0,
                        ThreadFilter = bp.TryGetProperty("threadFilter", out var t) ? t.GetInt64() : 0,
                        Desc         = bp.TryGetProperty("desc", out var dd) ? dd.GetString() : "",
                        Enabled      = true
                     };

                     profiles[name].BreakPoints[id] = breakPoint;
                     savedBps.Add(breakPoint);
                  }
               }

               // Load saved catchpoints
               if (root.TryGetProperty("catchpoints", out var cpsArr))
               {
                  foreach (var cp in cpsArr.EnumerateArray())
                  {
                     int id = NextId();

                     var catchPoint = new CatchPoint
                     {
                        Id           = id,
                        TypeName     = cp.TryGetProperty("typeName", out var tn) ? tn.GetString() : "",
                        CaughtOnly   = cp.TryGetProperty("caughtOnly", out var co) && co.GetBoolean(),
                        UncaughtOnly = cp.TryGetProperty("uncaughtOnly", out var uo) && uo.GetBoolean(),
                        Desc         = cp.TryGetProperty("desc", out var dd) ? dd.GetString() : "",
                        Enabled      = true
                     };

                     profiles[name].CatchPoints[id] = catchPoint;
                     savedCps.Add(catchPoint);
                  }
               }
            }
         }
         catch
         {
         }

         return (savedBps, savedCps);
      }

   #endregion

   }
}
