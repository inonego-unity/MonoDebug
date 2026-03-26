namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Shared context for all command handlers.
   /// </summary>
   // ============================================================
   class DebugContext
   {

   #region Fields

      // ------------------------------------------------------------
      /// <summary>
      /// Global singleton for command handler access.
      /// </summary>
      // ------------------------------------------------------------
      public static DebugContext Current { get; set; }

      public MonoDebugSession  Session  { get; set; }
      public ProfileCollection Profiles { get; }

      public long CurrentThreadId { get; set; }
      public int  CurrentFrame    { get; set; }

   #endregion

   #region Constructor

      public DebugContext
      (
         MonoDebugSession session,
         ProfileCollection profiles
      )
      {
         Session  = session;
         Profiles = profiles;
      }

   #endregion

   }
}
