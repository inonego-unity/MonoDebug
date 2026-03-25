namespace MonoDebug
{
   // ============================================================
   /// <summary>
   /// Shared constants.
   /// </summary>
   // ============================================================
   static class Constants
   {

      public const string Version        = "0.1.0";
      public const string PipePrefix     = "monodebug-";
      public const int    DaemonTimeout  = 10000;
      public const int    ConnectTimeout = 5000;

      // ============================================================
      /// <summary>
      /// Error code constants for IPC responses.
      /// </summary>
      // ============================================================
      public static class Error
      {
         public const string NoSession     = "NO_SESSION";
         public const string NotStopped    = "NOT_STOPPED";
         public const string InvalidArgs   = "INVALID_ARGS";
         public const string ConnectFailed = "CONNECT_FAILED";
         public const string EvalError     = "EVAL_ERROR";
         public const string Timeout       = "TIMEOUT";
         public const string AlreadyExists = "ALREADY_EXISTS";
         public const string NotFound      = "NOT_FOUND";
      }

   }
}
