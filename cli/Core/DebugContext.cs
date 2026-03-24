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

      public MonoDebugSession  Session  { get; }
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
