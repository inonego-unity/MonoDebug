using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

using Mono.Debugger.Soft;

namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// A debugging profile. Owns breakpoints and catchpoints.
   /// </summary>
   // ============================================================
   class DebugProfile
   {

   #region Fields

      public string Name   { get; set; }
      public string Desc   { get; set; }
      public bool   Active { get; set; }

      public Dictionary<int, BreakPoint> BreakPoints { get; } = new();
      public Dictionary<int, CatchPoint> CatchPoints { get; } = new();

   #endregion

   #region Constructor

      public DebugProfile(string name, string desc = null)
      {
         Name   = name;
         Desc   = desc;
         Active = false;
      }

   #endregion

   #region SetBreak

      // ------------------------------------------------------------
      /// <summary>
      /// Sets a breakpoint at file:line. Returns info or null.
      /// </summary>
      // ------------------------------------------------------------
      public BreakPoint SetBreak
      (
         MonoDebugSession session, int id,
         string file, int line,
         string condition = null, int hitCount = 0,
         long threadFilter = 0, bool isTemp = false,
         string desc = null, List<string> evalExpressions = null
      )
      {
         var location = ResolveLocation(session.VM, file, line);

         if (location == null)
         {
            return null;
         }

         var request = session.VM.SetBreakpoint(location.Method, location.ILOffset);
         request.Enable();

         if (hitCount > 0)
         {
            request.Count = hitCount;
         }

         var bp = new BreakPoint
         {
            Id              = id,
            File            = file,
            Line            = line,
            Enabled         = true,
            Hits            = 0,
            Desc            = desc,
            Condition       = condition,
            HitCount        = hitCount,
            ThreadFilter    = threadFilter,
            IsTemp          = isTemp,
            EvalExpressions = evalExpressions ?? new List<string>(),
            Request         = request
         };

         BreakPoints[id] = bp;
         return bp;
      }

   #endregion

   #region SetCatch

      // ------------------------------------------------------------
      /// <summary>
      /// Sets an exception catchpoint. Returns info or null.
      /// </summary>
      // ------------------------------------------------------------
      public CatchPoint SetCatch
      (
         MonoDebugSession session, int id,
         string typeName,
         bool caughtOnly = false, bool uncaughtOnly = false,
         string condition = null, int hitCount = 0,
         long threadFilter = 0, string desc = null
      )
      {
         var vm = session.VM;

         if (vm == null)
         {
            return null;
         }

         // Resolve exception type
         TypeMirror exType = null;
         string resolvedName = typeName == "(all)" ? null : typeName;

         if (!string.IsNullOrEmpty(resolvedName))
         {
            var types = vm.GetTypes(resolvedName, false);

            if (types.Count == 0)
            {
               types = vm.GetTypes("System." + resolvedName, false);
            }

            if (types.Count > 0)
            {
               exType = types[0];
            }
         }

         bool caught   = !uncaughtOnly;
         bool uncaught = !caughtOnly;

         var request = vm.CreateExceptionRequest(exType, caught, uncaught);
         request.Enable();

         if (hitCount > 0)
         {
            request.Count = hitCount;
         }

         var cp = new CatchPoint
         {
            Id           = id,
            TypeName     = typeName,
            Enabled      = true,
            Hits         = 0,
            Desc         = desc,
            Condition    = condition,
            HitCount     = hitCount,
            ThreadFilter = threadFilter,
            CaughtOnly   = caughtOnly,
            UncaughtOnly = uncaughtOnly,
            Request      = request
         };

         CatchPoints[id] = cp;
         return cp;
      }

   #endregion

   #region Remove / Enable / Disable

      // ------------------------------------------------------------
      /// <summary>
      /// Removes a debug point by ID.
      /// </summary>
      // ------------------------------------------------------------
      public bool Remove(int id)
      {
         if (BreakPoints.TryGetValue(id, out var bp))
         {
            bp.Request?.Disable();
            return BreakPoints.Remove(id);
         }

         if (CatchPoints.TryGetValue(id, out var cp))
         {
            cp.Request?.Disable();
            return CatchPoints.Remove(id);
         }

         return false;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Removes all breakpoints. Returns removed count.
      /// </summary>
      // ------------------------------------------------------------
      public int RemoveAllBreakPoints()
      {
         int count = BreakPoints.Count;

         foreach (var bp in BreakPoints.Values)
         {
            bp.Request?.Disable();
         }

         BreakPoints.Clear();

         return count;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Removes all catchpoints. Returns removed count.
      /// </summary>
      // ------------------------------------------------------------
      public int RemoveAllCatchPoints()
      {
         int count = CatchPoints.Count;

         foreach (var cp in CatchPoints.Values)
         {
            cp.Request?.Disable();
         }

         CatchPoints.Clear();

         return count;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Removes all debug points. Returns removed count.
      /// </summary>
      // ------------------------------------------------------------
      public int RemoveAll()
      {
         return RemoveAllBreakPoints() + RemoveAllCatchPoints();
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Enables a debug point by ID.
      /// </summary>
      // ------------------------------------------------------------
      public bool Enable(int id)
      {
         var point = Find(id);

         if (point == null)
         {
            return false;
         }

         point.Request?.Enable();
         point.Enabled = true;
         return true;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Disables a debug point by ID.
      /// </summary>
      // ------------------------------------------------------------
      public bool Disable(int id)
      {
         var point = Find(id);

         if (point == null)
         {
            return false;
         }

         point.Request?.Disable();
         point.Enabled = false;
         return true;
      }

   #endregion

   #region Query

      // ------------------------------------------------------------
      /// <summary>
      /// Finds a debug point by ID in either dictionary.
      /// </summary>
      // ------------------------------------------------------------
      public DebugPoint Find(int id)
      {
         if (BreakPoints.TryGetValue(id, out var bp))
         {
            return bp;
         }

         if (CatchPoints.TryGetValue(id, out var cp))
         {
            return cp;
         }

         return null;
      }

      // ------------------------------------------------------------
      /// <summary>
      /// Finds a debug point by its SDB EventRequest.
      /// </summary>
      // ------------------------------------------------------------
      public DebugPoint FindByRequest(EventRequest request)
      {
         foreach (var bp in BreakPoints.Values)
         {
            if (bp.Request == request)
            {
               return bp;
            }
         }

         foreach (var cp in CatchPoints.Values)
         {
            if (cp.Request == request)
            {
               return cp;
            }
         }

         return null;
      }

   #endregion

   #region Rebuild

      // ----------------------------------------------------------------------
      /// <summary>
      /// <br/> Rebuilds all debug points on the current VM.
      /// <br/> Re-creates SDB requests from existing data.
      /// <br/> Used after domain reload or reconnection.
      /// </summary>
      // ----------------------------------------------------------------------
      public void Rebuild(MonoDebugSession session)
      {
         // Rebuild breakpoints
         foreach (var bp in BreakPoints.Values.ToList())
         {
            bp.Request = null;

            var location = ResolveLocation(session.VM, bp.File, bp.Line);

            if (location != null)
            {
               var request = session.VM.SetBreakpoint(location.Method, location.ILOffset);
               request.Enable();

               if (bp.HitCount > 0)
               {
                  request.Count = bp.HitCount;
               }

               bp.Request = request;

               if (!bp.Enabled)
               {
                  request.Disable();
               }
            }
         }

         // Rebuild catchpoints
         foreach (var cp in CatchPoints.Values.ToList())
         {
            cp.Request = null;

            var vm = session.VM;
            TypeMirror exType = null;
            string resolvedName = cp.TypeName == "(all)" ? null : cp.TypeName;

            if (!string.IsNullOrEmpty(resolvedName))
            {
               var types = vm.GetTypes(resolvedName, false);

               if (types.Count == 0)
               {
                  types = vm.GetTypes("System." + resolvedName, false);
               }

               if (types.Count > 0)
               {
                  exType = types[0];
               }
            }

            bool caught   = !cp.UncaughtOnly;
            bool uncaught = !cp.CaughtOnly;

            var request = vm.CreateExceptionRequest(exType, caught, uncaught);
            request.Enable();

            if (cp.HitCount > 0)
            {
               request.Count = cp.HitCount;
            }

            cp.Request = request;

            if (!cp.Enabled)
            {
               request.Disable();
            }
         }
      }

   #endregion

   #region Hit Processing

      // ----------------------------------------------------------------------
      /// <summary>
      /// <br/> Processes a hit. Increments counter, removes if temp.
      /// <br/> Condition evaluation is caller's responsibility.
      /// </summary>
      // ----------------------------------------------------------------------
      public void ProcessHit(DebugPoint point)
      {
         point.Hits++;

         if (point is BreakPoint bp && bp.IsTemp)
         {
            Remove(bp.Id);
         }
      }

   #endregion

   #region Serialization

      // ------------------------------------------------------------
      /// <summary>
      /// Converts profile metadata to a dictionary.
      /// </summary>
      // ------------------------------------------------------------
      public Dictionary<string, object> ToDict()
      {
         var dict = new Dictionary<string, object>
         {
            ["name"]   = Name,
            ["active"] = Active
         };

         if (!string.IsNullOrEmpty(Desc))
         {
            dict["desc"] = Desc;
         }

         return dict;
      }

   #endregion

   #region Location Resolution

      // ------------------------------------------------------------
      /// <summary>
      /// Resolves file:line to a Mono.Debugger.Soft Location.
      /// </summary>
      // ------------------------------------------------------------
      internal static Location ResolveLocation
      (
         VirtualMachine vm, string file, int line
      )
      {
         string fileName = Path.GetFileName(file);

         try
         {
            string typeName = Path.GetFileNameWithoutExtension(fileName);
            var    types    = vm.GetTypes(typeName, true);

            if (types.Count == 0)
            {
               return null;
            }

            foreach (var type in types)
            {
               foreach (var method in type.GetMethods())
               {
                  try
                  {
                     foreach (var loc in method.Locations)
                     {
                        if (loc.LineNumber == line)
                        {
                           return loc;
                        }
                     }
                  }
                  catch { }
               }
            }
         }
         catch { }

         return null;
      }

   #endregion

   }
}
